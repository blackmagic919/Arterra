#ifndef PERLIN_SAMPLER
#define PERLIN_SAMPLER

#include "Assets/Resources/Compute/Utility/Noise.hlsl"

//Lower spread results in more spread out values
static const float _SPREAD_FACTOR = 0.5f;

//SamplerData
uint chunkSize;
uint meshSkipInc;
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

float GetRawNoise(int3 id, uint samplerIndex, float3 sOffset){
    NoiseSetting settings = _NoiseSettings[samplerIndex];
    int3 sPos = id * meshSkipInc;

    float amplitude = 1;
    float frequency = 1;
    float noiseHeight = 0;
                    
    for(uint i = _NoiseIndexes[samplerIndex].x; i < _NoiseIndexes[samplerIndex + 1].x; i++)
    {
        float3 sample = (sPos + _NoiseOffsets[i] + sOffset) / settings.noiseScale * frequency;
        float perlinValue = (snoise(sample) + 1) / 2.0f; //Default range -1 to 1
        noiseHeight = lerp(noiseHeight, perlinValue, amplitude);
        
        amplitude *= settings.persistence; //amplitude decreases -> effect of samples decreases 
        frequency *= settings.lacunarity; //frequency increases -> size of noise sampling increases -> more random
    }

    return noiseHeight;
}

float GetRawNoise2D(int2 id, uint samplerIndex, float2 sOffset){
    NoiseSetting settings = _NoiseSettings[samplerIndex];
    int2 sPos = id * meshSkipInc;

    float amplitude = 1;
    float frequency = 1;
    float noiseHeight = 0;
                    
    for(uint i = _NoiseIndexes[samplerIndex].x; i < _NoiseIndexes[samplerIndex + 1].x; i++)
    {
        float2 sample = (sPos + _NoiseOffsets[i].xy + sOffset) / settings.noiseScale * frequency;
        float perlinValue = (snoise2D(sample) + 1) / 2.0f; //Default range -1 to 1
        noiseHeight = lerp(noiseHeight, perlinValue, amplitude);
        
        amplitude *= settings.persistence; //amplitude decreases -> effect of samples decreases 
        frequency *= settings.lacunarity; //frequency increases -> size of noise sampling increases -> more random
    }

    return noiseHeight;
}



float GetRawNoise(int3 id, uint samplerIndex){ return GetRawNoise(id, samplerIndex, sOffset); }
float GetRawNoise(float3 id, uint samplerIndex){ return GetRawNoise(int3(floor(id)), samplerIndex); }

float GetRawNoise2D(uint2 id, uint samplerIndex){ return GetRawNoise2D(id, samplerIndex, sOffset.xz); }
float GetRawNoise2D(float2 id, uint samplerIndex){ return GetRawNoise2D(int2(floor(id)), samplerIndex, sOffset.xz); }
float GetNoise2D(uint2 id, uint samplerIndex){ return interpolateValue(GetRawNoise2D(id, samplerIndex, sOffset.xz), samplerIndex); }
float GetNoise2D(float2 id, uint samplerIndex){ return interpolateValue(GetRawNoise2D(int2(floor(id)), samplerIndex, sOffset.xz), samplerIndex); }

float GetNoise(uint3 id, uint samplerIndex) { return interpolateValue(GetRawNoise(id, samplerIndex), samplerIndex); }
float GetNoise(float3 id, uint samplerIndex) { return interpolateValue(GetRawNoise(id, samplerIndex), samplerIndex); }

#endif
