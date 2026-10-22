using System;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.Engine.Terrain.Readback;
using Unity.Mathematics;
using UnityEngine;
using static Arterra.Core.Storage.SharedResourceManager;

namespace Arterra.Data.Entity {
    /// <summary>
    /// GPU spawn candidates owned by one real chunk. Profiles use positive offsets and must fit
    /// within one chunk per axis. All GPU operations run on the generation context/main thread.
    /// Unresolved candidates are discarded on Release; persisting their progress is not implemented.
    /// </summary>
    public sealed class EntityGenerator {
        private static ComputeShader identifier;
        private static ComputeShader transcriber;
        private static int chunkSize;
        public static EntityGenOffsets bufferOffsets;

        private const int RegionCount = 8;
        private const byte AllRegions = 0xFF;
        private byte pendingMaps;
        private bool planned;
        public uint LeftoverEntities { get; private set; }
        // Bit zero internally tracks the origin; the remaining seven bits track positive neighbors.
        public byte PendingNeighbors => (byte)(pendingMaps >> 1);

        public static void Initialize() {
            chunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
            foreach (var authoring in Config.CURRENT.Generation.Entities.Reg) {
                uint3 size = authoring.Setting.profile.bounds;
                if (math.any(size > (uint)chunkSize))
                    throw new InvalidOperationException($"Entity profile {size} exceeds chunk size {chunkSize}.");
            }
            identifier = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Entities/EntityIdentifier");
            transcriber = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Entities/EntityTranscriber");
            int points = checked(chunkSize * chunkSize * chunkSize);
            bufferOffsets = new EntityGenOffsets(Arterra.Engine.Terrain.Map.Creator.bufferOffsets.biomeMapEnd, points, GenPoint.size);

            var gpu = GraphicsGeneration;
            gpu.SetInt(identifier, "numPointsPerAxis", chunkSize);
            gpu.SetInt(identifier, "GPConfig", (int)GenPoint.GenType.Entity);
            gpu.SetInt(identifier, "bCOUNTER_entities", bufferOffsets.candidateCounter);
            gpu.SetInt(identifier, "bCOUNTER_prune", bufferOffsets.acceptedCounter);
            gpu.SetInt(identifier, "bCOUNTER_allocation", bufferOffsets.allocationCounter);
            gpu.SetInt(identifier, "bSTART_entities", bufferOffsets.candidateStart);
            gpu.SetInt(identifier, "bSTART_prune", bufferOffsets.acceptedStart);

            foreach (string name in new[] { "Identify", "PrepareAllocation", "InitializeCandidates", "LoadCount", "Prune", "AddNeighbor" }) {
                int kernel = identifier.FindKernel(name);
                gpu.SetBuffer(identifier, kernel, "counter", gpu.Work.Scratch);
                if (name != "PrepareAllocation" && name != "LoadCount")
                    gpu.SetBuffer(identifier, kernel, "chunkEntities", gpu.Work.Scratch);
                if (name == "Identify")
                    gpu.SetBuffer(identifier, kernel, "BiomeMap", gpu.Work.Scratch);
                if (name != "Identify" && name != "PrepareAllocation")
                    gpu.SetBuffer(identifier, kernel, "CandidateAddresses", gpu.Memory.Address);
            }

            int transcribe = transcriber.FindKernel("CSMain");
            gpu.SetBuffer(transcriber, transcribe, "chunkEntities", gpu.Work.Scratch);
            gpu.SetBuffer(transcriber, transcribe, "counter", gpu.Work.Scratch);
            gpu.SetBuffer(transcriber, transcribe, "_AddressDict", gpu.Memory.Address);
            gpu.SetInt(transcriber, "bCOUNTER_entities", bufferOffsets.acceptedCounter);
            gpu.SetInt(transcriber, "bSTART_entities", bufferOffsets.acceptedStart);
        }

