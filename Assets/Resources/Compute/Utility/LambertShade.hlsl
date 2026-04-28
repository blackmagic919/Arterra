#ifndef LMBT_SHADE
#define LMBT_SHADE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Assets/Resources/Compute/MapData/WSLightSampler.hlsl"

static const float MinBrightness = 0.25f;
float3 _LightDirection; //Global Variable

float3 AddUnityBaseLights(float3 baseAlbedo, float3 normal, float3 positionWS){
    // Add main directional light
    float NdotL = saturate(dot(normal, _LightDirection));
    NdotL = NdotL * (1 - MinBrightness) + MinBrightness;
    float3 color = NdotL * _MainLightColor * baseAlbedo;
    // Add additional lights
    /*uint lightsCount = GetAdditionalLightsCount();
    for (uint i = 1u; i < lightsCount; ++i) //first additional light is the moon
    {
        Light light = GetAdditionalLight(i, positionWS);
        float NdotLAdd = saturate(dot(normal, light.direction));
        NdotLAdd = NdotLAdd * (1 - MinBrightness) + MinBrightness;
        float3 add = NdotLAdd * light.color.rgb * baseAlbedo * light.distanceAttenuation * light.shadowAttenuation;
        color += add;
    }*/
    return color;
}



float4 LambertShadeInternal(float3 baseAlbedo, uint light, float3 BaseIllumination){
    float shadow = 1.0 - (light >> 30 & 0x3) / 3.0f;
    float3 ObjectLight = float3(light & 0x3FF, (light >> 10) & 0x3FF, (light >> 20) & 0x3FF) / 1023.0f;
    ObjectLight = mad((1 - ObjectLight), unity_AmbientGround, ObjectLight * 2.5f); //linear interpolation
    ObjectLight *= baseAlbedo.rgb;

    float3 MinShadow = baseAlbedo * MinBrightness;
    float3 DynamicLight = BaseIllumination * shadow + MinShadow * (1-shadow);
    
    return float4(max(DynamicLight, ObjectLight), 1);
}

float4 LambertShade(float3 baseAlbedo, float3 normal, float3 positionWS){
    uint light = SampleLight(positionWS);
    float3 BaseIllumination = AddUnityBaseLights(baseAlbedo, normal, positionWS);
    return LambertShadeInternal(baseAlbedo, light, BaseIllumination);
}


#endif