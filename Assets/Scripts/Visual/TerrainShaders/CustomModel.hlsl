#include "Assets/Resources/Compute/Utility/LambertShade.hlsl"

struct ModelVaryings {
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float4 color : TEXCOORD2;
    nointerpolation uint flags : TEXCOORD3;
};

float4 UnpackModelColor(uint color) {
    return float4(color & 255, (color >> 8) & 255, (color >> 16) & 255, color >> 24) / 255.0;
}

#ifdef INDIRECT
struct ModelDrawVertex {
    float3 positionOS;
#ifndef COMPACT_MODEL_VERTICES
    float3 normalOS;
#endif
    int2 material;
};
StructuredBuffer<ModelDrawVertex> Vertices;
StructuredBuffer<uint3> Triangles;
StructuredBuffer<uint2> _AddressDict;
uint triAddress, vertAddress;
float4x4 _LocalToWorld;

ModelVaryings vert(uint id : SV_VertexID) {
#ifdef MODEL_DIRECT_BUFFER
    uint triangleStart = triAddress;
    uint vertexStart = vertAddress;
#else
    uint triangleStart = _AddressDict[triAddress].y;
    uint vertexStart = _AddressDict[vertAddress].y;
#endif
    uint3 tri = Triangles[triangleStart + id / 3];
    ModelDrawVertex input = Vertices[vertexStart + tri[id % 3]];
#ifdef COMPACT_MODEL_VERTICES
    float3 a = Vertices[vertexStart + tri.x].positionOS;
    float3 b = Vertices[vertexStart + tri.y].positionOS;
    float3 c = Vertices[vertexStart + tri.z].positionOS;
    float3 normal = cross(b - a, c - a);
#else
    float3 normal = input.normalOS;
#endif
    ModelVaryings output;
    output.positionWS = mul(_LocalToWorld, float4(input.positionOS, 1)).xyz;
    output.normalWS = normalize(mul(_LocalToWorld, float4(normal, 0)).xyz);
    output.positionCS = TransformWorldToHClip(output.positionWS);
    output.color = UnpackModelColor(asuint(input.material.y));
    output.flags = asuint(input.material.x) >> 24;
    return output;
}
#else
struct ModelAttributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; int2 material : TEXCOORD0; };
ModelVaryings vert(ModelAttributes input) {
    ModelVaryings output;
    output.positionWS = TransformObjectToWorld(input.positionOS);
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.positionCS = TransformWorldToHClip(output.positionWS);
    output.color = UnpackModelColor(asuint(input.material.y));
    output.flags = asuint(input.material.x) >> 24;
    return output;
}
#endif

float4 frag(ModelVaryings input) : SV_Target {
#ifdef MODEL_PREVIEW
    float3 tint = (input.flags & 2) != 0 ? float3(1, 0.2, 0.2)
        : (input.flags & 1) != 0 ? float3(0.2, 1, 0.2) : float3(1, 1, 1);
    return float4(input.color.rgb * tint, 0.5);
#elif defined(MODEL_DIRECT_BUFFER)
    Light light = GetMainLight();
    float illumination = saturate(dot(normalize(input.normalWS), light.direction));
    return float4(input.color.rgb * (0.25 + 0.75 * illumination * light.color), 1);
#else
    return LambertShade(input.color.rgb, normalize(input.normalWS), input.positionWS);
#endif
}
float4 depthFrag(ModelVaryings input) : SV_Target { return 0; }
