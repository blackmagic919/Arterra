#include "Assets/Scripts/TerrainGeneration/ComputeShaders/Includes/GetIndex.hlsl"
#include "Assets/Scripts/TerrainGeneration/DensityManager/BlendHelper.hlsl"

int SampleTextureWidth;
int SampleTextureHeight;
int SampleDepth;

Influences GetTextureInfluences(float3 UVZ){ //z = depth
    float3 fixedUVZ = UVZ * uint3(SampleTextureWidth - 1, SampleTextureHeight - 1, SampleDepth - 1);
    Influences influences = GetBlendInfo(fixedUVZ);
    return influences;
}

uint GetTextureIndex(uint3 mapCoord){
    mapCoord = min(mapCoord, uint3(SampleTextureWidth - 1, SampleTextureHeight - 1, SampleDepth - 1));
    return indexFromCoordIrregular(mapCoord, uint2(SampleTextureWidth, SampleTextureHeight));
}