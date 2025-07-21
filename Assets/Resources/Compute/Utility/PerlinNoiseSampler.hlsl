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
        float perlinValue = (snoise(sample) + 1) / 2.0f; //Default range -1 to 1
        noiseHeight += perlinValue * amplitude;
        maxHeight += amplitude;
        
        amplitude *= settings.persistence; //amplitude decreases -> effect of samples decreases 
        frequency *= settings.lacunarity; //frequency increases -> size of noise sampling increases -> more random
    }

    return noiseHeight / maxHeight;
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
        float perlinValue = (snoise2D(sample) + 1) / 2.0f; //Default range -1 to 1
        noiseHeight += perlinValue * amplitude;
        maxHeight += amplitude;
        
        amplitude *= settings.persistence; //amplitude decreases -> effect of samples decreases 
        frequency *= settings.lacunarity; //frequency increases -> size of noise sampling increases -> more random
    }

    return noiseHeight / maxHeight;
}


float GetRawNoise(int3 id, uint samplerIndex){ return GetRawNoise(id, samplerIndex, sOffset); }

float GetRawNoise2D(uint2 id, uint samplerIndex){ return GetRawNoise2D(id, samplerIndex, sOffset.xz); }
float GetRawNoise2D(float2 id, uint samplerIndex){ return GetRawNoise2D(id, samplerIndex, sOffset.xz); }
float GetNoise2D(uint2 id, uint samplerIndex){ return interpolateValue(GetRawNoise2D(id, samplerIndex, sOffset.xz), samplerIndex); }
float GetNoise2D(float2 id, uint samplerIndex){ return interpolateValue(GetRawNoise2D(id, samplerIndex, sOffset.xz), samplerIndex); }

float GetNoise(uint3 id, uint samplerIndex) { return interpolateValue(GetRawNoise(id, samplerIndex), samplerIndex); }
float GetNoise(float3 id, uint samplerIndex) { return interpolateValue(GetRawNoise(id, samplerIndex), samplerIndex); }


float3 SampleDerivNoise2D(float2 position, float stepSize){
    float s00 = snoise2D(position);
    float s01 = snoise2D(position + float2(0, stepSize));
    float s10 = snoise2D(position + float2(stepSize, 0));

    float dx = (s10 - s00) / stepSize;
    float dy = (s01 - s00) / stepSize;

    return float3(s00, dx, dy);
}

float2 GetDomainWarpOffset2D(float2 position, uint warpSampler){
    float2 q = float2(
        GetNoise2D((position + float2(0, 0)) / skipInc, warpSampler),
        GetNoise2D((position + float2(5.2f, 1.3f)) / skipInc, warpSampler)
    );
    return 4.0f * q;
}

float GetErodedNoise2D(float2 id, float erosion, uint warpSampler, uint samplerIndex){
    NoiseSetting settings = _NoiseSettings[samplerIndex];
    float2 sPos = id * skipInc;

    float amplitude = 1;
    float frequency = 1;
    float maxHeight = 0;
    float3 noiseHeight = 0;

    float2 warpOffset = GetDomainWarpOffset2D(sPos, warpSampler);
    for(uint i = _NoiseIndexes[samplerIndex].x; i < _NoiseIndexes[samplerIndex + 1].x; i++)
    {
        float2 sample = (sPos + _NoiseOffsets[i].xy + sOffset.xz) / settings.noiseScale * frequency + warpOffset;
        float3 simplexValue = SampleDerivNoise2D(sample, 1 / settings.noiseScale * frequency);
        simplexValue.x = simplexValue.x;

        float octaveAmplitude = amplitude / (1 + dot(noiseHeight.yz, noiseHeight.yz) * erosion);
        noiseHeight.yz += simplexValue.yz * octaveAmplitude;
        noiseHeight.x += simplexValue.x * octaveAmplitude;
        maxHeight += amplitude;
        
        amplitude *= settings.persistence; //amplitude decreases -> effect of samples decreases 
        frequency *= settings.lacunarity; //frequency increases -> size of noise sampling increases -> more random
    }

    return interpolateValue((noiseHeight.x / maxHeight + 1) / 2.0f, samplerIndex);
}

#endif
