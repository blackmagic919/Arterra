#ifndef RANDOM
#define RANDOM

uint RandomState(uint seed){
    seed ^= seed << 13;
    seed *= 0x85ebca6b;
    seed ^= seed >> 17;
    seed *= 0xc2b2ae35;
    seed ^= seed << 5;
    return seed;
}

float RandomFloat(uint seed){
    //output 0->1
    //exponent bias, bit shift into mantissa, subtract normalization
    return asfloat(0x3F800000u | (RandomState(seed) >> 9)) - 1.0f;
}
uint Random(uint seed) { return RandomState(seed); }
uint Random(float seed) { return RandomState(asuint(seed)); }
uint Random(int seed) { return RandomState(asuint(seed)); }
uint Random(int2 seed) { return RandomState(asuint(seed.x)) ^ RandomState(asuint(seed.y)); }
uint Random(int3 seed) { return RandomState(asuint(seed.x)) ^ RandomState(asuint(seed.y)) ^ RandomState(asuint(seed.z)); }
uint Random(float3 seed) { return RandomState(asuint(seed.x)) ^ RandomState(asuint(seed.y)) ^ RandomState(asuint(seed.z)); }

float RandomFloat(float seed){ return RandomFloat(asuint(seed)); }
float RandomFloat(float2 seed){ return RandomFloat(RandomState(asuint(seed.x)) ^ RandomState(asuint(seed.y))); }
float RandomFloat(float3 seed){return RandomFloat(RandomState(asuint(seed.x)) ^ RandomState(asuint(seed.y)) ^ RandomState(asuint(seed.z)));}

float3 Random3(uint seed){
    float3 ret = (float3)0;
    ret.x = RandomFloat(seed);
    ret.y = RandomFloat(ret.x);
    ret.z = RandomFloat(ret.y);
    return ret;
}

#endif