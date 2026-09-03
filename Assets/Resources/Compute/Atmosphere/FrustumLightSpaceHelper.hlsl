#ifndef FRUSTUM_LIGHT_SPACE_HELPER
#define FRUSTUM_LIGHT_SPACE_HELPER

// Matrix-based frustum-space frame for the camera footprint volume.
// wsToFs maps WS -> FS where FS.xy are footprint UVs and FS.z is light-axis depth.
// fsToWs maps FS -> WS.
float4x4 _FrustumLightWSToFS;
float4x4 _FrustumLightFSToWS;

float3 WSPosToFSPos(float3 wsPos) {
    return mul(_FrustumLightWSToFS, float4(wsPos, 1.0)).xyz;
}

float3 FSPosToWSPos(float3 fsPos) {
    return mul(_FrustumLightFSToWS, float4(fsPos, 1.0)).xyz;
}

// FS UV coordinates map to the footprint plane at FS.z = 0.
float3 FSUVToWSPos(float2 fsUV) {
    return FSPosToWSPos(float3(saturate(fsUV), 0.0));
}

bool WSPosInsideFSFootprint(float3 wsPos) {
    float2 fsPos = WSPosToFSPos(wsPos).xy;
    return all(fsPos >= -1e-4) && all(fsPos <= 1.0 + 1e-4);
}

#endif
