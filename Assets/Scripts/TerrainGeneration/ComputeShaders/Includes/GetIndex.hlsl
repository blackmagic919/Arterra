uint numPointsPerAxis; 

uint indexFromCoordIrregular(uint x, uint y, uint z, uint sizeX, uint sizeY) {
    return x + y * sizeX + z * sizeX * sizeY;
}

uint indexFromCoordManual(uint x, uint y, uint z, uint size) {
    return x * size * size + y * size + z;
}

uint indexFromCoord(uint x, uint y, uint z) {
    return x * numPointsPerAxis * numPointsPerAxis + y * numPointsPerAxis + z;
}

uint indexFromCoord2D(uint x, uint y) {
    return x * numPointsPerAxis + y;
}