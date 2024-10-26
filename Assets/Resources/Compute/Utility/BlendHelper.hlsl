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

Influences GetBlendInfo(float3 positionMS){
    Influences influences = (Influences)0;

    influences.origin = uint3(floor(positionMS));
    [unroll]for(int i = 7; i >= 0; i--){
        uint3 oppositeCorner = influences.origin + uint3(i & 1u, (i >> 1) & 1u, (i >> 2) & 1u);
        influences.corner[7-i] = abs((positionMS.x - oppositeCorner.x) * (positionMS.y - oppositeCorner.y) * (positionMS.z - oppositeCorner.z));
    }

    return influences;
}

struct Influences2D{
    float corner[4];
    uint2 origin;
};

Influences2D GetBlendInfo(float2 positionMS){
    Influences2D influences = (Influences2D)0;

    influences.origin = uint2(floor(positionMS));
    [unroll]for(int i = 3; i >= 0; i--){
        uint2 oppositeCorner = influences.origin + uint2(i & 1u, (i >> 1) & 1u);
        influences.corner[3-i] = abs((positionMS.x - oppositeCorner.x) * (positionMS.y - oppositeCorner.y));
    }

    return influences;
}
#endif