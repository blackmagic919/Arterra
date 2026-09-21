using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Arterra.Engine.Terrain.Readback;
using static Arterra.Engine.Terrain.Readback.IVertFormat;
using Arterra.Configuration;
using System.Collections.Generic;
using Arterra.Data.Material;
using Arterra.Utils;
using Arterra.Core.Storage;
using static Arterra.Core.Storage.SharedResourceManager;

public class RegionReconstructor{
    private static ComputeShader MarchRegion;
    private static RegionOffsets bufferOffsets;
    private static Material EditTerrainMat;
    private static Material EditLiquidMat;
    private AsyncMeshReadback meshHandler;
    private GameObject regionObj;

    public static void PresetData() {
        GraphicsResourceContext gpuContext = GraphicsGeneration;
        MarchRegion = Resources.Load<ComputeShader>("Compute/CGeometry/RegionMarch/MarchingCubes");
        bufferOffsets = new RegionOffsets(Config.CURRENT.Quality.Terrain.value.mapChunkSize/2, 0);
        Arterra.Engine.Terrain.Map.Creator.GeoGenOffsets wOffsets = Arterra.Engine.Terrain.Map.Creator.bufferOffsets;
        gpuContext.SetBuffer(MarchRegion, 0, "MapData", gpuContext.Work.Transfer);
        gpuContext.SetBuffer(MarchRegion, 0, "MapFlags", gpuContext.Work.Transfer);
        gpuContext.SetBuffer(MarchRegion, 0, "vertexes", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(MarchRegion, 0, "triangles", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(MarchRegion, 0, "triangleDict", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(MarchRegion, 0, "counter", gpuContext.Work.Scratch);
        gpuContext.SetInts(MarchRegion, "counterInd", new int[3]{wOffsets.vertexCounter, wOffsets.baseTriCounter, wOffsets.waterTriCounter});
        gpuContext.SetInt(MarchRegion, "meshSkipInc", 1); //we are only dealing with same size chunks in this model

        gpuContext.SetInt(MarchRegion, "bSTART_map", bufferOffsets.regionMapStart);
        gpuContext.SetInt(MarchRegion, "bSTART_flags", bufferOffsets.mapFlagStart);
        gpuContext.SetInt(MarchRegion, "bSTART_dict", wOffsets.dictStart);
        gpuContext.SetInt(MarchRegion, "bSTART_verts", wOffsets.vertStart);
        gpuContext.SetInt(MarchRegion, "bSTART_baseT", wOffsets.baseTriStart);
        gpuContext.SetInt(MarchRegion, "bSTART_waterT", wOffsets.waterTriStart);

        if(EditTerrainMat == null) EditTerrainMat = CoreUtils.CreateEngineMaterial("Unlit/EditTerrain");
        if (EditLiquidMat == null) EditLiquidMat = CoreUtils.CreateEngineMaterial("Unlit/EditLiquid");

    }

    public RegionReconstructor() {
        regionObj = new GameObject("EditRegion");
    }

    public void Release() {
        meshHandler?.ReleaseAllGeometry();
        GameObject.Destroy(regionObj);
    }

    public void ReflectMesh(List<MapSamplePoint> change, int3 GCoord, int3 rot) {
        GraphicsResourceContext gpuContext = GraphicsGeneration;
        int3 cMin; int3 cMax; (cMin, cMax) = FindModificationMinMax(change, GCoord, rot);
        int3 cAxis = cMax - cMin; MapData[] map = CaptureBaseMap(cMin, cMax);
        uint[] flags = ApplyModificationToMap(change, map, GCoord, rot, cMax, cMin);
        gpuContext.SetBufferData(gpuContext.Work.Transfer, map, 0, bufferOffsets.regionMapStart, map.Length);
        gpuContext.SetBufferData(gpuContext.Work.Transfer, flags, 0, bufferOffsets.mapFlagStart, flags.Length);

        Arterra.Configuration.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain;
        regionObj.transform.localScale = Vector3.one * rSettings.lerpScale;
        regionObj.transform.position = CPUMapManager.GSToWS(cMin);

        GenerateRegionMesh(cAxis, rSettings.IsoLevel);
        meshHandler?.ReleaseAllGeometry();
        Bounds boundsOS = new((float3)cAxis / 2.0f, (float3)cAxis);
        meshHandler = new AsyncMeshReadback(regionObj.transform, boundsOS);
        var wOffsets = Arterra.Engine.Terrain.Map.Creator.bufferOffsets;
        meshHandler.OffloadVerticesToGPU(wOffsets.vertexCounter);
        meshHandler.OffloadTrisToGPUNoRender(wOffsets.baseTriCounter, wOffsets.baseTriStart, (int)ReadbackMaterial.terrain);
        meshHandler.OffloadTrisToGPUNoRender(wOffsets.waterTriCounter, wOffsets.waterTriStart, (int)ReadbackMaterial.water);
        meshHandler.CreateRenderParamsForMaterial(wOffsets.baseTriCounter, (int)ReadbackMaterial.terrain, EditTerrainMat);
        meshHandler.CreateRenderParamsForMaterial(wOffsets.waterTriCounter, (int)ReadbackMaterial.water, EditLiquidMat);
    }

    public void ReflectChunk(MapData[] map, int3 cSize, int3 GCoord) {
        GraphicsResourceContext gpuContext = GraphicsGeneration;
        map = CustomUtility.RescaleLinearMap(map, cSize, 2, 1); //Rescale so edges are gas
        cSize += 2;

        gpuContext.SetBufferData(gpuContext.Work.Transfer, map, 0, bufferOffsets.regionMapStart, map.Length);
        Arterra.Configuration.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain;
        GenerateRegionMesh(cSize, rSettings.IsoLevel);
        meshHandler?.ReleaseAllGeometry();

        regionObj.transform.localScale = Vector3.one * rSettings.lerpScale;
        regionObj.transform.position = CPUMapManager.GSToWS(GCoord);

        Bounds boundsOS = new((float3)cSize / 2.0f, (float3)cSize);
        meshHandler = new AsyncMeshReadback(regionObj.transform, boundsOS);
        var wOffsets = Arterra.Engine.Terrain.Map.Creator.bufferOffsets;
        meshHandler.OffloadVerticesToGPU(wOffsets.vertexCounter);
        meshHandler.OffloadTrisToGPU(wOffsets.baseTriCounter, wOffsets.baseTriStart, (int)ReadbackMaterial.terrain);
        meshHandler.OffloadTrisToGPU(wOffsets.waterTriCounter, wOffsets.waterTriStart, (int)ReadbackMaterial.water);
    }

    public void BeginMeshReadback(Action<ReadbackTask<TVert>.SharedMeshInfo> cb) {
        if (meshHandler == null) return;
        meshHandler.BeginMeshReadback(cb);
    }

    private static (int3, int3) FindModificationMinMax(List<MapSamplePoint> change, int3 GCoord, int3 rot) {
        int3 min = int3.zero; int3 max = int3.zero;
        foreach (MapSamplePoint pt in change) {
            min = math.min(pt.Offset, min);
            max = math.max(pt.Offset, max);
        }
        int3 c1 = GCoord + math.mul(CustomUtility.RotationLookupTable[rot.y, rot.x, rot.z], min);
        int3 c2 = GCoord + math.mul(CustomUtility.RotationLookupTable[rot.y, rot.x, rot.z], max);
        return (math.min(c1, c2) - 1, math.max(c1, c2) + 1);
    }

    private static MapData[] CaptureBaseMap(int3 cMin, int3 cMax) {
        int3 cAxis = cMax - cMin + 1; int3 coord;
        MapData[] map = new MapData[cAxis.x * cAxis.y * cAxis.z];
        for (coord.x = cMin.x + 1; coord.x < cMax.x - 1; coord.x++) {
            for (coord.y = cMin.y + 1; coord.y < cMax.y - 1; coord.y++) {
                for (coord.z = cMin.z + 1; coord.z < cMax.z - 1; coord.z++) {
                    int index = CustomUtility.irregularIndexFromCoord(coord - cMin, cAxis.yz);
                    map[index] = CPUMapManager.SampleMap(coord);
                } } }
        return map;
    }

    private static uint[] ApplyModificationToMap(List<MapSamplePoint> change, MapData[] map, int3 GCoord, int3 rot, int3 cMax, int3 cMin) {
        bool any0 = false;
        int3 cAxis = cMax - cMin + 1;
        int numPoints = Mathf.CeilToInt(cAxis.x * cAxis.y * cAxis.z / 4.0f);
        uint[] flags = new uint[numPoints];
        foreach (MapSamplePoint pt in change) {
            int3 sCoord = GCoord + math.mul(CustomUtility.RotationLookupTable[rot.y, rot.x, rot.z], pt.Offset);
            int index = CustomUtility.irregularIndexFromCoord(sCoord - cMin, cAxis.yz);
            if (pt.check.OrFlag) {
                if (any0) continue;
                else any0 = true;
            }

            byte flag = 0;
            MapData sample = map[index];
            uint targetLiquid = pt.check.bounds.MinLiquid;
            uint targetSolid = pt.check.bounds.MinSolid + (pt.check.bounds.MaxSolid - pt.check.bounds.MinSolid) / 2;
            if (pt.HasMaterialCheck && !sample.IsGaseous && pt.material != sample.material)
                flag |= 0x2;
            else if (pt.check.bounds.MaxSolid < sample.SolidDensity || pt.check.bounds.MaxLiquid < sample.LiquidDensity)
                flag |= 0x2;
            else if (sample.SolidDensity < targetSolid || sample.LiquidDensity < targetLiquid) {
                flag |= 0x1;
                int placeMaterial = pt.HasMaterialCheck ? (int)pt.material : sample.material;
                map[index] = new MapData {
                    material = placeMaterial,
                    viscosity = (int)targetSolid,
                    density = (int)math.min(MapData.MaxDensity, targetSolid + targetLiquid)
                };
            };
            flags[index / 4] |= ((uint)flag & 0xFF) << ((index % 4) * 8);
        }
        return flags;
    }

    private static void GenerateRegionMesh(int3 regionSize, float IsoLevel) {
        GraphicsResourceContext gpuContext = GraphicsGeneration;
        gpuContext.Work.ClearRange(gpuContext.Work.Scratch, 3, 0);
        gpuContext.SetInts(MarchRegion, "numCubesPerAxis", new int[] { regionSize.x, regionSize.y, regionSize.z });
        gpuContext.SetFloat(MarchRegion, "IsoLevel", IsoLevel);

        MarchRegion.GetKernelThreadGroupSizes(0, out uint tGSx, out uint tGSy, out uint tGSz);
        int3 numThreadsPerAxis = (int3)math.ceil((float3)(regionSize+1) / new float3(tGSx, tGSy, tGSz));
        gpuContext.Dispatch(MarchRegion, 0, numThreadsPerAxis.x, numThreadsPerAxis.y, numThreadsPerAxis.z);
    }

    public struct RegionOffsets : BufferOffsets {
        public int regionMapStart;
        public int mapFlagStart;
        private int offsetStart; private int offsetEnd;
        /// <summary> The start of the buffer region that is used by the Regional Mesh generator.
        /// See <see cref="BufferOffsets.bufferStart"/> for more info. </summary>
        public int bufferStart{get{return offsetStart;}}
        /// <summary> The end of the buffer region that is used by the Regional Mesh generator.
        /// See <see cref="BufferOffsets.bufferEnd"/> for more info. </summary>
        public int bufferEnd{get{return offsetEnd;}}
        public RegionOffsets(int3 GridSize, int bufferStart) {
            int numOfPoints = (GridSize.x + 1) * (GridSize.y + 1) * (GridSize.z + 1);
            this.offsetStart = bufferStart;
            this.regionMapStart = bufferStart;
            this.mapFlagStart = regionMapStart + numOfPoints;
            this.offsetEnd = mapFlagStart + Mathf.CeilToInt(numOfPoints / 4.0f);
        }
    }
}