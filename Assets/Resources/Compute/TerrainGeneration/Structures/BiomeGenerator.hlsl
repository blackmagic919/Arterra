#include "Assets/Resources/Compute/Utility/PerlinNoiseSampler.hlsl"
#include "Assets/Resources/Compute/TerrainGeneration/SurfaceChunk/SurfBiomeSampler.hlsl"
#include "Assets/Resources/Compute/TerrainGeneration/BaseGeneration/CaveBiomeSampler.hlsl"
#include "Assets/Resources/Compute/TerrainGeneration/Structures/StructIDSettings.hlsl"

int _BSkyStart;

int SampleBiome (float3 position)
{
    float surfData[6];
    surfData[1] = GetRawNoise2D(position.xz, erosionSampler);
    surfData[2] = GetRawNoise2D(position.xz, squashSampler);
    surfData[3] = GetRawNoise2D(position.xz, PVSampler);
    surfData[4] = GetRawNoise2D(position.xz, continentalSampler);

    float PVNoise = interpolateValue(surfData[3], PVSampler) * 2 - 1;
    float continentalNoise = interpolateValue(surfData[4], continentalSampler);
    float erosionNoise = interpolateValue(surfData[1], erosionSampler);

    surfData[0] = continentalNoise + PVNoise * erosionNoise;
    surfData[3] = GetRawNoise2D(position.xz, InfHeightSampler);
    surfData[4] = GetRawNoise2D(position.xz, InfOffsetSampler);
    surfData[5] = GetRawNoise2D(position.xz, atmosphereSampler);
    int surfaceBiome = GetBiome(surfData);

    float InfHeight = interpolateValue(surfData[3], InfHeightSampler);
    float InfOffset = interpolateValue(surfData[4], InfOffsetSampler);
    float infMax = maxInfluenceHeight * (InfHeight * (1 - InfOffset));
    float infMin = -maxInfluenceHeight * (InfHeight * InfOffset);
    
    float terrainHeight = surfData[0] * maxTerrainHeight + heightOffset;
    float actualHeight = position.y + sOffset.y;
    float groundHeight = actualHeight - terrainHeight;

    float mapData[4];
    mapData[0] = GetRawNoise(position, caveFreqSampler);
    mapData[1] = GetRawNoise(position, caveSizeSampler);
    mapData[2] = GetRawNoise(position, caveShapeSampler);
    mapData[3] =  1.0f - exp(-abs(groundHeight)/heightSFalloff);

    int offset = groundHeight > 0 ? _BSkyStart : 0;
    int caveBiome = GetBiome(mapData, offset);

    if(infMin <= groundHeight && groundHeight <= infMax)
        return surfaceBiome;
    else
        return caveBiome;
}
