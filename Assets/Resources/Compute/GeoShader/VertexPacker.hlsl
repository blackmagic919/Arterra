#ifndef VERTEXPACKER
#define VERTEXPACKER

struct VertexInfo{
    float3 positionOS;
    float3 normalOS;
};

VertexInfo UnpackVertex(uint2 data){
    VertexInfo v = (VertexInfo)0;

    v.positionOS.x = (data.x & 0x3FFF);
    v.positionOS.y = (data.x >> 14) & 0x3FFF;
    v.positionOS.z = (data.y & 0x3FFF);
    v.positionOS = v.positionOS * (65.0f / 16383.0f) - 0.5f;

    v.normalOS.x = (data.y >> 14) & 0x3F;
    v.normalOS.y = (data.y >> 20) & 0x3F;
    v.normalOS.z = (data.y >> 26) & 0x3F;
    v.normalOS = v.normalOS / 32.0f - 1.0f;
    return v;
}

uint2 PackVertices(float3 positionOS, float3 normalOS){
    uint2 data;

    uint3 gridCoord = clamp(positionOS + 0.5f, 0, 65) * (16383.0f / 65.0f);
    uint3 normal = clamp(round(normalOS * 32) + 32, 0, 63);
    data.x = (gridCoord.x & 0x3FFF) | 
            (gridCoord.y & 0x3FFF) << 14;
    data.y = (gridCoord.z & 0x3FFF) | 
            (normal.x & 0x3F) << 14 | 
            (normal.y & 0x3F) << 20 |
            (normal.z & 0x3F) << 26;
    return data;
}

#endif