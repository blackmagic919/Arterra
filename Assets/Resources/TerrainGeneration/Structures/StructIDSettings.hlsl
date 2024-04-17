#ifndef STRUCTID_SETTINGS
#define STRUCTID_SETTINGS

struct structInfo{
    float3 position;
    //Various data may be contained here, treat as padding
    uint LoD; 
};

//Biome 
uint continentalSampler;
uint erosionSampler;
uint PVSampler;
uint squashSampler;
uint atmosphereSampler;
uint humiditySampler;

//Terrain
float IsoLevel;
uint caveCoarseSampler;
uint caveFineSampler;

float continentalHeight;
float PVHeight;
float squashHeight;
float heightOffset;

#endif