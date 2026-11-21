Shader "Unlit/EntityColor"
{
    Properties {
        _RotNoise ("Rot Overlay Region", 2D) = "gray" {}
        _RotEdgeColor ("Rot Edge Color", Color) = (0.25, 0.35, 0.08, 1)
        _FleshTex ("Flesh Texture", 2D) = "white" {}
        _FleshColor ("Flesh Color", Color) = (1, 1, 1, 1)
        _RotScale ("Rot Texture Scale", Float) = 0.5
        _RotEdgeWidth ("Rot Edge Width", Range(0.001, 0.25)) = 0.05
        RotAmount ("Rot Amount", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags {"LightMode" = "UniversalForward"}

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ NO_EDITORLIGHTING
            #pragma multi_compile _ ATMOSPHERE_DEFERRED
            
            #include "Assets/Resources/Compute/Utility/LambertShade.hlsl"
            TEXTURE2D(_FleshTex); SAMPLER(sampler_FleshTex);
            TEXTURE2D(_RotNoise); SAMPLER(sampler_RotNoise);

            CBUFFER_START(UnityPerMaterial)
                float4 _RotEdgeColor;
                float4 _FleshColor;
                float _RotScale;
                float _RotEdgeWidth;
                float RotAmount;
            CBUFFER_END

            struct appdata
            {
                float3 vertex : POSITION;
                float3 normal : NORMAL;
                float3 color : COLOR0;
            };

            struct v2f
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 color : TEXCOORD3;
                float3 positionOS : TEXCOORD4;
                float3 normalOS : TEXCOORD5;
            };

            v2f vert (appdata v)
            {
                v2f o;

                VertexPositionInputs posInputs = GetVertexPositionInputs(v.vertex.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(v.normal.xyz);

                o.positionCS = posInputs.positionCS;
                o.positionWS = posInputs.positionWS;
                o.normalWS = normInputs.normalWS;
                o.color = v.color;
                o.positionOS = v.vertex;
                o.normalOS = v.normal;
                return o;
            }

            float3 Triplanar(TEXTURE2D_PARAM(textureMap, textureSampler), float3 positionOS, float3 weights) {
                float3 position = positionOS / max(_RotScale, 0.001);
                return SAMPLE_TEXTURE2D(textureMap, textureSampler, position.yz).rgb * weights.x
                    + SAMPLE_TEXTURE2D(textureMap, textureSampler, position.xz).rgb * weights.y
                    + SAMPLE_TEXTURE2D(textureMap, textureSampler, position.xy).rgb * weights.z;
            }

            float4 frag (v2f IN) : SV_Target
            {
                float3 albedo = IN.color;
                if (RotAmount > 0) {
                    float3 weights = abs(IN.normalOS);
                    weights /= max(weights.x + weights.y + weights.z, 0.0001);
                    float noise = Triplanar(TEXTURE2D_ARGS(_RotNoise, sampler_RotNoise), IN.positionOS, weights).r;
                    float width = clamp(_RotEdgeWidth, 0.001, 0.25);
                    float boundary = lerp(width, 1 - width, saturate(noise));
                    float edge = smoothstep(boundary - width, boundary, saturate(RotAmount));
                    float exposed = smoothstep(boundary, boundary + width, saturate(RotAmount));
                    float3 flesh = Triplanar(TEXTURE2D_ARGS(_FleshTex, sampler_FleshTex), IN.positionOS, weights) * _FleshColor.rgb;
                    albedo = lerp(lerp(albedo, _RotEdgeColor.rgb, edge), flesh, exposed);
                }

                return LambertShade(albedo, IN.normalWS, IN.positionWS);
            }
            ENDHLSL
        }
    }
}
