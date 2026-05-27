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
        #pragma vertex vert
        #pragma fragment frag

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        #include "Assets/Resources/Compute/MapData/WSDensitySampler.hlsl"
        #include "Assets/Resources/Compute/Atmosphere/TextureInterpHelper.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct v2f
        {
            float4 positionHCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 viewVector : TEXCOORD1;
        };

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
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
            OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
            OUT.uv = IN.uv;

            //Z is forward
            float3 viewVector = mul(unity_CameraInvProjection, float4(IN.uv.xy * 2 - 1, 0, -1)).xyz;
			OUT.viewVector = mul(unity_CameraToWorld, float4(viewVector, 0)).xyz;

            return OUT;
        }
        ENDHLSL

        Pass
        {
            Name "Fog"

            HLSLPROGRAM
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

            ScatterData SampleScatterTree(float rayLength, float sampleDist, uint2 tCoord){
                uint depth = clamp(floor(rayLength / sampleDist) + 1, 1, _NumInScatterPoints - 1) 
                              + _NumInScatterPoints;
                //Get precision for last point
                ScatterData scatterData =  _OpticalInfo[GetTextureIndex(tCoord, depth)];
                scatterData.inScatteredLight *= fmod(rayLength, sampleDist);
                scatterData.extinction *= fmod(rayLength, sampleDist);
                depth--; 

                while(depth > 0){ //Operator precedence is == then & 
                    if((depth & 0x1) == 0) {
                        ScatterData cScatter = _OpticalInfo[GetTextureIndex(tCoord, depth)];
                        scatterData.inScatteredLight += cScatter.inScatteredLight * sampleDist; //256
                        scatterData.extinction += cScatter.extinction * sampleDist; //256 
                        depth--;
                    };
                    depth >>= 1;
                }
                return scatterData;
            }

            ScatterData CalculateScatterData(float rayLength, float sampleDist, Influences2D blend){
                ScatterData scatterData = (ScatterData)0;
                [unroll]for(uint i = 0; i < 4; i++){
                    if(blend.corner[i] == 0) continue;
                    ScatterData cScatter = SampleScatterTree(rayLength, sampleDist, blend.origin + uint2(i & 1u, (i >> 1) & 1u));
                    scatterData.inScatteredLight += cScatter.inScatteredLight * blend.corner[i]; //4
                    scatterData.extinction += cScatter.extinction * blend.corner[i]; //4
                }
                return scatterData;
            }


            half4 frag(v2f IN) : SV_TARGET
            {
                half4 originalColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                float screenDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, IN.uv);
                float linearDepth = LinearEyeDepth(screenDepth, _ZBufferParams) * length(IN.viewVector);

                //Assume atmosphere originates at viewer
                float dstThroughAtmosphere = min(_AtmosphereRadius, linearDepth);
                Influences2D rayInfluences = GetLookupBlend(IN.uv);
                float sampleDist = _AtmosphereRadius / (_NumInScatterPoints - 1);

                if(dstThroughAtmosphere > 0){
                    ScatterData atmosphereData = CalculateScatterData(dstThroughAtmosphere, sampleDist, rayInfluences);
                    return half4(atmosphereData.inScatteredLight + originalColor.xyz * exp(-atmosphereData.extinction), 0);
                }
                return originalColor;
            }
            ENDHLSL
        }
    }
}