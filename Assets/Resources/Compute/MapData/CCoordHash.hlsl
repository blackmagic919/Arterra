#ifndef CCOORD_HASH
#define CCOORD_HASH
uint numChunksAxis;

uint HashCoord(int3 CCoord){
    uint3 hashCC = (numChunksAxis + sign(CCoord) * (abs(CCoord) % numChunksAxis)) % numChunksAxis;

    return (hashCC.x * numChunksAxis * numChunksAxis) + (hashCC.y * numChunksAxis) + hashCC.z;
}
#endif