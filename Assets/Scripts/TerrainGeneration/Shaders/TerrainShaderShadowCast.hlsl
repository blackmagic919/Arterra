#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

struct Interpolators {
	float4 positionCS : SV_POSITION;
};

float3 _LightDirection;

float4 GetShadowCasterPositionCS(float3 positionWS, float3 normalWS) {
	float3 lightDirectionWS = _LightDirection;
	float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

#if UNITY_REVERSED_Z
	positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
	positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
	return positionCS;
}


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

Interpolators vert (uint vertexID: SV_VertexID){
    Interpolators output = (Interpolators)0;

    uint vertInd = Triangles[_AddressDict[triAddress].y + (vertexID/3)].axis[vertexID%3];
    DrawVertex input = Vertices[vertInd + _AddressDict[vertAddress].y];

    float3 positionWS = mul(_LocalToWorld, float4(input.positionOS, 1)).xyz;
    float3 normalWS = normalize(mul(_LocalToWorld, float4(input.normalOS, 0)).xyz);

    output.positionCS = GetShadowCasterPositionCS(positionWS, normalWS);
    return output;
}

#else

struct Attributes {
	float3 positionOS : POSITION;
	float3 normalOS : NORMAL;
	int2 material : TEXCOORD0;
};


Interpolators vert(Attributes input) {
	Interpolators output;

	VertexPositionInputs posnInputs = GetVertexPositionInputs(input.positionOS);
	VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

	output.positionCS = GetShadowCasterPositionCS(posnInputs.positionWS, normInputs.normalWS);
	return output;
}

#endif

float4 frag(Interpolators input) : SV_TARGET {
	return 0;
}