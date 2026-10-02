using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Arterra.Core.Network;
using Unity.Mathematics;

namespace Arterra.Core.Storage {
    public class MapChangeAccumulator {
        public readonly Dictionary<int3, PendingChangeFlusher> PendingChanges = new();

        public void AddChunk(int3 CCoord) {
            if (!PendingChanges.TryAdd(CCoord, new PendingChangeFlusher(CCoord))) return;
            // Border edits can affect local neighbors even in a remote-only chunk.
            if (ChunkGenerationDriver.Local != null) AddSource(CCoord, ChunkGenerationDriver.Local.Source);
        }

        public void AddSource(int3 CCoord, ChunkSource source) {
            if (PendingChanges.TryGetValue(CCoord, out var flusher))
                flusher.AddSource(source);
        }

        public void RemoveSource(int3 CCoord, ChunkSource source) {
            if (PendingChanges.TryGetValue(CCoord, out var flusher))
                flusher.RemoveSource(source);
        }

        public void RemoveChunk(CPUMapManager.ChunkMapInfo chunk) {
            if (!PendingChanges.Remove(chunk.CCoord, out var flusher)) return;
            // Visual subscribers outlive the CPU allocation. Deferred encoders retain this flusher
            // until its final edits are captured; clearing it here would silently drop those edits.
            flusher.FlushChanges();
        }

        public void Clear() {
            foreach (var flusher in PendingChanges.Values) flusher.Clear();
            PendingChanges.Clear();
        }

        public struct MetadataChange {
            public uint CellIndex;
            public object Value;
        }

        public void TrackMetadata(int3 CCoord, uint index, object value) {
            if (!TransportManager.Instance.IsRunning) return;
            if (PendingChanges.TryGetValue(CCoord, out var flusher))
                flusher.RegisterMetadata(index, value);
        }

        // Producers only copy values into a queue. Everything else runs on the flush/transport thread.
        public void TrackChange(int3 CCoord, uint MCoord, MapData data) {
            if (PendingChanges.TryGetValue(CCoord, out var flusher))
                flusher.Register(MCoord, data);
        }

        public void FlushChangesTick() {
            foreach (var flusher in PendingChanges.Values)
                flusher.FlushChanges();
        }

