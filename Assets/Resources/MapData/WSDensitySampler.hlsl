#include "Assets/Resources/Utility/GetIndex.hlsl"
#include "Assets/Resources/MapData/CCoordHash.hlsl"
#include "Assets/Resources/MapData/WSChunkCoord.hlsl"
#include "Assets/Resources/Utility/BlendHelper.hlsl"

//Information stored for every material
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

struct OpticalDepth{
    float opticalDensity;
    float3 scatterCoeffs;
};

struct OpticalInfo{
    float opticalDensity;
    float3 scatterCoeffs;
    float3 extinctionCoeff;
};

OpticalInfo SampleMapData(float3 samplePointWS){
    uint2 chunkHandle = _ChunkAddressDict[HashCoord(WSToCS(samplePointWS))];
    OpticalInfo mapData = (OpticalInfo)0;
    
    if(chunkHandle.x == 0) return mapData; else{
    uint chunkSize = mapChunkSize/chunkHandle.y;
    Influences blendInfo = GetBlendInfo(WSToMS(samplePointWS) / chunkHandle.y); //Blend pos using grid-fixed cube
    [unroll]for(uint i = 0; i < 8; i++){
        //unfortunately we have to clamp here
        //if you store duplice edge data in the map you don't have to do this
        uint3 MSCoord = clamp(blendInfo.origin + uint3(i & 1u, (i & 2u) >> 1, (i & 4u) >> 2), 0, chunkSize - 1); 
        uint pointAddress = chunkHandle.x + indexFromCoordManual(MSCoord, chunkSize) * POINT_STRIDE_4BYTE;

        uint info = _ChunkInfoBuffer[pointAddress];
        int material = info >> 16 & 0x7FFF;
        mapData.opticalDensity += (info & 0xFF) * blendInfo.corner[i];
        mapData.scatterCoeffs += _MatAtmosphericData[material].scatterCoeffs * blendInfo.corner[i];
        mapData.extinctionCoeff += _MatAtmosphericData[material].extinctCoeff * blendInfo.corner[i];
    }

    return mapData;
}}

OpticalDepth SampleOpticalDepth(float3 samplePointWS){
    uint2 chunkHandle = _ChunkAddressDict[HashCoord(WSToCS(samplePointWS))];
    OpticalDepth depth = (OpticalDepth)0;
    
    if(chunkHandle.x == 0) return depth; else{
    uint chunkSize = mapChunkSize/chunkHandle.y;
    Influences blendInfo = GetBlendInfo(WSToMS(samplePointWS) / chunkHandle.y); //Blend pos using grid-fixed cube
    [unroll]for(uint i = 0; i < 8; i++){
        //unfortunately we have to clamp here
        //if you store duplice edge data in the map you don't have to do this
        uint3 MSCoord = clamp(blendInfo.origin + uint3(i & 1u, (i & 2u) >> 1, (i & 4u) >> 2), 0, chunkSize - 1); 
        uint pointAddress = chunkHandle.x + indexFromCoordManual(MSCoord, chunkSize) * POINT_STRIDE_4BYTE;

        uint info = _ChunkInfoBuffer[pointAddress];
        int material = info >> 16 & 0x7FFF;
        depth.opticalDensity += (info & 0xFF) * blendInfo.corner[i];
        depth.scatterCoeffs += _MatAtmosphericData[material].scatterCoeffs * blendInfo.corner[i];
    }

    return depth;
}}

OpticalDepth SampleOpticalDepthRaw(float3 samplePointWS){
    uint2 chunkHandle = _ChunkAddressDict[HashCoord(WSToCS(samplePointWS))];
    OpticalDepth depth = (OpticalDepth)0;
    
    if(chunkHandle.x == 0) return depth; else{
    uint chunkSize = mapChunkSize/chunkHandle.y;
    uint3 MSCoord = clamp(round(WSToMS(samplePointWS) / chunkHandle.y), 0, chunkSize - 1); 
    uint pointAddress = chunkHandle.x + indexFromCoordManual(MSCoord, chunkSize) * POINT_STRIDE_4BYTE;

    uint info = _ChunkInfoBuffer[pointAddress];
    int material = info >> 16 & 0x7FFF;
    depth.opticalDensity = (info & 0xFF);
    depth.scatterCoeffs = _MatAtmosphericData[material].scatterCoeffs;

    return depth;
}}
