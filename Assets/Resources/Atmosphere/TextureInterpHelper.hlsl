#include "Assets/Resources/Utility/GetIndex.hlsl"
#include "Assets/Resources/Utility/BlendHelper.hlsl"

int SampleTextureWidth;
int SampleTextureHeight;
int SampleDepth;

Influences GetTextureInfluences(float3 UVZ){ //z = depth
    float3 fixedUVZ = UVZ * uint3(SampleTextureWidth - 1, SampleTextureHeight - 1, SampleDepth - 1);
    return GetBlendInfo(fixedUVZ);
}

uint GetTextureIndex(uint3 mapCoord){
    mapCoord = min(mapCoord, uint3(SampleTextureWidth - 1, SampleTextureHeight - 1, SampleDepth - 1));
    return indexFromCoordIrregular(mapCoord, uint2(SampleTextureHeight, SampleDepth));
}

uint GetTextureIndex(uint2 sampleCoord, uint depth){
    uint3 mapCoord = min(uint3(sampleCoord, depth), uint3(SampleTextureWidth - 1, SampleTextureHeight - 1, SampleDepth - 1));
    return indexFromCoordIrregular(mapCoord, uint2(SampleTextureHeight, SampleDepth));
}

Influences2D GetLookupBlend(float2 UV){
    return GetBlendInfo(UV * uint2(SampleTextureWidth - 1, SampleTextureHeight - 1));
}