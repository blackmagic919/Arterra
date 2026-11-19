#include "Assets/Resources/Compute/Utility/LambertShade.hlsl"

Texture2DArray _Textures;
SamplerState sampler_Textures;

struct v2f
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float2 uv : TEXCOORD2;
    nointerpolation int texInd: TEXCOORD3; //Materials are definate and can't be interpolated
};

struct appdata
{
    float3 vertex : POSITION;
    uint texInd: TEXCOORD0;
    uint uv : TEXCOORD1;
};

v2f vert (appdata v)
{
    v2f o = (v2f)0;

    VertexPositionInputs posInputs = GetVertexPositionInputs(v.vertex.xyz);
    VertexNormalInputs normInputs = GetVertexNormalInputs(float3(0, 0, 1) * sign(v.vertex.z));

    o.positionCS = posInputs.positionCS;
    o.positionWS = posInputs.positionWS;
    o.normalWS = normInputs.normalWS;
    o.uv = float2(
        (v.uv & 0xFFFF) / 65535.0f,
        ((v.uv >> 16) & 0xFFFF) / 65535.0f
    );
    o.texInd = v.texInd;
    return o;
}


float4 frag (v2f IN) : SV_Target
{
    float3 albedo = _Textures.Sample(sampler_Textures, float3(IN.uv, IN.texInd)).rgb;
    return LambertShade(albedo, IN.normalWS, IN.positionWS);
}
