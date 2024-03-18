#ifndef FOLIAGE_INCLUDED
#define FOLIAGE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "NMGFoliageHelpers.hlsl"

struct DrawVertex{
    float4 positionWS;
    float4 normalWS;
    float4 color;
    float2 uv;
};

struct VertexOutput {
    float3 positionWS   : TEXCOORD0; 
    float3 normalWS     : TEXCOORD1;
    float2 uv           : TEXCOORD3;

    float4 positionCS   : SV_POSITION;
};

StructuredBuffer<uint> _StorageMemory;
StructuredBuffer<uint> _AddressDict;
uint addressIndex;

uint _Vertex4ByteStride;

TEXTURE2D(_AlphaMap); SAMPLER(sampler_AlphaMap); float4 _AlphaMap_ST;
float4 _LeafColor;
float _FresnelFalloff;
float _AlphaClip;

TEXTURE2D(_WindNoiseTexture); SAMPLER(sampler_WindNoiseTexture); float4 _WindNoiseTexture_ST;
float _WindTimeMult;
float _WindAmplitude;


DrawVertex ReadVertex(uint vertexAddress){
    uint address = vertexAddress + _AddressDict[addressIndex];
    DrawVertex vertex = (DrawVertex)0;

    vertex.positionWS.x = asfloat(_StorageMemory[address]);
    vertex.positionWS.y = asfloat(_StorageMemory[address + 1]);
    vertex.positionWS.z = asfloat(_StorageMemory[address + 2]);

    vertex.normalWS.x = asfloat(_StorageMemory[address + 3]);
    vertex.normalWS.y = asfloat(_StorageMemory[address + 4]);
    vertex.normalWS.z = asfloat(_StorageMemory[address + 5]);

    vertex.uv.x = asfloat(_StorageMemory[address + 6]);
    vertex.uv.y = asfloat(_StorageMemory[address + 7]);

    vertex.color.x = asfloat(_StorageMemory[address + 8]);
    vertex.color.y = asfloat(_StorageMemory[address + 9]);
    vertex.color.z = asfloat(_StorageMemory[address + 10]);
    vertex.color.w = asfloat(_StorageMemory[address + 11]);

    return vertex;
}

VertexOutput Vertex(uint vertexID: SV_VertexID){
    VertexOutput output = (VertexOutput)0;
    if(_AddressDict[addressIndex] == 0)
        return output;

    uint vertexAddress = vertexID * _Vertex4ByteStride;
    DrawVertex input = ReadVertex(vertexAddress);

    output.positionWS = input.positionWS.xyz;
    output.normalWS = input.normalWS.xyz;
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

half4 Fragment(VertexOutput IN) : SV_TARGET{

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

    #if UNITY_VERSION >= 202120
	    return UniversalFragmentBlinnPhong(lightingInput, surfaceInput);
    #else
	    return UniversalFragmentBlinnPhong(lightingInput, surfaceInput.albedo, float4(surfaceInput.specular, 1), surfaceInput.smoothness, 0, surfaceInput.alpha);
    #endif
}

#endif