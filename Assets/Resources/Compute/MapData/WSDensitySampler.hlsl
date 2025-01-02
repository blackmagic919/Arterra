#include "Assets/Resources/Compute/Utility/GetIndex.hlsl"
#include "Assets/Resources/Compute/MapData/CCoordHash.hlsl"
#include "Assets/Resources/Compute/MapData/WSChunkCoord.hlsl"
#include "Assets/Resources/Compute/Utility/BlendHelper.hlsl"

//Information stored for every material
struct AtmosphericData{
    float3 scatterCoeffs;
    float3 extinctCoeff;
};


//Global Lookup Buffers
StructuredBuffer<AtmosphericData> _MatAtmosphericData; 

//MapData
StructuredBuffer<CInfo> _ChunkAddressDict;
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
    CInfo cHandle = _ChunkAddressDict[HashCoord(WSToCS(samplePointWS))];
    OpticalInfo mapData = (OpticalInfo)0;
    
    if(!Exists(cHandle, WSToCS(samplePointWS))) return mapData; else{
    float3 MSPoint = WSToMS(samplePointWS) / (cHandle.offset & 0xFF);
    MSPoint.x += (cHandle.offset >> 24) & 0xFF;
    MSPoint.y += (cHandle.offset >> 16) & 0xFF;
    MSPoint.z += (cHandle.offset >> 8) & 0xFF;

    Influences blendInfo = GetBlendInfo(MSPoint); //Blend pos using grid-fixed cube
    [unroll]for(uint i = 0; i < 8; i++){
        //unfortunately we have to clamp here
        //if you store duplice edge data in the map you don't have to do this
        uint3 MSCoord = clamp(blendInfo.origin + uint3(i & 1u, (i & 2u) >> 1, (i & 4u) >> 2), 0, mapChunkSize - 1); 
        uint pointAddress = cHandle.address + indexFromCoordManual(MSCoord, mapChunkSize) * POINT_STRIDE_4BYTE;

        uint info = _ChunkInfoBuffer[pointAddress];
        int material = info >> 16 & 0x7FFF;
        mapData.opticalDensity += (info & 0xFF) * blendInfo.corner[i];
        mapData.scatterCoeffs += _MatAtmosphericData[material].scatterCoeffs * blendInfo.corner[i];
        mapData.extinctionCoeff += _MatAtmosphericData[material].extinctCoeff * blendInfo.corner[i];
    }

    return mapData;
}}

OpticalDepth SampleOpticalDepth(float3 samplePointWS){
    CInfo cHandle = _ChunkAddressDict[HashCoord(WSToCS(samplePointWS))];
    OpticalDepth depth = (OpticalDepth)0;
    
    if(!Exists(cHandle, WSToCS(samplePointWS))) return depth; else{
    float3 MSPoint = WSToMS(samplePointWS) / (cHandle.offset & 0xFF);
    MSPoint.x += (cHandle.offset >> 24) & 0xFF;
    MSPoint.y += (cHandle.offset >> 16) & 0xFF;
    MSPoint.z += (cHandle.offset >> 8) & 0xFF;

    Influences blendInfo = GetBlendInfo(MSPoint); //Blend pos using grid-fixed cube
    [unroll]for(uint i = 0; i < 8; i++){
        //unfortunately we have to clamp here
        //if you store duplice edge data in the map you don't have to do this
        uint3 MSCoord = clamp(blendInfo.origin + uint3(i & 1u, (i & 2u) >> 1, (i & 4u) >> 2), 0, mapChunkSize - 1); 
        uint pointAddress = cHandle.address + indexFromCoordManual(MSCoord, mapChunkSize) * POINT_STRIDE_4BYTE;

        uint info = _ChunkInfoBuffer[pointAddress];
        int material = info >> 16 & 0x7FFF;
        depth.opticalDensity += (info & 0xFF) * blendInfo.corner[i];
        depth.scatterCoeffs += _MatAtmosphericData[material].scatterCoeffs * blendInfo.corner[i];
    }

    return depth;
}}
//
OpticalDepth SampleOpticalDepthRaw(float3 samplePointWS){
    CInfo cHandle = _ChunkAddressDict[HashCoord(WSToCS(samplePointWS))];
    OpticalDepth depth = (OpticalDepth)0;
    
    if(!Exists(cHandle, WSToCS(samplePointWS))) return depth; 
    float3 MSPoint = WSToMS(samplePointWS) / (cHandle.offset & 0xFF);
    MSPoint.x += (cHandle.offset >> 24) & 0xFF;
    MSPoint.y += (cHandle.offset >> 16) & 0xFF;
    MSPoint.z += (cHandle.offset >> 8) & 0xFF;

    uint3 MSCoord = clamp(round(WSToMS(MSPoint)), 0, mapChunkSize - 1); 
    uint pointAddress = cHandle.address + indexFromCoordManual(MSCoord, mapChunkSize) * POINT_STRIDE_4BYTE;

    uint info = _ChunkInfoBuffer[pointAddress];
    int material = info >> 16 & 0x7FFF;
    depth.opticalDensity = (info & 0xFF);
    depth.scatterCoeffs = _MatAtmosphericData[material].scatterCoeffs;

    return depth;
}
