#ifndef CCOORD_HASH
#define CCOORD_HASH
uint numChunksAxis;

uint HashCoord(int3 CCoord){
    uint3 hashCC = (numChunksAxis + sign(CCoord) * (abs(CCoord) % numChunksAxis)) % numChunksAxis;

    /*uint xHash = CCoord.x < 0 ? numChunksAxis - (abs(CCoord.x) % numChunksAxis) : abs(CCoord.x) % numChunksAxis;
    uint yHash = CCoord.y < 0 ? numChunksAxis - (abs(CCoord.y) % numChunksAxis) : abs(CCoord.y) % numChunksAxis;
    uint zHash = CCoord.z < 0 ? numChunksAxis - (abs(CCoord.z) % numChunksAxis) : abs(CCoord.z) % numChunksAxis;*/

    return (hashCC.x * numChunksAxis * numChunksAxis) + (hashCC.y * numChunksAxis) + hashCC.z;
}
#endif