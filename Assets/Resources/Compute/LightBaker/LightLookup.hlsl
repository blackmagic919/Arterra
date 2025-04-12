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
    float3 scatterCoeffs;
    float3 extinctCoeff;
    uint LightIntensity; 
    //Divided r -> 0x1F, g -> 0x3E, b -> 0x7C00,
    // 0x80000000 -> Light as Solid
    // 0x40000000 -> Light as Liquid
    // 0x20000000 -> Light as Gas
};

StructuredBuffer<AtmosphericData> _MatAtmosphericData; 
RWStructuredBuffer<uint> _MemoryBuffer;
StructuredBuffer<CInfo> _AddressDict;

RWStructuredBuffer<uint2> DirtySubChunks;
uint2 bCOUNT; //x -> Dirty, y -> Current
uint2 bSTART;
uint QueueSize;

uint chunkLMOffset; //Light Map
uint chunkLHOffset; //Light Hash
uint subChunksAxis;
uint subChunkSize;
uint IsoLevel;

bool IsInShadowDirect(uint3 SamplePointMS, uint address){
    uint index = indexFromCoord(SamplePointMS);
    address = address + index;
    uint mInfo = _MemoryBuffer[address];
    if((mInfo & 0xFF) >= IsoLevel) return 1; //Don't recieve light if underground
    else {
    address = address + chunkLMOffset + index / 2;
    return (_MemoryBuffer[address] >> ((index % 2) * 16 + 15)) & 0x1;
}}

uint SampleLuminDirect(uint3 SamplePointMS, uint address){
    uint index = indexFromCoord(SamplePointMS);
    address = address + chunkLMOffset + index / 2;
    return (_MemoryBuffer[address] >> ((index % 2) * 16)) & 0xFFFF;
}

uint SampleRawLight(int3 MCoord, uint address){
    address = address + indexFromCoord(MCoord);
    uint mInfo = _MemoryBuffer[address];
    uint material = (mInfo >> 16) & 0x7FFF;
    uint solid = (mInfo >> 8) & 0xFF;
    uint liquid = (mInfo & 0xFF) - solid;
    uint light = _MatAtmosphericData[material].LightIntensity;
    if(((solid >= IsoLevel) && ((light >> 31) & 0x1)) || 
    ((liquid >= IsoLevel) && ((light >> 30) & 0x1)) ||
    max(solid, liquid) < IsoLevel && ((light >> 29) & 0x1))
        return light & 0x7FFF;
    else return 0;
}

uint3 SampleBaseLight(int3 MCoord, uint address){
    uint rawLight = SampleRawLight(MCoord, address);
    return uint3(rawLight & 0x1F, (rawLight >> 5) & 0x1F, (rawLight >> 11) & 0x1F);
}

uint SampleLumin(uint3 SamplePointMS, CInfo cHandle){
    //If underground return 0
    if(any(SamplePointMS < 0 && SamplePointMS >= subChunksAxis)){
        //Convert from MapSpace to GridSpace
        int3 SampleGS = SamplePointMS * (cHandle.offset * 0xFF) + cHandle.CCoord * numPointsPerAxis;
        SamplePointMS = ((SampleGS % subChunksAxis) + subChunksAxis) % subChunksAxis;
        int3 CCoord = (SampleGS - SamplePointMS) / subChunksAxis;
        cHandle = _AddressDict[HashCoord(CCoord)];
        if(!Exists(cHandle, CCoord)) return 0;
        SamplePointMS /= (cHandle.offset & 0xFF);
        SamplePointMS.x += (cHandle.offset >> 24) & 0xFF;
        SamplePointMS.y += (cHandle.offset >> 16) & 0xFF;
        SamplePointMS.z += (cHandle.offset >> 8) & 0xFF;
    }

    return SampleLuminDirect(SamplePointMS, cHandle.address);
}

bool IsInShadow(uint3 SamplePointMS, CInfo cHandle){
    //If underground return 0
    if(any(SamplePointMS < 0 && SamplePointMS >= subChunksAxis)){
        //Convert from MapSpace to GridSpace
        int3 SampleGS = SamplePointMS * (cHandle.offset * 0xFF) + cHandle.CCoord * numPointsPerAxis;
        SamplePointMS = ((SampleGS % subChunksAxis) + subChunksAxis) % subChunksAxis;
        int3 CCoord = (SampleGS - SamplePointMS) / subChunksAxis;
        cHandle = _AddressDict[HashCoord(CCoord)];
        //IMPORTANT: If above chunk doesn't exist, it is not in shadow
        if(!Exists(cHandle, CCoord)) return 0; 
        SamplePointMS /= (cHandle.offset & 0xFF);
        SamplePointMS.x += (cHandle.offset >> 24) & 0xFF;
        SamplePointMS.y += (cHandle.offset >> 16) & 0xFF;
        SamplePointMS.z += (cHandle.offset >> 8) & 0xFF;
    }

    return IsInShadowDirect(SamplePointMS, cHandle.address);
}

//IMPORTANT: Caller should ensure CCoord is the coord of the origin of the chunk
void AddDirtySubchunk(int3 CCoord, int3 SCoord){
    //Add the subchunk to the dirty list
    uint CIndex = HashCoord(CCoord);
    uint SIndex = indexFromCoord(SCoord);
    CInfo cHandle = _AddressDict[CIndex];
    if(!Exists(cHandle, CCoord)) return;
    uint addr = cHandle.address + chunkLHOffset;
    addr += SIndex / 32;

    uint value;
    InterlockedOr(_MemoryBuffer[addr], 1 << (SIndex % 32), value);
    if(value >> (SIndex % 32) & 0x1) return;
    //Add to the dirty list
    InterlockedAdd(DirtySubChunks[bCOUNT.x].y, 1, value);
    uint ind = bSTART.x + ((value + DirtySubChunks[bCOUNT.x].x) % QueueSize);
    DirtySubChunks[ind] = uint2(CIndex, SIndex);
}

#endif