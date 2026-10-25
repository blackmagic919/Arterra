#ifndef PERLIN_SAMPLER
#define PERLIN_SAMPLER

#include "Assets/Resources/Compute/Utility/Noise.hlsl"

//Lower spread results in more spread out values
static const float _SPREAD_FACTOR = 0.5f;

//SamplerData
uint skipInc;
float3 sOffset; //Short for sample offset

struct NoiseSetting{
    float noiseScale;
    float persistence;
    float lacunarity;
};

StructuredBuffer<uint2> _NoiseIndexes;
StructuredBuffer<NoiseSetting> _NoiseSettings;
StructuredBuffer<float3> _NoiseOffsets;
StructuredBuffer<float4> _NoiseSplinePoints;

float invLerp(float from, float to, float value){
    return (value - from) / (to - from);
}

//Ideally do binary search... however this file is really corrupted and I'm bad at coding and can't fix it so yeah
uint Search(float targetValue, uint samplerIndex)
{   
    uint index = _NoiseIndexes[samplerIndex].y + 1; //must have at least 2 points
    for(; index < _NoiseIndexes[samplerIndex + 1].y; index++){
        if(_NoiseSplinePoints[index].x >= targetValue)
            break;
    }
    return index;
}

float interpolateValue(float value, uint samplerIndex){
    uint upperBoundIndex = Search(value, samplerIndex);

    float4 upperBound = _NoiseSplinePoints[upperBoundIndex];
    float4 lowerBound = _NoiseSplinePoints[upperBoundIndex - 1];

    float progress = invLerp(lowerBound.x, upperBound.x, value);
    float dt = upperBound.x - lowerBound.x;

    float lowerAnchor = lowerBound.y + lowerBound.w * dt;
    float upperAnchor = upperBound.y - upperBound.z * dt;
    float anchor = (lowerAnchor + upperAnchor) / 2;
    return lerp(
        lerp(lowerBound.y, anchor, progress), 
        lerp(anchor, upperBound.y, progress), 
        progress
    );
}

// Derivative of the same quadratic spline used by interpolateValue.
float interpolateDerivative(float value, uint samplerIndex){
    uint upperBoundIndex = Search(value, samplerIndex);
    float4 upperBound = _NoiseSplinePoints[upperBoundIndex];
    float4 lowerBound = _NoiseSplinePoints[upperBoundIndex - 1];
    float dt = upperBound.x - lowerBound.x;
    float progress = invLerp(lowerBound.x, upperBound.x, value);
    float anchor = (lowerBound.y + lowerBound.w * dt
                  + upperBound.y - upperBound.z * dt) / 2;
    return 2 * lerp(anchor - lowerBound.y, upperBound.y - anchor, progress) / dt;
}

// Returns (mapped value, dValue/dWorldX, dValue/dWorldZ).
// Derivatives are with respect to base grid units, independent of skipInc.
float3 GetNoise2DGradient(float2 id, uint samplerIndex){
    NoiseSetting settings = _NoiseSettings[samplerIndex];
    float2 position = id * skipInc;
    float amplitude = 1;
    float frequency = 1;
    float maxHeight = 0;
    float3 result = 0;
    for(uint i = _NoiseIndexes[samplerIndex].x; i < _NoiseIndexes[samplerIndex + 1].x; i++){
        float scale = frequency / settings.noiseScale;
        float2 sample = (position + _NoiseOffsets[i].xy + sOffset.xz) / settings.noiseScale * frequency;
        float3 octave = snoise2D_grad(sample);
        result += float3(octave.x, octave.yz * scale) * amplitude;
        maxHeight += amplitude;
        amplitude *= settings.persistence;
        frequency *= settings.lacunarity;
    }
    result /= 2 * maxHeight;
    result.x += 0.5;
    result.yz *= interpolateDerivative(result.x, samplerIndex);
    result.x = interpolateValue(result.x, samplerIndex);
    return result;
}

float GetRawNoise(float3 id, uint samplerIndex, float3 sOffset){
    NoiseSetting settings = _NoiseSettings[samplerIndex];
    float3 sPos = id * skipInc;

    float amplitude = 1;
    float frequency = 1;
    float noiseHeight = 0;
    float maxHeight = 0;
                    
    for(uint i = _NoiseIndexes[samplerIndex].x; i < _NoiseIndexes[samplerIndex + 1].x; i++)
    {
        float3 sample = (sPos + _NoiseOffsets[i] + sOffset) / settings.noiseScale * frequency;
        float perlinValue = snoise(sample); //Default range -1 to 1
        noiseHeight += perlinValue * amplitude;
        maxHeight += amplitude;
        
        amplitude *= settings.persistence; //amplitude decreases -> effect of samples decreases 
        frequency *= settings.lacunarity; //frequency increases -> size of noise sampling increases -> more random
    }

    return (noiseHeight / maxHeight + 1.0f) / 2.0f;
}


float GetRawNoise2D(float2 id, uint samplerIndex, float2 sOffset){
    NoiseSetting settings = _NoiseSettings[samplerIndex];
    float2 sPos = id * skipInc;

    float amplitude = 1;
    float frequency = 1;
    float noiseHeight = 0;
    float maxHeight = 0;
    
    for(uint i = _NoiseIndexes[samplerIndex].x; i < _NoiseIndexes[samplerIndex + 1].x; i++)
    {
        float2 sample = (sPos + _NoiseOffsets[i].xy + sOffset) / settings.noiseScale * frequency;
        float perlinValue = snoise2D(sample);
        noiseHeight += perlinValue * amplitude;
        maxHeight += amplitude;
        
        amplitude *= settings.persistence; //amplitude decreases -> effect of samples decreases 
        frequency *= settings.lacunarity; //frequency increases -> size of noise sampling increases -> more random
    }

    return (noiseHeight / maxHeight + 1.0f) / 2.0f; //Default range -1 to 1
}


