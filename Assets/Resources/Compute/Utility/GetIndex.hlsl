#ifndef GETINDEX_HELPER
#define GETINDEX_HELPER
uint numPointsPerAxis; 

//Irregular(these two are reversed)
inline uint indexFromCoordIrregular(uint3 pos, uint2 size) {
    return pos.x * size.x * size.y + pos.y * size.y + pos.z;
}

inline uint indexFromCoordIrregular(uint x, uint y, uint z, uint sizeY, uint sizeZ) {
    return x * sizeY * sizeZ + y * sizeZ + z;
}

//Manual
inline uint indexFromCoordManual(uint3 pos, uint size) {
    return pos.x * size * size + pos.y * size + pos.z;
}

inline uint indexFromCoordManual(uint x, uint y, uint z, uint size) {
    return x * size * size + y * size + z;
}

//Regular
inline uint indexFromCoord(uint3 pos) {
    return pos.x * numPointsPerAxis * numPointsPerAxis + pos.y * numPointsPerAxis + pos.z;
}

inline uint indexFromCoord(uint x, uint y, uint z) {
    return x * numPointsPerAxis * numPointsPerAxis + y * numPointsPerAxis + z;
}

//2D
inline uint indexFromCoord2D(uint2 pos) {
    return pos.x * numPointsPerAxis + pos.y;
}

inline uint indexFromCoord2D(uint x, uint y) {
    return x * numPointsPerAxis + y;
}

//2D Manual
inline uint indexFromCoord2DManual(uint2 pos, uint sizeY) {
    return pos.x * sizeY + pos.y;
}

inline uint indexFromCoord2DManual(uint x, uint y, uint sizeY) {
    return x * sizeY + y;
}
#endif