#include "Assets/Resources/Compute/MapData/CCoordHash.hlsl"
#include "Assets/Resources/Compute/Utility/GetIndex.hlsl"

StructuredBuffer<uint> _MemoryBuffer;
StructuredBuffer<uint2> _AddressDict;
int3 CCoord;
uint meshSkipInc;
int numCubesPerAxis;

//Water is 1, terrain is 0
uint ReadMapData(int3 coord){
    int3 dCC = floor(coord / (float)numCubesPerAxis);
    coord = abs(dCC * numCubesPerAxis - coord);
    int3 sCCoord = dCC + CCoord;

    uint2 chunkHandle = _AddressDict[HashCoord(sCCoord)];
    if(chunkHandle.x == 0) return 0; 
    else{
    coord = (coord * meshSkipInc) / (chunkHandle.y & 0xFF);
    coord.x += (chunkHandle.y >> 24) & 0xFF;
    coord.y += (chunkHandle.y >> 16) & 0xFF;
    coord.z += (chunkHandle.y >> 8) & 0xFF;
    uint address = indexFromCoordManual(coord, numCubesPerAxis * meshSkipInc) + chunkHandle.x;

    return _MemoryBuffer[address];
    }
}

float SampleDensity(int3 coord, uint isWater) {
    uint mapData = ReadMapData(coord);
    float density = (mapData & 0xFF) / 255.0f;
    float viscosity = ((mapData >> 8) & 0xFF) / 255.0f;

    if(isWater) return (density - viscosity);
    else return viscosity;
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