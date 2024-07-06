using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Unity.Mathematics;
using static EndlessTerrain;
using UnityEngine.Rendering;
using Utils;
using Unity.Collections.LowLevel.Unsafe;

//Benefits of unified chunk map memory
//1. No runtime allocation of native memory
//2. Generalization of map data access
//3. Accessibility of map for jobs(no managed memory)
//4. Cleaner management and writing to storage(disk/ssd)
public static class CPUDensityManager
{
    private static TerrainChunk[] _ChunkManagers;
    private static NativeArray<MapData> _SectionedMemory;
    private static NativeArray<ChunkMapInfo> _AddressDict;
    private static Stack<int> addressStack;
    private static int numChunksAxis;
    private static int numPoints;
    private static int mapChunkSize;
    private static float lerpScale;
    private static bool initialized = false;

    public static void Intiialize(){
        Release();
        RenderSettings rSettings = WorldStorageHandler.WORLD_OPTIONS.Rendering.value;
        mapChunkSize = rSettings.mapChunkSize;
        lerpScale = rSettings.lerpScale;

        int numPointsAxis = mapChunkSize;
        numPoints = numPointsAxis * numPointsAxis * numPointsAxis;

        numChunksAxis = 2 * Mathf.CeilToInt(rSettings.detailLevels.value[0].distanceThresh / mapChunkSize);
        int numChunks = (numChunksAxis+1) * (numChunksAxis+1) * (numChunksAxis+1);

        _ChunkManagers = new TerrainChunk[numChunks];
        _SectionedMemory = new NativeArray<MapData>((numChunks + 1) * numPoints, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _AddressDict = new NativeArray<ChunkMapInfo>(numChunks + 1, Allocator.Persistent, NativeArrayOptions.ClearMemory);

        addressStack = new Stack<int>();
        addressStack.Push(0);
        initialized = true;
    } 

    public static void Release(){
        if(!initialized) return;
        initialized = false;
        
        SaveAllChunksSync();
        _SectionedMemory.Dispose();
        _AddressDict.Dispose();
    }

    static void SaveAllChunksSync(){
        for(int i = 0; i < _ChunkManagers.Length; i++){
            if(_AddressDict[i].isDirty) ChunkStorageManager.SaveChunkToBinSync(_SectionedMemory, (int)_AddressDict[i].address, _ChunkManagers[i].CCoord);
        }
    }

    public static int HashCoord(int3 CCoord){
        float xHash = CCoord.x < 0 ? numChunksAxis - (Mathf.Abs(CCoord.x) % numChunksAxis) : Mathf.Abs(CCoord.x) % numChunksAxis;
        float yHash = CCoord.y < 0 ? numChunksAxis - (Mathf.Abs(CCoord.y) % numChunksAxis) : Mathf.Abs(CCoord.y) % numChunksAxis;
        float zHash = CCoord.z < 0 ? numChunksAxis - (Mathf.Abs(CCoord.z) % numChunksAxis) : Mathf.Abs(CCoord.z) % numChunksAxis;

        int hash = ((int)xHash * numChunksAxis * numChunksAxis) + ((int)yHash * numChunksAxis) + (int)zHash;
        return hash;
    }

    public static void AllocateChunk(TerrainChunk chunk, int3 CCoord, ChunkStorageManager.OnWriteComplete OnReleaseComplete){
        int chunkHash = HashCoord(CCoord);

        int addressIndex = addressStack.Pop();
        if(addressStack.Count == 0) addressStack.Push(addressIndex + numPoints);

        ChunkMapInfo prevChunk = _AddressDict[chunkHash];
        ChunkMapInfo newChunk = new ChunkMapInfo
        {
            address = (uint)addressIndex,
            valid = true,
            isDirty = false
        };

        //Release Previous Chunk
        if(prevChunk.isDirty) ChunkStorageManager.SaveChunkToBin(_SectionedMemory, (int)prevChunk.address, _ChunkManagers[chunkHash].CCoord, OnReleaseComplete); //Write to disk
        else OnReleaseComplete(true);

        if(prevChunk.valid) addressStack.Push((int)prevChunk.address);

        _AddressDict[chunkHash] = newChunk;
        _ChunkManagers[chunkHash] = chunk;
    }

    public unsafe static NativeArray<MapData> AccessChunk(int3 CCoord){
        return AccessChunk(HashCoord(CCoord));
    }
    public unsafe static NativeArray<MapData> AccessChunk(int chunkHash){
        if(!_AddressDict[chunkHash].valid) return default(NativeArray<MapData>);

        //Unsafe slice of code
        MapData* memStart = ((MapData*)NativeArrayUnsafeUtility.GetUnsafePtr(_SectionedMemory)) + (int)_AddressDict[chunkHash].address;
        NativeArray<MapData> nativeSlice = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<MapData>((void *)memStart, numPoints, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS //Saftey handles don't exist in release mode
        NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref nativeSlice, AtomicSafetyHandle.GetTempMemoryHandle());
#endif


        return nativeSlice;
    }

    public static NativeSlice<MapData> AccessChunkSlice(int chunkHash){
        if(!_AddressDict[chunkHash].valid) return default(NativeSlice<MapData>);

        int address = (int)_AddressDict[chunkHash].address;
        return _SectionedMemory.Slice(address, numPoints);
    }

    //Terraforming
    static bool SphereIntersectsBox(Vector3 sphereCentre, float sphereRadius, Vector3 boxCentre, Vector3 boxSize)
    {
        float closestX = Mathf.Clamp(sphereCentre.x, boxCentre.x - boxSize.x / 2, boxCentre.x + boxSize.x / 2);
        float closestY = Mathf.Clamp(sphereCentre.y, boxCentre.y - boxSize.y / 2, boxCentre.y + boxSize.y / 2);
        float closestZ = Mathf.Clamp(sphereCentre.z, boxCentre.z - boxSize.z / 2, boxCentre.z + boxSize.z / 2);

        float dx = closestX - sphereCentre.x;
        float dy = closestY - sphereCentre.y;
        float dz = closestZ - sphereCentre.z;

        float sqrDstToBox = dx * dx + dy * dy + dz * dz;
        return sqrDstToBox < sphereRadius * sphereRadius;
    }

    public static void Terraform(Vector3 terraformPoint, float terraformRadius, Func<MapData, float, MapData> handleTerraform)
    {
        int CCCoordX = Mathf.RoundToInt(terraformPoint.x / (mapChunkSize*lerpScale));
        int CCCoordY = Mathf.RoundToInt(terraformPoint.y / (mapChunkSize*lerpScale));
        int CCCoordZ = Mathf.RoundToInt(terraformPoint.z / (mapChunkSize*lerpScale));

        int chunkTerraformRadius = Mathf.CeilToInt(terraformRadius / (mapChunkSize* lerpScale));
        for(int x = -chunkTerraformRadius; x <= chunkTerraformRadius; x++)
        {
            for (int y = -chunkTerraformRadius; y <= chunkTerraformRadius; y++)
            {
                for (int z = -chunkTerraformRadius; z <= chunkTerraformRadius; z++)
                {
                    int3 viewedCC = new int3(x + CCCoordX, y + CCCoordY, z + CCCoordZ);

                    if (!terrainChunkDict.ContainsKey(viewedCC))
                        continue;
                    //For some reason terraformRadius itself isn't updating all the chunks properly
                    if (SphereIntersectsBox(terraformPoint, (terraformRadius+1), CustomUtility.AsVector(viewedCC) * mapChunkSize * lerpScale, (mapChunkSize+1) * lerpScale * Vector3.one)) { 
                        if(_AddressDict[HashCoord(viewedCC)].valid) TerraformChunk(HashCoord(viewedCC), terraformPoint, terraformRadius, handleTerraform);
                    }
                }
            }
        }
    }

    public static void BeginMapReadback(int3 CCoord){ //Need a wrapper class to maintain reference to the native array
        int GPUChunkHash = GPUDensityManager.HashCoord(CCoord);
        int CPUChunkHash = HashCoord(CCoord);

        AsyncGPUReadback.Request(GPUDensityManager.AccessAddresses(), size: 8, offset: 8 * GPUChunkHash, ret => onChunkAddressRecieved(ret, CPUChunkHash));
    }

    static unsafe void onChunkAddressRecieved(AsyncGPUReadbackRequest request, int chunkHash){
        if(!initialized) return;
        uint2 memHandle = request.GetData<uint2>().ToArray()[0];
        ChunkMapInfo destChunk = _AddressDict[chunkHash];
        if(!destChunk.valid) return;

        int memAddress = (int)memHandle.x;
        int meshSkipInc = (int)memHandle.y;
        
        NativeArray<CPUDensityManager.MapData> dest = AccessChunk(chunkHash);
        AsyncGPUReadback.RequestIntoNativeArray(ref dest, GPUDensityManager.AccessStorage(), size: 4 * numPoints, offset: 4 * memAddress);

        /*Currently broken because safety checks operate on the object level (i.e. _SectionedMemory can only be read to one at a time)
        
        NativeSlice<TerrainChunk.MapData> dest = _SectionedMemory.Slice((int)destChunk.address, numPoints);
        AsyncGPUReadback.RequestIntoNativeSlice(ref dest, GPUDensityManager.AccessStorage(), size: 4 * numPoints, offset: 4 * memAddress);*/
    }

    public static void TerraformChunk(int chunkHash, Vector3 targetPosition, float terraformRadius, Func<MapData, float, MapData> handleTerraform)
    {
        ChunkMapInfo mapInfo = _AddressDict[chunkHash];
        TerrainChunk chunk = _ChunkManagers[chunkHash];
        int addressIndex = (int)mapInfo.address;

        Vector3 targetPointLocal = chunk.WorldToLocal(targetPosition);
        int closestX = Mathf.Max(0, Mathf.Min(Mathf.RoundToInt(targetPointLocal.x), mapChunkSize));
        int closestY = Mathf.Max(0, Mathf.Min(Mathf.RoundToInt(targetPointLocal.y), mapChunkSize));
        int closestZ = Mathf.Max(0, Mathf.Min(Mathf.RoundToInt(targetPointLocal.z), mapChunkSize));
        int localRadius = Mathf.CeilToInt((1.0f / lerpScale) * terraformRadius);

        for (int x = -localRadius; x <= localRadius; x++)
        {
            for (int y = -localRadius; y <= localRadius; y++)
            {
                for (int z = -localRadius; z <= localRadius; z++)
                {
                    int3 vertPosition = new(closestX + x, closestY + y, closestZ + z);
                    if (Mathf.Max(vertPosition.x, vertPosition.y, vertPosition.z) > mapChunkSize)
                        continue;
                    if (Mathf.Min(vertPosition.x, vertPosition.y, vertPosition.z) < 0)
                        continue;

                    int index = CustomUtility.indexFromCoord(vertPosition.x, vertPosition.y, vertPosition.z, mapChunkSize);

                    Vector3 dR = new Vector3(vertPosition.x, vertPosition.y, vertPosition.z) - targetPointLocal;
                    float sqrDistWS = lerpScale * (dR.x * dR.x + dR.y * dR.y + dR.z * dR.z);

                    float brushStrength = 1.0f - Mathf.InverseLerp(0, terraformRadius * terraformRadius, sqrDistWS);
                    _SectionedMemory[addressIndex + index] = handleTerraform(_SectionedMemory[addressIndex + index], brushStrength);
                }
            }
        }

        //Regenerate The Chunk
        if(mapInfo.valid) chunk.RecalculateChunkImmediate(addressIndex, ref _SectionedMemory);
        mapInfo.isDirty = true; _AddressDict[chunkHash] = mapInfo;
    }

    //RayCast

    //Algorithm here -> http://www.cse.yorku.ca/~amana/research/grid.pdf
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
            if(_AddressDict[chunkHash].valid)
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

        ChunkMapInfo mapInfo = _AddressDict[chunkHash];
        TerrainChunk chunk = _ChunkManagers[chunkHash];
        int addressIndex = (int)mapInfo.address;

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
            if(callback(_SectionedMemory[addressIndex + index])) { //If the Raycast Hits
                hitPoint = chunk.LocalToWorld(new Vector3(mCoord.x, mCoord.y, mCoord.z)); 
                return true; 
            }

            //Test adjacent points 
            int3 adjPts = new int3(mCoord.x + step.x, mCoord.y + step.y, mCoord.z + step.z);
            if(adjPts.x >= 0 && adjPts.x <= mapChunkSize){
                index = CustomUtility.indexFromCoord(adjPts.x, mCoord.y, mCoord.z, mapChunkSize); 
                if(callback(_SectionedMemory[addressIndex + index])) { hitPoint = chunk.LocalToWorld(new Vector3(adjPts.x, mCoord.y, mCoord.z)); return true; } 
            } if (adjPts.y >= 0 && adjPts.y <= mapChunkSize){
                index = CustomUtility.indexFromCoord(mCoord.x, adjPts.y, mCoord.z, mapChunkSize);
                if(callback(_SectionedMemory[addressIndex + index])) { hitPoint = chunk.LocalToWorld(new Vector3(mCoord.x, adjPts.y, mCoord.z)); return true; } 
            } if (adjPts.z >= 0 && adjPts.z <= mapChunkSize){
                index = CustomUtility.indexFromCoord(mCoord.x, mCoord.y, adjPts.z, mapChunkSize);
                if(callback(_SectionedMemory[addressIndex + index])) { hitPoint = chunk.LocalToWorld(new Vector3(mCoord.x, mCoord.y, adjPts.z)); return true; } 
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
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct MapData
    {
        public uint data;

        public bool isDirty{ 
            readonly get => (data & 0x80000000) != 0;
            //Should not edit, but some functions need to
            set => data = value ? data | 0x80000000 : data & 0x7FFFFFFF;
        }

        public int density
        {
            readonly get => (int)data & 0xFF;
            set => data = (data & 0xFFFFFF00) | ((uint)value & 0xFF) | 0x80000000;
        }

        public int viscosity
        {
            readonly get => (int)(data >> 8) & 0xFF;
            set => data = (data & 0xFFFF00FF) | (((uint)value & 0xFF) << 8) | 0x80000000;
        }

        public int material
        {
            readonly get => (int)((data >> 16) & 0x7FFF);
            set => data = (data & 0x8000FFFF) | (uint)((value & 0x7FFF) << 16) | 0x80000000;
        }
    }
    

    public struct ChunkMapInfo{
        public uint address;
        public bool valid;
        public bool isDirty;
    }
}
