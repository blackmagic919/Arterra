#ifndef WSCHUNKCOORD_HELPER
#define WSCHUNKCOORD_HELPER
float WorldLerpScale;
uint WorldMapChunkSize;


//object space to chuck space
int3 WSToCS(float3 positionWS){ return round(positionWS / WorldLerpScale / WorldMapChunkSize); }

//object space to map space, equal to (GSToMS o WSToGS) ()
float3 WSToMS(float3 positionWS){
    float3 positionMS = positionWS / WorldLerpScale + WorldMapChunkSize / 2;
    return fmod((WorldMapChunkSize + fmod(positionMS, WorldMapChunkSize)), WorldMapChunkSize);
}

float3 GSToMS(float3 positionGS) {
    return fmod((WorldMapChunkSize + fmod(positionGS, WorldMapChunkSize)), WorldMapChunkSize);
}

float3 WSToGS(float3 positionWS){
    return positionWS / WorldLerpScale + WorldMapChunkSize / 2;
}
#endif