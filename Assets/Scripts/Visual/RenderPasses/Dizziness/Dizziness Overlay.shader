Shader "Hidden/DizzinessOverlay"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        HLSLINCLUDE
        #pragma vertex vert
        #pragma fragment frag

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct Varyings
        {
            float4 positionHCS : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_History1Tex);
        SAMPLER(sampler_History1Tex);
        TEXTURE2D(_History2Tex);
        SAMPLER(sampler_History2Tex);

        float _Strength;
        float _HistoryWeight1;
        float _HistoryWeight2;

        Varyings vert(Attributes input)
        {
            Varyings output;
            output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = input.uv;
            return output;
        }

        half4 frag(Varyings input) : SV_TARGET
        {
            float2 uv = input.uv;
            half4 current = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
            half4 history1 = SAMPLE_TEXTURE2D(_History1Tex, sampler_History1Tex, uv);
            half4 history2 = SAMPLE_TEXTURE2D(_History2Tex, sampler_History2Tex, uv);

            float weight1 = saturate(_HistoryWeight1);
            float weight2 = saturate(_HistoryWeight2);
            float totalWeight = 1.0 + weight1 + weight2;

            half3 delayedBlend = (current.rgb + history1.rgb * weight1 + history2.rgb * weight2) / totalWeight;
            half overlayAmount = saturate(_Strength);
            half3 color = lerp(current.rgb, delayedBlend, overlayAmount);

            return half4(color, current.a);
        }
        ENDHLSL

        Pass
        {
            Name "DizzinessOverlay"

            HLSLPROGRAM
            ENDHLSL
        }
    }
}
