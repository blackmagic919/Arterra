#ifndef VERTEXPACKER
#define VERTEXPACKER

struct VertexInfo{
    float3 positionOS;
    float3 normalOS;
};

VertexInfo UnpackVertex(uint2 data){
    VertexInfo v = (VertexInfo)0;

    v.positionOS.x = (data.x & 0x3F);
    v.positionOS.y = (data.x >> 6) & 0x3F;
    v.positionOS.z = (data.x >> 12) & 0x3F;
    v.positionOS = v.positionOS + 0.5f;

    float3 offset;
    offset.x = data.y & 0xFF;
    offset.y = (data.y >> 8) & 0xFF;
    offset.z = (data.y >> 16) & 0xFF;
    offset = offset / 128.0f - 1.0f;
    v.positionOS = v.positionOS + offset;

    v.normalOS.x = (data.x >> 18) & 0x3F;
    v.normalOS.y = (data.x >> 24) & 0x3F;
    v.normalOS.z = (data.y >> 24) & 0x3F;
    v.normalOS = v.normalOS / 32.0f - 1.0f;
    return v;
}

uint2 PackVertices(float3 positionOS, float3 normalOS){
    uint2 data;

    uint3 gridCoord = clamp(floor(positionOS), 0, 63);
    uint3 offsetCoord = clamp((positionOS - gridCoord + 0.5) * 128, 0, 255);
    uint3 normal = clamp(round(normalOS * 32) + 32, 0, 63);
    data.x = (gridCoord.x & 0x3F) | 
            (gridCoord.y & 0x3F) << 6 | 
            (gridCoord.z & 0x3F) << 12 | 
            (normal.x & 0x3F) << 18 | 
            (normal.y & 0x3F) << 24;
    data.y = (offsetCoord.x & 0xFF) | 
            (offsetCoord.y & 0xFF) << 8 | 
            (offsetCoord.z & 0xFF) << 16 | 
            (normal.z & 0x3F) << 24;
    return data;
}

#endif