        /// <summary>
        /// Called once the caller's GPU or CPU map is ready. Appends accepted GenPoints to the caller's
        /// shared readback; the caller issues that readback after all generators contribute.
        /// Saved-entity chunks pass generate=false to advance lower neighbors without respawning.
        /// Rebuild this chunk's biome scratch immediately before calling when generate=true.
        /// </summary>
        public void PlanEntities(AsyncGenInfoReadback readback, int biomeStart, int3 CCoord, bool generate = true) {
            if (readback == null) throw new ArgumentNullException(nameof(readback));
            if (planned) return;
            if (identifier == null) throw new InvalidOperationException("EntityGenerator is not initialized.");
            if (!CPUMapManager.TryGetChunkInfo(CCoord, out var chunk) || (!chunk.HasGraphicsMap && !chunk.HasMap))
                throw new InvalidOperationException("Entity planning requires a retained GPU map or completed CPU map.");
            if (chunk.Generation.Entities != null && !ReferenceEquals(chunk.Generation.Entities, this))
                throw new InvalidOperationException($"Entity planning already has an owner at {CCoord}.");
            chunk.Generation.Entities = this;

            if (generate) {
                IdentifyCandidates(biomeStart, CCoord);
                for (int region = 0; region < RegionCount; region++) {
                    if (!CPUMapManager.TryGetChunkInfo(CCoord + RegionOffset(region), out var neighbor) || (!neighbor.HasGraphicsMap && !neighbor.HasMap))
                        continue;
                    UploadMap(neighbor);
                    CheckRegion(region, readback, 0, neighbor); // These candidates remain relative to this chunk.
                }
            }
            planned = true;

            bool uploaded = false;
            for (int region = 1; region < RegionCount; region++) {
                if (!CPUMapManager.TryGetChunkInfo(CCoord - RegionOffset(region), out var neighbor)) continue;
                EntityGenerator owner = neighbor.Generation.Entities;
                if (owner == null || (owner.pendingMaps & (1 << region)) == 0) continue;
                if (!uploaded) { UploadMap(chunk); uploaded = true; }
                owner.CheckRegion(region, readback, region, chunk);
            }
        }

        private static void UploadMap(CPUMapManager.ChunkMapInfo map) {
            if (map.HasGraphicsMap) return;
            var gpu = GraphicsGeneration;
            gpu.SetBufferData(gpu.Work.Transfer, map.ChunkData, map.Offset, 0, map.Length);
        }

        private void IdentifyCandidates(int biomeStart, int3 CCoord) {
            var gpu = GraphicsGeneration;
            if (bufferOffsets.bufferEnd > gpu.Work.Scratch.count)
                throw new InvalidOperationException("Entity candidate scratch buffer is too small.");
            gpu.Work.ClearRange(gpu.Work.Scratch, bufferOffsets.counterEnd - bufferOffsets.bufferStart, bufferOffsets.bufferStart);
            gpu.SetInts(identifier, "CCoord", new[] { CCoord.x, CCoord.y, CCoord.z });
            gpu.SetInt(identifier, "bSTART_biome", biomeStart);
            int kernel = identifier.FindKernel("Identify");
            identifier.GetKernelThreadGroupSizes(kernel, out uint threads, out _, out _);
            int groups = (chunkSize + (int)threads - 1) / (int)threads;
            gpu.Dispatch(identifier, kernel, groups, groups, groups);

            kernel = identifier.FindKernel("PrepareAllocation");
            gpu.Dispatch(identifier, kernel, 1, 1, 1);
            LeftoverEntities = gpu.Memory.AllocateMemory(gpu.Work.Scratch, EntityGenOffsets.candidateStride, bufferOffsets.allocationCounter);
            if (LeftoverEntities == 0) throw new InvalidOperationException("Could not allocate entity candidates.");
            kernel = BindCandidates("InitializeCandidates");
            var args = gpu.Args.CountToArgs(identifier, gpu.Work.Scratch, bufferOffsets.allocationCounter, kernel: kernel);
            gpu.DispatchIndirect(identifier, kernel, args);
            pendingMaps = AllRegions;
        }

