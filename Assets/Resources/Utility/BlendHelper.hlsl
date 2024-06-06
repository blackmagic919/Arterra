#ifndef BLEND_HELPER
#define BLEND_HELPER
/*
* z
* ^     6--------7
* |    /|       /|
* |   / |      / |    y
* |  5--+-----4  |   /\
* |  |  |     |  |   /
* |  |  0-----+--1  /
* |  | /      | /  /
* |  3________2/  /
* +---------> x  /
*/

struct Corner{
    float influence;
    uint3 mapCoord;
};

struct Influences{
    Corner corner[8];
};

Influences GetBlendInfo(float3 positionMS);

Influences GetBlendInfo(float3 positionMS){
    Influences influences = (Influences)0;

    uint3 fCoordMS = uint3(floor(positionMS));
    //Ceil & Floor will casue error if positionMS is grid-aligned. With this, caller must guarantee positionMS is not max value
    influences.corner[0].mapCoord = fCoordMS + uint3(0, 1, 0);
    influences.corner[1].mapCoord = fCoordMS + uint3(1, 1, 0);
    influences.corner[2].mapCoord = fCoordMS + uint3(1, 0, 0);
    influences.corner[3].mapCoord = fCoordMS;
    influences.corner[4].mapCoord = fCoordMS + uint3(1, 0, 1);
    influences.corner[5].mapCoord = fCoordMS + uint3(0, 0, 1);
    influences.corner[6].mapCoord = fCoordMS + uint3(0, 1, 1);
    influences.corner[7].mapCoord = fCoordMS + uint3(1, 1, 1);

    [unroll]for(uint i = 0; i < 8; i++){
        uint3 oppositeCorner = influences.corner[(i+4u)%8u].mapCoord;
        influences.corner[i].influence = abs(positionMS.x - oppositeCorner.x) * abs(positionMS.y - oppositeCorner.y) * abs(positionMS.z - oppositeCorner.z);
    }

    return influences;
}

struct Corner2D{
    float influence;
    uint2 mapCoord;
};

struct Influences2D{
    Corner2D corner[4];
};

Influences2D GetBlendInfo(float2 positionMS){
    Influences2D influences = (Influences2D)0;

    uint2 fCoordMS = uint2(floor(positionMS));
    //Ceil & Floor will casue error if positionMS is grid-aligned. With this, caller must guarantee positionMS is not max value
    influences.corner[0].mapCoord = fCoordMS + uint2(0, 1);
    influences.corner[1].mapCoord = fCoordMS + uint2(1, 1);
    influences.corner[2].mapCoord = fCoordMS + uint2(1, 0);
    influences.corner[3].mapCoord = fCoordMS;

    [unroll]for(uint i = 0; i < 4; i++){
        uint2 oppositeCorner = influences.corner[(i+2u)%4u].mapCoord;
        influences.corner[i].influence = abs(positionMS.x - oppositeCorner.x) * abs(positionMS.y - oppositeCorner.y);
    }

    return influences;
}
#endif