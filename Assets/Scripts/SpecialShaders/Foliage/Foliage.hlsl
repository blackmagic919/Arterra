#ifndef FOLIAGE_INCLUDED
#define FOLIAGE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "NMGFoliageHelpers.hlsl"

struct DrawVertex{
    float3 positionWS;
    float3 normalWS;
    float2 uv;
};

struct DrawTriangle{
    DrawVertex vertices[3];
};

StructuredBuffer<DrawTriangle> _DrawTriangles;

struct VertexOutput {
    float3 positionWS   : TEXCOORD0; 
    float3 normalWS     : TEXCOORD1;
    float2 uv           : TEXCOORD3;

    float4 positionCS   : SV_POSITION;
};

TEXTURE2D(_AlphaMap); SAMPLER(sampler_AlphaMap); float4 _AlphaMap_ST;
float4 _LeafColor;
float _FresnelFalloff;
float _AlphaClip;

TEXTURE2D(_WindNoiseTexture); SAMPLER(sampler_WindNoiseTexture); float4 _WindNoiseTexture_ST;
float _WindTimeMult;
float _WindAmplitude;

VertexOutput Vertex(uint vertexID: SV_VertexID){
    VertexOutput output = (VertexOutput)0;

    DrawTriangle tri = _DrawTriangles[vertexID / 3];
    DrawVertex input = tri.vertices[vertexID % 3];

    output.positionWS = input.positionWS;
    output.normalWS = input.normalWS;
    output.positionCS = CalculatePositionCSWithShadowCasterLogic(output.positionWS, input.normalWS);

    output.uv = input.uv;
    return output;
}

float2 mapCoordinates(float3 worldPos)
{
    float2 projXY = worldPos.xy;
    float2 projXZ = worldPos.xz;
    float2 projYZ = worldPos.yz;

    float2 worldUV = (projXY + projXZ + projYZ) / 3;

    return worldUV;
}

float3 _LightDirection;

half4 Fragment(VertexOutput IN) : SV_TARGET{

    float2 windUV = TRANSFORM_TEX(mapCoordinates(IN.positionWS), _WindNoiseTexture) + _Time.y * _WindTimeMult;
    // Sample the wind noise texture and remap to range from -1 to 1
    float2 windNoise = SAMPLE_TEXTURE2D(_WindNoiseTexture, sampler_WindNoiseTexture, windUV).xy * 2 - 1;
 
    IN.uv = IN.uv + windNoise * _WindAmplitude;

    InputData lightingInput = (InputData)0;
    lightingInput.positionWS = IN.positionWS;
    lightingInput.viewDirectionWS = GetViewDirectionFromPosition(IN.positionWS);
    lightingInput.shadowCoord = CalculateShadowCoord(IN.positionWS, IN.positionCS);

    float fresnel = pow(1.0 - saturate(dot(lightingInput.viewDirectionWS, normalize(IN.normalWS))), _FresnelFalloff);

    lightingInput.normalWS = (1-fresnel) * NormalizeNormalPerPixel(IN.normalWS) + fresnel * (_LightDirection);

    SurfaceData surfaceInput = (SurfaceData)0;
	surfaceInput.albedo = _LeafColor.rgb;
	surfaceInput.alpha = SAMPLE_TEXTURE2D(_AlphaMap, sampler_AlphaMap, IN.uv).a;
    clip(surfaceInput.alpha - _AlphaClip);

    #if UNITY_VERSION >= 202120
	    return UniversalFragmentBlinnPhong(lightingInput, surfaceInput);
    #else
	    return UniversalFragmentBlinnPhong(lightingInput, surfaceInput.albedo, float4(surfaceInput.specular, 1), surfaceInput.smoothness, 0, surfaceInput.alpha);
    #endif
}

#endif