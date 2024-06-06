#include "Assets/Resources/MapData/CCoordHash.hlsl"
#include "Assets/Resources/Utility/GetIndex.hlsl"

#ifndef MAP_SAMPLER
#define MAP_SAMPLER
StructuredBuffer<uint> _MemoryBuffer;
StructuredBuffer<uint2> _AddressDict;
int3 CCoord;
float meshSkipInc;
int numCubesPerAxis;

const static int POINT_STRIDE_4BYTE = 1;
#endif

//mem access, if statement, operations, vector operations
int3 GetSampleCCoord(int3 coord){//(-inf, -1] => -1, [0, numCubesPerAxis-1] => 0, [numCubesPerAxis, inf) => 1
    return sign(sign(coord + 1) + sign(coord - numCubesPerAxis));
}

//Water is 1, terrain is 0
uint ReadMapData(int3 coord, int3 CCoord){
    int3 dCC = GetSampleCCoord(coord);
    coord = abs(dCC * numCubesPerAxis - coord);
    CCoord += dCC;

    uint2 chunkHandle = _AddressDict[HashCoord(CCoord)];
    if(chunkHandle.x == 0) return 0; else{
    uint chunkResize = meshSkipInc / (float)chunkHandle.y;
    uint address = indexFromCoordManual(coord * chunkResize, numCubesPerAxis * chunkResize) * POINT_STRIDE_4BYTE + chunkHandle.x;

    return _MemoryBuffer[address];}
}

float SampleDensity(int3 coord, uint isWater) {
    uint mapData = ReadMapData(coord, CCoord);
    return ((mapData & 0xFF) / 255.0f) * max(isWater, (mapData >> 8 & 0xFF) / 255.0f);
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