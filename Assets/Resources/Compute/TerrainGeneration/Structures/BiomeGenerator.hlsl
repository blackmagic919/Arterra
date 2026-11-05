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

    float2x2 warpJacobian;
    float2 warpOffset = GetDomainWarpOffset2D(position.xz, majorWarpSampler, minorWarpSampler, warpJacobian);
    float erosionNoise = interpolateValue(surfData[1], erosionSampler);
    float2 terrainSample = GetErodedNoise2D(position.xz, erosionNoise, warpOffset, warpJacobian, continentalSampler);
    surfData[0] = terrainSample.x;
    surfData[3] = GetRawNoise2D(position.xz, InfHeightSampler);
    surfData[4] = terrainSample.y; // Steepness
    surfData[5] = GetRawNoise2D(position.xz, atmosphereSampler);

    float infHeight = interpolateValue(surfData[3], InfHeightSampler);
    float infOffset = GetNoise2D(position.xz, InfOffsetSampler);
    float infMax = maxInfluenceHeight * (infHeight * (1 - infOffset));
    float infMin = -maxInfluenceHeight * (infHeight * infOffset);
    
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

float SampleHeightFactor(float3 position) {
    float2x2 warpJacobian;
    float2 warpOffset = GetDomainWarpOffset2D(position.xz, majorWarpSampler, minorWarpSampler, warpJacobian);
    float erosionNoise = GetNoise2D(position.xz, erosionSampler);
    float terrainHeight = GetErodedNoise2D(position.xz, erosionNoise, warpOffset, warpJacobian, continentalSampler).x;
    float infHeight = GetNoise2D(position.xz, InfHeightSampler);
    float infOffset = GetNoise2D(position.xz, InfOffsetSampler);
    float infMax = maxInfluenceHeight * (infHeight * (1 - infOffset));
    float infMin = -maxInfluenceHeight * (infHeight * infOffset);

    terrainHeight = terrainHeight * maxTerrainHeight + heightOffset;
    float actualHeight = position.y + sOffset.y;
    float groundHeight = actualHeight - terrainHeight;

    return groundHeight > 0 ? groundHeight / infMax : groundHeight / infMin;
}
