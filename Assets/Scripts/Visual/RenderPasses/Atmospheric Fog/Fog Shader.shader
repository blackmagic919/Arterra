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

        #include "Assets/Resources/Compute/MapData/WSDensitySampler.hlsl"
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
            float3 viewVector : TEXCOORD1;
        };

        TEXTURE2D_X(_BlitTexture);
        TEXTURE2D(_CameraDepthTexture);
        SAMPLER(sampler_CameraDepthTexture);

        struct ScatterData{
            float3 inScatteredLight;
            float3 extinction;
        };

        StructuredBuffer<ScatterData> _OpticalInfo;
        float4 _MainTex_TexelSize;
        float _AtmosphereRadius;
    
        int _NumInScatterPoints;


        v2f vert(Attributes IN)
        {
            v2f OUT;
            float2 positionUV = float2((IN.vertexID << 1) & 2, IN.vertexID & 2);
            OUT.positionHCS = float4(positionUV * 2.0 - 1.0, 0.0, 1.0);
            OUT.uv = positionUV;
#if UNITY_UV_STARTS_AT_TOP
            OUT.uv.y = 1.0 - OUT.uv.y;
#endif

            //Z is forward

            float3 viewVector = mul(unity_CameraInvProjection, float4(OUT.uv.xy * 2 - 1, 0, -1)).xyz;
			OUT.viewVector = mul(unity_CameraToWorld, float4(viewVector, 0)).xyz;

            return OUT;
        }
        ENDHLSL

        Pass
        {
            Name "Fog"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float2 raySphere(float3 s0, float sr, float3 r0, float3 rd) {
                float a = dot(rd, rd);
                float3 s0_r0 = r0 - s0;
                float b = 2.0 * dot(rd, s0_r0);
                float c = dot(s0_r0, s0_r0) - (sr * sr);
	            float disc = b * b - 4.0 * a* c;
                    
                if (disc < 0.0) {
                    return float2(-1.0, -1.0);
                }else{
                    float t1 = max((-b - sqrt(disc)) / (2.0 * a), 0);
                    float t2 = max((-b + sqrt(disc)) / (2.0 * a), 0);
                    return float2(t1, t2-t1);
	            }
            }

            ScatterData SampleScatterVolume(float rayLength, float sampleDist, Influences2D blend){
                ScatterData scatterData = (ScatterData)0;
                float maxDepth = max((float)(_NumInScatterPoints - 1), 0.0);
                float depthf = clamp(rayLength / max(sampleDist, 1e-6), 0.0, maxDepth);
                float depthFloor = floor(depthf);
                uint z0 = (uint)depthFloor;
                uint z1 = min(z0 + 1u, (uint)maxDepth);
                float zBlend = depthf - depthFloor;

                [unroll]for(uint i = 0; i < 4; i++){
                    if(blend.corner[i] == 0) continue;

                    uint2 sampleCoord = blend.origin + uint2(i & 1u, (i >> 1) & 1u);
                    ScatterData s0 = _OpticalInfo[GetTextureIndex(sampleCoord, z0)];
                    ScatterData s1 = _OpticalInfo[GetTextureIndex(sampleCoord, z1)];

                    float3 blendedInScatter = lerp(s0.inScatteredLight, s1.inScatteredLight, zBlend);
                    float3 blendedExtinction = lerp(s0.extinction, s1.extinction, zBlend);
                    scatterData.inScatteredLight += blendedInScatter * blend.corner[i];
                    scatterData.extinction += blendedExtinction * blend.corner[i];
                }

                return scatterData;
            }


            half4 frag(v2f IN) : SV_TARGET
            {
                half4 originalColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, IN.uv);
                float screenDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, IN.uv);
                float linearDepth = LinearEyeDepth(screenDepth, _ZBufferParams) * length(IN.viewVector);

                //Assume atmosphere originates at viewer
                float dstThroughAtmosphere = min(_AtmosphereRadius, linearDepth);
                Influences2D rayInfluences = GetLookupBlend(IN.uv);
                float sampleDist = _AtmosphereRadius / max((float)(_NumInScatterPoints - 1), 1.0);

                if(dstThroughAtmosphere > 0){
                    ScatterData atmosphereData = SampleScatterVolume(dstThroughAtmosphere, sampleDist, rayInfluences);
                    return half4(atmosphereData.inScatteredLight + originalColor.xyz * exp(-atmosphereData.extinction), 0);
                }
                return originalColor;
            }
            ENDHLSL
        }

    }
}