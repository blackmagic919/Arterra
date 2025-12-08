#ifndef LBLOOKUP
#define LBLOOKUP


const static int3 dp[6] = {
    {1,0,0}, {-1,0,0}, 
    {0,1,0}, {0,-1,0}, 
    {0,0,1}, {0,0,-1}
};

const static int3x2 dNA[3] = {
    {0, 0, 1, 0, 0, 1},
    {1, 0, 0, 0, 0, 1},
    {1, 0, 0, 1, 0, 0}
};

//Information stored for every material
struct AtmosphericData{
    float3 inScatterCoeffs;
    float3 outScatterCoeffs;
    float3 extinctCoeff;
    uint LightIntensity; 
    //Divided SolidLight: r -> 0x1F, g -> 0x3E, b -> 0x7C00,
    //Liquid-Gas Light: LightIntensity >> 16 & 0x7FFF
};

StructuredBuffer<AtmosphericData> _MatAtmosphericData; 
RWStructuredBuffer<uint> _MemoryBuffer;
StructuredBuffer<CInfo> _AddressDict;

RWStructuredBuffer<uint2> DirtySubChunks;
uint3 bCOUNT; //x -> Dirty, y -> CurrentShadow, z -> CurrentObject
uint3 bSTART;
uint QueueSize;
int mapChunkSize;
int subChunksAxis;

uint chunkLMOffset; //Light Map
uint chunkLHOffset; //Light Hash
uint subChunkSize;
uint IsoLevel;

bool IsInShadowDirect(uint3 SamplePointMS, uint address){
    uint index = indexFromCoord(SamplePointMS);
    uint pAddress = address + index;
    uint mInfo = _MemoryBuffer[pAddress];
    if((mInfo >> 8 & 0xFF) >= IsoLevel) return 1; //Don't recieve light if underground
    else {
    pAddress = address + chunkLMOffset + index / 2;
    return (_MemoryBuffer[pAddress] >> ((index % 2) * 16 + 15)) & 0x1;
}}

uint SampleLuminDirect(uint3 SamplePointMS, uint address){
    uint index = indexFromCoord(SamplePointMS);
    address = address + chunkLMOffset + index / 2;
    return (_MemoryBuffer[address] >> ((index % 2) * 16)) & 0x7FFF;
}

uint SampleRawLight(int3 MCoord, uint address){
    address = address + indexFromCoord(MCoord);
    uint mInfo = _MemoryBuffer[address];
    uint material = (mInfo >> 16) & 0x7FFF;
    uint solid = (mInfo >> 8) & 0xFF;
    uint liquid = (mInfo & 0xFF) - solid;
    uint light = _MatAtmosphericData[material].LightIntensity;
    if(solid >= IsoLevel){
        return light & 0x7FFF;
    } else if (liquid >= IsoLevel || max(solid, liquid) < IsoLevel){
        return (light >> 16) & 0x7FFF;
    } else return 0;
}

uint3 SampleBaseLight(int3 MCoord, uint address){
    uint rawLight = SampleRawLight(MCoord, address);
    return uint3(rawLight & 0x1F, (rawLight >> 5) & 0x1F, (rawLight >> 10) & 0x1F);
}

uint SampleLumin(int3 SamplePointMS, CInfo cHandle){
    //If underground return 0
    if(any(SamplePointMS < 0 || SamplePointMS >= (int)numPointsPerAxis)){
        //Convert from MapSpace to GridSpace
        int3 SampleGS = SamplePointMS * (cHandle.offset & 0xFF) + cHandle.CCoord * mapChunkSize;
        SamplePointMS = ((SampleGS % mapChunkSize) + mapChunkSize) % mapChunkSize;
        int3 CCoord = (SampleGS - SamplePointMS) / mapChunkSize;
        cHandle = _AddressDict[HashCoord(CCoord)];
        if(!Contains(cHandle, CCoord)) 
            cHandle.address = 0; 
        SamplePointMS /= (cHandle.offset & 0xFF);
        SamplePointMS.x += (cHandle.offset >> 24) & 0xFF;
        SamplePointMS.y += (cHandle.offset >> 16) & 0xFF;
        SamplePointMS.z += (cHandle.offset >> 8) & 0xFF;
    }

    if(cHandle.address == 0) return 0;
    else return SampleLuminDirect(SamplePointMS, cHandle.address);
}

