#include "Assets/Scripts/TerrainGeneration/ComputeShaders/Includes/GetIndex.hlsl"
#include "Assets/Scripts/TerrainGeneration/DensityManager/CCoordHash.hlsl"
#include "Assets/Scripts/TerrainGeneration/DensityManager/WSChunkCoord.hlsl"
#include "Assets/Scripts/TerrainGeneration/DensityManager/BlendHelper.hlsl"

StructuredBuffer<uint2> _ChunkAddressDict;
StructuredBuffer<uint> _ChunkInfoBuffer;
const static int POINT_STRIDE_4BYTE = 2;

struct MapInfo{
    float density;
    int material;
};

MapInfo ReadChunkPoint(uint address){
    MapInfo mapInfo = (MapInfo)0;

    mapInfo.density  = asfloat(_ChunkInfoBuffer[address]);
    mapInfo.material = asint(_ChunkInfoBuffer[address + 1]);

    return mapInfo;
}

const static float3 _PlanetCenter_T = float3(0, -750, 0);
const static float _PlanetRadius_T = 500;
const static float _AtmosphereRadius_T = 1000;
const static float _DensityFalloff_T = 5;

float densityAtPoint(float3 samplePointWS){
    /*
    float density = 0;

    int3 chunkCoord = GetChunkCoord(samplePointWS);
    uint chunkHash = HashCoord(chunkCoord);

    uint2 chunkHandle = _ChunkAddressDict[chunkHash];
    uint chunkAddress = chunkHandle.x;
    uint meshSkipInc = chunkHandle.y;
    uint numPointsPerAxis = (mapChunkSize / meshSkipInc) + 1;

    if(chunkAddress != 0u){ 
        float heightAboveSurface = length(samplePointWS - _PlanetCenter_T) - _PlanetRadius_T;
        float height01 = heightAboveSurface / (_AtmosphereRadius_T - _PlanetRadius_T);
        float localDensity = exp(-height01 * _DensityFalloff_T) * (1-height01);
        density = localDensity;
    }

    return density;*/
    float density = 0;

    int3 chunkCoord = GetChunkCoord(samplePointWS);
    uint chunkHash = HashCoord(chunkCoord);

    uint2 chunkHandle = _ChunkAddressDict[chunkHash];
    uint chunkAddress = chunkHandle.x;
    uint meshSkipInc = chunkHandle.y;
    uint numPointsPerAxis = (mapChunkSize / meshSkipInc) + 1;

    if(chunkAddress != 0u){ //if chunk defined
        float3 pointCoord = GetMapCoord(samplePointWS, meshSkipInc);
        Influences blendInfo = GetBlendInfo(pointCoord); //Blend pos using grid-fixed cube 

        [unroll]for(uint i = 0; i < 8; i++){
            uint pointIndex = indexFromCoordManual(blendInfo.corner[i].mapCoord, numPointsPerAxis);
            uint pointAddress = chunkAddress + pointIndex * POINT_STRIDE_4BYTE;
            MapInfo pointInfo = ReadChunkPoint(pointAddress);

            density += pointInfo.density * blendInfo.corner[i].influence;
        }
    }

    return density;
}