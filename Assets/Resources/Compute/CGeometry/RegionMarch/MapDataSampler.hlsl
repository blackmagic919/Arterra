#include "Assets/Resources/Compute/Utility/GetIndex.hlsl"
#define SAMPLE_TERRAIN 0
#define SAMPLE_WATER 1
#define SAMPLE_TSP 2

static const float3 Epsilon = float3(1E-6, 1E-6, 1E-6);
StructuredBuffer<uint> MapFlags;
StructuredBuffer<uint> MapData;
int3 numCubesPerAxis;
uint bSTART_map;
uint bSTART_flags;
float IsoLevel;


//MapInfo
struct matTerrain{
    int textureIndex;
    float baseTextureScale;
    uint flipStateRendering;
    uint geoShaderInd;
};

struct Corner{
    float density[3];
    int material;
};

StructuredBuffer<matTerrain> _MatTerrainData;

uint ReadMapData(int3 coord){
    coord = clamp(coord, 0, numCubesPerAxis);
    uint address = indexFromCoordIrregular(coord, numCubesPerAxis.yz + 1);
    return MapData[address + bSTART_map];
}

uint ReadFlags(int3 coord) {
    coord = clamp(coord, 0, numCubesPerAxis);
    uint address = indexFromCoordIrregular(coord, numCubesPerAxis.yz + 1);
    uint offset = (address % 4) * 8; address /= 4;
    return (MapFlags[address + bSTART_flags] >> offset) & 0xFF;
}

//c must be 0 < c < 1
float triLerp(float x, float c) {return min(saturate(x / c), saturate((1.0 - x) / (1.0 - c))); }
Corner SampleStateInfo(uint mapData){
    float density = (mapData & 0xFF) / 255.0f;
    float viscosity = ((mapData >> 8) & 0xFF) / 255.0f;
    Corner o;

    o.material = ((mapData >> 16) & 0x7FFF);
    if(_MatTerrainData[o.material].flipStateRendering){
        o.density[0] = max(density - viscosity, triLerp(viscosity, IsoLevel) * (IsoLevel - Epsilon.x));
        o.density[1] = max(density - viscosity, viscosity);
        o.density[2] = viscosity;
    } else {
        o.density[0] = viscosity;
        o.density[1] = density - viscosity;
        o.density[2] = triLerp(viscosity, IsoLevel) * (IsoLevel - Epsilon.x);
    }
    return o;
}

float SampleDensity(int3 coord, uint state) {
    uint mapData = ReadMapData(coord);
    return SampleStateInfo(mapData).density[state];
}

static const int3x3 VertexDir[3] = {
    int3x3(0, 0, 1, 1, 0, 0, 0, 1, 0), //+x
    int3x3(1, 0, 0, 0, 0, 1, 0, 1, 0), //+y
    int3x3(1, 0, 0, 0, 1, 0, 0, 0, 1) //+y
};

float3 CalculateNormal(int3 cCoord, uint state) {
	int3 offsetX = int3(1, 0, 0);
	int3 offsetY = int3(0, 1, 0);
	int3 offsetZ = int3(0, 0, 1);

    float dx = SampleDensity(cCoord - offsetX, state) - SampleDensity(cCoord + offsetX, state);
    float dy = SampleDensity(cCoord - offsetY, state) - SampleDensity(cCoord + offsetY, state);
	float dz = SampleDensity(cCoord - offsetZ, state) - SampleDensity(cCoord + offsetZ, state);

	return normalize(float3(dx, dy, dz));
}


float3 GetVertexNormal(int3 p1Coord, int dir, float interp, uint state){
    float3 dir3 = mul(VertexDir[dir], int3(0, 0, 1));
    int3 p2Coord = p1Coord + dir3;
    float3 p1Norm = CalculateNormal(p1Coord, state);
    float3 p2Norm = CalculateNormal(p2Coord, state);

    float3 Norm = p1Norm + interp*(p2Norm - p1Norm);
    return Norm;
}
