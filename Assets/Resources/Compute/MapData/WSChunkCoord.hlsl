#ifndef WSCHUNKCOORD_HELPER
#define WSCHUNKCOORD_HELPER
float lerpScale;
uint mapChunkSize;


//object space to chuck space
int3 WSToCS(float3 positionWS){ return round(positionWS / lerpScale / mapChunkSize); }

//object space to map space
float3 WSToMS(float3 positionWS){
    float3 positionMS = positionWS / lerpScale + mapChunkSize / 2;
    return fmod((mapChunkSize + fmod(positionMS, mapChunkSize)), mapChunkSize);
}

float3 WSToGS(float3 positionWS){
    return positionWS / lerpScale + mapChunkSize / 2;
}
#endif