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
    //Divided SolidLight: r -> 0x1F, g -> 0x3E, b -> 0x7C00,
    //Liquid-Gas Light: LightIntensity >> 16 & 0x7FFF
};

StructuredBuffer<AtmosphericData> _MatAtmosphericData; 
RWStructuredBuffer<uint> _MemoryBuffer;
StructuredBuffer<CInfo> _AddressDict;

RWStructuredBuffer<uint2> DirtySubChunks;
uint2 bCOUNT; //x -> Dirty, y -> Current
uint2 bSTART;
uint QueueSize;
int mapChunkSize;

uint chunkLMOffset; //Light Map
uint chunkLHOffset; //Light Hash
uint subChunksAxis;
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

void AddDirtySubchunk(CInfo cHandle, int3 SCoord){
    //Add the subchunk to the dirty list
    uint SIndex = indexFromCoordManual(SCoord, subChunksAxis);
    uint addr = cHandle.address + chunkLHOffset;
    addr += SIndex / 32;

    uint value;
    InterlockedOr(_MemoryBuffer[addr], 1 << (SIndex % 32), value);
    if(value >> (SIndex % 32) & 0x1) return;
    //Add to the dirty list
    InterlockedAdd(DirtySubChunks[bCOUNT.x].y, 1, value);
    uint ind = bSTART.x + ((value + DirtySubChunks[bCOUNT.x].x) % QueueSize);

    //Note cHandle.CCoord is NOT necessarily CCoord
    DirtySubChunks[ind] = uint2(HashCoord(cHandle.CCoord), SIndex);
}

#endif