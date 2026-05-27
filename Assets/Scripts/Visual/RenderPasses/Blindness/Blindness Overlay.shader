Shader "Hidden/BlindnessOverlay"
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
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

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
        float4 _MainTex_TexelSize;

        float _Strength;
        float _DepthStart;
        float _DepthEnd;
        float _MaxBlurPixels;
        int _KernelRadius;

        #define MAX_KERNEL_RADIUS 6

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
            half4 center = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

            float rawDepth = SampleSceneDepth(uv);
            float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
            float depthFactor = saturate((eyeDepth - _DepthStart) / max(_DepthEnd - _DepthStart, 0.0001));

            float blurPixels = _MaxBlurPixels * saturate(_Strength) * depthFactor;
            if (blurPixels <= 0.05)
                return center;

            int kernelRadius = clamp(_KernelRadius, 1, MAX_KERNEL_RADIUS);
            float2 kernelStep = _MainTex_TexelSize.xy * (blurPixels / max((float)kernelRadius, 1.0));

            half4 sum = 0;
            float sampleCount = 0;

            [loop]
            for (int y = -MAX_KERNEL_RADIUS; y <= MAX_KERNEL_RADIUS; y++)
            {
                [loop]
                for (int x = -MAX_KERNEL_RADIUS; x <= MAX_KERNEL_RADIUS; x++)
                {
                    if (abs(x) > kernelRadius || abs(y) > kernelRadius)
                        continue;

                    float2 offset = float2((float)x, (float)y) * kernelStep;
                    sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + offset);
                    sampleCount += 1.0;
                }
            }

            return sum / max(sampleCount, 1.0);
        }
        ENDHLSL

        Pass
        {
            Name "BlindnessOverlay"

            HLSLPROGRAM
            ENDHLSL
        }
    }
}