bool IsInShadow(int3 SamplePointMS, CInfo cHandle){
    //If underground return 0
    if(any(SamplePointMS < 0 || SamplePointMS >= (int)numPointsPerAxis)){
        //Convert from MapSpace to GridSpace
        int3 SampleGS = SamplePointMS * (cHandle.offset & 0xFF) + cHandle.CCoord * mapChunkSize;
        SamplePointMS = ((SampleGS % mapChunkSize) + mapChunkSize) % mapChunkSize;
        int3 CCoord = (SampleGS - SamplePointMS) / mapChunkSize;
        cHandle = _AddressDict[HashCoord(CCoord)];
        //IMPORTANT: If above chunk doesn't exist, it is not in shadow
        if(!Contains(cHandle, CCoord)) 
            cHandle.address = 0; 
        SamplePointMS /= (cHandle.offset & 0xFF);
        SamplePointMS.x += (cHandle.offset >> 24) & 0xFF;
        SamplePointMS.y += (cHandle.offset >> 16) & 0xFF;
        SamplePointMS.z += (cHandle.offset >> 8) & 0xFF;
    }

    if(cHandle.address == 0) return 0;
    else return IsInShadowDirect(SamplePointMS, cHandle.address);
}

void AddDirtySubchunk(CInfo cHandle, int3 SCoord, uint HashInd){
    //Add the subchunk to the dirty list
    uint SIndex = indexFromCoordManual(SCoord, subChunksAxis);
    uint addr = cHandle.address + chunkLHOffset + SIndex / 4;

    uint value; uint shift = ((SIndex % 4) * 8);
    InterlockedOr(_MemoryBuffer[addr], HashInd << shift, value);
    if((value >> shift) & HashInd) return;
    //Add to the dirty list
    InterlockedAdd(DirtySubChunks[bCOUNT.x].y, 1, value);
    uint ind = bSTART.x + ((value + DirtySubChunks[bCOUNT.x].x) % QueueSize);

    //Note cHandle.CCoord is NOT necessarily CCoord
    DirtySubChunks[ind] = uint2(HashCoord(cHandle.CCoord), SIndex);
}

void AddDirtyNeighboringChunkSubChunks(int3 CCoord, int3 SCoord, int skipInc, uint cxt){
    //In case the adjacent subchunk is outside the chunk
    //scales subchunks to absolute subchunk coordinates
    int dir = cxt & 0xFF;
    SCoord = SCoord * skipInc + CCoord * subChunksAxis;
    //If going upwards, add skipInc, else subtract just 1 
    SCoord += dp[dir] * ((dir % 2) == 0 ? skipInc : 1);

    CCoord = ((SCoord % subChunksAxis) + subChunksAxis) % subChunksAxis;
    CCoord = (SCoord - CCoord) / subChunksAxis;
    CInfo nHandle = _AddressDict[HashCoord(CCoord)];

    if(!Contains(nHandle, CCoord)) return;
    //Note: nHandle.CCoord is coord of the origin of the actual chunk
    //While CCoord is the CCoord of a chunk possibly with the actual adjacent chunk
    uint nSkip = (nHandle.offset & 0xFF);
    SCoord = (SCoord - (((SCoord % nSkip) + nSkip) % nSkip)) / nSkip;  //takes floor of the division
    SCoord = ((SCoord % subChunksAxis) + subChunksAxis) % subChunksAxis;

    nSkip = max(skipInc / nSkip, 1);
    for(uint k = 0; k < nSkip * nSkip; k++){
        int2 dSC = int2(k / nSkip, k % nSkip);
        int3 nSC = clamp(SCoord + mul(dNA[dir/2], dSC), 0, subChunksAxis-1);
        AddDirtySubchunk(nHandle, nSC, (cxt >> 8) & 0xFF);
    }
}


bool IsNeighborPropogating(int3 CCoord, int3 SCoord, int skipInc){
    for(int j = 0; j < 6; j++){
        int3 nSCoord = SCoord + dp[j];
        nSCoord = nSCoord * skipInc + CCoord * subChunksAxis; 
        int3 nCCoord = ((nSCoord % subChunksAxis) + subChunksAxis) % subChunksAxis;
        nCCoord = (nSCoord - nCCoord) / subChunksAxis;
        CInfo nHandle = _AddressDict[HashCoord(nCCoord)];
        if(!Contains(nHandle, nCCoord)) continue;

        uint nSkip = (nHandle.offset & 0xFF); 
        nSCoord = (nSCoord - (((nSCoord % nSkip) + nSkip) % nSkip)) / nSkip; 
        nSCoord = ((nSCoord % subChunksAxis) + subChunksAxis) % subChunksAxis;

        nSkip = max(skipInc / nSkip, 1);
        for(uint k = 0; k < nSkip * nSkip; k++){
            int2 dSC = int2(k / nSkip, k % nSkip);
            int3 nSC = clamp(nSCoord + mul(dNA[j >> 1], dSC), 0, subChunksAxis-1);
            uint SIndex = indexFromCoordManual(nSC, subChunksAxis);
            uint heapInfo = _MemoryBuffer[nHandle.address + chunkLHOffset + SIndex / 4];
            heapInfo >>= ((SIndex % 4) * 8);
            // xor 0x1 to get the opposite direction
            heapInfo = (heapInfo >> (j ^ 0x1)) & 0x1;
            if(heapInfo) return true;
        }
    }
    return false;
}

#endif