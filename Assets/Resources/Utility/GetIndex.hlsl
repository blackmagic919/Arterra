#ifndef GETINDEX_HELPER
#define GETINDEX_HELPER
uint numPointsPerAxis; 

//Irregular(these two are reversed)
uint indexFromCoordIrregular(uint3 pos, uint2 size) {
    return pos.x + pos.y * size.x + pos.z * size.x * size.y;
}

uint indexFromCoordIrregular(uint x, uint y, uint z, uint sizeX, uint sizeY) {
    return x + y * sizeX + z * sizeX * sizeY;
}

//Manual
uint indexFromCoordManual(uint3 pos, uint size) {
    return pos.x * size * size + pos.y * size + pos.z;
}

uint indexFromCoordManual(uint x, uint y, uint z, uint size) {
    return x * size * size + y * size + z;
}

//Regular
uint indexFromCoord(uint3 pos) {
    return pos.x * numPointsPerAxis * numPointsPerAxis + pos.y * numPointsPerAxis + pos.z;
}

uint indexFromCoord(uint x, uint y, uint z) {
    return x * numPointsPerAxis * numPointsPerAxis + y * numPointsPerAxis + z;
}

//2D
uint indexFromCoord2D(uint2 pos) {
    return pos.x * numPointsPerAxis + pos.y;
}

uint indexFromCoord2D(uint x, uint y) {
    return x * numPointsPerAxis + y;
}

//2D Manual
uint indexFromCoord2DManual(uint2 pos, uint sizeY) {
    return pos.x * sizeY + pos.y;
}

uint indexFromCoord2DManual(uint x, uint y, uint sizeY) {
    return x * sizeY + y;
}

#endif