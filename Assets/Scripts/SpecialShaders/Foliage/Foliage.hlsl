#ifndef FOLIAGE_INCLUDED
#define FOLIAGE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "NMGFoliageHelpers.hlsl"

struct DrawVertex{
    float3 positionOS;
    float3 normalOS;
    float2 uv;
};

struct DrawTriangle{
    DrawVertex vertex[3];
};

struct VertexOutput {
    float3 positionWS   : TEXCOORD0; 
    float3 normalWS     : TEXCOORD1;
    float2 uv           : TEXCOORD3;

    float4 positionCS   : SV_POSITION;
};

StructuredBuffer<DrawTriangle> _StorageMemory;
StructuredBuffer<uint2> _AddressDict;
float4x4 _LocalToWorld;
uint addressIndex;

TEXTURE2D(_AlphaMap); SAMPLER(sampler_AlphaMap); float4 _AlphaMap_ST;
float4 _LeafColor;
float _FresnelFalloff;
float _AlphaClip;

TEXTURE2D(_WindNoiseTexture); SAMPLER(sampler_WindNoiseTexture); float4 _WindNoiseTexture_ST;
float _WindTimeMult;
float _WindAmplitude;

VertexOutput Vertex(uint vertexID: SV_VertexID){
    VertexOutput output = (VertexOutput)0;
    if(_AddressDict[addressIndex].x == 0)
        return output;

    uint triAddress = vertexID / 3 + _AddressDict[addressIndex].y;
    uint vertexIndex = vertexID % 3;
    DrawVertex input = _StorageMemory[triAddress].vertex[vertexIndex];

    output.positionWS = mul(_LocalToWorld, float4(input.positionOS, 1)).xyz;
    output.normalWS = normalize(mul(_LocalToWorld, float4(input.normalOS, 0)).xyz);
    output.uv = input.uv;
    output.positionCS = CalculatePositionCSWithShadowCasterLogic(output.positionWS, output.normalWS);

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

half3 Fragment(VertexOutput IN) : SV_TARGET{

    float2 windUV = TRANSFORM_TEX(mapCoordinates(IN.positionWS), _WindNoiseTexture) + _Time.y * _WindTimeMult;
    // Sample the wind noise texture and remap to range from -1 to 1
    float2 windNoise = SAMPLE_TEXTURE2D(_WindNoiseTexture, sampler_WindNoiseTexture, windUV).xy * 2 - 1;
 
    IN.uv = IN.uv + windNoise * _WindAmplitude;

    InputData lightingInput = (InputData)0;
    lightingInput.positionWS = IN.positionWS;
    lightingInput.viewDirectionWS = GetViewDirectionFromPosition(IN.positionWS);
    lightingInput.shadowCoord = CalculateShadowCoord(IN.positionWS, IN.positionCS);
    lightingInput.normalWS = NormalizeNormalPerPixel(IN.normalWS);

    //float fresnel = pow(1.0 - saturate(dot(lightingInput.viewDirectionWS, normalize(IN.normalWS))), _FresnelFalloff);
    //lightingInput.normalWS = (1-fresnel) * lightingInput.normalWS + fresnel * (_LightDirection);

    SurfaceData surfaceInput = (SurfaceData)0;
	surfaceInput.albedo = _LeafColor.rgb;
	surfaceInput.alpha = 1;
    clip(SAMPLE_TEXTURE2D(_AlphaMap, sampler_AlphaMap, IN.uv).a - _AlphaClip);

    return max(UniversalFragmentPBR(lightingInput, surfaceInput).rgb, surfaceInput.albedo * unity_AmbientEquator);
}

#endif