// Two-pass box blur shader created for URP 12 and Unity 2021.2
// Made by Alexander Ameye 
// https://alexanderameye.github.io/

Shader "Hidden/Fog"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white"
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"
        }
        ZWrite Off
        
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        #include "Assets/Resources/Compute/Atmosphere/TextureInterpHelper.hlsl"

        struct Attributes
        {
            uint vertexID : SV_VertexID;
        };
//
        struct v2f
        {
            float4 positionHCS : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        TEXTURE2D_X(_BlitTexture);
        Texture2D<float> _CameraDepthTexture;
        StructuredBuffer<ScatterData> _OpticalInfo;
        StructuredBuffer<ScatterData> _RemainderScatter;
        float _GroundExtinctionMultiplier;

        v2f vert(Attributes IN)
        {
            v2f OUT;
            float2 positionUV = float2((IN.vertexID << 1) & 2, IN.vertexID & 2);
            OUT.positionHCS = float4(positionUV * 2.0 - 1.0, 0.0, 1.0);
            OUT.uv = positionUV;
#if UNITY_UV_STARTS_AT_TOP
            OUT.uv.y = 1.0 - OUT.uv.y;
#endif

            return OUT;
        }
        ENDHLSL

        Pass
        {
            Name "Fog"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            half4 frag(v2f IN) : SV_TARGET
            {
                half4 originalColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, IN.uv);
                uint2 pixel = (uint2)IN.positionHCS.xy;
                float2 uv = GetAtmospherePixelUV(pixel);
                float rawDepth = _CameraDepthTexture.Load(int3(pixel, 0));
                float rayLength = GetAtmosphereRayLength(rawDepth, GetViewVectorWS(uv));
                if(rayLength <= 0.0f) return originalColor;

                uint layer = GetScatterLayer(rayLength, GetScatterStep());
                ScatterData scatter = _RemainderScatter[GetRemainderIndex(pixel)];

                if(layer > 0u) {
                    Influences2D blend = GetLookupBlend(uv);
                    [unroll]for(uint i = 0u; i < 4u; i++) {
                        if(blend.corner[i] == 0.0f) continue;

                        uint2 sampleCoord = blend.origin + uint2(i & 1u, i >> 1);
                        ScatterData cached = _OpticalInfo[GetTextureIndex(sampleCoord, layer)];
                        scatter.inScatteredLight += cached.inScatteredLight * blend.corner[i];
                        scatter.extinction += cached.extinction * blend.corner[i];
                    }
                }
                
                float3 backgroundTransmittance = exp(-scatter.extinction * _GroundExtinctionMultiplier);
                return half4(scatter.inScatteredLight + originalColor.xyz * backgroundTransmittance, 0);
            }
            ENDHLSL
        }

    }
}
