Shader "Unlit/ColorDisplay"
{
    Properties
    {

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

            #include "Assets/Resources/Compute/MapData/WSLightSampler.hlsl"
            #include "Assets/Resources/Compute/Utility/LambertShade.hlsl"

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
                return o;
            }

            float4 frag (v2f IN) : SV_Target
            {
                float3 albedo = IN.color;

                uint light = SampleLight(IN.positionWS);
                float shadow = 1.0 - (light >> 30 & 0x3) / 3.0f;
                float3 DynamicLight = LambertShade(albedo.rgb, IN.normalWS, shadow);
                float3 ObjectLight = float3(light & 0x3FF, (light >> 10) & 0x3FF, (light >> 20) & 0x3FF) / 1023.0f;
                ObjectLight = mad((1 - ObjectLight), unity_AmbientGround, ObjectLight * 2.5f); //linear interpolation
                ObjectLight *= albedo.rgb;

                return float4(max(DynamicLight, ObjectLight), 1);
            }
            ENDHLSL
        }
    }
}
