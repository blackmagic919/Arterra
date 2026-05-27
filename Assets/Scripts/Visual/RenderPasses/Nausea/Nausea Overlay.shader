Shader "Hidden/NauseaOverlay"
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
        float4 _MainTex_TexelSize;

        float _Strength;
        float _NoiseScale;
        float _ScrollSpeed;
        float _EdgePadding;
        float _EdgeFeather;

        Varyings vert(Attributes input)
        {
            Varyings output;
            output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = input.uv;
            return output;
        }

        float2 hash2(float2 p)
        {
            p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
            return frac(sin(p) * 43758.5453);
        }

        float2 smoothNoise2(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            float2 u = f * f * (3.0 - 2.0 * f);

            float2 a = hash2(i + float2(0.0, 0.0));
            float2 b = hash2(i + float2(1.0, 0.0));
            float2 c = hash2(i + float2(0.0, 1.0));
            float2 d = hash2(i + float2(1.0, 1.0));

            float2 x1 = lerp(a, b, u.x);
            float2 x2 = lerp(c, d, u.x);
            return lerp(x1, x2, u.y) * 2.0 - 1.0;
        }

        float2 layeredNoise(float2 uv, float t)
        {
            float2 p0 = uv * _NoiseScale + float2(0.0, t * _ScrollSpeed);
            float2 p1 = uv * (_NoiseScale * 1.97) + float2(t * (_ScrollSpeed * 0.7), 0.0);
            float2 p2 = uv * (_NoiseScale * 3.6) - float2(t * (_ScrollSpeed * 0.45), t * (_ScrollSpeed * 0.28));

            float2 n = smoothNoise2(p0) * 0.55;
            n += smoothNoise2(p1) * 0.3;
            n += smoothNoise2(p2) * 0.15;
            return n;
        }

        half4 frag(Varyings input) : SV_TARGET
        {
            float2 uv = input.uv;
            float t = _Time.y;

            float2 distVector = layeredNoise(uv, t);

            float edgeDistance = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
            float edgeMask = smoothstep(_EdgePadding, _EdgePadding + max(_EdgeFeather, 0.0001), edgeDistance);

            float pixelSafety = max(_MainTex_TexelSize.x, _MainTex_TexelSize.y) * 2.0;
            float2 distortedUV = uv + distVector * (_Strength * 0.06 * edgeMask);
            distortedUV = clamp(distortedUV, pixelSafety.xx, 1.0 - pixelSafety.xx);

            half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, distortedUV);

            half vignette = smoothstep(0.2, 0.95, edgeDistance * 2.0);
            color.rgb *= lerp(1.02, 0.98, _Strength * (1.0 - vignette));

            return color;
        }
        ENDHLSL

        Pass
        {
            Name "NauseaOverlay"

            HLSLPROGRAM
            ENDHLSL
        }
    }
}
