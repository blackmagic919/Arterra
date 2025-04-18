#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Assets/Resources/Compute/MapData/WSLightSampler.hlsl"

struct matTerrain{
    int textureIndex;
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
    matTerrain tInfo =  _MatTerrainData[material];
    float3 baseColor = tInfo.baseColor.xyz;
    float colorStrength = tInfo.baseColorStrength;
    float3 textureColor = triplanar(IN.positionWS, tInfo.baseTextureScale, blendAxes, tInfo.textureIndex);

    uint light = SampleLight(IN.positionWS);
    float shadow = (1.0 - (light >> 30 & 0x3) / 3.0f) * 0.5 + 0.5;
    //float shadow = (dot((1, 1, 1), WSToCS(IN.positionWS)) % 2 == 0 ? 1 : 0) * 0.5 + 0.5;//
    InputData lightingInput = (InputData)0;
	lightingInput.positionWS = IN.positionWS;
	lightingInput.normalWS = normalize(IN.normalWS);
    lightingInput.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
	lightingInput.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);

	SurfaceData surfaceInput = (SurfaceData)0;
	surfaceInput.albedo = baseColor * colorStrength + textureColor * (1-colorStrength);

    float3 DynamicLight = UniversalFragmentPBR(lightingInput, surfaceInput) * shadow;
    float3 ObjectLight = float3(light & 0x3FF, (light >> 10) & 0x3FF, (light >> 20) & 0x3FF) / 1023.0f;
    ObjectLight = mad((1 - ObjectLight), unity_AmbientGround, ObjectLight * 2.5f); //linear interpolation
    ObjectLight *= surfaceInput.albedo;


	return max(DynamicLight, ObjectLight).rgb;
}