#include "Assets/Resources/TerrainGeneration/SurfaceChunk/BiomeSampler.hlsl"
#include "Assets/Resources/Utility/PerlinNoiseSampler.hlsl"
#include "Assets/Resources/TerrainGeneration/Structures/StructIDSettings.hlsl"

int SampleBiome (float3 position)
{
    float3 sOffset2D = sOffset;
    sOffset2D.y = 0.0f; position.y = 0.0f;

    float mapData[6];
    mapData[1] = GetRawNoise(position, erosionSampler, sOffset2D);
    mapData[2] = GetRawNoise(position, squashSampler, sOffset2D);
    mapData[3] = GetRawNoise(position, PVSampler, sOffset2D);
    mapData[4] = GetRawNoise(position, continentalSampler, sOffset2D);

    float PVNoise = interpolateValue(mapData[3], PVSampler) * 2 - 1;
    float continentalNoise = interpolateValue(mapData[4], continentalSampler);
    float erosionNoise = interpolateValue(mapData[1], erosionSampler);

    mapData[0] = continentalNoise + PVNoise * erosionNoise;
    mapData[3] = GetRawNoise(position, caveFreqSampler, sOffset2D);
    mapData[4] = GetRawNoise(position, caveSizeSampler, sOffset2D);
    mapData[5] = GetRawNoise(position, caveShapeSampler, sOffset2D);

    return GetBiome(mapData);
}
