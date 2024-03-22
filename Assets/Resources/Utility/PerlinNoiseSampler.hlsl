#ifndef PERLIN_SAMPLER
#define PERLIN_SAMPLER

#include "Assets/Resources/Utility/Noise.hlsl"

//Lower spread results in more spread out values
static const float _SPREAD_FACTOR = 0.5f;

//Noise
uint chunkSize;
uint meshSkipInc;
float3 sOffset; //Short for sample offset

struct NoiseSetting{
    float noiseScale;
    float persistence;
    float lacunarity;
    float maxPossibleHeight;
};

StructuredBuffer<uint2> _NoiseIndexes;
StructuredBuffer<NoiseSetting> _NoiseSettings;
StructuredBuffer<float3> _NoiseOffsets;
StructuredBuffer<float4> _NoiseSplinePoints;

float lerp(float a, float b, float t){
    return a + (b-a) * t;
}

float invLerp(float from, float to, float value){
    return (value - from) / (to - from);
}

//Ideally do binary search... however this file is really corrupted and I'm bad at coding and can't fix it so yeah
uint Search(float targetValue, uint samplerIndex)
{
    uint splinePointStart = _NoiseIndexes[samplerIndex].y;
    uint splinePointEnd = _NoiseIndexes[samplerIndex + 1].y;
    uint index = splinePointStart;

    for(uint i = splinePointStart; i < splinePointEnd; i++){
        if(_NoiseSplinePoints[i].x < targetValue)
            index++;//
    }
    return index;
}

float interpolateValue(float value, uint samplerIndex){
    float ret = value;

    uint upperBoundIndex = Search(value, samplerIndex);

    if(upperBoundIndex == _NoiseIndexes[samplerIndex].y || _NoiseSplinePoints[upperBoundIndex].x < value) 
        ret = _NoiseSplinePoints[upperBoundIndex].y;
    else{
        uint lowerBoundIndex = upperBoundIndex - 1;//

        float progress = invLerp(_NoiseSplinePoints[lowerBoundIndex].x, _NoiseSplinePoints[upperBoundIndex].x, value);
        float dt = _NoiseSplinePoints[upperBoundIndex].x - _NoiseSplinePoints[lowerBoundIndex].x;

        float lowerAnchor = _NoiseSplinePoints[lowerBoundIndex].y + _NoiseSplinePoints[lowerBoundIndex].w * dt;
        float upperAnchor = _NoiseSplinePoints[upperBoundIndex].y - _NoiseSplinePoints[upperBoundIndex].z * dt;

        float l1 = lerp(_NoiseSplinePoints[lowerBoundIndex].y, lowerAnchor, progress);
        float l2 = lerp(upperAnchor, _NoiseSplinePoints[upperBoundIndex].y, progress);

        ret = lerp(l1, l2, progress);
    }
    return ret;
}

float GetRawNoise(int3 id, uint samplerIndex, float3 sOffset){
    float center = chunkSize / 2;

    int x = id.x * meshSkipInc;
    int y = id.y * meshSkipInc;
    int z = id.z * meshSkipInc;

    uint octaveStart = _NoiseIndexes[samplerIndex].x;
    uint octaveEnd = _NoiseIndexes[samplerIndex + 1].x;
    NoiseSetting settings = _NoiseSettings[samplerIndex];

    float amplitude = 1;
    float frequency = 1;
    float noiseHeight = 0;
                    
    for(uint i = octaveStart; i < octaveEnd; i++)
    {
        float3 offset = _NoiseOffsets[i];
        float sampleX = (x-center + offset.x + sOffset.x) / settings.noiseScale * frequency;
        float sampleY = (y-center + offset.y + sOffset.y) / settings.noiseScale * frequency;
        float sampleZ = (z-center + offset.z + sOffset.z) / settings.noiseScale * frequency;

        float perlinValue = snoise(float3(sampleX, sampleY, sampleZ)); //Default range -1 to 1
        noiseHeight += perlinValue * amplitude;
        
        amplitude *= settings.persistence; //amplitude decreases -> effect of samples decreases 
        frequency *= settings.lacunarity; //frequency increases -> size of noise sampling increases -> more random
    }

    float smoothNoise = (sign(noiseHeight) * pow(abs(noiseHeight / settings.maxPossibleHeight), _SPREAD_FACTOR) + 1) / 2;
    return smoothNoise;
}

float GetRawNoise(int3 id, uint samplerIndex){
    return GetRawNoise(id, samplerIndex, sOffset);
}

float GetRawNoise(float3 id, uint samplerIndex){
    //Implicit conversion eliminates decimal
    return GetRawNoise(int3(floor(id.x), floor(id.y), floor(id.z)), samplerIndex);
}

float GetRawNoise(float3 id, uint samplerIndex, float3 sOffset){
    return GetRawNoise(int3(floor(id.x), floor(id.y), floor(id.z)), samplerIndex, sOffset);
}

float GetNoise(uint3 id, uint samplerIndex) {
    float rawNoise = GetRawNoise(id, samplerIndex);
    return interpolateValue(rawNoise, samplerIndex);
}

float GetNoise(float3 id, uint samplerIndex) {
    float rawNoise = GetRawNoise(id, samplerIndex);
    return interpolateValue(rawNoise, samplerIndex);
}

float GetNoise(float3 id, uint samplerIndex, float3 sOffset){
    float rawNoise = GetRawNoise(id, samplerIndex, sOffset);
    return interpolateValue(rawNoise, samplerIndex);
}

#endif
