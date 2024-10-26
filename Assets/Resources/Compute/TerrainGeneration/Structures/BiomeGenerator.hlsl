#include "Assets/Resources/Compute/TerrainGeneration/SurfaceChunk/BiomeSampler.hlsl"
#include "Assets/Resources/Compute/Utility/PerlinNoiseSampler.hlsl"
#include "Assets/Resources/Compute/TerrainGeneration/Structures/StructIDSettings.hlsl"

int SampleBiome (float3 position)
{
    float mapData[6];
    mapData[1] = GetRawNoise2D(position.xz, erosionSampler);
    mapData[2] = GetRawNoise2D(position.xz, squashSampler);
    mapData[3] = GetRawNoise2D(position.xz, PVSampler);
    mapData[4] = GetRawNoise2D(position.xz, continentalSampler);

    float PVNoise = interpolateValue(mapData[3], PVSampler) * 2 - 1;
    float continentalNoise = interpolateValue(mapData[4], continentalSampler);
    float erosionNoise = interpolateValue(mapData[1], erosionSampler);

    mapData[0] = continentalNoise + PVNoise * erosionNoise;
    mapData[3] = GetRawNoise2D(position.xz, caveFreqSampler);
    mapData[4] = GetRawNoise2D(position.xz, caveSizeSampler);
    mapData[5] = GetRawNoise2D(position.xz, caveShapeSampler);

    return GetBiome(mapData);
}
