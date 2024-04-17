#include "Assets/Resources/MapData/CCoordHash.hlsl"
#include "Assets/Resources/Utility/GetIndex.hlsl"

#ifndef MAP_SAMPLER
#define MAP_SAMPLER
StructuredBuffer<uint> _MemoryBuffer;
StructuredBuffer<uint2> _AddressDict;
int3 CCoord;
float meshSkipInc;

const static int POINT_STRIDE_4BYTE = 3;
#endif


int3 GetSampleCCoord(int3 coord){
    int3 dCC = int3(0, 0, 0);
    //Add 1 because 0 case
    dCC.x = sign(sign(coord.x + 1) + sign(coord.x - (int)numPointsPerAxis));
    dCC.y = sign(sign(coord.y + 1) + sign(coord.y - (int)numPointsPerAxis));
    dCC.z = sign(sign(coord.z + 1) + sign(coord.z - (int)numPointsPerAxis));
    return dCC;
}

//Water is 1, terrain is 0
float ReadBaseDensity(int3 coord, int3 CCoord, uint isWater){
    float density = 0;

    uint chunkHash = HashCoord(CCoord);
    uint2 chunkHandle = _AddressDict[chunkHash];
    if(chunkHandle.x != 0){
        int chunkResize = meshSkipInc / (float)chunkHandle.y;
        uint mapPtsPerAxis = (uint)((numPointsPerAxis-1) * chunkResize) + 1;
        uint address = indexFromCoordManual(coord * chunkResize, mapPtsPerAxis) * POINT_STRIDE_4BYTE + chunkHandle.x;

        density = asfloat(_MemoryBuffer[address]) * max(isWater, asfloat(_MemoryBuffer[address + 1]));
    }

    return density;
}

float SampleDensity(int3 coord, uint isWater) {
    int3 dCC = GetSampleCCoord(coord);
    int3 nCoord = abs(dCC * ((int)numPointsPerAxis-1) - coord);
    return ReadBaseDensity(nCoord, CCoord + dCC, isWater);
}

float3 CalculateNormal(int3 cCoord, uint isWater) {
	int3 offsetX = int3(1, 0, 0);
	int3 offsetY = int3(0, 1, 0);
	int3 offsetZ = int3(0, 0, 1);

    float dx = SampleDensity(cCoord - offsetX, isWater) - SampleDensity(cCoord + offsetX, isWater);
    float dy = SampleDensity(cCoord - offsetY, isWater) - SampleDensity(cCoord + offsetY, isWater);
	float dz = SampleDensity(cCoord - offsetZ, isWater) - SampleDensity(cCoord + offsetZ, isWater);

	return float3(dx, dy, dz);
}


float3 GetVertexNormal(int3 p1Coord, int3 p2Coord, float interp, uint isWater){
    float3 p1Norm = CalculateNormal(p1Coord, isWater);
    float3 p2Norm = CalculateNormal(p2Coord, isWater);

    return p1Norm + interp*(p2Norm - p1Norm);
}