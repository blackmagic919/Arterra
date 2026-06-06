#ifndef FOLIAGE_INCLUDED
#define FOLIAGE_INCLUDED

#include "Assets/Resources/Compute/Utility/LambertShade.hlsl"
#include "Assets/Resources/Compute/GeoShader/VertexPacker.hlsl"

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

float2 mapCoordinates(float3 worldPos);

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

    float2 worldWindUV = mapCoordinates(output.positionWS);
    float2 windNoiseUV = TRANSFORM_TEX(worldWindUV, _WindNoiseTexture) + _Time.y * _WindTimeMult;
    float3 windNoise = SAMPLE_TEXTURE2D_LOD(_WindNoiseTexture, sampler_WindNoiseTexture, windNoiseUV, 0).rgb * 2 - 1;
    float windStrength = windNoise.r;
    float3 windDirection = windNoise;
    float windDirLength = length(windDirection);
    if (windDirLength > 1e-4)
    {
        windDirection /= windDirLength;
        output.positionWS += windDirection * (windStrength * _WindAmplitude);
    }

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
    Settings cxt = VariantSettings[IN.variant];
    clip(_Textures.Sample(sampler_Textures, float3(IN.uv, cxt.TexIndex)).a - _AlphaClip);
    float3 normal = NormalizeNormalPerPixel(IN.normalWS);
    float3 albedo = cxt.LeafColor.rgb;

    return LambertShade(albedo, IN.normalWS, IN.positionWS);
}

#endif