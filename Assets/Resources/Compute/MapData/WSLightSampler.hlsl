#include "Assets/Resources/Compute/Utility/GetIndex.hlsl"
#include "Assets/Resources/Compute/MapData/CCoordHash.hlsl"
#include "Assets/Resources/Compute/MapData/WSChunkCoord.hlsl"
#include "Assets/Resources/Compute/Utility/BlendHelper.hlsl"

StructuredBuffer<uint2> _ChunkAddressDict;
StructuredBuffer<uint> _ChunkInfoBuffer;
uint chunkLMOffset; //Light Map


// NOTE: Though Light is stored as 2bytes, because we take a trilinear
// interpolation, storing as 2 bytes will introduce extreme quantitization artifacts
// so we rescale to 4 bytes to allow more precision
uint SampleLight(float3 samplePointWS){
    int3 CSCoord = WSToCS(samplePointWS);
    CInfo cHandle = _ChunkAddressDict[HashCoord(CSCoord)];
    float4 light = 0; //rgb -> Light, s -> Shadow
    
    if(!Exists(cHandle, CSCoord)) return light; else{
    float3 MSPoint = WSToMS(samplePointWS) / (cHandle.offset & 0xFF);
    MSPoint += float3(
        (cHandle.offset >> 24) & 0xFF,
        (cHandle.offset >> 16) & 0xFF,
        (cHandle.offset >> 8) & 0xFF
    );
    
    float CumulativeWeights = 1.0f;
    Influences blendInfo = GetBlendInfo(MSPoint); //Blend pos using grid-fixed cube
    [unroll]for(uint i = 0; i < 8; i++){
        uint3 MSCoord = clamp(blendInfo.origin + uint3(i & 1u, (i & 2u) >> 1, (i & 4u) >> 2), 0, mapChunkSize - 1); 
        uint index = indexFromCoordManual(MSCoord, mapChunkSize);
        uint pointAddress = cHandle.address + index * POINT_STRIDE_4BYTE;

        uint info = _ChunkInfoBuffer[pointAddress];
        int solid = info >> 8 & 0xFF;
        if(solid >= IsoLevel) {CumulativeWeights -= blendInfo.corner[i]; continue;}

        pointAddress = cHandle.address + chunkLMOffset + index / 2;
        uint lRaw = _ChunkInfoBuffer[pointAddress] >> ((index % 2) * 16) & 0xFFFF;
        light.x = mad((lRaw & 0x1F), blendInfo.corner[i], light);
        light.y = mad(((lRaw >> 5) & 0x1F), blendInfo.corner[i], light);
        light.z = mad(((lRaw >> 10) & 0x1F), blendInfo.corner[i], light);
        light.w = mad(((lRaw >> 15) & 0x1), blendInfo.corner[i], light);
    }
    light /= max(CumulativeWeights, 1E-5f); //Avoid division by 0
    light.xyz = light.xyz / 31.0f;
    light.xyz *= 1023.0f; //0x3FF or 10 bit
    light.w *= 3.0f; //0x1 or 1 bit

    uint lightC = ((int)round(light.w) << 30) | 
                  ((int)round(light.z) << 20) | 
                  ((int)round(light.y) << 10) | 
                  ((int)round(light.x));
    return light;
}}
