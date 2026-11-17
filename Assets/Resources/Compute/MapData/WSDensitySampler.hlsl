#include "Assets/Resources/Compute/Utility/GetIndex.hlsl"
#include "Assets/Resources/Compute/MapData/CCoordHash.hlsl"
#include "Assets/Resources/Compute/MapData/WSChunkCoord.hlsl"
#include "Assets/Resources/Compute/Utility/BlendHelper.hlsl"

//Information stored for every material
struct AtmosphericData{
    float3 inScatterCoeffs;
    float3 absorptionCoeffs;
    uint LightIntensity; 
};


//Global Lookup Buffers
StructuredBuffer<AtmosphericData> _MatAtmosphericData; 

//MapData
#ifndef MAPINFO_SAMPLER
#define MAPINFO_SAMPLER
StructuredBuffer<CInfo> _ChunkAddressDict;
StructuredBuffer<uint> _ChunkInfoBuffer;
uint IsoLevel;
#endif
const static int POINT_STRIDE_4BYTE = 1;

struct OpticalInfo{
    // Coefficients include voxel density before interpolation, in raw density units.
    float opticalDensity;
    float3 scatterCoeffs;
    float3 extinctionCoeff;
};

OpticalInfo SampleMapData(float3 samplePointWS){
    int3 CSCoord = WSToCS(samplePointWS);
    CInfo cHandle = _ChunkAddressDict[HashCoord(CSCoord)];
    OpticalInfo mapData = (OpticalInfo)0;
    
    if(!Contains(cHandle, CSCoord)) return mapData; else{
    float3 MSPoint = WSToMS(samplePointWS) / (cHandle.offset & 0xFF);
    MSPoint += float3(
        (cHandle.offset >> 24) & 0xFF,
        (cHandle.offset >> 16) & 0xFF,
        (cHandle.offset >> 8) & 0xFF
    );

    Influences blendInfo = GetBlendInfo(MSPoint); //Blend pos using grid-fixed cube
    [unroll]for(uint i = 0; i < 8; i++){
        //unfortunately we have to clamp here
        //if you store duplice edge data in the map you don't have to do this
        uint3 MSCoord = clamp(blendInfo.origin + uint3(i & 1u, (i & 2u) >> 1, (i & 4u) >> 2), 0, WorldMapChunkSize - 1); 
        uint pointAddress = cHandle.address + indexFromCoordManual(MSCoord, WorldMapChunkSize) * POINT_STRIDE_4BYTE;

        uint info = _ChunkInfoBuffer[pointAddress];
        AtmosphericData material = _MatAtmosphericData[info >> 16 & 0x7FFF];
        float weightedDensity = (info & 0xFF) * blendInfo.corner[i];
        mapData.opticalDensity += weightedDensity;
        mapData.scatterCoeffs = mad(material.inScatterCoeffs, weightedDensity, mapData.scatterCoeffs);
        mapData.extinctionCoeff = mad(material.inScatterCoeffs + material.absorptionCoeffs, weightedDensity, mapData.extinctionCoeff);
    }

    return mapData;
}}

OpticalInfo SampleMapDataRaw(float3 samplePointWS){
    int3 CSCoord = WSToCS(samplePointWS);
    CInfo cHandle = _ChunkAddressDict[HashCoord(CSCoord)];
    OpticalInfo mapData = (OpticalInfo)0;
    
    if(!Contains(cHandle, CSCoord)) return mapData; else{
    float3 MSPoint = WSToMS(samplePointWS) / (cHandle.offset & 0xFF);
    MSPoint += float3(
        (cHandle.offset >> 24) & 0xFF,
        (cHandle.offset >> 16) & 0xFF,
        (cHandle.offset >> 8) & 0xFF
    );

    uint3 MSCoord = clamp(round(WSToMS(MSPoint)), 0, WorldMapChunkSize - 1); 
    uint pointAddress = cHandle.address + indexFromCoordManual(MSCoord, WorldMapChunkSize) * POINT_STRIDE_4BYTE;

    uint info = _ChunkInfoBuffer[pointAddress];
    AtmosphericData material = _MatAtmosphericData[info >> 16 & 0x7FFF];
    mapData.opticalDensity = (info & 0xFF);
    mapData.scatterCoeffs = material.inScatterCoeffs * mapData.opticalDensity;
    mapData.extinctionCoeff = (material.inScatterCoeffs + material.absorptionCoeffs) * mapData.opticalDensity;

    return mapData;
}}
