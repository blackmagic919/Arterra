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
    float2 heights; //sharing height
    DrawVertex vertices[3];
};

StructuredBuffer<DrawTriangle> _DrawTriangles;

// Vertex function output and geometry function input
struct VertexOutput {
    float4 uvAndHeight  : TEXCOORD0; // UV, no scaling applied
    float3 positionWS   : TEXCOORD1; // Position in world space
    float3 normalWS     : TEXCOORD2; // Normal vector in world space

    float4 positionCS   : SV_POSITION;
};

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
float _TrampleSize;
float _DistortionClip;
float _TrampleDistortion;
// Vertex functions

VertexOutput Vertex(uint vertexID: SV_VertexID) {
    // Initialize the output struct
    VertexOutput output = (VertexOutput)0;

    DrawTriangle tri = _DrawTriangles[vertexID/3];
    DrawVertex input = tri.vertices[vertexID % 3];

    output.positionWS = input.positionWS;
    output.normalWS = input.normalWS;
    output.uvAndHeight = float4(input.uv, tri.heights);

    output.positionCS = CalculatePositionCSWithShadowCasterLogic(output.positionWS, output.normalWS);
    
    return output;
}

// Fragment functions

half4 Fragment(VertexOutput input) : SV_Target {

    float2 uv = input.uvAndHeight.xy;
    float height = input.uvAndHeight.z;

    float3 tramplePosition = _CameraPosition.xyz - float3(0, _CameraHeight, 0);

    float distance = length(input.positionWS - tramplePosition);
    float influence = (1-clamp(distance, 0, _TrampleSize)) * _TrampleDistortion;

    float2 distortionFactor = mapCoordinates(normalize(tramplePosition-input.positionWS) * influence);

    clip(_DistortionClip-influence);

    uv = uv + distortionFactor*height;

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
    float colorLerp = input.uvAndHeight.w;
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