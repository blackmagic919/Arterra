#ifndef BLEND_HELPER
#define BLEND_HELPER
/*
* z
* ^     6--------7
* |    /|       /|
* |   / |      / |    y
* |  4--+-----5  |   /\
* |  |  |     |  |   /
* |  |  2-----+--3  /
* |  | /      | /  /
* |  0________1/  /
* +---------> x  /
*/

struct Influences{
    float corner[8];
    uint3 origin;
};

inline Influences GetBlendInfo(float3 positionMS) {
    Influences inf;

    float3 f = frac(positionMS);
    float3 oneMinusF = 1.0 - f;

    inf.origin = uint3(positionMS); // implicitly floor()
    inf.corner[0] = oneMinusF.x * oneMinusF.y * oneMinusF.z; // (0,0,0)
    inf.corner[1] = f.x         * oneMinusF.y * oneMinusF.z; // (1,0,0)
    inf.corner[2] = oneMinusF.x * f.y         * oneMinusF.z; // (0,1,0)
    inf.corner[3] = f.x         * f.y         * oneMinusF.z; // (1,1,0)
    inf.corner[4] = oneMinusF.x * oneMinusF.y * f.z;         // (0,0,1)
    inf.corner[5] = f.x         * oneMinusF.y * f.z;         // (1,0,1)
    inf.corner[6] = oneMinusF.x * f.y         * f.z;         // (0,1,1)
    inf.corner[7] = f.x         * f.y         * f.z;         // (1,1,1)
    return inf;
}

struct Influences2D{
    float corner[4];
    uint2 origin;
};

inline Influences2D GetBlendInfo(float2 positionMS) {
    Influences2D inf;

    float2 f = frac(positionMS);
    float2 oneMinusF = 1.0 - f;

    inf.origin = uint2(positionMS); // floor(positionMS)
    inf.corner[0] = oneMinusF.x * oneMinusF.y; // (0,0)
    inf.corner[1] = f.x         * oneMinusF.y; // (1,0)
    inf.corner[2] = oneMinusF.x * f.y;         // (0,1)
    inf.corner[3] = f.x         * f.y;         // (1,1)
    return inf;
}

#endif