#include "Assets/Resources/Compute/Utility/PerlinNoiseSampler.hlsl"
#include "Assets/Resources/Compute/TerrainGeneration/SurfaceChunk/SurfBiomeSampler.hlsl"
#include "Assets/Resources/Compute/TerrainGeneration/BaseGeneration/CaveBiomeSampler.hlsl"
#include "Assets/Resources/Compute/TerrainGeneration/Structures/StructIDSettings.hlsl"

int _BSeafloorStart;
int _BSurfaceStart;
int _BCaveStart;
int _BSkyStart;
int _BSeaStart;
int _BIgnoreBiome;

int SampleBiome (float3 position)
{
    float surfData[6];
    surfData[1] = GetRawNoise2D(position.xz, erosionSampler);
    surfData[2] = GetRawNoise2D(position.xz, squashSampler);

    float2 warpOffset = GetDomainWarpOffset2D(position.xz, majorWarpSampler, minorWarpSampler);
    float erosionNoise = interpolateValue(surfData[1], erosionSampler);
    surfData[0] = GetErodedNoise2D(position.xz, erosionNoise, warpOffset, continentalSampler);
    surfData[3] = GetRawNoise2D(position.xz, InfHeightSampler);
    surfData[4] = GetRawNoise2D(position.xz, InfOffsetSampler);
    surfData[5] = GetRawNoise2D(position.xz, atmosphereSampler);

    float InfHeight = interpolateValue(surfData[3], InfHeightSampler);
    float InfOffset = interpolateValue(surfData[4], InfOffsetSampler);
    float infMax = maxInfluenceHeight * (InfHeight * (1 - InfOffset));
    float infMin = -maxInfluenceHeight * (InfHeight * InfOffset);
    
    float squashNoise = interpolateValue(surfData[2], squashSampler) * squashHeight;
    float terrainHeight = surfData[0] * maxTerrainHeight + heightOffset;
    float actualHeight = position.y + sOffset.y;
    float groundHeight = actualHeight - terrainHeight;
    float surfaceBase = terrainHeight - squashNoise;

    float mapData[4];
    mapData[0] = GetRawNoise(position, caveFreqSampler);
    mapData[1] = GetRawNoise(position, caveSizeSampler);
    mapData[2] = GetRawNoise(position, caveShapeSampler);
    mapData[3] =  1.0f - exp(-abs(groundHeight)/heightSFalloff);

    int offset = surfaceBase > waterHeight ? _BSurfaceStart : _BSeafloorStart;
    int surfaceBiome = GetBiome(surfData, offset);

    offset = _BCaveStart;
    if(groundHeight > 0) offset = _BSkyStart;
    if(actualHeight > surfaceBase && actualHeight < waterHeight) offset = _BSeaStart;
    int caveBiome = GetBiome(mapData, offset);
    
    if(surfaceBiome != _BIgnoreBiome
        && infMin <= groundHeight
        && groundHeight <= infMax)
        return surfaceBiome;
    else
        return caveBiome;
}
