Shader "Custom/Terrain"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _TextureScale("Scale", Float) = 1
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0 
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        // Physically based Standard lighting model, and enable shadows on all light types
        #pragma surface surf Standard fullforwardshadows
        #pragma vertex vert Standard fullforwardshadows

        // Use shader model 3.0 target, to get nicer looking lighting
        #pragma target 3.0

        const static int maxMatCount = 8;

        int baseColorCount;
        float4 baseColors[maxMatCount];
        float baseColorStrength[maxMatCount];
        float baseTextureScales[maxMatCount];

        UNITY_DECLARE_TEX2DARRAY(baseTextures);

        struct Input
        {
            float2 uv_MainTex;
            float4 color;
            float3 worldPos;
            float3 worldNormal;
        };

        sampler2D _MainTex;
        float _TextureScale;
        half _Glossiness;
        half _Metallic;
        fixed4 _Color;


        // Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
        // See https://docs.unity3d.com/Manual/GPUInstancing.html for more information about instancing.
        // #pragma instancing_options assumeuniformscaling
        UNITY_INSTANCING_BUFFER_START(Props)
            // put more per-instance properties here
        UNITY_INSTANCING_BUFFER_END(Props)

        //called by engine
        void vert(inout appdata_full v, out Input o) {
			UNITY_INITIALIZE_OUTPUT(Input, o);
            o.color = v.color;
		}

        float4 lerp(float4 a, float4 b, float value){
            return (a*value) + (b*(1-value));
        }

        float3 lerp(float3 a, float3 b, float value){
            return (a*value) + (b*(1-value));
        }

        float lerp(float a, float b, float value){
            return (a*value) + (b*(1-value));
        }

        float3 triplanar(float3 worldPos, float scale, float3 blendAxes, int texInd){
            float3 scaledWorldPos = worldPos / scale;
            
            float3 xProjection = UNITY_SAMPLE_TEX2DARRAY(baseTextures, float3(scaledWorldPos.y, scaledWorldPos.z, texInd)) * blendAxes.x;
            float3 yProjection = UNITY_SAMPLE_TEX2DARRAY(baseTextures, float3(scaledWorldPos.x, scaledWorldPos.z, texInd)) * blendAxes.y; 
            float3 zProjection = UNITY_SAMPLE_TEX2DARRAY(baseTextures, float3(scaledWorldPos.x, scaledWorldPos.y, texInd)) * blendAxes.z;
            return xProjection + yProjection + zProjection;
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float3 blendAxes = abs(IN.worldNormal);
            blendAxes /= blendAxes.x + blendAxes.y + blendAxes.z;

            int a = (int)IN.color.r;
            int b = (int)IN.color.g;
            float interpFactor = IN.color.b/((float)255);

            float3 baseColor = lerp(baseColors[a], baseColors[b], interpFactor);
            
            float3 texA = triplanar(IN.worldPos, baseTextureScales[a], blendAxes, a);
            float3 texB = triplanar(IN.worldPos, baseTextureScales[b], blendAxes, b);

            float3 textureColor = lerp(texA, texB, interpFactor);
            
            float colorStrength = lerp(baseColorStrength[a], baseColorStrength[b], interpFactor);

            o.Albedo = baseColor*colorStrength + textureColor*(1-colorStrength);

           

        }
        ENDCG
    }
    FallBack "Diffuse"
} 
