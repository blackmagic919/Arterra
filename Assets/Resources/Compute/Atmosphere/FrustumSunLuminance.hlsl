#ifndef FRUSTUM_SUN_LUMINANCE_INCLUDED
#define FRUSTUM_SUN_LUMINANCE_INCLUDED

#include "Assets/Resources/Compute/Atmosphere/FrustumLightSpace.hlsl"

StructuredBuffer<float3> sunLuminance;
StructuredBuffer<float> sunRayLengths;
uint sunHeight;
uint sunWidth;
uint _NumSunDepthPoints;

inline uint FrustumSunIndex2D(uint sampleX, uint sampleY) {
    return sampleX * sunHeight + sampleY;
}

inline uint FrustumSunIndex3D(uint sampleX, uint sampleY, uint sampleZ) {
    return sampleX * sunHeight * _NumSunDepthPoints + sampleY * _NumSunDepthPoints + sampleZ;
}

float3 SampleFrustumSunDepthColumn(uint sampleX, uint sampleY, float fsDepth) {
    uint maxDepth = max(_NumSunDepthPoints, 1u) - 1u;
    float maxDepthF = max((float)_NumSunDepthPoints - 1.0f, 0.0f);

    uint sampleIndex = FrustumSunIndex2D(sampleX, sampleY);
    float sunRayLength = max(sunRayLengths[sampleIndex], 1e-6);
    float fsDepth01 = 1.0f - (fsDepth / sunRayLength);
    float zf = saturate(fsDepth01) * maxDepthF;
    float zFloor = floor(zf);
    uint z0 = min((uint)zFloor, maxDepth);
    uint z1 = min(z0 + 1u, maxDepth);
    float zBlend = zf - zFloor;

    uint index0 = FrustumSunIndex3D(sampleX, sampleY, z0);
    uint index1 = FrustumSunIndex3D(sampleX, sampleY, z1);
    return lerp(sunLuminance[index0], sunLuminance[index1], zBlend);
}

// Trilinear sample in frustum light-space (z within each XY column first).
float3 SampleFrustumSunOpticalDepthAtWS(float3 worldPos) {
    float3 fsPos = WSPosToFSPos(worldPos);
    uint maxX = max(sunWidth, 1u) - 1u;
    uint maxY = max(sunHeight, 1u) - 1u;

    float2 fsScaled = saturate(fsPos.xy) * float2((float)sunWidth, (float)sunHeight) - 0.5f;
    fsScaled = clamp(fsScaled, 0.0f, float2((float)maxX, (float)maxY));
    float2 fsFloor = floor(fsScaled);
    uint x0 = min((uint)fsFloor.x, maxX);
    uint y0 = min((uint)fsFloor.y, maxY);
    uint x1 = min(x0 + 1u, maxX);
    uint y1 = min(y0 + 1u, maxY);
    float xBlend = fsScaled.x - fsFloor.x;
    float yBlend = fsScaled.y - fsFloor.y;

    float3 c00 = SampleFrustumSunDepthColumn(x0, y0, fsPos.z);
    float3 c10 = SampleFrustumSunDepthColumn(x1, y0, fsPos.z);
    float3 c01 = SampleFrustumSunDepthColumn(x0, y1, fsPos.z);
    float3 c11 = SampleFrustumSunDepthColumn(x1, y1, fsPos.z);

    float3 row0 = lerp(c00, c10, xBlend);
    float3 row1 = lerp(c01, c11, xBlend);
    return lerp(row0, row1, yBlend);
}

#endif