        // The voxel section stays binary: coordinate, count, then eight bytes per cell.
        // Only messages containing metadata have a trailing JSON array.
        public static Dictionary<uint, MapData> DecodeChanges(byte[] bytes, int cellCount,
            out int3 CCoord, out MetadataChange[] metadata) {
            if (bytes.Length < 16) throw new InvalidDataException("Invalid chunk map update length.");

            CCoord = new int3(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4)));
            int count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12, 4));
            if (count < 0 || count > cellCount || count > (bytes.Length - 16) / 8)
                throw new InvalidDataException("Invalid chunk map update count.");

            int end = 16 + count * 8;
            var changes = new Dictionary<uint, MapData>();
            for (int offset = 16; offset < end; offset += 8) {
                uint index = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
                if (index >= cellCount) throw new InvalidDataException("Chunk map update index is out of bounds.");
                changes[index] = new MapData { data = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4)) };
            }

            metadata = end == bytes.Length ? Array.Empty<MetadataChange>()
                : JsonConvert.DeserializeObject<MetadataChange[]>(Encoding.UTF8.GetString(bytes, end, bytes.Length - end));
            if (metadata == null) throw new InvalidDataException("Invalid chunk metadata update.");
            foreach (var change in metadata)
                if (change.CellIndex >= cellCount) throw new InvalidDataException("Chunk metadata index is out of bounds.");
            return changes;
        }

        // Reference type: deferred callbacks and the chunk dictionary must share the same state.
        public sealed class PendingChangeFlusher {
            private readonly int3 CCoord;
            private readonly ConcurrentQueue<(uint coord, MapData data)> pendingChanges = new();
            private readonly Dictionary<uint, Change<MapData>> activeChanges = new();
            private readonly ConcurrentQueue<(uint index, byte[] bytes)> pendingMetadata = new();
            private readonly Dictionary<uint, Change<byte[]>> activeMetadata = new();
            private readonly Dictionary<ChunkSource, int> subscriberCounts = new();
            private readonly Dictionary<ChunkSource, int?> sources = new();
            private readonly Dictionary<int, ChunkSource> pendingSends = new();
            private readonly HashSet<int> AnsweredWindow = new();
            private int NextSendGen;
            private int AnsweredGen;

            public PendingChangeFlusher(int3 CCoord) => this.CCoord = CCoord;

            public void Register(uint MCoord, MapData data) => pendingChanges.Enqueue((MCoord, data));

            public void RegisterMetadata(uint index, object value) {
                // Capture at the mutation site; deferred transport must never serialize a live inventory.
                byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new MetadataChange {
                    CellIndex = index, Value = value
                }));
                pendingMetadata.Enqueue((index, bytes));
            }

            public void AddSource(ChunkSource source) {
                subscriberCounts.TryGetValue(source, out int count);
                subscriberCounts[source] = count + 1;
                if (count == 0 && source.DefersMapChanges) sources.Add(source, null);
            }

            public void RemoveSource(ChunkSource source) {
                if (!subscriberCounts.TryGetValue(source, out int count)) return;
                if (count > 1) {
                    subscriberCounts[source] = count - 1;
                    return;
                }

                subscriberCounts.Remove(source);
                if (sources.TryGetValue(source, out var generation) && generation.HasValue)
                    CompleteGeneration(generation.Value);
                sources.Remove(source);
            }

            public void FlushChanges() {
                if (pendingChanges.IsEmpty && pendingMetadata.IsEmpty) return;

                if (sources.Count == 0) { // Singleplayer/local reception: no generations, retained deltas, or serialization.
                    var localChanges = DrainPendingEvents();
                    if (localChanges.Count != 0)
                        foreach (var source in subscriberCounts.Keys)
                            source.ReflectChunk(CCoord, localChanges);
                    return;
                }

                // Reserve every recipient before any source can synchronously consume its callback.
                var sends = new List<(ChunkSource source, int generation)>();
                foreach (var pair in sources) {
                    if (pair.Value.HasValue) continue;
                    int generation = NextSendGen; 
                    NextSendGen = unchecked(NextSendGen + 1);
                    sends.Add((pair.Key, generation));
                }

                foreach (var send in sends) {
                    sources[send.source] = send.generation;
                    pendingSends.Add(send.generation, send.source);
                }

                var changes = DrainPendingEvents();
                foreach (var source in subscriberCounts.Keys) {
                    if (!source.DefersMapChanges && changes.Count != 0) 
                        source.ReflectChunk(CCoord, changes);
                }

                for (int i = 0; i < sends.Count; i++) {
                    var send = sends[i];
                    int generation = send.generation;
                    try {
                        send.source.ReflectChunk(CCoord, changes,
                            () => SendToPeer(generation),
                            () => CompleteGeneration(generation)
                        );
                    } catch {
                        // No callback was handed off successfully for the remaining sends.
                        for (int j = i; j < sends.Count; j++) CompleteGeneration(sends[j].generation);
                        throw;
                    }
                }
            }

            private Dictionary<uint, MapData> DrainPendingEvents() {
                var changes = new Dictionary<uint, MapData>();
                // The latest reserved send must capture these values before they can be retired.
                int generation = unchecked(NextSendGen - 1);
                while (pendingChanges.TryDequeue(out var change)) {
                    changes[change.coord] = change.data;
                    if (sources.Count != 0) {
                        activeChanges[change.coord] = new Change<MapData> {
                            data = change.data,
                            generation = generation
                        };
                    }
                }
                while (pendingMetadata.TryDequeue(out var change)) {
                    if (sources.Count != 0)
                        activeMetadata[change.index] = new Change<byte[]> { 
                            data = change.bytes,
                            generation = generation
                        };
                }
                return changes;
            }

            public byte[] SendToPeer(int generation) {
                pendingSends.TryGetValue(generation, out var source);
                int count = 0;
                if (source != null) {
                    foreach (var change in activeChanges.Values)
                        if (!Before(change.generation, generation))
                            count++;
                }

                using var metadata = new MemoryStream();
                if (HasRealChunkSubscription(source)) {
                    foreach (var change in activeMetadata.Values) {
                        if (Before(change.generation, generation)) continue;
                        metadata.WriteByte(metadata.Length == 0 ? (byte)'[' : (byte)',');
                        metadata.Write(change.data, 0, change.data.Length);
                    }
                }
                if (metadata.Length != 0) metadata.WriteByte((byte)']');

                byte[] bytes = new byte[checked(16 + count * 8 + (int)metadata.Length)];
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), CCoord.x);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), CCoord.y);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), CCoord.z);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12, 4), count);
                int offset = 16;
                if (source != null) {
                    foreach (var pair in activeChanges) {
                        if (Before(pair.Value.generation, generation)) continue;
                        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), pair.Key);
                        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 4, 4), pair.Value.data.data);
                        offset += 8;
                    }
                }

                metadata.Position = 0;
                metadata.Read(bytes, offset, (int)metadata.Length);

                // Completion means captured into bytes, not acknowledged by the remote peer.
                CompleteGeneration(generation);
                return bytes;
            }

            // Visual-only subscribers need voxel changes, but not material-instance metadata.
            private bool HasRealChunkSubscription(ChunkSource source) {
                if (source == null || !CPUMapManager.TryGetChunkInfo(CCoord, out var chunk)) return false;
                foreach (var driver in chunk.Generation.GetDriverSet()) {
                    if (ReferenceEquals(driver.Source, source)) return true;
                }
                return false;
            }

            /// <summary>Also call when abandoning a deferred send without invoking its callback.</summary>
            public void CompleteGeneration(int generation) {
                if (!pendingSends.Remove(generation, out var source)) return;
                sources[source] = null;
                AnsweredWindow.Add(generation);
                if (!TryAdvanceGeneration()) return;

                foreach (var coord in new List<uint>(activeChanges.Keys)) {
                    if (Before(activeChanges[coord].generation, AnsweredGen))
                        activeChanges.Remove(coord);
                }
                foreach (var index in new List<uint>(activeMetadata.Keys)) {
                    if (Before(activeMetadata[index].generation, AnsweredGen))
                        activeMetadata.Remove(index);
                }
            }

            public bool TryAdvanceGeneration() {
                int startingGen = AnsweredGen;
                while (AnsweredWindow.Remove(AnsweredGen))
                    AnsweredGen = unchecked(AnsweredGen + 1);
                return startingGen != AnsweredGen;
            }

            public void Clear() {
                foreach (int generation in new List<int>(pendingSends.Keys)) CompleteGeneration(generation);
                sources.Clear();
                subscriberCounts.Clear();
                activeChanges.Clear();
                activeMetadata.Clear();
                while (pendingMetadata.TryDequeue(out _)) { }
                while (pendingChanges.TryDequeue(out _)) { }
            }

            private static bool Before(int left, int right) => unchecked(left - right) < 0;

            private struct Change<T> {
                public T data;
                public int generation;
            }
        }
    }
}
