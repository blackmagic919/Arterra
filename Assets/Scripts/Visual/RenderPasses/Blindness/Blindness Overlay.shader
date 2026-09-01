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
            uint vertexID : SV_VertexID;
        };

        struct Varyings
        {
            float4 positionHCS : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        TEXTURE2D_X(_BlitTexture);
        float4 _BlitTexture_TexelSize;

        float _Strength;
        float _DepthStart;
        float _DepthEnd;
        float _MaxBlurPixels;
        int _KernelRadius;

        #define MAX_KERNEL_RADIUS 6

        Varyings vert(Attributes input)
        {
            Varyings output;
            float2 positionUV = float2((input.vertexID << 1) & 2, input.vertexID & 2);
            output.positionHCS = float4(positionUV * 2.0 - 1.0, 0.0, 1.0);
            output.uv = positionUV;
#if UNITY_UV_STARTS_AT_TOP
            output.uv.y = 1.0 - output.uv.y;
#endif
            return output;
        }

        half4 frag(Varyings input) : SV_TARGET
        {
            float2 uv = input.uv;
            half4 center = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

            float rawDepth = SampleSceneDepth(uv);
            float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
            float depthFactor = saturate((eyeDepth - _DepthStart) / max(_DepthEnd - _DepthStart, 0.0001));

            float blurPixels = _MaxBlurPixels * saturate(_Strength) * depthFactor;
            if (blurPixels <= 0.05)
                return center;

            int kernelRadius = clamp(_KernelRadius, 1, MAX_KERNEL_RADIUS);
            float2 kernelStep = _BlitTexture_TexelSize.xy * (blurPixels / max((float)kernelRadius, 1.0));

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
                    sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + offset);
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
