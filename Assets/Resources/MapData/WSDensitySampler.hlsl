#include "Assets/Resources/Utility/GetIndex.hlsl"
#include "Assets/Resources/MapData/CCoordHash.hlsl"
#include "Assets/Resources/MapData/WSChunkCoord.hlsl"
#include "Assets/Resources/Utility/BlendHelper.hlsl"

struct AtmosphericData{
    float3 scatterCoeffs;
    float extinctCoeff;
};

//Global Lookup Buffers
StructuredBuffer<AtmosphericData> _MatAtmosphericData; 

//MapData
StructuredBuffer<uint2> _ChunkAddressDict;
StructuredBuffer<uint> _ChunkInfoBuffer;
const static int POINT_STRIDE_4BYTE = 2;

struct MapInfo{
    float density;
    int material;
};

struct SurMapData{
    MapInfo info[8];
    float weight[8];
    bool hasChunk;
};

MapInfo ReadChunkPoint(uint address){
    MapInfo mapInfo = (MapInfo)0;

    mapInfo.density  = asfloat(_ChunkInfoBuffer[address]);
    mapInfo.material = asint(_ChunkInfoBuffer[address + 1]);

    return mapInfo;
}

SurMapData SampleMapData(float3 samplePointWS, float IsoLevel){
    int3 chunkCoord = GetChunkCoord(samplePointWS);
    uint chunkHash = HashCoord(chunkCoord);

    uint2 chunkHandle = _ChunkAddressDict[chunkHash];
    uint chunkAddress = chunkHandle.x;
    uint meshSkipInc = chunkHandle.y;
    uint numPointsPerAxis = (mapChunkSize / meshSkipInc) + 1;

    SurMapData mapData = (SurMapData)0;
    if(chunkAddress != 0u){ //if chunk defined
        float3 pointCoord = GetMapCoord(samplePointWS, meshSkipInc);
        Influences blendInfo = GetBlendInfo(pointCoord); //Blend pos using grid-fixed cube 
        mapData.hasChunk = true;

        [unroll]for(uint i = 0; i < 8; i++){
            uint pointIndex = indexFromCoordManual(blendInfo.corner[i].mapCoord, numPointsPerAxis);
            uint pointAddress = chunkAddress + pointIndex * POINT_STRIDE_4BYTE;
            MapInfo pointInfo = ReadChunkPoint(pointAddress);

            mapData.info[i] = pointInfo;
            mapData.weight[i] = pointInfo.density > IsoLevel ? 0 : blendInfo.corner[i].influence;
        }
    }
    return mapData;
}

float GetDensity(SurMapData mapData){
    float density = 0;

    if(mapData.hasChunk){
        [unroll]for(uint i = 0; i < 8; i++){
            density += mapData.info[i].density * mapData.weight[i];
        }
    }
    return density;
}

float3 GetScatterCoeffs(SurMapData mapData){
    float3 scatterCoeffs = float3(0, 0, 0);
    if(mapData.hasChunk){
        [unroll]for(uint i = 0; i < 8; i++){
            scatterCoeffs += _MatAtmosphericData[mapData.info[i].material].scatterCoeffs * mapData.weight[i];
        }
    }
    return scatterCoeffs;
}

float GetExtinction(SurMapData mapData){
    float extinction = 0;
    if(mapData.hasChunk){
        [unroll]for(uint i = 0; i < 8; i++){
            extinction += _MatAtmosphericData[mapData.info[i].material].extinctCoeff * mapData.weight[i];
        }
    }
    return extinction;
}