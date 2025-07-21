#include "Assets/Resources/Compute/Utility/PerlinNoiseSampler.hlsl"
#include "Assets/Resources/Compute/TerrainGeneration/Structures/StructIDSettings.hlsl"
const static int Epsilon = 0.0001;

float GetNoiseCentered(float val, float center){
    float ret = (val > center) ? 1-smoothstep(center, 1, val)
                : smoothstep(0, center, val);
    return ret;
}

uint SampleTerrain (float3 position)
{
    //Get SurfaceData
    float erosion = GetNoise2D(position.xz, erosionSampler);
    float terrainHeight = GetErodedNoise2D(position.xz, erosion, PVSampler, continentalSampler);
    float squashFactor = GetNoise2D(position.xz, squashSampler) * squashHeight;

    terrainHeight = terrainHeight * maxTerrainHeight + heightOffset;

    //Get Base Density
    float caveShape = GetNoise(position, caveShapeSampler);
    float blendBase = lerp(
        GetNoiseCentered(GetNoise(position, caveFineSampler), caveShape),
        GetNoiseCentered(GetNoise(position, caveCoarseSampler), caveShape),
        GetNoise(position, caveSizeSampler)
    );
    
    float baseDensity = pow(abs(1.0f-blendBase), GetNoise(position, caveFreqSampler));

    //Solve for density
    float actualHeight = position.y + sOffset.y;
    float terrainFactor = clamp((terrainHeight - actualHeight) / (squashFactor + Epsilon), 0, 1);
    float density = baseDensity * (terrainFactor * (1-IsoLevel) + IsoLevel);

    uint mapInfo = (uint)round(density * 255.0f);
    mapInfo = mapInfo << 8 | mapInfo; //Set viscosity to density
    if(actualHeight > (terrainHeight - squashFactor) && actualHeight < waterHeight && density < IsoLevel)
        mapInfo = (mapInfo & 0xFFFF) | 0xFF; //set density to one
    
    return mapInfo;
}