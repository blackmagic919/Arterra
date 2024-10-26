using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Unity.Mathematics;
using static EndlessTerrain;
using UnityEngine.Rendering;
using Utils;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;
using UnityEditor;

//Benefits of unified chunk map memory
//1. No runtime allocation of native memory
//2. Generalization of map data access
//3. Accessibility of map for jobs(no managed memory)
//4. Cleaner management and writing to storage(disk/ssd)
public static class CPUDensityManager
{
    public static TerrainChunk[] _ChunkManagers;
    public static NativeArray<MapData> SectionedMemory;
    public static NativeArray<ChunkMapInfo> AddressDict;
    public static int numChunksAxis;
    private static int numPoints;
    private static int mapChunkSize;
    private static float lerpScale;
    private static uint IsoValue;
    private static bool initialized = false;

    public static void Initialize(){
        Release();
        RenderSettings rSettings = WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value;
        mapChunkSize = rSettings.mapChunkSize;
        lerpScale = rSettings.lerpScale;
        IsoValue = (uint)Math.Round(rSettings.IsoLevel * 255.0f);

        int numPointsAxis = mapChunkSize;
        numPoints = numPointsAxis * numPointsAxis * numPointsAxis;

        numChunksAxis = 2 * rSettings.detailLevels.value[0].chunkDistThresh;
        int numChunks = numChunksAxis * numChunksAxis * numChunksAxis;

        _ChunkManagers = new TerrainChunk[numChunks];
        SectionedMemory = new NativeArray<MapData>((numChunks + 1) * numPoints, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        AddressDict = new NativeArray<ChunkMapInfo>(numChunks + 1, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        initialized = true;
    } 

    public static void Release(){
        if(!initialized) return;
        initialized = false;
        
        SaveAllChunksSync();
        SectionedMemory.Dispose();
        AddressDict.Dispose();
    }

    static void SaveAllChunksSync(){
        for(int i = 0; i < _ChunkManagers.Length; i++){
            if(AddressDict[i].isDirty) ChunkStorageManager.SaveChunkToBinSync(SectionedMemory, i * numPoints, _ChunkManagers[i].CCoord);
        }
    }

    public static int HashCoord(int3 CCoord){
        int3 hashCoord = ((CCoord % numChunksAxis) + numChunksAxis) % numChunksAxis;
        int hash = (hashCoord.x * numChunksAxis * numChunksAxis) + (hashCoord.y * numChunksAxis) + hashCoord.z;
        return hash;
    }


    public static void AllocateChunk(TerrainChunk chunk, int3 CCoord, ChunkStorageManager.OnWriteComplete OnReleaseComplete){
        int chunkHash = HashCoord(CCoord);

        ChunkMapInfo prevChunk = AddressDict[chunkHash];
        ChunkMapInfo newChunk = new ChunkMapInfo
        {
            CCoord = CCoord,
            valid = true,
            isDirty = false
        };

        //Release Previous Chunk
        if(prevChunk.isDirty) ChunkStorageManager.SaveChunkToBin(SectionedMemory, chunkHash * numPoints, prevChunk.CCoord, OnReleaseComplete); //Write to disk
        else OnReleaseComplete(true);

        AddressDict[chunkHash] = newChunk;
        _ChunkManagers[chunkHash] = chunk;
    }

    public unsafe static NativeArray<MapData> AccessChunk(int3 CCoord){
        return AccessChunk(HashCoord(CCoord));
    }
    public unsafe static NativeArray<MapData> AccessChunk(int chunkHash){
        if(!AddressDict[chunkHash].valid) return default(NativeArray<MapData>);

        //Unsafe slice of code
        MapData* memStart = ((MapData*)NativeArrayUnsafeUtility.GetUnsafePtr(SectionedMemory)) + (chunkHash * numPoints);
        NativeArray<MapData> nativeSlice = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<MapData>((void *)memStart, numPoints, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS //Saftey handles don't exist in release mode
        NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref nativeSlice, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
        return nativeSlice;
    }

    public static NativeSlice<MapData> AccessChunkSlice(int chunkHash){
        if(!AddressDict[chunkHash].valid) return default(NativeSlice<MapData>);

        int address = chunkHash * numPoints;
        return SectionedMemory.Slice(address, numPoints);
    }

    //Algorithm here -> http://www.cse.yorku.ca/~amana/research/grid.pdf
    public static bool RayCastTerrain(float3 oGS, float3 rayDir, float rayLength, Func<int3, uint> callback, out float3 hit){
        int3 step = (int3)math.sign(rayDir);
        int3 GCoord = (int3)math.floor(oGS);
        int3 sPlane = math.max(step, 0);
        //If rayDir is negative, then GCoord is the next plane
        //Otherwise GCoord + sPlane is the next plane

        float3 tDelta = 1.0f / math.abs(rayDir); float3 tMax = tDelta;
        tMax.x *= rayDir.x >= 0 ? 1 - (oGS.x - GCoord.x) : (oGS.x - GCoord.x);
        tMax.y *= rayDir.y >= 0 ? 1 - (oGS.y - GCoord.y) : (oGS.y - GCoord.y);
        tMax.z *= rayDir.z >= 0 ? 1 - (oGS.z - GCoord.z) : (oGS.z - GCoord.z);

        uint density = 0;
        uint nDensity = density;
        hit = oGS; 
        do{
            if(tMax.x < tMax.y){
                if(tMax.x < tMax.z){
                    tMax.x += tDelta.x;
                    nDensity = GetRayPlaneIntersectionX(ref hit, rayDir, GCoord.x + sPlane.x, callback);
                    GCoord.x += step.x;
                } else {
                    tMax.z += tDelta.z;
                    nDensity = GetRayPlaneIntersectionZ(ref hit, rayDir, GCoord.z + sPlane.z, callback);
                    GCoord.z += step.z;
                }
            } else {
                if(tMax.y < tMax.z){
                    tMax.y += tDelta.y;
                    nDensity = GetRayPlaneIntersectionY(ref hit, rayDir, GCoord.y + sPlane.y, callback);
                    GCoord.y += step.y;
                } else {
                    tMax.z += tDelta.z;
                    nDensity = GetRayPlaneIntersectionZ(ref hit, rayDir, GCoord.z + sPlane.z, callback);
                    GCoord.z += step.z;
                }
            }

            if(nDensity >= IsoValue){ 
                float t = Mathf.InverseLerp(density, nDensity, IsoValue);
                hit = oGS * (1 - t) + hit * t;
                return true;
            }

            density = nDensity;
            oGS = hit;
        } while(Mathf.Min(tMax.x, tMax.y, tMax.z) < rayLength);
        return false;
    }

    public static uint GetRayPlaneIntersectionX(ref float3 rayOrigin, float3 rayDir, int XPlane, Func<int3, uint> SampleMap){
        float t = (XPlane - rayOrigin.x) / rayDir.x;
        rayOrigin = new (XPlane, rayOrigin.y + t * rayDir.y, rayOrigin.z + t * rayDir.z);
        return BilinearDensity(rayOrigin.y, rayOrigin.z, (int y, int z) => SampleMap(new int3(XPlane, y, z)));
    }

    public static uint GetRayPlaneIntersectionY(ref float3 rayOrigin, float3 rayDir, int YPlane, Func<int3, uint> SampleMap){
        float t = (YPlane - rayOrigin.y) / rayDir.y;
        rayOrigin = new (rayOrigin.x + t * rayDir.x, YPlane, rayOrigin.z + t * rayDir.z);
        return BilinearDensity(rayOrigin.x, rayOrigin.z, (int x, int z) => SampleMap(new int3(x, YPlane, z)));
    }

    public static uint GetRayPlaneIntersectionZ(ref float3 rayOrigin, float3 rayDir, int ZPlane, Func<int3, uint> SampleMap){
        float t = (ZPlane - rayOrigin.z) / rayDir.z;
        rayOrigin = new (rayOrigin.x + t * rayDir.x, rayOrigin.y + t * rayDir.y, ZPlane);
        return BilinearDensity(rayOrigin.x, rayOrigin.y, (int x, int y) => SampleMap(new int3(x, y, ZPlane)));
    }

    public static uint BilinearDensity(float x, float y, Func<int, int, uint> SampleMap){
        int x0 = (int)Math.Floor(x); int x1 = x0 + 1;
        int y0 = (int)Math.Floor(y); int y1 = y0 + 1;

        uint c00 = SampleMap(x0, y0);
        uint c10 = SampleMap(x1, y0);
        uint c01 = SampleMap(x0, y1);
        uint c11 = SampleMap(x1, y1);

        float xd = x - x0;
        float yd = y - y0;

        float c0 = c00 * (1 - xd) + c10 * xd;
        float c1 = c01 * (1 - xd) + c11 * xd;
        return (uint)Math.Round(c0 * (1 - yd) + c1 * yd);
    }

    //Terraforming
    public static void Terraform(float3 tPointGS, int terraformRadius, Func<MapData, float, MapData> handleTerraform)
    {
        int3 tPointGSInt = (int3)math.floor(tPointGS);

        for(int x = -terraformRadius; x <= terraformRadius + 1; x++)
        {
            for (int y = -terraformRadius; y <= terraformRadius + 1; y++)
            {
                for (int z = -terraformRadius; z <= terraformRadius + 1; z++)
                {
                    int3 GCoord = tPointGSInt + new int3(x, y, z);

                    //Calculate Brush Strength
                    float3 dR = GCoord - tPointGS;
                    float sqrDistWS = dR.x * dR.x + dR.y * dR.y + dR.z * dR.z;
                    float brushStrength = 1.0f - Mathf.InverseLerp(0, terraformRadius * terraformRadius, sqrDistWS);
                    SetMap(handleTerraform(SampleMap(GCoord), brushStrength), GCoord);
                    TerrainUpdateManager.AddUpdate(GCoord);
                }
            }
        }
    }

    public static void BeginMapReadback(int3 CCoord){ //Need a wrapper class to maintain reference to the native array
        int GPUChunkHash = GPUDensityManager.HashCoord(CCoord);
        int CPUChunkHash = HashCoord(CCoord);

        AsyncGPUReadback.Request(GPUDensityManager.Address, size: 8, offset: 8 * GPUChunkHash, ret => onChunkAddressRecieved(ret, CPUChunkHash));
    }

    static unsafe void onChunkAddressRecieved(AsyncGPUReadbackRequest request, int chunkHash){
        if(!initialized) return;
        uint2 memHandle = request.GetData<uint2>()[0];
        ChunkMapInfo destChunk = AddressDict[chunkHash];
        if(!destChunk.valid) return;

        int memAddress = (int)memHandle.x;
        int meshSkipInc = (int)memHandle.y;
        
        NativeArray<MapData> dest = AccessChunk(chunkHash);
        AsyncGPUReadback.RequestIntoNativeArray(ref dest, GPUDensityManager.Storage, size: 4 * numPoints, offset: 4 * memAddress);

        /*Currently broken because safety checks operate on the object level (i.e. SectionedMemory can only be read to one at a time)
        
        NativeSlice<TerrainChunk.MapData> dest = SectionedMemory.Slice((int)destChunk.address, numPoints);
        AsyncGPUReadback.RequestIntoNativeSlice(ref dest, GPUDensityManager.AccessStorage(), size: 4 * numPoints, offset: 4 * memAddress);*/
    }

    public static float3 WSToGS(float3 WSPos){return WSPos / lerpScale + mapChunkSize / 2;}
    public static float3 GSToWS(float3 GSPos){return (GSPos - mapChunkSize / 2) * lerpScale;}
    public static void SetMap(MapData data, int3 GCoord){
        int3 MCoord = ((GCoord % mapChunkSize) + mapChunkSize) % mapChunkSize;
        int3 CCoord = (GCoord - MCoord) / mapChunkSize;
        int3 HCoord = ((CCoord % numChunksAxis) + numChunksAxis) % numChunksAxis;

        int PIndex = MCoord.x * mapChunkSize * mapChunkSize + MCoord.y * mapChunkSize + MCoord.z;
        int CIndex = HCoord.x * numChunksAxis * numChunksAxis + HCoord.y * numChunksAxis + HCoord.z;
        ChunkMapInfo mapInfo = AddressDict[CIndex];
        //Not available(currently readingback) || Out of Bounds
        if(!mapInfo.valid || math.any(mapInfo.CCoord != CCoord)) 
            return;

        //Update Handles
        SectionedMemory[CIndex * numPoints + PIndex] = data;
        var chunkMapInfo = AddressDict[CIndex];
        chunkMapInfo.isDirty = true;
        AddressDict[CIndex] = chunkMapInfo;
        _ChunkManagers[CIndex].ReflectChunk();
    }
    public static MapData SampleMap(int3 GCoord){
        int3 MCoord = ((GCoord % mapChunkSize) + mapChunkSize) % mapChunkSize;
        int3 CCoord = (GCoord - MCoord) / mapChunkSize;
        int3 HCoord = ((CCoord % numChunksAxis) + numChunksAxis) % numChunksAxis;

        int PIndex = MCoord.x * mapChunkSize * mapChunkSize + MCoord.y * mapChunkSize + MCoord.z;
        int CIndex = HCoord.x * numChunksAxis * numChunksAxis + HCoord.y * numChunksAxis + HCoord.z;
        ChunkMapInfo mapInfo = AddressDict[CIndex];
        //Not available(currently readingback) || Out of Bounds
        if(!mapInfo.valid || math.any(mapInfo.CCoord != CCoord)) 
            return new MapData{data = 0xFFFFFFFF};

        return SectionedMemory[CIndex * numPoints + PIndex];
    }

    public static int SampleTerrain(int3 GCoord){
        MapData mapData = SampleMap(GCoord);
        return mapData.viscosity;
    }
    

    public struct ChunkMapInfo{
        public int3 CCoord;
        public bool valid;
        public bool isDirty;
    }

    public unsafe struct MapContext{
        [NativeDisableUnsafePtrRestriction]
        public MapData* MapData;
        [NativeDisableUnsafePtrRestriction]
        public ChunkMapInfo* AddressDict;
        public int mapChunkSize;
        public int numChunksAxis;
        public int IsoValue;
    }

    public static unsafe void ReflectChunkJob(TerrainChunk.ChunkStatus* status){
        status->SetMap = true;
        status->UpdateMesh = true;
    }

    [BurstCompile]
    public unsafe static MapData SampleMap(in int3 GCoord, in MapContext context){
        int3 MCoord = ((GCoord % context.mapChunkSize) + context.mapChunkSize) % context.mapChunkSize;
        int3 CCoord = (GCoord - MCoord) / context.mapChunkSize;
        int3 HCoord = ((CCoord % context.numChunksAxis) + context.numChunksAxis) % context.numChunksAxis;

        int PIndex = MCoord.x * context.mapChunkSize * context.mapChunkSize + MCoord.y * context.mapChunkSize + MCoord.z;
        int CIndex = HCoord.x * context.numChunksAxis * context.numChunksAxis + HCoord.y * context.numChunksAxis + HCoord.z;
        int numPoints = context.mapChunkSize * context.mapChunkSize * context.mapChunkSize;
        ChunkMapInfo mapInfo = context.AddressDict[CIndex];
        if(!mapInfo.valid) return new MapData{data = 4294967295};//Not available(currently readingback)
        if(math.any(mapInfo.CCoord != CCoord)) return new MapData{data = 4294967295}; //Out of bounds

        return context.MapData[CIndex * numPoints + PIndex];
    }

    [BurstCompile]
    public static int SampleTerrain(in int3 GCoord, in MapContext context){
        MapData mapData = SampleMap(GCoord, context);
        return mapData.viscosity;
    }

    [Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct MapData
    {
        [HideInInspector] public uint data;

        public bool isDirty{ 
            readonly get => (data & 0x80000000) != 0;
            //Should not edit, but some functions need to
            set => data = value ? data | 0x80000000 : data & 0x7FFFFFFF;
        }

        public int density
        {
            readonly get => (int)data & 0xFF;
            set{
                data = (data & 0xFFFF00FF) | ((uint)math.min(viscosity, value & 0xFF) << 8);
                data = (data & 0xFFFFFF00) | ((uint)value & 0xFF) | 0x80000000;
            }
        }

        public int viscosity
        {
            readonly get => (int)(data >> 8) & 0xFF;
            set{
                data = (data & 0xFFFFFF00) | ((uint)math.max(density, value & 0xFF));
                data = (data & 0xFFFF00FF) | (((uint)value  & 0xFF) << 8) | 0x80000000;
            }
        }

        public int material
        {
            readonly get => (int)((data >> 16) & 0x7FFF);
            set => data = (data & 0x8000FFFF) | (uint)((value & 0x7FFF) << 16) | 0x80000000;
        }

        public readonly int SolidDensity{
            get => viscosity;
        }
        public readonly int LiquidDensity{
            get => density - viscosity;
        }
        public readonly bool IsSolid{
            get => SolidDensity >= IsoValue;
        }
        public readonly bool IsLiquid{
            get => LiquidDensity >= IsoValue;
        }
        public readonly bool IsGaseous{
            get => !IsSolid && !IsLiquid;
        }
    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(MapData))]
    public class MapDataDrawer : PropertyDrawer{
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty dataProp = property.FindPropertyRelative("data");
            uint data = dataProp.uintValue;

            //bool isDirty = (data & 0x80000000) != 0;
            int[] values = new int[3]{
                (int)((data >> 16) & 0x7FFF),
                (int)((data >> 8) & 0xFF),
                (int)(data & 0xFF)
            };
            bool isDirty = (data & 0x80000000) != 0;

            Rect rect = new (position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            
            EditorGUI.MultiIntField(rect, new GUIContent[]{new ("Material"), new ("Viscosity"), new ("Density")}, values);
            rect.y += EditorGUIUtility.singleLineHeight;
            isDirty = EditorGUI.Toggle(rect, "Is Dirty", isDirty);
            rect.y += EditorGUIUtility.singleLineHeight;
            //isDirty = EditorGUI.Toggle(rect, "Is Dirty", isDirty);
            //rect.y += EditorGUIUtility.singleLineHeight;

            //data = (isDirty ? data | 0x80000000 : data & 0x7FFFFFFF);
            data = (data & 0x8000FFFF) | ((uint)values[0] << 16);
            data = (data & 0xFFFF00FF) | (((uint)values[1] & 0xFF) << 8);
            data = (data & 0xFFFFFF00) | ((uint)values[2] & 0xFF);
            data = isDirty ? data | 0x80000000 : data & 0x7FFFFFFF;

            dataProp.uintValue = data;
        }

        // Override this method to make space for the custom fields
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * 2;
        }
    }
#endif

    /*    
    public static bool RayCastTerrain(Vector3 rayOrigin, Vector3 rayDir, float rayLength, Func<MapData, bool> callback, out Vector3 hitPoint){
        hitPoint = Vector3.zero;

        Vector3 oCoord = rayOrigin / (mapChunkSize * lerpScale) + Vector3.one * 0.5f; //World to Coord Space
        int3 CCoord = new (Mathf.FloorToInt(oCoord.x), Mathf.FloorToInt(oCoord.y), Mathf.FloorToInt(oCoord.z));
        int3 step = new ((int)Mathf.Sign(rayDir.x), (int)Mathf.Sign(rayDir.y), (int)Mathf.Sign(rayDir.z));

        Vector3 tDelta = new (1.0f / Mathf.Abs(rayDir.x), 1.0f / Mathf.Abs(rayDir.y), 1.0f / Mathf.Abs(rayDir.z)); Vector3 tMax = tDelta;
        tMax.x *= rayDir.x >= 0 ? 1 - (oCoord.x - Mathf.Floor(oCoord.x)) : (oCoord.x - Mathf.Floor(oCoord.x));
        tMax.y *= rayDir.y >= 0 ? 1 - (oCoord.y - Mathf.Floor(oCoord.y)) : (oCoord.y - Mathf.Floor(oCoord.y));
        tMax.z *= rayDir.z >= 0 ? 1 - (oCoord.z - Mathf.Floor(oCoord.z)) : (oCoord.z - Mathf.Floor(oCoord.z));

        int chunkHash;
        do{
            chunkHash = HashCoord(CCoord);
            if(AddressDict[chunkHash].valid)
                if(RayCastChunk(chunkHash, rayOrigin, rayDir, rayLength, callback, out hitPoint)) return true; //if hits, return

            if(tMax.x < tMax.y){
                if(tMax.x < tMax.z){
                    tMax.x += tDelta.x;
                    CCoord.x += step.x;
                } else {
                    tMax.z += tDelta.z;
                    CCoord.z += step.z;
                }
            } else {
                if(tMax.y < tMax.z){
                    tMax.y += tDelta.y;
                    CCoord.y += step.y;
                } else {
                    tMax.z += tDelta.z;
                    CCoord.z += step.z;
                }
            }
            
        } while(Mathf.Min(tMax.x, tMax.y, tMax.z) < rayLength);

        return false;
    }


    public static bool RayCastChunk(int chunkHash, Vector3 rayOrigin, Vector3 rayDir, float rayLength, Func<MapData, bool> callback, out Vector3 hitPoint){
        hitPoint = Vector3.zero;

        ChunkMapInfo mapInfo = AddressDict[chunkHash];
        TerrainChunk chunk = _ChunkManagers[chunkHash];
        int addressIndex = chunkHash * numPoints;

        //Readback Unity Bug Here -> https://forum.unity.com/threads/asyncgpureadback-requestintonativearray-causes-invalidoperationexception-on-nativearray.1011955/
        if(!mapInfo.valid) return false;
        //Caller guarantees that ray intersects with chunk
        rayOrigin = chunk.WorldToLocal(rayOrigin);
        rayDir = chunk.WorldToLocalDir(rayDir);
        chunk.GetRayIntersect(new Ray(rayOrigin, rayDir), out float dist); //Direction is same as world space
        rayOrigin += rayDir * Mathf.Max(dist, 0); //Move to intersection point
        rayLength /= lerpScale; //Convert to local space

        //Ray origin is chunk aligned therefore positive so equivalent to floor
        int3 mCoord = new((int)rayOrigin.x, (int)rayOrigin.y, (int)rayOrigin.z); 
        int3 step = new ((int)Mathf.Sign(rayDir.x), (int)Mathf.Sign(rayDir.y), (int)Mathf.Sign(rayDir.z));
        Vector3 tDelta = new (1.0f / Mathf.Abs(rayDir.x), 1.0f / Mathf.Abs(rayDir.y), 1.0f / Mathf.Abs(rayDir.z)); Vector3 tMax = tDelta;
        tMax.x *= rayDir.x >= 0 ? 1 - (rayOrigin.x - Mathf.Floor(rayOrigin.x)) : (rayOrigin.x - Mathf.Floor(rayOrigin.x));
        tMax.y *= rayDir.y >= 0 ? 1 - (rayOrigin.y - Mathf.Floor(rayOrigin.y)) : (rayOrigin.y - Mathf.Floor(rayOrigin.y));
        tMax.z *= rayDir.z >= 0 ? 1 - (rayOrigin.z - Mathf.Floor(rayOrigin.z)) : (rayOrigin.z - Mathf.Floor(rayOrigin.z));

        do{
            //4 points, as that is enough to ensure all 8 points are hit(look at adapted marching cubes algo)
            int index = CustomUtility.indexFromCoord(mCoord.x, mCoord.y, mCoord.z, mapChunkSize); 
            if(callback(SectionedMemory[addressIndex + index])) { //If the Raycast Hits
                hitPoint = chunk.LocalToWorld(new Vector3(mCoord.x, mCoord.y, mCoord.z)); 
                return true; 
            }

            //Test adjacent points 
            int3 adjPts = new int3(mCoord.x + step.x, mCoord.y + step.y, mCoord.z + step.z);
            if(adjPts.x >= 0 && adjPts.x <= mapChunkSize){
                index = CustomUtility.indexFromCoord(adjPts.x, mCoord.y, mCoord.z, mapChunkSize); 
                if(callback(SectionedMemory[addressIndex + index])) { hitPoint = chunk.LocalToWorld(new Vector3(adjPts.x, mCoord.y, mCoord.z)); return true; } 
            } if (adjPts.y >= 0 && adjPts.y <= mapChunkSize){
                index = CustomUtility.indexFromCoord(mCoord.x, adjPts.y, mCoord.z, mapChunkSize);
                if(callback(SectionedMemory[addressIndex + index])) { hitPoint = chunk.LocalToWorld(new Vector3(mCoord.x, adjPts.y, mCoord.z)); return true; } 
            } if (adjPts.z >= 0 && adjPts.z <= mapChunkSize){
                index = CustomUtility.indexFromCoord(mCoord.x, mCoord.y, adjPts.z, mapChunkSize);
                if(callback(SectionedMemory[addressIndex + index])) { hitPoint = chunk.LocalToWorld(new Vector3(mCoord.x, mCoord.y, adjPts.z)); return true; } 
            }
            
            if(tMax.x < tMax.y){
                if(tMax.x < tMax.z){
                    tMax.x += tDelta.x;
                    mCoord.x += step.x;
                } else {
                    tMax.z += tDelta.z;
                    mCoord.z += step.z;
                }
            } else {
                if(tMax.y < tMax.z){
                    tMax.y += tDelta.y;
                    mCoord.y += step.y;
                } else {
                    tMax.z += tDelta.z;
                    mCoord.z += step.z;
                }
            }
            
            if(Vector3.Distance(new Vector3(mCoord.x, mCoord.y, mCoord.z), rayOrigin) + dist > rayLength) //exceed raycast distance
                return false;
        } while(Mathf.Min(mCoord.x, mCoord.y, mCoord.z) >= 0 && Mathf.Max(mCoord.x, mCoord.y, mCoord.z) <= mapChunkSize);

        return false;
    }*/
}
