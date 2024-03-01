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
        
        HLSLINCLUDE
        #pragma vertex vert
        #pragma fragment frag
        #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        #include "Assets/Scripts/TerrainGeneration/DensityManager/WSDensitySampler.hlsl"
        #include "Assets/Scripts/Atmospheric Fog/BakeData/TextureInterpHelper.hlsl"

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

        StructuredBuffer<float3> _LuminanceLookup;

        float4 _MainTex_TexelSize;
        float4 _MainTex_ST;

        float _AtmosphereRadius;
        float3 _LightDirection; 
    
        int _NumInScatterPoints;
        int _NumSunRayPoints;

        float _IsoLevel;

        struct ScatterData{
            float3 inScatteredLight;
            float3 opticalDepth;
            float extinction;
        };


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

            float3 sampleLuminance(float3 UVZ){
                float3 opticalDepth = 0;
                Influences influence = GetTextureInfluences(UVZ);
                [unroll]for(int i = 0; i < 8; i++){
                    uint index = GetTextureIndex(influence.corner[i].mapCoord);
                    opticalDepth += _LuminanceLookup[index] * influence.corner[i].influence;
                }
                return opticalDepth;
            }

            /*float opticalDepth(float3 rayOrigin, float3 rayDir, float rayLength){
                float3 densitySamplePoint = rayOrigin;
                float stepSize = rayLength / (_NumOpticalDepthPoints - 1);
                float opticalDepth = 0;
            
                for(int i = 0; i < _NumOpticalDepthPoints; i++){
                    float localDensity = densityAtPoint(densitySamplePoint) / _IsoLevel;
                    opticalDepth += localDensity * stepSize;
                    densitySamplePoint += rayDir * stepSize;
                }
            
                return opticalDepth;
            }*/
            
            float calculateOcclusionFactor(float3 rayOrigin, float3 rayDir, float rayLength){

                half cascadeIndex = ComputeCascadeIndex(rayOrigin);
                float stepSize = pow(2, cascadeIndex);
                int NumShadowPoints = max(1, rayLength / stepSize);

                float3 shadowPoint = rayOrigin;
                float transmittanceCount = 0;

                for(int i = 0; i < NumShadowPoints; i++){
                    transmittanceCount += MainLightRealtimeShadow(TransformWorldToShadowCoord(shadowPoint));
                    shadowPoint += rayDir * stepSize;
                }
                return (transmittanceCount / NumShadowPoints);
            }

            ScatterData CalculateScatterData(float3 rayOrigin, float3 rayDir, float rayLength, 
                                            float sampleDist, Influences2D rayInfluences, int sampleStride){
                int NumInScatterPoints = max(1, rayLength / sampleDist);

                float3 inScatterPoint = rayOrigin;
                ScatterData scatterData = (ScatterData)0;

                for(int i = 0; i < NumInScatterPoints; i++){
                    float stepSize = min(rayLength - length(inScatterPoint - rayOrigin), sampleDist);
                    float occlusionFactor = calculateOcclusionFactor(inScatterPoint, rayDir, stepSize);
                    
                }
            }

            ScatterData calculateInScatterLight(float3 rayOrigin, float3 rayDir, float rayLength, float2 CSuv){
                float stepSize = rayLength / (_NumInScatterPoints - 1);

                float3 inScatterPoint = rayOrigin;
                ScatterData scatterData = (ScatterData)0;

                for(int i = 0; i < _NumInScatterPoints; i++){
                    float occlusionFactor = calculateOcclusionFactor(inScatterPoint, rayDir, stepSize); //Gives more detail to shadows

                    float rayDepth = length(inScatterPoint - rayOrigin) / _AtmosphereRadius; 
                    float3 sunOpticalDepth = sampleLuminance(float3(CSuv, rayDepth));// Represented by PPc in paper 

                    SurMapData mapData = SampleMapData(inScatterPoint);
                    float pointDensity = GetDensity(mapData) / _IsoLevel;
                    float3 ScatterCoeffs = GetScatterCoeffs(mapData);
                    float extinctionCoeff = GetExtinction(mapData);

                    float3 transmittance = exp((-(sunOpticalDepth + scatterData.opticalDepth))); // exp(-t(PPc, lambda)-t(PPa, lambda))

                    scatterData.inScatteredLight += pointDensity * transmittance * occlusionFactor * stepSize * ScatterCoeffs; //implement trapezoid-rule later
                    scatterData.opticalDepth += ScatterCoeffs * pointDensity * stepSize;
                    scatterData.extinction += extinctionCoeff * pointDensity * stepSize;
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

                if(dstThroughAtmosphere > 0){
                    float3 pointInAtmosphere = rayOrigin + rayDir * dstToAtmosphere;//Get first point in atmosphere
                    ScatterData atmosphereData = calculateInScatterLight(pointInAtmosphere, rayDir, dstThroughAtmosphere, IN.uv);

                    return half4(atmosphereData.inScatteredLight * emissionColor + originalColor.xyz * exp(-atmosphereData.opticalDepth * atmosphereData.extinction), 0);
                }
                return originalColor;
            }
            ENDHLSL
        }
    }
}