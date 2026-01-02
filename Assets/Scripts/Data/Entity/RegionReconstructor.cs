using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Arterra.Core.Terrain.Readback;
using static Arterra.Core.Terrain.Readback.IVertFormat;
using Arterra.Configuration;
using System.Collections.Generic;
using Arterra.Configuration.Generation.Material;
using Utils;
using Arterra.Core.Storage;

public class RegionReconstructor{
    private static ComputeShader MarchRegion;
    private static RegionOffsets bufferOffsets;
    private static Material EditTerrainMat;
    private static Material EditLiquidMat;
    private AsyncMeshReadback meshHandler;
    private GameObject regionObj;

    public static void PresetData() {
        MarchRegion = Resources.Load<ComputeShader>("Compute/CGeometry/RegionMarch/MarchingCubes");
        bufferOffsets = new RegionOffsets(Config.CURRENT.Quality.Terrain.value.mapChunkSize/2, 0);
        Arterra.Core.Terrain.Map.Generator.GeoGenOffsets wOffsets = Arterra.Core.Terrain.Map.Generator.bufferOffsets;
        MarchRegion.SetBuffer(0, "MapData", UtilityBuffers.TransferBuffer);
        MarchRegion.SetBuffer(0, "MapFlags", UtilityBuffers.TransferBuffer);
        MarchRegion.SetBuffer(0, "vertexes", UtilityBuffers.GenerationBuffer);
        MarchRegion.SetBuffer(0, "triangles", UtilityBuffers.GenerationBuffer);
        MarchRegion.SetBuffer(0, "triangleDict", UtilityBuffers.GenerationBuffer);
        MarchRegion.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        MarchRegion.SetInts("counterInd", new int[3]{wOffsets.vertexCounter, wOffsets.baseTriCounter, wOffsets.waterTriCounter});
        MarchRegion.SetInt("meshSkipInc", 1); //we are only dealing with same size chunks in this model

        MarchRegion.SetInt("bSTART_map", bufferOffsets.regionMapStart);
        MarchRegion.SetInt("bSTART_flags", bufferOffsets.mapFlagStart);
        MarchRegion.SetInt("bSTART_dict", wOffsets.dictStart);
        MarchRegion.SetInt("bSTART_verts", wOffsets.vertStart);
        MarchRegion.SetInt("bSTART_baseT", wOffsets.baseTriStart);
        MarchRegion.SetInt("bSTART_waterT", wOffsets.waterTriStart);

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

    public void ReflectMesh(List<ConditionedGrowthMat.MapSamplePoint> change, int3 GCoord, int3 rot) {
        int3 cMin; int3 cMax; (cMin, cMax) = FindModificationMinMax(change, GCoord, rot);
        int3 cAxis = cMax - cMin; MapData[] map = CaptureBaseMap(cMin, cMax);
        uint[] flags = ApplyModificationToMap(change, map, GCoord, rot, cMax, cMin);
        UtilityBuffers.TransferBuffer.SetData(map, 0, bufferOffsets.regionMapStart, map.Length);
        UtilityBuffers.TransferBuffer.SetData(flags, 0, bufferOffsets.mapFlagStart, flags.Length);
        
        Arterra.Configuration.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain;
        regionObj.transform.localScale = Vector3.one * rSettings.lerpScale;
        regionObj.transform.position = CPUMapManager.GSToWS(cMin);
        
        GenerateRegionMesh(cAxis, rSettings.IsoLevel);
        meshHandler?.ReleaseAllGeometry();
        Bounds boundsOS = new((float3)cAxis / 2.0f, (float3)cAxis);
        meshHandler = new AsyncMeshReadback(regionObj.transform, boundsOS);
        var wOffsets = Arterra.Core.Terrain.Map.Generator.bufferOffsets;
        meshHandler.OffloadVerticesToGPU(wOffsets.vertexCounter);
        meshHandler.OffloadTrisToGPUNoRender(wOffsets.baseTriCounter, wOffsets.baseTriStart, (int)ReadbackMaterial.terrain);
        meshHandler.OffloadTrisToGPUNoRender(wOffsets.waterTriCounter, wOffsets.waterTriStart, (int)ReadbackMaterial.water);
        meshHandler.CreateRenderParamsForMaterial(wOffsets.baseTriCounter, (int)ReadbackMaterial.terrain, EditTerrainMat);
        meshHandler.CreateRenderParamsForMaterial(wOffsets.waterTriCounter, (int)ReadbackMaterial.water, EditLiquidMat);
    }

    public void ReflectChunk(MapData[] map, int3 cSize, int3 GCoord) {
        map = CustomUtility.RescaleLinearMap(map, cSize, 2, 1); //Rescale so edges are gas
        cSize += 2;
        
        UtilityBuffers.TransferBuffer.SetData(map, 0, bufferOffsets.regionMapStart, map.Length);
        Arterra.Configuration.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain;
        GenerateRegionMesh(cSize, rSettings.IsoLevel);
        meshHandler?.ReleaseAllGeometry();

        regionObj.transform.localScale = Vector3.one * rSettings.lerpScale;
        regionObj.transform.position = CPUMapManager.GSToWS(GCoord);
        
        Bounds boundsOS = new((float3)cSize / 2.0f, (float3)cSize);
        meshHandler = new AsyncMeshReadback(regionObj.transform, boundsOS);
        var wOffsets = Arterra.Core.Terrain.Map.Generator.bufferOffsets;
        meshHandler.OffloadVerticesToGPU(wOffsets.vertexCounter);
        meshHandler.OffloadTrisToGPU(wOffsets.baseTriCounter, wOffsets.baseTriStart, (int)ReadbackMaterial.terrain);
        meshHandler.OffloadTrisToGPU(wOffsets.waterTriCounter, wOffsets.waterTriStart, (int)ReadbackMaterial.water);
    }

    public void BeginMeshReadback(Action<ReadbackTask<TVert>.SharedMeshInfo> cb) {
        if (meshHandler == null) return;
        meshHandler.BeginMeshReadback(cb);
    }

    private static (int3, int3) FindModificationMinMax(List<ConditionedGrowthMat.MapSamplePoint> change, int3 GCoord, int3 rot) {
        int3 min = int3.zero; int3 max = int3.zero;
        foreach (ConditionedGrowthMat.MapSamplePoint pt in change) {
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

    private static uint[] ApplyModificationToMap(List<ConditionedGrowthMat.MapSamplePoint> change, MapData[] map, int3 GCoord, int3 rot, int3 cMax, int3 cMin) {
        bool any0 = false;
        int3 cAxis = cMax - cMin + 1;
        int numPoints = Mathf.CeilToInt(cAxis.x * cAxis.y * cAxis.z / 4.0f);
        uint[] flags = new uint[numPoints];
        foreach (ConditionedGrowthMat.MapSamplePoint pt in change) {
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
        UtilityBuffers.ClearRange(UtilityBuffers.GenerationBuffer, 3, 0);
        MarchRegion.SetInts("numCubesPerAxis", new int[] { regionSize.x, regionSize.y, regionSize.z });
        MarchRegion.SetFloat("IsoLevel", IsoLevel);

        MarchRegion.GetKernelThreadGroupSizes(0, out uint tGSx, out uint tGSy, out uint tGSz);
        int3 numThreadsPerAxis = (int3)math.ceil((float3)(regionSize+1) / new float3(tGSx, tGSy, tGSz));
        MarchRegion.Dispatch(0, numThreadsPerAxis.x, numThreadsPerAxis.y, numThreadsPerAxis.z);
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