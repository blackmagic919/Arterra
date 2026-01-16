//max 8 values
uint GetSignEncoding(int3 c, uint select = 7)
{
    uint off = 0; int shift = 0;
    if ((select & 0x1) != 0){
        off |= c.x < 0 ? (1u << shift) : 0u;
        shift++;
    } if ((select & 0x2) != 0){
        off |= c.y < 0 ? (1u << shift) : 0u;
        shift++;
    } if ((select & 0x4) != 0){
        off |= c.z < 0 ? (1u << shift) : 0u;
        shift++;
    } return off;
}

//max 6 values
uint GetOrderEncoding(int3 aC)
{
    if (aC.x >= aC.z && aC.z >= aC.y) return 0;
    else if (aC.x >= aC.y && aC.y >= aC.z) return 1;
    else if (aC.y >= aC.x && aC.x >= aC.z) return 2;
    else if (aC.y >= aC.z && aC.z >= aC.x) return 3;
    else if (aC.z >= aC.y && aC.y >= aC.x) return 4;
    else return 5; //c.z >= c.y && c.y >= c.x   
}

//Encode such that the smallest(L1 distance) coords to origin(0,0,0) have smallest number
uint DistanceEncode(int3 DCoord)
{
    int3 absCoord = abs(DCoord);
    int majorAxis = max(absCoord.x, max(absCoord.y, absCoord.z));
    int minorAxis = min(absCoord.x, min(absCoord.y, absCoord.z));
    int interAxis = (absCoord.x + absCoord.y + absCoord.z) - (majorAxis + minorAxis);

    uint majorOffset = (uint)max(2 * majorAxis - 1, 0);
    uint interOffset = (uint)max(2 * interAxis - 1, 0);
    uint minorOffset = (uint)max(2 * minorAxis - 1, 0);
    majorOffset = majorOffset * majorOffset * majorOffset;
    interOffset = interOffset * interOffset * 6; 
    if (majorAxis == interAxis) minorOffset *= 12; //12 total cube edges
    else minorOffset *= 24; //6 faces, 4 edges for each square

    uint subOff;
    if (minorAxis == majorAxis){
        if (minorAxis == 0) subOff = 0; //1 Center
        else subOff = GetSignEncoding(DCoord); //8 cube corners
    }else if (majorAxis == interAxis){
        if (minorAxis == 0){ //12 edge centers
            uint mAxisInd = GetOrderEncoding(-absCoord) / 2;
            subOff = GetSignEncoding(DCoord, ~(1u << (int)mAxisInd)) + mAxisInd * 4; 
        } else subOff = GetSignEncoding(DCoord) + (GetOrderEncoding(-absCoord) >> 1) * 8; //24 edge duplicates
    } else if (interAxis == minorAxis){
        if (minorAxis == 0) { //6 Face Center
            int mAxind = (int)GetOrderEncoding(absCoord) / 2;
            subOff = GetSignEncoding(DCoord, 1u << mAxind) + (uint)mAxind * 2; 
        } else subOff = GetSignEncoding(DCoord) + (GetOrderEncoding(absCoord) >> 1) * 8; //24 face corner
    } else {
        if (minorAxis == 0) { //24 face central axis
            int mAxisInd = (int)GetOrderEncoding(-absCoord) / 2;
            subOff = GetSignEncoding(DCoord, ~(1u << mAxisInd)) + GetOrderEncoding(absCoord) * 4; 
        } else subOff = GetSignEncoding(DCoord) + GetOrderEncoding(absCoord) * 8; //48 regular point
    }
    return majorOffset + interOffset + minorOffset + subOff;
}


inline uint Part1By2(uint x)
{
  x &= 0x000003ff;                  // x = ---- ---- ---- ---- ---- --98 7654 3210
  x = (x ^ (x << 16)) & 0xff0000ff; // x = ---- --98 ---- ---- ---- ---- 7654 3210
  x = (x ^ (x <<  8)) & 0x0300f00f; // x = ---- --98 ---- ---- 7654 ---- ---- 3210
  x = (x ^ (x <<  4)) & 0x030c30c3; // x = ---- --98 ---- 76-- --54 ---- 32-- --10
  x = (x ^ (x <<  2)) & 0x09249249; // x = ---- 9--8 --7- -6-- 5--4 --3- -2-- 1--0
  return x;
}

inline uint Part1By1(uint x) {
    x &= 0x0000ffff;                // x = ---- ---- ---- ---- fedc ba98 7654 3210
    x = (x | (x << 8)) & 0x00FF00FF; // spread by 8 bits
    x = (x | (x << 4)) & 0x0F0F0F0F; // spread by 4 bits
    x = (x | (x << 2)) & 0x33333333; // spread by 2 bits
    x = (x | (x << 1)) & 0x55555555; // spread by 1 bit
    return x;
}

inline uint EncodeMorton2(uint2 v)
{
  return Part1By1(v.x) | (Part1By1(v.y) << 1);
}

inline uint EncodeMorton3(uint3 v)
{
    return Part1By2(v.x) | (Part1By2(v.y) << 1) | (Part1By2(v.z) << 2);
}