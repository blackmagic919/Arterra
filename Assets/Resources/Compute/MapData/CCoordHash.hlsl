#ifndef CCOORD_HASH
#define CCOORD_HASH
uint numChunksAxis;

struct CInfo {
    uint address;
    uint offset;
    int3 CCoord;
};

uint HashCoord(in int3 CCoord){
    uint3 hashCC = (numChunksAxis + sign(CCoord) * (abs(CCoord) % numChunksAxis)) % numChunksAxis;
    return (hashCC.x * numChunksAxis * numChunksAxis) + (hashCC.y * numChunksAxis) + hashCC.z;
}

bool Exists(in CInfo info, in int3 CCoord){
    return info.address != 0 && all(info.CCoord == CCoord);
}
#endif