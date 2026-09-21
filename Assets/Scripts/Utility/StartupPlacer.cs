using Unity.Mathematics;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Data.Entity;
using Arterra.Core.Storage;
using static Arterra.Core.Storage.SharedResourceManager;

namespace Arterra.Utils {
    public static class StartupPlacer {
        private static ComputeShader SurfaceFinder;
        private static ComputeShader WeightedPlacement;

        static StartupPlacer() {
            SurfaceFinder = Resources.Load<ComputeShader>("Compute/StartupPlacement/SurfaceFinder");
            WeightedPlacement = Resources.Load<ComputeShader>("Compute/StartupPlacement/WeightedPlacement");
        }
        public static void Initialize() {
            GraphicsResourceContext gpuContext = GraphicsGeneration;
            Arterra.Configuration.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain;
            Arterra.Data.Generation.Surface surface = Config.CURRENT.Generation.Surface.value;
            gpuContext.SetInt(SurfaceFinder, "continentalSampler", surface.ContinentalIndex);
            gpuContext.SetInt(SurfaceFinder, "majorWarpSampler", surface.MajorWarpIndex);
            gpuContext.SetInt(SurfaceFinder, "minorWarpSampler", surface.MinorWarpIndex);
            gpuContext.SetInt(SurfaceFinder, "erosionSampler", surface.ErosionIndex);
            gpuContext.SetInt(SurfaceFinder, "squashSampler", surface.SquashIndex);
            gpuContext.SetFloat(SurfaceFinder, "maxTerrainHeight", surface.MaxTerrainHeight);
            gpuContext.SetFloat(SurfaceFinder, "squashHeight", surface.MaxSquashHeight);
            gpuContext.SetFloat(SurfaceFinder, "heightOffset", surface.terrainOffset);

            int kernel = SurfaceFinder.FindKernel("FindSurface");
            gpuContext.SetBuffer(SurfaceFinder, kernel, "Result", gpuContext.Work.Transfer);
            gpuContext.SetInt(SurfaceFinder, "bSTART", 0);

            gpuContext.SetInt(WeightedPlacement, "SearchRadius", rSettings.viewDistUpdate);
            gpuContext.SetInt(WeightedPlacement, "ProfileEntity", Config.CURRENT.Generation.Entities.RetrieveIndex("Player"));
            gpuContext.SetInt(WeightedPlacement, "mapChunkSize", rSettings.mapChunkSize);
            gpuContext.SetInt(WeightedPlacement, "numPointsPerAxis", rSettings.mapChunkSize);
            gpuContext.SetInt(WeightedPlacement, "bSTART", 0);
            gpuContext.SetInt(WeightedPlacement, "bLOCK", 3);

            kernel = WeightedPlacement.FindKernel("WeightedPlace");
            gpuContext.SetBuffer(WeightedPlacement, kernel, "_AddressDict", GPUMapManager.Address);
            gpuContext.SetBuffer(WeightedPlacement, kernel, "_MemoryBuffer", GPUMapManager.Storage);
            gpuContext.SetBuffer(WeightedPlacement, kernel, "Result", gpuContext.Work.Transfer);
            gpuContext.SetBuffer(WeightedPlacement, kernel, "Lock", gpuContext.Work.Transfer);
            kernel = WeightedPlacement.FindKernel("FindSmallest");
            gpuContext.SetBuffer(WeightedPlacement, kernel, "Result", gpuContext.Work.Transfer);
            gpuContext.SetBuffer(WeightedPlacement, kernel, "Lock", gpuContext.Work.Transfer);
        }

        public static float3 FindClearingAround(float3 startPos) {
            GraphicsResourceContext gpuContext = GraphicsGeneration;
            gpuContext.SetFloats(SurfaceFinder, "startPosXZ", new float[] { startPos.x, startPos.z });
            int kernel = SurfaceFinder.FindKernel("FindSurface");
            gpuContext.Dispatch(SurfaceFinder, kernel, 1, 1, 1);

            float[] height = new float[1];
            gpuContext.GetData(gpuContext.Work.Transfer, height, 0, 0, 1);
            startPos.y = height[0];
            return startPos;
        }

        public static void MoveToClearing(Entity entity) {
            GraphicsResourceContext gpuContext = GraphicsGeneration;
            //Setup Lock Value
            Arterra.Configuration.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain;
            gpuContext.SetBufferData(gpuContext.Work.Transfer, new uint[] { uint.MaxValue }, 0, 3, 1);

            int3 center = (int3)math.round(entity.position);
            gpuContext.SetInts(WeightedPlacement, "SearchCenter", new int[] { center.x, center.y, center.z });
            int kernel = WeightedPlacement.FindKernel("WeightedPlace");
            WeightedPlacement.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
            int numThreadsAxis = Mathf.CeilToInt(rSettings.viewDistUpdate * 2 / (float)threadGroupSize);
            gpuContext.Dispatch(WeightedPlacement, kernel, numThreadsAxis, numThreadsAxis, numThreadsAxis);

            kernel = WeightedPlacement.FindKernel("FindSmallest");
            WeightedPlacement.GetKernelThreadGroupSizes(kernel, out threadGroupSize, out _, out _);
            numThreadsAxis = Mathf.CeilToInt(rSettings.viewDistUpdate * 2 / (float)threadGroupSize);
            gpuContext.Dispatch(WeightedPlacement, kernel, numThreadsAxis, numThreadsAxis, numThreadsAxis);

            int[] position = new int[4];
            gpuContext.GetData(gpuContext.Work.Transfer, position, 0, 0, 4);
            int3 deltaPos = new(position[0], position[1], position[2]);
            deltaPos = math.clamp(deltaPos, -rSettings.viewDistUpdate, rSettings.viewDistUpdate);
            Debug.Log(deltaPos);
            entity.position += deltaPos;
        }
    }
}
