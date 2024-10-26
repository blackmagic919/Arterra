#include "Assets/Resources/Compute/Utility/Noise.hlsl"

//Lower spread results in more spread out values
static const float _SPREAD_FACTOR = 0.5f;
static const uint _MAX_OCTAVES = 10;
static const uint _MAX_SPLINE = 20;

//Noise
uint chunkSize;
uint meshSkipInc;
float persistence;
float lacunarity;
float noiseScale;

float3 sOffset; //Short for sample offset
//We use arrays instead of buffers because faster and no need to manage
float4 offsets[_MAX_OCTAVES];
uint octaves;

float4 SplinePoints[_MAX_SPLINE]; //x = time, y = value
uint numSplinePoints;

float lerp(float a, float b, float t){
    return a + (b-a) * t;
}

float invLerp(float from, float to, float value){
    return (value - from) / (to - from);
}

//Ideally do binary search... however this file is really corrupted and I'm bad at coding and can't fix it so yeah
uint Search(float targetValue)
{
    uint index = 0;
    for(uint i = 0; i < numSplinePoints; i++){
        if(SplinePoints[i].x < targetValue)
            index++;//
    }
    return index;
}

float interpolateValue(float value){
    uint upperBoundIndex = Search(value);
    if(SplinePoints[upperBoundIndex].x < value || upperBoundIndex == 0) return SplinePoints[upperBoundIndex].y;
    else{
        uint lowerBoundIndex = upperBoundIndex - 1;//

        float progress = invLerp(SplinePoints[lowerBoundIndex].x, SplinePoints[upperBoundIndex].x, value);
        float dt = SplinePoints[upperBoundIndex].x - SplinePoints[lowerBoundIndex].x;

        float lowerAnchor = SplinePoints[lowerBoundIndex].y + SplinePoints[lowerBoundIndex].w * dt;
        float upperAnchor = SplinePoints[upperBoundIndex].y - SplinePoints[upperBoundIndex].z * dt;

        return lerp(
            lerp(SplinePoints[lowerBoundIndex].y, lowerAnchor, progress),
            lerp(upperAnchor, SplinePoints[upperBoundIndex].y, progress),
            progress
        );
    }
}

float GetRawNoise(int3 id){
    int x = id.x * meshSkipInc;
    int y = id.y * meshSkipInc;
    int z = id.z * meshSkipInc;

    float amplitude = 1;
    float frequency = 1;
    float noiseHeight = 0;
                    
    for(uint i = 0; i < octaves; i++)
    {
        float sampleX = (x + offsets[i].x + sOffset.x) / noiseScale * frequency;
        float sampleY = (y + offsets[i].y + sOffset.y) / noiseScale * frequency;
        float sampleZ = (z + offsets[i].z + sOffset.z) / noiseScale * frequency;

        float perlinValue = (snoise(float3(sampleX, sampleY, sampleZ)) + 1) / 2.0f; //Default range -1 to 1
        noiseHeight = (1 - amplitude) * noiseHeight + perlinValue * amplitude;
        
        amplitude *= persistence; //amplitude decreases -> effect of samples decreases 
        frequency *= lacunarity; //frequency increases -> size of noise sampling increases -> more random
    }

    //float smoothNoise = invLerp(-maxPossibleHeight, maxPossibleHeight, noiseHeight);
    return noiseHeight;
}

float GetRawNoise(float3 id){
    //Implicit conversion eliminates decimal
    return GetRawNoise(int3(floor(id.x), floor(id.y), floor(id.z)));
}


float GetNoise(uint3 id) {
    float rawNoise = GetRawNoise(id);
    return interpolateValue(rawNoise);
}

