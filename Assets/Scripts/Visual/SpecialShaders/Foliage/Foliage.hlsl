#ifndef FOLIAGE_INCLUDED
#define FOLIAGE_INCLUDED

#include "Assets/Resources/Compute/Utility/LambertShade.hlsl"
#include "Assets/Resources/Compute/GeoShader/VertexPacker.hlsl"
#include "Assets/Resources/Compute/MapData/WSLightSampler.hlsl"

struct DrawTriangle{
    uint3 vertex[3];
};

struct VertexOutput {
    float4 positionCS   : SV_POSITION;
    float3 positionWS   : TEXCOORD0; 
    float3 normalWS     : TEXCOORD1;
    float2 uv           : TEXCOORD3;
    nointerpolation int variant : TEXCOORD4;
};

struct Settings{
    float QuadSize;
    float InflationFactor;
    float4 LeafColor;
    int TexIndex;
    int QuadType;
};

StructuredBuffer<Settings> VariantSettings;
StructuredBuffer<DrawTriangle> _StorageMemory;
StructuredBuffer<uint2> _AddressDict;
float4x4 _LocalToWorld;
uint addressIndex;

Texture2DArray _Textures;
SamplerState sampler_Textures;
float _AlphaClip;
TEXTURE2D(_WindNoiseTexture); SAMPLER(sampler_WindNoiseTexture); float4 _WindNoiseTexture_ST;
float _WindTimeMult;
float _WindAmplitude;

VertexOutput Vertex(uint vertexID: SV_VertexID){
    VertexOutput output = (VertexOutput)0;
    if(_AddressDict[addressIndex].x == 0)
        return output;

    uint triAddress = vertexID / 3 + _AddressDict[addressIndex].y;
    uint vertexIndex = vertexID % 3;
    uint3 input = _StorageMemory[triAddress].vertex[vertexIndex];
    VertexInfo v = UnpackVertex(input);
    
    float2 uv;
    uv.x = (input.z >> 24) & 0xF;
    uv.y = (input.z >> 28) & 0xF;

    output.positionWS = mul(_LocalToWorld, float4(v.positionOS, 1)).xyz;
    output.normalWS = normalize(mul(_LocalToWorld, float4(v.normalOS, 0)).xyz);
    output.positionCS = TransformWorldToHClip(output.positionWS);
    output.variant = v.variant;
    output.uv = uv;

    return output;
}

float2 mapCoordinates(float3 worldPos)
{
    float2 projXY = worldPos.xy;
    float2 projXZ = worldPos.xz;
    float2 projYZ = worldPos.yz;

    float2 worldUV = (projXY + projXZ + projYZ) / 3;

    return worldUV;
}


half3 Fragment(VertexOutput IN) : SV_TARGET{
    float2 windUV = TRANSFORM_TEX(mapCoordinates(IN.positionWS), _WindNoiseTexture) + _Time.y * _WindTimeMult;
    // Sample the wind noise texture and remap to range from -1 to 1
    float2 windNoise = SAMPLE_TEXTURE2D(_WindNoiseTexture, sampler_WindNoiseTexture, windUV).xy * 2 - 1;
    IN.uv = clamp(IN.uv + windNoise * _WindAmplitude, 0, 0.992);

    Settings cxt = VariantSettings[IN.variant];
    clip(_Textures.Sample(sampler_Textures, float3(IN.uv, cxt.TexIndex)).a - _AlphaClip);
    float3 normal = NormalizeNormalPerPixel(IN.normalWS);
    float3 albedo = cxt.LeafColor.rgb;

    uint light = SampleLight(IN.positionWS);
    float shadow = (1.0 - (light >> 30 & 0x3) / 3.0f);
    float3 DynamicLight = LambertShade(albedo, normal, shadow);
    float3 ObjectLight = float3(light & 0x3FF, (light >> 10) & 0x3FF, (light >> 20) & 0x3FF) / 1023.0f;
    ObjectLight = mad((1 - ObjectLight), unity_AmbientEquator, ObjectLight * 2.5f); //linear interpolation
    ObjectLight *= albedo;

    return max(DynamicLight, ObjectLight).rgb;
}

#endif