#include "/Noise.hlsl"

//Lower spread results in more spread out values
static const float _SPREAD_FACTOR = 0.5f;

//Noise
uint chunkSize;
uint octaves;
uint meshSkipInc;
StructuredBuffer<float3> offsets;
float persistence;
float lacunarity;
float noiseScale;
float maxPossibleHeight;

StructuredBuffer<float4> SplinePoints; //x = time, y = value
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
    if(numSplinePoints != 0){
        for(uint i = 0; i < numSplinePoints; i++){
            if(SplinePoints[i].x < targetValue)
                index++;//
        }
    }
    return index;
}

float interpolateValue(float value){
    float ret = value;

    uint upperBoundIndex = Search(value);

    if(SplinePoints[upperBoundIndex].x < value || upperBoundIndex == 0) 
        ret = SplinePoints[upperBoundIndex].y;
    else{
        uint lowerBoundIndex = upperBoundIndex - 1;//

        float progress = invLerp(SplinePoints[lowerBoundIndex].x, SplinePoints[upperBoundIndex].x, value);
        float dt = SplinePoints[upperBoundIndex].x - SplinePoints[lowerBoundIndex].x;

        float lowerAnchor = SplinePoints[lowerBoundIndex].y + SplinePoints[lowerBoundIndex].w * dt;
        float upperAnchor = SplinePoints[upperBoundIndex].y - SplinePoints[upperBoundIndex].z * dt;

        float l1 = lerp(SplinePoints[lowerBoundIndex].y, lowerAnchor, progress);
        float l2 = lerp(upperAnchor, SplinePoints[upperBoundIndex].y, progress);

        ret = lerp(l1, l2, progress);
    }
    return ret;
    //
}

float GetRawNoise(uint3 id){
    float center = chunkSize / 2;

    uint x = id.x * meshSkipInc;
    uint y = id.y * meshSkipInc;
    uint z = id.z * meshSkipInc;

    float amplitude = 1;
    float frequency = 1;
    float noiseHeight = 0;
                    
    for(uint i = 0; i < octaves; i++)
    {
        float sampleX = (x-center + offsets[i].x) / noiseScale * frequency;
        float sampleY = (y-center + offsets[i].y) / noiseScale * frequency;
        float sampleZ = (z-center + offsets[i].z) / noiseScale * frequency;

        float perlinValue = snoise(float3(sampleX, sampleY, sampleZ)); //Default range -1 to 1
        noiseHeight += perlinValue * amplitude;
        
        amplitude *= persistence; //amplitude decreases -> effect of samples decreases 
        frequency *= lacunarity; //frequency increases -> size of noise sampling increases -> more random
    }

    float smoothNoise = (sign(noiseHeight) * pow(abs(noiseHeight / maxPossibleHeight), _SPREAD_FACTOR) + 1) / 2;
    //float smoothNoise = invLerp(-maxPossibleHeight, maxPossibleHeight, noiseHeight);
    return smoothNoise;
}

float GetNoise(uint3 id) {
    float rawNoise = GetRawNoise(id);
    return interpolateValue(rawNoise);
}

