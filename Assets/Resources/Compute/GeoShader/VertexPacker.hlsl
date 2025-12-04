#ifndef VERTEXPACKER
#define VERTEXPACKER
#include "Assets/Resources/Compute/Utility/GetIndex.hlsl"

struct VertexInfo{
    float3 positionOS;
    float3 normalOS;
    uint variant;
};

struct SourceVertex{
    float3 positionOS;
    float3 normalOS;
    int2 material; 
};

struct matTerrain{
    int textureIndex;
    float baseTextureScale;
    uint flipStateRendering;
    uint geoShaderInd;
};

StructuredBuffer<matTerrain> _MatTerrainData;
uint geoInd;

float3 safeNormalize(float3 n) {
    return (dot(n, n) > 1e-12) ? normalize(n) : float3(0,1,0);
}

float2 Encode(float3 norm) {
    norm = safeNormalize(norm);
    return norm.xy;
}

float3 Decode(float2 proj) {
    float invP = proj.x * proj.x + proj.y * proj.y;
    if (invP > 1) return float3(0, 1, 0);
    else return float3(proj, sqrt(1 - invP));
}


VertexInfo UnpackVertex(uint3 data){
    VertexInfo v = (VertexInfo)0;

    v.positionOS.x = (data.x & 0xFFFF);
    v.positionOS.y = (data.x >> 16) & 0xFFFF;
    v.positionOS.z = (data.y & 0xFFFF);
    v.positionOS = v.positionOS * (65.0 / 65535.0) - 0.5f;
    v.variant = (data.y >> 16) & 0xFFFF;

    uint ex = data.z & 0xFFF;
    uint ey = (data.z >> 12) & 0xFFF;
    float2 e = float2(ex, ey) / 4095.0f * 2.0f - 1.0f;
    v.normalOS = Decode(e);
    return v;
}

uint3 PackVertices(float3 positionOS, float3 normalOS, uint variant){
    uint3 data;

    float2 e = Encode(normalOS);
    e = e * 0.5f + 0.5f;
    uint ex = (uint)round(e.x * 4095.0);
    uint ey = (uint)round(e.y * 4095.0);

    uint3 gridCoord = clamp(positionOS + 0.5f, 0, 65) * (65535.0 / 65.0);
    data.x = (gridCoord.x & 0xFFFF) | 
            (gridCoord.y & 0xFFFF) << 16;
    data.y = ((variant & 0xFFFF) << 16) | (gridCoord.z & 0xFFFF); 
    data.z = (ey << 12) | ex; 
    return data;
}

uint GetShaderVariant(SourceVertex vertices[3]){
    uint subVariant = 0;
    [unroll]for(int i = 0; i < 3; i++){
        uint geoShad = _MatTerrainData[vertices[i].material.x].geoShaderInd;
        if((geoShad & 0x80000000) == 0) continue;
        if(((geoShad >> 16) & 0x7FFF) != geoInd) continue;
        subVariant = geoShad & 0xFFFF;
    } return subVariant;
}

StructuredBuffer<uint> SubChunkInfo;
uint bSTART_sChunkI;
uint sChunkSize;
uint sChunksPerAxis;
uint GetBaseSubChunk(SourceVertex vertices[3]) {
    int3 center = floor((vertices[0].positionOS + vertices[1].positionOS + vertices[2].positionOS) / 3);
    int3 remainder = center % sChunkSize;
    int3 SCoord = min((center - remainder) / sChunkSize, sChunksPerAxis - 1);
    return EncodeMorton3(SCoord);
}

uint GetDetailLevel(uint SubChunkIndex) {
    return SubChunkInfo[bSTART_sChunkI + SubChunkIndex];
}

#endif