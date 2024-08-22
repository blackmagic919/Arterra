#include "Assets/Resources/Utility/GetIndex.hlsl"
#include "Assets/Resources/MapData/CCoordHash.hlsl"
#include "Assets/Resources/MapData/WSChunkCoord.hlsl"
#include "Assets/Resources/Utility/BlendHelper.hlsl"

struct AtmosphericData{
    float3 scatterCoeffs;
    float3 extinctCoeff;
};

//Global Lookup Buffers
StructuredBuffer<AtmosphericData> _MatAtmosphericData; 

//MapData
StructuredBuffer<uint2> _ChunkAddressDict;
StructuredBuffer<uint> _ChunkInfoBuffer;
const static int POINT_STRIDE_4BYTE = 1;


struct SurMapData{
    uint info[8];
    float weight[8];
};

SurMapData SampleMapData(float3 samplePointWS){
    uint2 chunkHandle = _ChunkAddressDict[HashCoord(WSToCS(samplePointWS))];
    SurMapData mapData = (SurMapData)0;
    
    if(chunkHandle.x == 0) return mapData; else{
    uint chunkSize = mapChunkSize/chunkHandle.y;
    Influences blendInfo = GetBlendInfo(WSToMS(samplePointWS) / chunkHandle.y); //Blend pos using grid-fixed cube
    [unroll]for(uint i = 0; i < 8; i++){
        //unfortunately we have to clamp here
        //if you store duplice edge data in the map you don't have to do this
        uint3 MSCoord = clamp(blendInfo.origin + uint3(i & 1u, (i & 2u) >> 1, (i & 4u) >> 2), 0, chunkSize - 1); 
        uint pointAddress = chunkHandle.x + indexFromCoordManual(MSCoord, chunkSize) * POINT_STRIDE_4BYTE;

        mapData.weight[i] = blendInfo.corner[i];
        mapData.info[i] = _ChunkInfoBuffer[pointAddress];
    }

    return mapData;
}}

float GetDensity(SurMapData mapData){
    float density = 0;
    [unroll]for(uint i = 0; i < 8; i++){
        density += ((mapData.info[i] & 0xFF) / 255.0f) * mapData.weight[i];
    }
    return density;
}

float3 GetScatterCoeffs(SurMapData mapData){
    float3 scatterCoeffs = float3(0, 0, 0);
    [unroll]for(uint i = 0; i < 8; i++){
        int material = mapData.info[i] >> 16 & 0x7FFF;
        scatterCoeffs += _MatAtmosphericData[material].scatterCoeffs * mapData.weight[i];
    }
    return scatterCoeffs;
}

float3 GetExtinction(SurMapData mapData){
    float3 extinction = 0;
    [unroll]for(uint i = 0; i < 8; i++){
        int material = mapData.info[i] >> 16 & 0x7FFF;
        extinction += _MatAtmosphericData[material].extinctCoeff * mapData.weight[i];
    }
    return extinction;
}