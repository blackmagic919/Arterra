#ifndef LMBT_SHADE
#define LMBT_SHADE

static const float MinBrightness = 0.25f;
static const float NormFalloff = 5.0f;
float3 _LightDirection; //Global Variable
float3 LambertShade(float3 baseAlbedo, float3 normal, float shadow){
    float NdotL = clamp(dot(normal, _LightDirection) + 1 * 0.5f, 0, 1);
    NdotL = pow(NdotL, NormFalloff);
    NdotL = NdotL * (1-MinBrightness) + MinBrightness; 
    float3 BaseIllumination = NdotL * _MainLightColor.rgb * baseAlbedo;
    float3 MinShadow = baseAlbedo * MinBrightness;
    return BaseIllumination * shadow + MinShadow * (1-shadow);
    //NOTE: You should not be able to tell the time of day if underground(shadow should be same color day and night)
}

#endif