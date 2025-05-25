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

bool Overlaps(in CInfo c1, in CInfo c2) {
    int3 minC1 = c1.CCoord; int3 maxC1 = minC1 + (c1.offset & 0xFF);
    int3 minC2 = c2.CCoord; int3 maxC2 = minC2 + (c2.offset & 0xFF);

    return (c1.address != 0) && (c2.address != 0) && all(minC1 <= maxC2 && maxC1 >= minC2);
}
#endif