        private void CheckRegion(int region, AsyncGenInfoReadback output, int outputRegion, CPUMapManager.ChunkMapInfo map) {
            var gpu = GraphicsGeneration;
            int kernel = BindCandidates("LoadCount");
            gpu.Dispatch(identifier, kernel, 1, 1, 1);
            kernel = BindCandidates(region == 0 ? "Prune" : "AddNeighbor");
            ComputeBuffer source = gpu.Work.Transfer;
            int sourceOffset = 0;
            if (map.HasGraphicsMap) map.Generation.Pipeline.GetMap(out source, out sourceOffset);
            gpu.SetBuffer(identifier, kernel, "NeighborMap", source);
            gpu.SetInt(identifier, "NeighborMapStart", sourceOffset);
            gpu.SetInt(identifier, "NeighborIndex", region);
            int3 offset = RegionOffset(region);
            gpu.SetInts(identifier, "NeighborOffset", new[] { offset.x, offset.y, offset.z });
            int3 outputOffset = RegionOffset(outputRegion);
            gpu.SetInts(identifier, "OutputOffset", new[] { outputOffset.x, outputOffset.y, outputOffset.z });
            var args = gpu.Args.CountToArgs(identifier, gpu.Work.Scratch, bufferOffsets.candidateCounter, kernel: kernel);
            gpu.DispatchIndirect(identifier, kernel, args);

            int address = output.AddGenPoints(gpu.Work.Scratch, bufferOffsets.acceptedCounter, bufferOffsets.allocationCounter);
            kernel = transcriber.FindKernel("CSMain");
            gpu.SetBuffer(transcriber, kernel, "_MemoryBuffer", gpu.Memory.GetBlockBuffer(address));
            gpu.SetInt(transcriber, "addressIndex", address);
            args = gpu.Args.CountToArgs(transcriber, gpu.Work.Scratch, bufferOffsets.acceptedCounter, kernel: kernel);
            gpu.DispatchIndirect(transcriber, kernel, args);

            pendingMaps &= (byte)~(1 << region);
            if (pendingMaps == 0) ReleaseAllocation();
        }

        private int BindCandidates(string name) {
            int kernel = identifier.FindKernel(name);
            var gpu = GraphicsGeneration;
            gpu.SetBuffer(identifier, kernel, "Candidates", gpu.Memory.GetBlockBuffer(LeftoverEntities));
            gpu.SetInt(identifier, "CandidateAddress", (int)LeftoverEntities);
            return kernel;
        }

        private static int3 RegionOffset(int region) => new(region >> 2, (region >> 1) & 1, region & 1);

        private void ReleaseAllocation() {
            if (LeftoverEntities != 0) GraphicsGeneration.Memory.ReleaseMemory(LeftoverEntities);
            LeftoverEntities = 0;
        }

        public void Release(int3 CCoord) {
            if (CPUMapManager.TryGetChunkInfo(CCoord, out var chunk) && ReferenceEquals(chunk.Generation.Entities, this))
                chunk.Generation.Entities = null;
            pendingMaps = 0;
            ReleaseAllocation();
        }

        public struct EntityGenOffsets : BufferOffsets {
            public int candidateCounter;
            public int acceptedCounter;
            public int allocationCounter;
            public int counterEnd;
            public int candidateStart;
            public int acceptedStart;
            private int offsetStart; private int offsetEnd;
            public int bufferStart { get { return offsetStart; } }
            public int bufferEnd { get { return offsetEnd; } }
            // Persistent candidates add a region mask and accumulated any-test state to GenPoint.
            private const int StateWords = 2;
            public static int candidateStride => GenPoint.size + StateWords;

            public EntityGenOffsets(int bufferStart, int numPoints, int entityStride) {
                offsetStart = bufferStart;
                candidateCounter = bufferStart;
                acceptedCounter = candidateCounter + 1;
                allocationCounter = acceptedCounter + 1;
                counterEnd = allocationCounter + 1;
                candidateStart = Mathf.CeilToInt(counterEnd / (float)entityStride);
                acceptedStart = candidateStart + numPoints;
                offsetEnd = (acceptedStart + numPoints) * entityStride;
            }
        }

    }
}
