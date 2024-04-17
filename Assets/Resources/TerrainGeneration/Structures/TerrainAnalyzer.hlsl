#include "Assets/Resources/Utility/PerlinNoiseSampler.hlsl"
#include "Assets/Resources/TerrainGeneration/Structures/StructIDSettings.hlsl"
const static int Epsilon = 0.0001;

struct CaveGen{
    float coarse;
    float shape;
    float frequency;
};

float GetNoiseCentered(float val, float center){
    float clamped = clamp(val, 0, 1);
    float ret = (clamped > center) ? 1-invLerp(center, 1, clamped)
                : invLerp(0, center, clamped);
                
    return ret;
}

StructuredBuffer<CaveGen> _BiomeCaveData;


bool SampleTerrain (float3 position, int biome)
{
    float3 position2D = float3(position.x, 0, position.z);
    float3 sOffset2D = float3(sOffset.x, 0, sOffset.z);

    //Get Base Density
    CaveGen caveData = _BiomeCaveData[biome];

    float coarseCave = GetNoise(position, caveCoarseSampler);
    float fineCave = GetNoise(position, caveFineSampler);

    float coarseCentered = GetNoiseCentered(coarseCave, caveData.shape);
    float fineCentered = GetNoiseCentered(fineCave, caveData.shape);

    float centeredNoise = caveData.coarse * coarseCentered + (1.0f - caveData.coarse) * fineCentered;
    float baseDensity = pow(abs(1.0f-centeredNoise), caveData.frequency);

    //Get SurfaceData
    float continental = GetNoise(position2D, continentalSampler, sOffset2D) * continentalHeight;
    float erosion = GetNoise(position2D, erosionSampler, sOffset2D);
    float PV = GetNoise(position2D, PVSampler, sOffset2D) * PVHeight;
    float squashFactor = GetNoise(position2D, squashSampler, sOffset2D) * squashHeight;

    float terrainHeight = continental + erosion * PV + heightOffset;

    //Solve for density
    float actualHeight = position.y + (sOffset.y - chunkSize / 2.0f);
    float terrainFactor = clamp((terrainHeight - actualHeight) / (squashFactor + Epsilon), 0, 1) * (1-IsoLevel) + IsoLevel;
    float density = baseDensity * terrainFactor;

    return density >= IsoLevel;
}
