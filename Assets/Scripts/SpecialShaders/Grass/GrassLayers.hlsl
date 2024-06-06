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
#include "NMGGrassLayersHelpers.hlsl"

struct DrawVertex{
    float3 positionWS;
    float3 normalWS;
    float2 uv;
};

struct DrawTriangle{
    DrawVertex vertex[3];
};

// Vertex function output and geometry function input
struct VertexOutput {
    float3 positionWS   : TEXCOORD0; // Position in world space
    float3 normalWS     : TEXCOORD1; // Normal vector in world space
    float2 uv  : TEXCOORD2; // UV
    float2 height : TEXCOORD3; // Height of the layer

    float4 positionCS   : SV_POSITION;
};

StructuredBuffer<DrawTriangle> _StorageMemory;
StructuredBuffer<uint2> _AddressDict;
uint addressIndex;


// Properties
float4 _BaseColor;
float4 _TopColor;
// These two textures are combined to create the grass pattern in the fragment function
TEXTURE2D(_DetailNoiseTexture); SAMPLER(sampler_DetailNoiseTexture); float4 _DetailNoiseTexture_ST;
float _DetailDepthScale;
TEXTURE2D(_SmoothNoiseTexture); SAMPLER(sampler_SmoothNoiseTexture); float4 _SmoothNoiseTexture_ST;
float _SmoothDepthScale;
// Wind properties
TEXTURE2D(_WindNoiseTexture); SAMPLER(sampler_WindNoiseTexture); float4 _WindNoiseTexture_ST;
float _WindTimeMult;
float _WindAmplitude;

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
float _WSToUVScale;

// Vertex functions

VertexOutput Vertex(uint vertexID: SV_VertexID)
{
    VertexOutput output = (VertexOutput)0;
    if(_AddressDict[addressIndex].x == 0)
        return output;

    uint triAddress = vertexID / 3 + _AddressDict[addressIndex].y;
    uint vertexIndex = vertexID % 3;
    DrawVertex input = _StorageMemory[triAddress].vertex[vertexIndex];

    output.positionWS = input.positionWS.xyz;
    output.normalWS = input.normalWS.xyz;
    output.uv = mapCoordinates(input.positionWS) * _WSToUVScale;
    output.height = input.uv;

    output.positionCS = CalculatePositionCSWithShadowCasterLogic(output.positionWS, output.normalWS);
    
    return output;
}

// Fragment functions

half4 Fragment(VertexOutput input) : SV_Target {

    float2 uv = input.uv;
    float height = input.height.x;

    // Calculate wind
    // Get the wind noise texture uv by applying scale and offset and then adding a time offset
    float2 windUV = TRANSFORM_TEX(uv, _WindNoiseTexture) + _Time.y * _WindTimeMult;
    // Sample the wind noise texture and remap to range from -1 to 1
    float2 windNoise = SAMPLE_TEXTURE2D(_WindNoiseTexture, sampler_WindNoiseTexture, windUV).xy * 2 - 1;
    // Offset the grass UV by the wind. Higher layers are affected more
    uv = uv + windNoise * (_WindAmplitude * height);

    // Sample the two noise textures, applying their scale and offset
    float detailNoise = SAMPLE_TEXTURE2D(_DetailNoiseTexture, sampler_DetailNoiseTexture, TRANSFORM_TEX(uv, _DetailNoiseTexture)).r;
    float smoothNoise = SAMPLE_TEXTURE2D(_SmoothNoiseTexture, sampler_SmoothNoiseTexture, TRANSFORM_TEX(uv, _SmoothNoiseTexture)).r;
    // Combine the textures together using these scale variables. Lower values will reduce a texture's influence
    detailNoise = 1 - (1 - detailNoise) * _DetailDepthScale;
    smoothNoise = 1 - (1 - smoothNoise) * _SmoothDepthScale;
    // If detailNoise * smoothNoise is less than height, this pixel will be discarded by the renderer
    // I.E. this pixel will not render. The fragment function returns as well
    clip(detailNoise * smoothNoise - height);

    // If the code reaches this far, this pixel should render

#ifdef SHADOW_CASTER_PASS
    // If we're in the shadow caster pass, it's enough to return now. We don't care about color
    return 0;
#else
    // Gather some data for the lighting algorithm
    InputData lightingInput = (InputData)0;
    lightingInput.positionWS = input.positionWS;
    lightingInput.normalWS = NormalizeNormalPerPixel(input.normalWS); // Renormalize the normal to reduce interpolation errors
    lightingInput.viewDirectionWS = GetViewDirectionFromPosition(input.positionWS); // Calculate the view direction
    lightingInput.shadowCoord = CalculateShadowCoord(input.positionWS, input.positionCS); // Calculate the shadow map coord

    // Lerp between the two grass colors based on layer height
    float colorLerp = input.height.y;
    float3 albedo = lerp(_BaseColor, _TopColor, colorLerp).rgb;

    SurfaceData surfaceInput = (SurfaceData)0;
	surfaceInput.albedo = albedo;
	surfaceInput.alpha = 1;

    // The URP simple lit algorithm
    // The arguments are lighting input data, albedo color, specular color, smoothness, emission color, and alpha
    //return UniversalFragmentBlinnPhong(lightingInput, albedo, 1, 0, 0, 1); <-- code has been depreciated
    return UniversalFragmentBlinnPhong(lightingInput, surfaceInput);
#endif
}

#endif