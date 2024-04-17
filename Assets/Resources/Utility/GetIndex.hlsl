#ifndef GETINDEX_HELPER
#define GETINDEX_HELPER
uint numPointsPerAxis; 

//Irregular
uint indexFromCoordIrregular(uint3 pos, uint2 size) {
    return pos.x + pos.y * size.x + pos.z * size.x * size.y;
}

uint indexFromCoordIrregular(uint x, uint y, uint z, uint sizeX, uint sizeY) {
    return x + y * sizeX + z * sizeX * sizeY;
}

//Manual
uint indexFromCoordManual(uint3 pos, uint size) {
    return pos.x + pos.y * size + pos.z * size * size;
}

uint indexFromCoordManual(uint x, uint y, uint z, uint size) {
    return x + y * size + z * size * size;
}

//Regular
uint indexFromCoord(uint3 pos) {
    return pos.x + pos.y * numPointsPerAxis + pos.z * numPointsPerAxis * numPointsPerAxis;
}

uint indexFromCoord(uint x, uint y, uint z) {
    return x + y * numPointsPerAxis + z * numPointsPerAxis * numPointsPerAxis;
}

//2D
uint indexFromCoord2D(uint2 pos) {
    return pos.x + pos.y * numPointsPerAxis;
}

uint indexFromCoord2D(uint x, uint y) {
    return x + y * numPointsPerAxis;
}

//2D Manual
uint indexFromCoord2DManual(uint2 pos, uint sizeX) {
    return pos.x + pos.y * sizeX;
}

uint indexFromCoord2DManual(uint x, uint y, uint sizeX) {
    return x + y * sizeX;
}

#endif