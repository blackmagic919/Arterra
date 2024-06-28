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
        #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        #include "Assets/Resources/MapData/WSDensitySampler.hlsl"
        #include "Assets/Resources/Atmosphere/TextureInterpHelper.hlsl"

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
            float3 opticalDepth;
            float3 extinction;
        };

        struct OpticalInfo{
            float opticalDensity;
            float occlusionFactor;
            float3 scatterCoeffs;
            float3 extinctionCoeff;
            float3 opticalDepth;
        };

        StructuredBuffer<float3> _LuminanceLookup;
        StructuredBuffer<OpticalInfo> _OpticalInfo;

        float4 _MainTex_TexelSize;
        float4 _MainTex_ST;
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

            OpticalInfo sampleOpticalInfo(Influences2D sampleIndex, uint sampleDepth){
                OpticalInfo info = (OpticalInfo)0;
                [unroll]for(int i = 0; i < 4; i++){
                    uint index = GetTextureIndex(sampleIndex.corner[i].mapCoord, sampleDepth);
                    info.opticalDensity += _OpticalInfo[index].opticalDensity * sampleIndex.corner[i].influence;
                    info.scatterCoeffs += _OpticalInfo[index].scatterCoeffs * sampleIndex.corner[i].influence;
                    info.extinctionCoeff += _OpticalInfo[index].extinctionCoeff * sampleIndex.corner[i].influence;
                    info.opticalDepth += _OpticalInfo[index].opticalDepth * sampleIndex.corner[i].influence;
                    info.occlusionFactor += _OpticalInfo[index].occlusionFactor * sampleIndex.corner[i].influence;
                }
                return info;
            }
            


            ScatterData CalculateScatterData(float3 rayOrigin, float3 rayDir, float rayLength, 
                                            float sampleDist, Influences2D rayInfluences){
                int NumInScatterPoints = max(1, ceil(rayLength / sampleDist));
                ScatterData scatterData = (ScatterData)0;
                float3 inScatterPoint = rayOrigin;

                for(int depth = 0; depth < NumInScatterPoints; depth++){
                    float stepSize = min(rayLength - sampleDist*depth, sampleDist);
                    OpticalInfo opticalInfo = sampleOpticalInfo(rayInfluences, depth); 

                    float3 transmittance = exp((-(opticalInfo.opticalDepth + scatterData.opticalDepth))); // exp(-t(PPc, lambda)-t(PPa, lambda))
                    scatterData.inScatteredLight += opticalInfo.scatterCoeffs * opticalInfo.opticalDensity * transmittance * opticalInfo.occlusionFactor * stepSize;
                    scatterData.opticalDepth += opticalInfo.scatterCoeffs * opticalInfo.opticalDensity * stepSize;
                    scatterData.extinction += opticalInfo.extinctionCoeff * opticalInfo.opticalDensity * stepSize;
                    inScatterPoint += rayDir * stepSize;
                }
                return scatterData;
            }

            half4 frag(v2f IN) : SV_TARGET
            {
                float2 res = _MainTex_TexelSize.xy;

                half4 originalColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                float screenDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, IN.uv);
                float linearDepth = LinearEyeDepth(screenDepth, _ZBufferParams) * length(IN.viewVector);

                float3 rayOrigin = _WorldSpaceCameraPos;
                float3 rayDir = normalize(IN.viewVector);
                float3 emissionColor = _MainLightColor.xyz;

                float2 hitInfo = raySphere(_WorldSpaceCameraPos, _AtmosphereRadius, rayOrigin, rayDir);
                float dstToAtmosphere = hitInfo.x;
                float dstThroughAtmosphere = hitInfo.y;
                dstThroughAtmosphere = min(dstThroughAtmosphere, linearDepth-dstToAtmosphere);

                Influences2D rayInfluences = GetLookupBlend(IN.uv);
                float sampleDist = _AtmosphereRadius / (_NumInScatterPoints - 1);

                if(dstThroughAtmosphere > 0){
                    float3 pointInAtmosphere = rayOrigin + rayDir * dstToAtmosphere;//Get first point in atmosphere
                    ScatterData atmosphereData = CalculateScatterData(pointInAtmosphere, rayDir, dstThroughAtmosphere, sampleDist, rayInfluences);

                    return half4(atmosphereData.inScatteredLight * emissionColor + originalColor.xyz * exp(-atmosphereData.extinction), 0);
                }
                return originalColor;
            }
            ENDHLSL
        }
    }
}