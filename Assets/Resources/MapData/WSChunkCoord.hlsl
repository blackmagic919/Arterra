#ifndef WSCHUNKCOORD_HELPER
#define WSCHUNKCOORD_HELPER
float lerpScale;
uint mapChunkSize;

int3 GetChunkCoord(float3 positionWS){
    float3 positionOS = positionWS / lerpScale;

    int3 CCoord;
    CCoord.x = round(positionOS.x / mapChunkSize);
    CCoord.y = round(positionOS.y / mapChunkSize);
    CCoord.z = round(positionOS.z / mapChunkSize);

    return CCoord;
}

float3 GetMapCoord(float3 positionWS, uint meshSkipInc){
    float3 positionOS = positionWS / lerpScale;

    float3 positionMS;
    positionMS.x = fmod(positionOS.x + mapChunkSize/2, mapChunkSize);
    positionMS.y = fmod(positionOS.y + mapChunkSize/2, mapChunkSize);
    positionMS.z = fmod(positionOS.z + mapChunkSize/2, mapChunkSize);

    positionMS.x = positionMS.x < 0 ? positionMS.x + mapChunkSize : positionMS.x;
    positionMS.y = positionMS.y < 0 ? positionMS.y + mapChunkSize : positionMS.y;
    positionMS.z = positionMS.z < 0 ? positionMS.z + mapChunkSize : positionMS.z;

    return positionMS / meshSkipInc;
}
#endif
