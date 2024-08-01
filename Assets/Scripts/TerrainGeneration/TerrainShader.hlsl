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
    nointerpolation int material: TEXCOORD2; //Materials are definate and can't be interpolated
};

#ifdef INDIRECT

float4x4 _LocalToWorld;

struct DrawVertex{
    float3 positionOS;
    float3 normalOS;
    int2 material;
};

struct vInfo{
    uint axis[3];
};

StructuredBuffer<DrawVertex> Vertices;
StructuredBuffer<vInfo> Triangles;
StructuredBuffer<uint2> _AddressDict;
uint triAddress;
uint vertAddress;

v2f vert (uint vertexID: SV_VertexID){
    v2f o = (v2f)0;

    uint vertInd = Triangles[_AddressDict[triAddress].y + (vertexID/3)].axis[vertexID%3];
    DrawVertex input = Vertices[vertInd + _AddressDict[vertAddress].y];

    o.positionWS = mul(_LocalToWorld, float4(input.positionOS, 1)).xyz;
    o.normalWS = normalize(mul(_LocalToWorld, float4(input.normalOS, 0)).xyz);
    o.positionCS = TransformWorldToHClip(o.positionWS);
    o.material = input.material.x;
    return o;
}

#else

struct appdata
{
    float3 vertex : POSITION;
    float3 normal : NORMAL;
    int2 material: TEXCOORD0;
};

v2f vert (appdata v)
{
    v2f o = (v2f)0;

    VertexPositionInputs posInputs = GetVertexPositionInputs(v.vertex.xyz);
	VertexNormalInputs normInputs = GetVertexNormalInputs(v.normal.xyz);

    o.positionCS = posInputs.positionCS;
    o.positionWS = posInputs.positionWS;
    o.normalWS = normInputs.normalWS;
    o.material = v.material.x;
    return o;
}

#endif


float3 triplanar(float3 worldPos, float scale, float3 blendAxes, int texInd){
    float3 scaledWorldPos = worldPos / scale;
    
    float3 xProjection = _Textures.Sample(sampler_Textures, float3(scaledWorldPos.y, scaledWorldPos.z, texInd)).xyz * blendAxes.x;
    float3 yProjection = _Textures.Sample(sampler_Textures, float3(scaledWorldPos.x, scaledWorldPos.z, texInd)).xyz * blendAxes.y;
    float3 zProjection = _Textures.Sample(sampler_Textures, float3(scaledWorldPos.x, scaledWorldPos.y, texInd)).xyz * blendAxes.z;

    return xProjection + yProjection + zProjection;
}


float3 frag (v2f IN) : SV_Target
{
    float3 blendAxes = abs(IN.normalWS);
    blendAxes /= blendAxes.x + blendAxes.y + blendAxes.z;

    int material = IN.material;

    float3 baseColor = _MatTerrainData[material].baseColor.xyz;

    float3 textureColor = triplanar(IN.positionWS, _MatTerrainData[material].baseTextureScale, blendAxes, material);

    float colorStrength = _MatTerrainData[material].baseColorStrength;

    InputData lightingInput = (InputData)0;
	lightingInput.positionWS = IN.positionWS;
	lightingInput.normalWS = normalize(IN.normalWS);
    lightingInput.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
	lightingInput.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);

	SurfaceData surfaceInput = (SurfaceData)0;
	surfaceInput.albedo = baseColor * colorStrength + textureColor * (1-colorStrength);

	return max(UniversalFragmentPBR(lightingInput, surfaceInput).rgb, surfaceInput.albedo * unity_AmbientGround);
}