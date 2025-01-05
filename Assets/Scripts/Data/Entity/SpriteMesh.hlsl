#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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


float3 frag (v2f IN) : SV_Target
{
    float3 textureColor = _Textures.Sample(sampler_Textures, float3(IN.uv, IN.texInd)).xyz;

    InputData lightingInput = (InputData)0;
	lightingInput.positionWS = IN.positionWS;
	lightingInput.normalWS = normalize(IN.normalWS);
    lightingInput.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
	lightingInput.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);

	SurfaceData surfaceInput = (SurfaceData)0;
	surfaceInput.albedo = textureColor;

	return UniversalFragmentPBR(lightingInput, surfaceInput);
}