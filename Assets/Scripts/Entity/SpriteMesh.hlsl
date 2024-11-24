#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

struct matTerrain{
    float4 baseColor;
    float baseTextureScale;
    float baseColorStrength;
    int geoShaderInd;
};

StructuredBuffer<matTerrain> _MatTerrainData;
Texture2DArray _Textures;
SamplerState sampler_Textures;

struct v2f
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float2 uv : TEXCOORD2;
    nointerpolation int material: TEXCOORD3; //Materials are definate and can't be interpolated
};

struct appdata
{
    float3 vertex : POSITION;
    uint material: TEXCOORD0;
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
    o.material = v.material;
    return o;
}


float3 frag (v2f IN) : SV_Target
{
    int material = IN.material;
    matTerrain mInfo = _MatTerrainData[material];
    float3 baseColor = mInfo.baseColor.xyz;
    float3 textureColor = _Textures.Sample(sampler_Textures, float3(IN.uv, material)).xyz;
    float colorStrength = mInfo.baseColorStrength;

    InputData lightingInput = (InputData)0;
	lightingInput.positionWS = IN.positionWS;
	lightingInput.normalWS = normalize(IN.normalWS);
    lightingInput.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
	lightingInput.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);

	SurfaceData surfaceInput = (SurfaceData)0;
	surfaceInput.albedo = baseColor * colorStrength + textureColor * (1-colorStrength);

	return UniversalFragmentPBR(lightingInput, surfaceInput);
}