float GetRawNoise(int3 id, uint samplerIndex){ return GetRawNoise(id, samplerIndex, sOffset); }

float GetRawNoise2D(uint2 id, uint samplerIndex){ return GetRawNoise2D(id, samplerIndex, sOffset.xz); }
float GetRawNoise2D(float2 id, uint samplerIndex){ return GetRawNoise2D(id, samplerIndex, sOffset.xz); }
float GetNoise2D(uint2 id, uint samplerIndex){ return interpolateValue(GetRawNoise2D(id, samplerIndex, sOffset.xz), samplerIndex); }
float GetNoise2D(float2 id, uint samplerIndex){ return interpolateValue(GetRawNoise2D(id, samplerIndex, sOffset.xz), samplerIndex); }

float GetNoise(uint3 id, uint samplerIndex) { return interpolateValue(GetRawNoise(id, samplerIndex), samplerIndex); }
float GetNoise(float3 id, uint samplerIndex) { return interpolateValue(GetRawNoise(id, samplerIndex), samplerIndex); }


float3 SampleDerivNoise2D(float2 position){
    return snoise2D_grad(position);
}

float4 SampleDerivNoise3D(float3 position){
    return snoise_grad(position);
}


// Jacobian rows are gradients of warp.x and warp.y in base grid units.
float2 GetDomainWarpOffset2D(float2 id, uint majorWarp, uint minorWarp, out float2x2 warpJacobian){
    float2 position = id * skipInc;
    float3 majorX = GetNoise2DGradient(id, majorWarp) * 4.0;
    float3 majorY = GetNoise2DGradient((position + float2(5.2, 1.3)) / skipInc, majorWarp) * 4.0;
    float2 q = float2(majorX.x, majorY.x);
    float2x2 innerJacobian = float2x2(1 + majorX.y, majorX.z,
                                     majorY.y, 1 + majorY.z);
    float3 minorX = GetNoise2DGradient((position + q + float2(1.7, 9.2)) / skipInc, minorWarp);
    float3 minorY = GetNoise2DGradient((position + q + float2(8.3, 2.8)) / skipInc, minorWarp);
    float2 dx = mul(minorX.yz, innerJacobian);
    float2 dy = mul(minorY.yz, innerJacobian);
    warpJacobian = float2x2(dx.x, dx.y, dy.x, dy.y);
    return float2(minorX.x, minorY.x);
}

float GetErodedNoise2D(float2 id, float erosion, float2 warpOffset, float2x2 warpJacobian, uint samplerIndex){
    NoiseSetting settings = _NoiseSettings[samplerIndex];
    float2 sPos = id * skipInc;

    float amplitude = 1;
    float frequency = 1;
    float maxHeight = 0;
    float3 noiseHeight = 0;
    for(uint i = _NoiseIndexes[samplerIndex].x; i < _NoiseIndexes[samplerIndex + 1].x; i++) {
        float2 sample = (sPos + _NoiseOffsets[i].xy + sOffset.xz) / settings.noiseScale * frequency + warpOffset;
        float3 simplexValue = SampleDerivNoise2D(sample);

        // sample = position * scale + warp(position). Apply its Jacobian,
        // then divide by scale to retain the existing unwarped erosion units.
        // This is an erosion slope estimate, not the full derivative of the
        // height: derivatives of the adaptive octave weights are not included.

        float scale = frequency / settings.noiseScale;
        float2 erosionGradient = simplexValue.yz + mul(simplexValue.yz, warpJacobian) / scale;
        float octaveAmplitude = amplitude / (1 + dot(noiseHeight.yz, noiseHeight.yz) * erosion);
        
        noiseHeight.yz += erosionGradient * octaveAmplitude;
        noiseHeight.x += simplexValue.x * octaveAmplitude;
        maxHeight += amplitude;
        
        amplitude *= settings.persistence; //amplitude decreases -> effect of samples decreases 
        frequency *= settings.lacunarity; //frequency increases -> size of noise sampling increases -> more random
    }

    return interpolateValue((noiseHeight.x / maxHeight + 1) / 2.0f, samplerIndex);
}


float GetErodedNoise3D(float3 id, float erosion, float3 warpOffset, uint samplerIndex){
    NoiseSetting settings = _NoiseSettings[samplerIndex];
    float3 sPos = id * skipInc;

    float amplitude = 1;
    float frequency = 1;
    float maxHeight = 0;
    float4 noiseHeight = 0;
    for(uint i = _NoiseIndexes[samplerIndex].x; i < _NoiseIndexes[samplerIndex + 1].x; i++) {
        float3 sample = (sPos + _NoiseOffsets[i] + sOffset) / settings.noiseScale * frequency + warpOffset;
        float4 simplexValue = SampleDerivNoise3D(sample);

        float octaveAmplitude = amplitude / (1 + dot(noiseHeight.yzw, noiseHeight.yzw) * erosion);
        noiseHeight.yzw += simplexValue.yzw * octaveAmplitude;
        noiseHeight.x += simplexValue.x * octaveAmplitude;
        maxHeight += amplitude;
        
        amplitude *= settings.persistence; //amplitude decreases -> effect of samples decreases 
        frequency *= settings.lacunarity; //frequency increases -> size of noise sampling increases -> more random
    }

    return interpolateValue((noiseHeight.x / maxHeight + 1) / 2.0f, samplerIndex);
}

#endif
