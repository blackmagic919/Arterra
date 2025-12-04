#ifndef LMBT_SHADE
#define LMBT_SHADE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

static const float MinBrightness = 0.25f;
static const float NormFalloff = 1.0f;
float3 _LightDirection; //Global Variable

float3 LambertShade(float3 baseAlbedo, float3 normal, float shadow){
    float NdotL = saturate(dot(normal, _LightDirection));
    NdotL = pow(NdotL, NormFalloff);
    NdotL = NdotL * (1-MinBrightness) + MinBrightness; 
    float3 BaseIllumination = NdotL * _MainLightColor.rgb * baseAlbedo;
    float3 MinShadow = baseAlbedo * MinBrightness;
    return BaseIllumination * shadow + MinShadow * (1-shadow);
    //NOTE: You should not be able to tell the time of day if underground(shadow should be same color day and night)
}

#endif