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

bool Contains(in CInfo info, in int3 CCoord){
    int3 minCC = info.CCoord;
    int3 maxCC = minCC + (info.offset & 0xFF);
    return info.address != 0 && all(minCC <= CCoord && CCoord < maxCC);
}
#endif