#ifndef CCOORD_HASH
#define CCOORD_HASH
uint numChunksAxis;

uint HashCoord(int3 CCoord){
    uint xHash = CCoord.x < 0 ? numChunksAxis - (abs(CCoord.x) % numChunksAxis) : abs(CCoord.x) % numChunksAxis;
    uint yHash = CCoord.y < 0 ? numChunksAxis - (abs(CCoord.y) % numChunksAxis) : abs(CCoord.y) % numChunksAxis;
    uint zHash = CCoord.z < 0 ? numChunksAxis - (abs(CCoord.z) % numChunksAxis) : abs(CCoord.z) % numChunksAxis;

    uint hash = (xHash * numChunksAxis * numChunksAxis) + (yHash * numChunksAxis) + zHash;
    return hash;
}
#endif