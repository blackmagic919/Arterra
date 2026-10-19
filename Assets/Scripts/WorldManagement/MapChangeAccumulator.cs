using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Arterra.Core.Storage {
    public class MapChangeAccumulator {
        public readonly Dictionary<int3, PendingChangeFlusher> PendingChanges = new();

        // Producers only copy values into a queue. Everything else runs on the flush/transport thread.
        public void TrackChange(int3 CCoord, uint MCoord, MapData data) {
            if (PendingChanges.TryGetValue(CCoord, out var flusher))
                flusher.Register(MCoord, data);
        }

        public void FlushChangesTick() {
            // Discover subscriptions even when a chunk has no new changes this tick.
            foreach (var pair in CPUMapManager.ChunkData) {
                if (!PendingChanges.TryGetValue(pair.Key, out var flusher)) {
                    flusher = new PendingChangeFlusher(pair.Key);
                    PendingChanges.Add(pair.Key, flusher);
                }
                var sources = new HashSet<ChunkSource>();
                var drivers = pair.Value.Generation?.GetDriverSet();
                if (drivers != null)
                    foreach (var driver in drivers) sources.Add(driver.Source);

                flusher.FlushChanges(sources);
            }

            foreach (var coordinate in new List<int3>(PendingChanges.Keys)) {
                if (CPUMapManager.TryGetChunkInfo(coordinate, out _)) continue;
                if (PendingChanges.Remove(coordinate, out var flusher)) flusher.Clear();
            }
        }

        // Reference type: deferred callbacks and the chunk dictionary must share the same state.
        public sealed class PendingChangeFlusher {
            private readonly int3 coordinate;
            private readonly ConcurrentQueue<(uint coord, MapData data)> pendingChanges = new();
            private readonly Dictionary<uint, Change> activeChanges = new();
            private readonly Dictionary<ChunkSource, int?> sources = new();
            private readonly Dictionary<int, ChunkSource> pendingSends = new();
            private readonly HashSet<int> AnsweredWindow = new();
            private int NextSendGen;
            private int AnsweredGen;

            public PendingChangeFlusher(int3 coordinate) => this.coordinate = coordinate;

            public void Register(uint MCoord, MapData data) => pendingChanges.Enqueue((MCoord, data));

            public void FlushChanges(HashSet<ChunkSource> currentSources) {
                //Remove an exisitng listener(source) on the chunk
                foreach (var source in new List<ChunkSource>(sources.Keys)) {
                    if (currentSources.Contains(source)) continue;
                    var generation = sources[source];
                    if (generation.HasValue)
                        CompleteGeneration(generation.Value);
                    sources.Remove(source);
                }
                

                //For all new sources add a new listener entry
                foreach (var source in currentSources) {
                    sources.TryAdd(source, null);
                }

                if (pendingChanges.IsEmpty) return;

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

                DrainPendingEvents();
                if (sources.Count == 0) activeChanges.Clear();

                for (int i = 0; i < sends.Count; i++) {
                    var send = sends[i];
                    int generation = send.generation;
                    try {
                        send.source.ReflectChunk(() => SendToPeer(generation));
                    } catch {
                        // No callback was handed off successfully for the remaining sends.
                        for (int j = i; j < sends.Count; j++) CompleteGeneration(sends[j].generation);
                        throw;
                    }
                }
            }

            private void DrainPendingEvents() {
                // The latest reserved send must capture these values before they can be retired.
                int generation = unchecked(NextSendGen - 1);
                while (pendingChanges.TryDequeue(out var change)) {
                    activeChanges[change.coord] = new Change {
                        data = change.data,
                        generation = generation
                    };
                }
            }

            public byte[] SendToPeer(int generation) {
                pendingSends.TryGetValue(generation, out var source);
                int count = 0;
                if (source != null) {
                    foreach (var change in activeChanges.Values)
                        if (!Before(change.generation, generation))
                            count++;
                }

                byte[] bytes = new byte[checked(12 + count * 8)];
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), coordinate.x);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), coordinate.y);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), coordinate.z);
                int offset = 12;
                if (source != null) {
                    foreach (var pair in activeChanges) {
                        if (Before(pair.Value.generation, generation)) continue;
                        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), pair.Key);
                        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 4, 4), pair.Value.data.data);
                        offset += 8;
                    }
                }

                // Completion means captured into bytes, not acknowledged by the remote peer.
                CompleteGeneration(generation);
                return bytes;
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
                activeChanges.Clear();
                while (pendingChanges.TryDequeue(out _)) { }
            }

            private static bool Before(int left, int right) => unchecked(left - right) < 0;

            private struct Change {
                public MapData data;
                public int generation;
            }
        }
    }
}
