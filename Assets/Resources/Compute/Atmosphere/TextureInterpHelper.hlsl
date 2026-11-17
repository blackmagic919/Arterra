#include "Assets/Resources/Compute/Utility/GetIndex.hlsl"
#include "Assets/Resources/Compute/Utility/BlendHelper.hlsl"

struct ScatterData {
    float3 inScatteredLight;
    float3 extinction;
};

uint SampleTextureWidth;
uint SampleTextureHeight;
uint _NumInScatterPoints;
float _AtmosphereRadius;
float4 _AtmospherePixelSize;

uint GetTextureIndex(uint2 sampleCoord, uint depth) {
    return indexFromCoordIrregular(sampleCoord.x, sampleCoord.y, depth, SampleTextureHeight, _NumInScatterPoints);
}

Influences2D GetLookupBlend(float2 uv) {
    return GetBlendInfo(saturate(uv) * uint2(SampleTextureWidth - 1u, SampleTextureHeight - 1u));
}

float GetScatterStep() {
    return _AtmosphereRadius / max((float)_NumInScatterPoints - 1.0f, 1.0f);
}

uint GetScatterLayer(float rayLength, float sampleDist) {
    if(rayLength >= _AtmosphereRadius)
        return _NumInScatterPoints - 1u;

    return (uint)clamp(floor(rayLength / max(sampleDist, 1e-6f)), 0.0f, (float)_NumInScatterPoints - 1.0f);
}

float3 GetViewVectorWS(float2 uv) {
    float3 viewVector = mul(unity_CameraInvProjection, float4(uv * 2.0f - 1.0f, 0.0f, -1.0f)).xyz;
    return mul(unity_CameraToWorld, float4(viewVector, 0.0f)).xyz;
}

float2 GetAtmospherePixelUV(uint2 pixel) {
    return (float2(pixel) + 0.5f) * _AtmospherePixelSize.zw;
}

uint GetRemainderIndex(uint2 pixel) {
    return pixel.y * (uint)_AtmospherePixelSize.x + pixel.x;
}

float GetAtmosphereRayLength(float rawDepth, float3 viewVector) {
    return clamp(LinearEyeDepth(rawDepth, _ZBufferParams) * length(viewVector), 0.0f, _AtmosphereRadius);
}
