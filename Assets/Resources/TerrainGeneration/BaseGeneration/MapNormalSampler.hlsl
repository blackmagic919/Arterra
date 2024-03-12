#include "Assets/Resources/MapData/CCoordHash.hlsl"
#include "Assets/Resources/Utility/GetIndex.hlsl"
#include "Assets/Resources/Utility/BlendHelper.hlsl"

#ifndef MAP_SAMPLER
#define MAP_SAMPLER
StructuredBuffer<uint> _MemoryBuffer;
StructuredBuffer<uint2> _AddressDict;
int3 CCoord;

const static int POINT_STRIDE_4BYTE = 2;
#endif

const static int dx[8] = {0, 1, 1, 0, 0, 1, 1, 0};
const static int dy[8] = {1, 1, 0, 0, 1, 1, 0, 0};
const static int dz[8] = {0, 0, 0, 0, 1, 1, 1, 1};

struct sNormal{
    float3 normal;
};

struct CubeNormals{
    sNormal corners[8];
};

struct BakedDensity{
    float density[64];
};

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
            uint address = indexFromCoordManual(nMapInfo.corner[i].mapCoord, nPointsPerAxis) * POINT_STRIDE_4BYTE + chunkHandle.x;
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

float3 CalculateNormal(BakedDensity bake, int3 cCoord) {
	int3 offsetX = int3(1, 0, 0);
	int3 offsetY = int3(0, 1, 0);
	int3 offsetZ = int3(0, 0, 1);

    float dx1 = bake.density[indexFromCoordManual(cCoord - offsetX, 4)];
    float dx2 = bake.density[indexFromCoordManual(cCoord + offsetX, 4)];
	float dx = dx1 - dx2;

    float dy1 = bake.density[indexFromCoordManual(cCoord - offsetY, 4)];
    float dy2 = bake.density[indexFromCoordManual(cCoord + offsetY, 4)];
	float dy = dy1 - dy2;

    float dz1 = bake.density[indexFromCoordManual(cCoord - offsetZ, 4)];
    float dz2 = bake.density[indexFromCoordManual(cCoord + offsetZ, 4)];
	float dz = dz1 - dz2;

	return float3(dx, dy, dz);
}

BakedDensity PreCalculateDensity(int3 oCoord){
    BakedDensity bake = (BakedDensity)0;
    [unroll]for(int x = 0; x < 4; x++){
        [unroll]for(int y = 0; y < 4; y++){
            [unroll]for(int z = 0; z < 4; z++){
                int index = indexFromCoordManual(x, y, z, 4);
                bake.density[index] = SampleDensity(oCoord + int3(x-1,y-1,z-1));
            }
        }
    }
    return bake;
}


CubeNormals GetCornerNormals(int3 oCoord){
    BakedDensity bake = PreCalculateDensity(oCoord);
    CubeNormals normals = (CubeNormals)0;
    [unroll] for(int i = 0; i < 8; i++)
        normals.corners[i].normal = CalculateNormal(bake, int3(dx[i] + 1, dy[i] + 1, dz[i] + 1));

    return normals;
}

float3 GetVertexNormal(CubeNormals normals, int p1Ind, int p2Ind, float interp){
    float3 p1Norm = normals.corners[p1Ind].normal;
    float3 p2Norm = normals.corners[p2Ind].normal;

    return p1Norm + interp*(p2Norm - p1Norm);
}