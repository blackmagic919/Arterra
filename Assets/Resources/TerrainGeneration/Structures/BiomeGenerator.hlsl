#include "Assets/Resources/TerrainGeneration/SurfaceChunk/BiomeSampler.hlsl"
#include "Assets/Resources/Utility/PerlinNoiseSampler.hlsl"
#include "Assets/Resources/TerrainGeneration/Structures/StructIDSettings.hlsl"

int SampleBiome (float3 position)
{
    //Sample 2D noise
    position.y = 0.0f;

    float3 sOffset2D = sOffset;
    sOffset2D.y = 0.0f;

    float continentalRaw = GetRawNoise(position, continentalSampler, sOffset2D);
    float erosionRaw = GetRawNoise(position, erosionSampler, sOffset2D);
    float peaksValleysRaw = GetRawNoise(position, PVSampler, sOffset2D);
    float squashRaw = GetRawNoise(position, squashSampler, sOffset2D);
    float atmosphereRaw = GetRawNoise(position, atmosphereSampler, sOffset2D);
    float humidityRaw = GetRawNoise(position, humiditySampler, sOffset2D);

    float mapData[6] = {continentalRaw, erosionRaw, peaksValleysRaw, squashRaw, atmosphereRaw, humidityRaw};
    return GetBiome(mapData);
}
