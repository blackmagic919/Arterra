#include "Assets/Scripts/TerrainGeneration/DensityManager/CCoordHash.hlsl"
#include "Assets/Scripts/TerrainGeneration/ComputeShaders/Includes/GetIndex.hlsl"
#include "Assets/Scripts/TerrainGeneration/DensityManager/BlendHelper.hlsl"

#ifndef MAP_SAMPLER
#define MAP_SAMPLER
StructuredBuffer<uint> _MemoryBuffer;
StructuredBuffer<uint2> _AddressDict;
int3 CCoord;

const static int POINT_STRIDE_4BYTE = 2;
#endif

int3 GetSampleCCoord(int3 coord){
    int3 dCC = int3(0, 0, 0);
    //Add 1 because 0 case
    dCC.x = clamp(sign(coord.x + 1) + sign(coord.x - (int)numPointsPerAxis), -1, 1);
    dCC.y = clamp(sign(coord.y + 1) + sign(coord.y - (int)numPointsPerAxis), -1, 1);
    dCC.z = clamp(sign(coord.z + 1) + sign(coord.z - (int)numPointsPerAxis), -1, 1);
    return dCC;
}

float GetNeighborDensity(int3 oCCoord, int3 dCC, int3 oCoord, int oMeshSkipInc){
    float density = 0;

    int3 nCCoord = oCCoord + dCC;
    uint chunkHash = HashCoord(nCCoord);
    uint2 chunkHandle = _AddressDict[chunkHash];
    if(chunkHandle.x != 0){ //If it is undefined, it's ok to return 0 because it's probably at edge of world

        float oToNChunk = (oMeshSkipInc / (float)chunkHandle.y);
        float3 nCoord = abs(dCC * ((int)numPointsPerAxis-1) - oCoord) * oToNChunk;
        uint nPointsPerAxis = (uint)((numPointsPerAxis-1) * oToNChunk) + 1;
        Influences nMapInfo = GetBlendInfo(nCoord);

        [unroll] for(int i = 0; i < 8; i++){
            uint3 nMapCoord = min(nMapInfo.corner[i].mapCoord, nPointsPerAxis);
            uint address = indexFromCoordManual(nMapCoord, nPointsPerAxis) * POINT_STRIDE_4BYTE + chunkHandle.x;
            density += asfloat(_MemoryBuffer[address]) * nMapInfo.corner[i].influence;
        }
    }
    
    return density;
}

float SampleDensity(int3 coord) {
    float density = 0;

    int3 dCC = GetSampleCCoord(coord);
    uint chunkHash = HashCoord(CCoord);
    if(dCC.x == 0 && dCC.y == 0 && dCC.z == 0){ //Cur chunk
        uint address = indexFromCoord(coord) * POINT_STRIDE_4BYTE + _AddressDict[chunkHash].x;
        density = asfloat(_MemoryBuffer[address]);
    }
    else { //Needs to pull data out of chunk
        density = GetNeighborDensity(CCoord, dCC, coord, _AddressDict[chunkHash].y);
    }

	return density;
}

float3 CalculateNormal(int3 coord) {
	int3 offsetX = int3(1, 0, 0);
	int3 offsetY = int3(0, 1, 0);
	int3 offsetZ = int3(0, 0, 1);

    //Someone fix this later(I tried it's pain)
    //Either expand 3D map by 2 overlap points(which will make everything else very hard)
    //Or get 6 edge planes from neighbors(also hard)

	float dx = SampleDensity(coord - offsetX) - SampleDensity(coord + offsetX);
	float dy = SampleDensity(coord - offsetY) - SampleDensity(coord + offsetY);
	float dz = SampleDensity(coord - offsetZ) - SampleDensity(coord + offsetZ);

	return float3(dx, dy, dz);
}

float3 GetVertexNormal(float3 p1, float3 p2, float interp){
    float3 p1Norm = CalculateNormal(p1);
    float3 p2Norm = CalculateNormal(p2);

    return p1Norm + interp*(p2Norm - p1Norm);
}