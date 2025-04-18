#include "Assets/Resources/Compute/MapData/CCoordHash.hlsl"
#include "Assets/Resources/Compute/MapData/WSChunkCoord.hlsl"
#include "Assets/Resources/Compute/Utility/BlendHelper.hlsl"


#ifndef MAPINFO_SAMPLER
#define MAPINFO_SAMPLER
StructuredBuffer<CInfo> _ChunkAddressDict;
StructuredBuffer<uint> _ChunkInfoBuffer;
uint IsoLevel;
#endif

uint chunkLMOffset; //Light Map

uint2 GetChunkAddress(CInfo cHandle, int3 MCoord){
    //If underground return 0
    if(any(MCoord < 0 || MCoord >= (int)mapChunkSize)){
        //Convert from MapSpace to GridSpace
        int3 SampleGS = MCoord * (cHandle.offset & 0xFF) + cHandle.CCoord * mapChunkSize;
        MCoord = ((SampleGS % mapChunkSize) + mapChunkSize) % mapChunkSize;
        int3 CCoord = (SampleGS - MCoord) / (int)mapChunkSize;
        cHandle = _ChunkAddressDict[HashCoord(CCoord)];
        if(!Contains(cHandle, CCoord)) return 0;
        MCoord /= (cHandle.offset & 0xFF);
        MCoord.x += (cHandle.offset >> 24) & 0xFF;
        MCoord.y += (cHandle.offset >> 16) & 0xFF;
        MCoord.z += (cHandle.offset >> 8) & 0xFF;
    }
    uint index = MCoord.x * mapChunkSize * mapChunkSize + MCoord.y * mapChunkSize + MCoord.z;
    return uint2(cHandle.address, index);
}

// NOTE: Though Light is stored as 2bytes, because we take a trilinear
// interpolation, storing as 2 bytes will introduce extreme quantitization artifacts
// so we rescale to 4 bytes to allow more precision
uint SampleLight(float3 samplePointWS){
    int3 CSCoord = WSToCS(samplePointWS);
    CInfo cHandle = _ChunkAddressDict[HashCoord(CSCoord)];
    float4 light = 0; //rgb -> Light, s -> Shadow

    if(!Contains(cHandle, CSCoord)) return 0; else{
    float3 MSPoint = WSToMS(samplePointWS) / (cHandle.offset & 0xFF);
    MSPoint += float3(
        (cHandle.offset >> 24) & 0xFF,
        (cHandle.offset >> 16) & 0xFF,
        (cHandle.offset >> 8) & 0xFF
    );

    float cumulate = 0.001f;
    Influences blendInfo = GetBlendInfo(MSPoint); //Blend pos using grid-fixed cube
    [unroll]for(uint i = 0; i < 8; i++){
        uint3 MSCoord = blendInfo.origin + uint3(i & 1u, (i & 2u) >> 1, (i & 4u) >> 2); 
        uint2 addInfo = GetChunkAddress(cHandle, MSCoord);
        uint address = addInfo.x; uint index = addInfo.y;
        if(address == 0) continue;

        uint pointAddress = address + index;
        if((_ChunkInfoBuffer[pointAddress] >> 8 & 0xFF) >= IsoLevel)
            continue;

        pointAddress = address + chunkLMOffset + index / 2;        
        uint lRaw = (_ChunkInfoBuffer[pointAddress] >> ((index % 2) * 16)) & 0xFFFF;
        light.x = mad((lRaw & 0x1F), blendInfo.corner[i], light.x);
        light.y = mad(((lRaw >> 5) & 0x1F), blendInfo.corner[i], light.y);
        light.z = mad(((lRaw >> 10) & 0x1F), blendInfo.corner[i], light.z);
        light.w = mad((lRaw >> 15) & 0x1, blendInfo.corner[i], light.w);
        cumulate += blendInfo.corner[i];
    }
    light.xyz = clamp(light.xyz / 31.0f, 0.0f, 1.0f); //0x1F or 5 bit
    light = min(light / cumulate, 1.0f);
    light.xyz *= 1023.0f; //0x3FF or 10 bit
    light.w *= 3.0f; //0x3 or 2 bit

    uint lightC = ((uint)round(light.w) << 30) | 
                  ((uint)round(light.z) << 20) | 
                  ((uint)round(light.y) << 10) | 
                  ((uint)round(light.x));
    return lightC;
}}


//We don't allow Out-Of-Bounds Rehashing, and we don't look at if it's underground or not
uint SampleLightFast(float3 samplePointWS){
    int3 CSCoord = WSToCS(samplePointWS);
    CInfo cHandle = _ChunkAddressDict[HashCoord(CSCoord)];
    float4 light = 0; //rgb -> Light, s -> Shadow

    if(!Contains(cHandle, CSCoord)) return 0; else{
    float3 MSPoint = WSToMS(samplePointWS) / (cHandle.offset & 0xFF);
    MSPoint += float3(
        (cHandle.offset >> 24) & 0xFF,
        (cHandle.offset >> 16) & 0xFF,
        (cHandle.offset >> 8) & 0xFF
    );

    float4 light = 0; //rgb -> Light, s -> Shadow
    Influences blendInfo = GetBlendInfo(MSPoint); //Blend pos using grid-fixed cube
    [unroll]for(uint i = 0; i < 8; i++){
        uint3 MSCoord = clamp(blendInfo.origin + uint3(i & 1u, (i & 2u) >> 1, (i & 4u) >> 2), 0, mapChunkSize - 1); 
        uint index = MSCoord.x * mapChunkSize * mapChunkSize + MSCoord.y * mapChunkSize + MSCoord.z;
        uint pointAddress = cHandle.address + chunkLMOffset + (index/ 2);
        uint lRaw = (_ChunkInfoBuffer[pointAddress] >> ((index % 2) * 16)) & 0xFFFF;
        light.x = mad((lRaw & 0x1F), blendInfo.corner[i], light.x);
        light.y = mad(((lRaw >> 5) & 0x1F), blendInfo.corner[i], light.y);
        light.z = mad(((lRaw >> 10) & 0x1F), blendInfo.corner[i], light.z);
        light.w = mad((lRaw >> 15) & 0x1, blendInfo.corner[i], light.w);
    }

    light.xyz = light.xyz / 31.0f; //0x1F or 5 bit
    light.xyz *= 1023.0f; //0x3FF or 10 bit
    light.w *= 3.0f; //0x3 or 2 bit

    uint lightC = ((uint)round(light.w) << 30) | 
                  ((uint)round(light.z) << 20) | 
                  ((uint)round(light.y) << 10) | 
                  ((uint)round(light.x));
    return lightC;
}}