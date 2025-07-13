// MIT License

// Copyright (c) 2020 NedMakesGames

// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files(the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions :

// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.

// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

// Make sure this file is not included twice
#ifndef GRASSLAYERS_INCLUDED
#define GRASSLAYERS_INCLUDED

// Include some helper functions
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Assets/Resources/Compute/GeoShader/VertexPacker.hlsl"
#include "Assets/Resources/Compute/MapData/WSLightSampler.hlsl"
#include "Assets/Resources/Compute/Utility/LambertShade.hlsl"

struct DrawTriangle{
    uint3 vertex[3];
};

// Vertex function output and geometry function input
struct VertexOutput {
    float3 positionWS   : TEXCOORD0; // Position in world space
    float3 normalWS     : TEXCOORD1; // Normal vector in world space
    float2 uv  : TEXCOORD2; // UV
    float2 height : TEXCOORD3; // Height of the layer
    nointerpolation int variant : TEXCOORD4;//

    float4 positionCS   : SV_POSITION;
};

struct Settings{
    float TotalHeight;
    int MaxLayers;
    int TexIndex;
    float4 BaseColor;
    float4 TopColor;
    float Scale;
    float CenterHeight;
    float WindFrequency;
    float WindStrength;
};

StructuredBuffer<Settings> VariantSettings;
StructuredBuffer<DrawTriangle> _StorageMemory;
StructuredBuffer<uint2> _AddressDict;
float4x4 _LocalToWorld;
uint addressIndex;


// Wind properties
Texture2DArray _Textures;
SamplerState sampler_Textures;
TEXTURE2D(_WindNoiseTexture); SAMPLER(sampler_WindNoiseTexture); float4 _WindNoiseTexture_ST;

float2 mapCoordinates(float3 worldPos)
{
    float2 projXY = worldPos.xy;
    float2 projXZ = worldPos.xz;
    float2 projYZ = worldPos.yz;

    float2 worldUV = (projXY + projXZ + projYZ) / 3;

    return worldUV;
}

float4 _CameraPosition;
float _CameraHeight;

// Vertex functions

VertexOutput Vertex(uint vertexID: SV_VertexID)
{
    VertexOutput output = (VertexOutput)0;
    if(_AddressDict[addressIndex].x == 0)
        return output;

    uint triAddress = vertexID / 3 + _AddressDict[addressIndex].y;
    uint vertexIndex = vertexID % 3;
    uint3 input = _StorageMemory[triAddress].vertex[vertexIndex];

    VertexInfo v = UnpackVertex(input);
    output.positionWS = mul(_LocalToWorld, float4(v.positionOS, 1)).xyz;
    output.normalWS = normalize(mul(_LocalToWorld, float4(v.normalOS, 0)).xyz);
    output.uv = mapCoordinates(output.positionWS) * VariantSettings[v.variant].Scale;
    output.positionCS = TransformWorldToHClip(output.positionWS);
    output.variant = v.variant;

    float height = (uint)((input.z >> 24) & 0xFF) / 255.0f;
    output.height.xy = height;

    return output;
}


// Fragment functions
half3 Fragment(VertexOutput input) : SV_Target {

    float2 uv = input.uv;
    float height = input.height.x;

    // Calculate wind
    Settings cxt = VariantSettings[input.variant];
    float2 windUV = TRANSFORM_TEX(uv, _WindNoiseTexture) + _Time.y * cxt.WindFrequency;
    float2 windNoise = SAMPLE_TEXTURE2D(_WindNoiseTexture, sampler_WindNoiseTexture, windUV).xy * 2 - 1;
    uv = uv + windNoise * (cxt.WindStrength * height);

    float3 detailNoise = _Textures.Sample(sampler_Textures, float3(uv, cxt.TexIndex));
    float value;
    if(height > cxt.CenterHeight)
        value = ((height - cxt.CenterHeight) * detailNoise.b + (1 - height) * detailNoise.g) * (1/(1-cxt.CenterHeight));
    else
        value = ((height) * detailNoise.g + (cxt.CenterHeight - height) * detailNoise.r) * (1/cxt.CenterHeight);

    clip(value-0.5f);

    // Lerp between the two grass colors based on layer height
    float colorLerp = input.height.y;
    float3 albedo = lerp(cxt.BaseColor, cxt.TopColor, colorLerp).rgb;
    float3 normal = NormalizeNormalPerPixel(input.normalWS);

    uint light = SampleLight(input.positionWS);
    float shadow = (1.0 - (light >> 30 & 0x3) / 3.0f);
    float3 DynamicLight = LambertShade(albedo, normal, shadow);
    float3 ObjectLight = float3(light & 0x3FF, (light >> 10) & 0x3FF, (light >> 20) & 0x3FF) / 1023.0f;
    ObjectLight = mad((1 - ObjectLight), unity_AmbientEquator, ObjectLight * 2.5f); //linear interpolation
    ObjectLight *= albedo;

    return max(DynamicLight, ObjectLight).rgb;
}

#endif