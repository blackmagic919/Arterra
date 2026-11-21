Shader "Unlit/TextureDisplay"
{
    Properties
    {
        _MainTexture("Color", 2D) = "white" {} 
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
            TEXTURE2D(_MainTexture); SAMPLER(sampler_MainTexture); 

            struct appdata
            {
                float3 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD3;
            };

            v2f vert (appdata v)
            {
                v2f o;

                VertexPositionInputs posInputs = GetVertexPositionInputs(v.vertex.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(v.normal.xyz);

                o.positionCS = posInputs.positionCS;
                o.positionWS = posInputs.positionWS;
                o.normalWS = normInputs.normalWS;
                o.uv = v.uv;
                return o;
            }

            float4 frag (v2f IN) : SV_Target
            {
                float2 uv = IN.uv;
                float3 albedo = SAMPLE_TEXTURE2D(_MainTexture, sampler_MainTexture, uv).rgb;

                return LambertShade(albedo, IN.normalWS, IN.positionWS);
            }
            ENDHLSL
        }
    }
}
