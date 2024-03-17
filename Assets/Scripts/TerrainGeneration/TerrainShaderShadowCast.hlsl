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
    int2 id;
    int material;
};

StructuredBuffer<uint> _StorageMemory;
StructuredBuffer<uint> _AddressDict;
uint addressIndex;
uint _Vertex4ByteStride;

DrawVertex ReadVertex(uint vertexAddress){
    uint address = vertexAddress + _AddressDict[addressIndex];
    DrawVertex vertex = (DrawVertex)0;

    vertex.positionOS.x = asfloat(_StorageMemory[address]);
    vertex.positionOS.y = asfloat(_StorageMemory[address + 1]);
    vertex.positionOS.z = asfloat(_StorageMemory[address + 2]);

    vertex.normalOS.x = asfloat(_StorageMemory[address + 3]);
    vertex.normalOS.y = asfloat(_StorageMemory[address + 4]);
    vertex.normalOS.z = asfloat(_StorageMemory[address + 5]);

    vertex.id.x = asint(_StorageMemory[address + 6]);
    vertex.id.y = asint(_StorageMemory[address + 7]);

    vertex.material = asint(_StorageMemory[address + 8]);

    return vertex;
}

Interpolators vert (uint vertexID: SV_VertexID){
    Interpolators output = (Interpolators)0;

    uint vertexAddress = vertexID * _Vertex4ByteStride;
    DrawVertex input = ReadVertex(vertexAddress);

    float3 positionWS = mul(_LocalToWorld, float4(input.positionOS, 1)).xyz;
    float3 normalWS = normalize(mul(_LocalToWorld, float4(input.normalOS, 0)).xyz);

    output.positionCS = GetShadowCasterPositionCS(positionWS, normalWS);
    return output;
}

#else

struct Attributes {
	float3 positionOS : POSITION;
	float3 normalOS : NORMAL;
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