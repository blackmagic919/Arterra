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

    //Get SurfaceData
    float PV = GetNoise2D(position.xz, PVSampler) * 2 - 1;
    float continental = GetNoise2D(position.xz, continentalSampler);
    float erosion = GetNoise2D(position.xz, erosionSampler);
    float squashFactor = GetNoise2D(position.xz, squashSampler) * squashHeight;

    float terrainHeight = (continental + PV * erosion) * maxTerrainHeight + heightOffset;

    //Get Base Density
    float caveShape = GetNoise2D(position.xz, caveShapeSampler);
    float blendBase = lerp(
        GetNoiseCentered(GetNoise(position, caveFineSampler), caveShape),
        GetNoiseCentered(GetNoise(position, caveCoarseSampler), caveShape),
        GetNoise2D(position.xz, caveSizeSampler)
    );
    
    float baseDensity = pow(abs(1.0f-blendBase), GetNoise2D(position.xz, caveFreqSampler));

    //Solve for density
    float actualHeight = position.y + sOffset.y;
    float terrainFactor = clamp((terrainHeight - actualHeight) / (squashFactor + Epsilon), 0, 1);
    float density = baseDensity * (terrainFactor * (1-IsoLevel) + IsoLevel);

    uint mapInfo = ((uint)round(density * 255.0f)) | 0xFF00;
    if(actualHeight > (terrainHeight - squashFactor) && actualHeight < waterHeight && density < IsoLevel)
        mapInfo = ((mapInfo << 8) & 0xFFFF) | 0xFF; //swap density and viscosity
    
    return mapInfo;
}


float SampleTerrainHeight(float3 position){
    float PV = GetNoise2D(position.xz, PVSampler) * 2 - 1;
    float continental = GetNoise2D(position.xz, continentalSampler);
    float erosion = GetNoise2D(position.xz, erosionSampler);

    return (continental + PV * erosion) * maxTerrainHeight + heightOffset;
}