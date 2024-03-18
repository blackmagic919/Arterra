#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

StructuredBuffer<float4> _BaseColors;
StructuredBuffer<float> _BaseColorStrength;
StructuredBuffer<float> _BaseTextureScales;

Texture2DArray _Textures;
SamplerState sampler_Textures;

struct v2f
{
    float2 uv : TEXCOORD0;
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD1;
    float3 normalWS : TEXCOORD2;
    nointerpolation float4 color: COLOR; //Materials are definate and can't be interpolated
};

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

v2f vert (uint vertexID: SV_VertexID){
    v2f o = (v2f)0;

    uint vertexAddress = vertexID * _Vertex4ByteStride;
    DrawVertex input = ReadVertex(vertexAddress);

    o.positionWS = mul(_LocalToWorld, float4(input.positionOS, 1)).xyz;
    o.normalWS = normalize(mul(_LocalToWorld, float4(input.normalOS, 0)).xyz);
    o.positionCS = TransformWorldToHClip(o.positionWS);

    o.color = float4(input.material, 0, 0, 1);
    return o;
}

#else

float4x4 _LocalToWorld;

struct appdata
{
    float4 vertex : POSITION;
    float4 normal : NORMAL;
    float4 color: COLOR;
};

v2f vert (appdata v)
{
    v2f o = (v2f)0;

    VertexPositionInputs posInputs = GetVertexPositionInputs(v.vertex.xyz);
	VertexNormalInputs normInputs = GetVertexNormalInputs(v.normal.xyz);

    o.positionCS = posInputs.positionCS;
    o.positionWS = posInputs.positionWS;
    o.normalWS = normInputs.normalWS;
    o.color = v.color;
    return o;
}

#endif

float4 lerp(float4 a, float4 b, float value){
    return (a*value) + (b*(1-value));
}

float3 lerp(float3 a, float3 b, float value){
    return (a*value) + (b*(1-value));
}

float lerp(float a, float b, float value){
    return (a*value) + (b*(1-value));
}

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

    int material = (int)IN.color.r;
    float alpha = IN.color.a;

    float3 baseColor = _BaseColors[material].xyz;

    float3 textureColor = triplanar(IN.positionWS, _BaseTextureScales[material], blendAxes, material);

    float colorStrength = _BaseColorStrength[material];

    InputData lightingInput = (InputData)0;
	lightingInput.positionWS = IN.positionWS;
	lightingInput.normalWS = normalize(IN.normalWS);
    lightingInput.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
	lightingInput.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);

	SurfaceData surfaceInput = (SurfaceData)0;
	surfaceInput.albedo = baseColor * colorStrength + textureColor * (1-colorStrength);
	surfaceInput.alpha = alpha;

#if UNITY_VERSION >= 202120
	return UniversalFragmentBlinnPhong(lightingInput, surfaceInput);
#else
	return UniversalFragmentBlinnPhong(lightingInput, surfaceInput.albedo, float4(surfaceInput.specular, 1), surfaceInput.smoothness, 0, surfaceInput.alpha);
#endif
}