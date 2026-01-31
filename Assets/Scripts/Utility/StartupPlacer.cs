using Unity.Mathematics;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Data.Entity;
using Arterra.Core.Storage;

namespace Arterra.Utils {
    public static class StartupPlacer {
        private static ComputeShader SurfaceFinder;
        private static ComputeShader WeightedPlacement;

        static StartupPlacer() {
            SurfaceFinder = Resources.Load<ComputeShader>("Compute/StartupPlacement/SurfaceFinder");
            WeightedPlacement = Resources.Load<ComputeShader>("Compute/StartupPlacement/WeightedPlacement");
        }
        public static void Initialize() {
            Arterra.Configuration.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain;
            Arterra.Data.Generation.Surface surface = Config.CURRENT.Generation.Surface.value;
            SurfaceFinder.SetInt("continentalSampler", surface.ContinentalIndex);
            SurfaceFinder.SetInt("majorWarpSampler", surface.MajorWarpIndex);
            SurfaceFinder.SetInt("minorWarpSampler", surface.MinorWarpIndex);
            SurfaceFinder.SetInt("erosionSampler", surface.ErosionIndex);
            SurfaceFinder.SetInt("squashSampler", surface.SquashIndex);
            SurfaceFinder.SetFloat("maxTerrainHeight", surface.MaxTerrainHeight);
            SurfaceFinder.SetFloat("squashHeight", surface.MaxSquashHeight);
            SurfaceFinder.SetFloat("heightOffset", surface.terrainOffset);

            int kernel = SurfaceFinder.FindKernel("FindSurface");
            SurfaceFinder.SetBuffer(kernel, "Result", UtilityBuffers.TransferBuffer);
            SurfaceFinder.SetInt("bSTART", 0);

            WeightedPlacement.SetInt("SearchRadius", rSettings.viewDistUpdate);
            WeightedPlacement.SetInt("ProfileEntity", Config.CURRENT.Generation.Entities.RetrieveIndex("Player"));
            WeightedPlacement.SetInt("mapChunkSize", rSettings.mapChunkSize);
            WeightedPlacement.SetInt("numPointsPerAxis", rSettings.mapChunkSize);
            WeightedPlacement.SetInt("bSTART", 0);
            WeightedPlacement.SetInt("bLOCK", 3);

            kernel = WeightedPlacement.FindKernel("WeightedPlace");
            WeightedPlacement.SetBuffer(kernel, "_AddressDict", GPUMapManager.Address);
            WeightedPlacement.SetBuffer(kernel, "_MemoryBuffer", GPUMapManager.Storage);
            WeightedPlacement.SetBuffer(kernel, "Result", UtilityBuffers.TransferBuffer);
            WeightedPlacement.SetBuffer(kernel, "Lock", UtilityBuffers.TransferBuffer);
            kernel = WeightedPlacement.FindKernel("FindSmallest");
            WeightedPlacement.SetBuffer(kernel, "Result", UtilityBuffers.TransferBuffer);
            WeightedPlacement.SetBuffer(kernel, "Lock", UtilityBuffers.TransferBuffer);
        }

        public static void PlaceOnSurface(Entity entity) {
            SurfaceFinder.SetFloats("startPosXZ", new float[] { entity.position.x, entity.position.z });
            int kernel = SurfaceFinder.FindKernel("FindSurface");
            SurfaceFinder.Dispatch(kernel, 1, 1, 1);

            float[] height = new float[1];
            UtilityBuffers.TransferBuffer.GetData(height, 0, 0, 1);
            entity.position = new float3(entity.position.x, height[0], entity.position.z);
        }

        public static void MoveToClearing(Entity entity) {
            //Setup Lock Value
            Arterra.Configuration.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain;
            UtilityBuffers.TransferBuffer.SetData(new uint[] { uint.MaxValue }, 0, 3, 1);

            int3 center = (int3)math.round(entity.position);
            WeightedPlacement.SetInts("SearchCenter", new int[] { center.x, center.y, center.z });
            int kernel = WeightedPlacement.FindKernel("WeightedPlace");
            WeightedPlacement.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
            int numThreadsAxis = Mathf.CeilToInt(rSettings.viewDistUpdate * 2 / (float)threadGroupSize);
            WeightedPlacement.Dispatch(kernel, numThreadsAxis, numThreadsAxis, numThreadsAxis);

            kernel = WeightedPlacement.FindKernel("FindSmallest");
            WeightedPlacement.GetKernelThreadGroupSizes(kernel, out threadGroupSize, out _, out _);
            numThreadsAxis = Mathf.CeilToInt(rSettings.viewDistUpdate * 2 / (float)threadGroupSize);
            WeightedPlacement.Dispatch(kernel, numThreadsAxis, numThreadsAxis, numThreadsAxis);

            int[] position = new int[4];
            UtilityBuffers.TransferBuffer.GetData(position, 0, 0, 4);
            int3 deltaPos = new(position[0], position[1], position[2]);
            deltaPos = math.clamp(deltaPos, -rSettings.viewDistUpdate, rSettings.viewDistUpdate);
            Debug.Log(deltaPos);
            entity.position += deltaPos;
        }
    }
}
