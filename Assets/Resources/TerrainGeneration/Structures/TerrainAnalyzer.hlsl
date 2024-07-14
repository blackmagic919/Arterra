#include "Assets/Resources/Utility/PerlinNoiseSampler.hlsl"
#include "Assets/Resources/TerrainGeneration/Structures/StructIDSettings.hlsl"
const static int Epsilon = 0.0001;

float GetNoiseCentered(float val, float center){
    float ret = (val > center) ? 1-smoothstep(center, 1, val)
                : smoothstep(0, center, val);
    return ret;
}

uint SampleTerrain (float3 position)
{
    float3 position2D = float3(position.x, 0, position.z);
    float3 sOffset2D = float3(sOffset.x, 0, sOffset.z);

    //Get SurfaceData
    float PV = GetNoise(position2D, PVSampler, sOffset2D) * 2 - 1;
    float continental = GetNoise(position2D, continentalSampler, sOffset2D);
    float erosion = GetNoise(position2D, erosionSampler, sOffset2D);
    float squashFactor = GetNoise(position2D, squashSampler, sOffset2D) * squashHeight;

    float terrainHeight = (continental + PV * erosion) * maxTerrainHeight + heightOffset;

    //Get Base Density
    float blendBase = lerp(
        GetNoise(position, caveFineSampler),
        GetNoise(position, caveCoarseSampler),
        GetNoise(position2D, caveSizeSampler, sOffset2D)
    );
    
    float centeredBase = GetNoiseCentered(blendBase,  GetNoise(position2D, caveShapeSampler, sOffset2D));
    float baseDensity = pow(abs(1.0f-centeredBase), GetNoise(position2D, caveFreqSampler, sOffset2D));

    //Solve for density
    float actualHeight = position.y + sOffset.y;
    float terrainFactor = clamp((terrainHeight - actualHeight) / (squashFactor + Epsilon), 0, 1) * (1-IsoLevel) + IsoLevel;
    float density = baseDensity * terrainFactor;

    uint mapInfo = ((uint)round(density * 255.0f)) | 0xFF00;
    if(actualHeight > (terrainHeight - squashFactor) && actualHeight < waterHeight && density < IsoLevel)
        mapInfo = ((mapInfo << 8) & 0xFFFF) | 0xFF; //swap density and viscosity
    
    return mapInfo;
}
