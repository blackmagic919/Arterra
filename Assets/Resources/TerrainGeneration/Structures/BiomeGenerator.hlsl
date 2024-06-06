#include "Assets/Resources/TerrainGeneration/SurfaceChunk/BiomeSampler.hlsl"
#include "Assets/Resources/Utility/PerlinNoiseSampler.hlsl"
#include "Assets/Resources/TerrainGeneration/Structures/StructIDSettings.hlsl"

int SampleBiome (float3 position)
{
    //Sample 2D noise
    position.y = 0.0f;

    float3 sOffset2D = sOffset;
    sOffset2D.y = 0.0f;

    float mapData[6];
    mapData[0] = GetRawNoise(position, PVSampler, sOffset2D);
    mapData[1] = GetRawNoise(position, continentalSampler, sOffset2D);
    mapData[2] = GetRawNoise(position, erosionSampler, sOffset2D);
    mapData[3] = GetRawNoise(position, squashSampler, sOffset2D);
    mapData[4] = GetRawNoise(position, atmosphereSampler, sOffset2D);
    mapData[5] = GetRawNoise(position, humiditySampler, sOffset2D);

    float PVNoise = interpolateValue(mapData[0], PVSampler);
    float continentalNoise = interpolateValue(mapData[1], continentalSampler);
    float erosionNoise = interpolateValue(mapData[2], erosionSampler);
    mapData[0] = continentalNoise * (1-heightInfluence) + erosionNoise * PVNoise * heightInfluence;


    return GetBiome(mapData);
}
