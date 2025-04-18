#ifndef FOLIAGE_INCLUDED
#define FOLIAGE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "NMGFoliageHelpers.hlsl"
#include "NMGFoliageHelpers.hlsl"
#include "Assets/Resources/Compute/GeoShader/VertexPacker.hlsl"
#include "Assets/Resources/Compute/MapData/WSLightSampler.hlsl"

struct DrawTriangle{
    uint2 vertex[3];
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
    uint2 input = _StorageMemory[triAddress].vertex[vertexIndex];

    VertexInfo v = UnpackVertex(input);
    
    float2 uv;
    uv.x = (input >> 30) & 1;
    uv.y = (input >> 31) & 1;

    output.positionWS = mul(_LocalToWorld, float4(v.positionOS, 1)).xyz;
    output.normalWS = normalize(mul(_LocalToWorld, float4(v.normalOS, 0)).xyz);
    output.positionCS = TransformWorldToHClip(output.positionWS);
    output.uv = uv;

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

    uint light = SampleLight(IN.positionWS);
    float shadow = (1.0 - (light >> 30 & 0x3) / 3.0f) * 0.5 + 0.5;
    float3 DynamicLight = UniversalFragmentPBR(lightingInput, surfaceInput) * shadow;
    float3 ObjectLight = float3(light & 0x3FF, (light >> 10) & 0x3FF, (light >> 20) & 0x3FF) / 1023.0f;
    ObjectLight = mad((1 - ObjectLight), unity_AmbientEquator, ObjectLight * 2.5f); //linear interpolation
    ObjectLight *= surfaceInput.albedo;

    return max(DynamicLight, ObjectLight).rgb;
}

#endif