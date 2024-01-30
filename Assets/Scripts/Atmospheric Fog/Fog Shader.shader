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

        TEXTURE2D(m_ShadowRawDepth);
        SAMPLER(sampler_m_ShadowRawDepth);

        TEXTURE2D(_inScatterBaked);
        SAMPLER(sampler_inScatterBaked);


        float4 _MainTex_TexelSize;
        float4 _MainTex_ST;

        float3 _ScatteringCoeffs; //In order RGB
        float3 _PlanetCenter;
        float3 _LightDirection; 
        float _PlanetRadius;
        float _AtmosphereRadius;
        float _DensityFalloff;
        float _GroundExtinction;
        float _SurfaceOffset;
        int _NumInScatterPoints;
        int _NumOpticalDepthPoints;


        v2f vert(Attributes IN)
        {
            v2f OUT;
            OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
            OUT.uv = IN.uv;

            //Z is forward
            float3 viewVector = mul(unity_CameraInvProjection, float4(IN.uv.xy * 2 - 1, 0, -1));
			OUT.viewVector = mul(unity_CameraToWorld, float4(viewVector, 0));

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

            float densityAtPoint(float3 samplePoint){
                float heightAboveSurface = length(samplePoint - _PlanetCenter) - _PlanetRadius;
                float height01 = heightAboveSurface / (_AtmosphereRadius - _PlanetRadius);
                float localDensity = exp(-height01 * _DensityFalloff) * (1-height01);
                return localDensity;
            }

            float opticalDepth(float3 rayOrigin, float3 rayDir, float rayLength){
                float3 densitySamplePoint = rayOrigin;
                float stepSize = rayLength / (_NumOpticalDepthPoints - 1);
                float opticalDepth = 0;

                for(int i = 0; i < _NumOpticalDepthPoints; i++){
                    float localDensity = densityAtPoint(densitySamplePoint);
                    opticalDepth += localDensity * stepSize;
                    densitySamplePoint += rayDir * stepSize;
                }

                return opticalDepth;
            }
            
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

            half3 calculateInScatterLight(float3 rayOrigin, float3 rayDir, float rayLength){
                float3 inScatterPoint = rayOrigin;
                float stepSize = rayLength / (_NumInScatterPoints - 1);
                float3 inScatteredLight = 0;

                for(int i = 0; i < _NumInScatterPoints; i++){
                    float occlusionFactor = calculateOcclusionFactor(inScatterPoint, rayDir, stepSize); //MainLightRealtimeShadow(TransformWorldToShadowCoord(inScatterPoint));
                    float sunRayLength = raySphere(_PlanetCenter, _AtmosphereRadius, inScatterPoint, _LightDirection).y;    
                    float sunOpticalDepth = opticalDepth(inScatterPoint, _LightDirection, sunRayLength);// Represented by PPc in paper 
                    float cameraOpticalDepth = opticalDepth(inScatterPoint, -rayDir, stepSize * i);// Represented by PPa in paper
                    float3 transmittance = exp((-(sunOpticalDepth + cameraOpticalDepth)) * _ScatteringCoeffs); // exp(-t(PPc, lambda)-t(PPa, lambda))
                    float pointDensity = densityAtPoint(inScatterPoint);

                    inScatteredLight += pointDensity * transmittance * occlusionFactor * stepSize; //implement trapezoid-rule later
                    inScatterPoint += rayDir * stepSize;
                }
                inScatteredLight *= _ScatteringCoeffs;

                return inScatteredLight;
            }

            half4 frag(v2f IN) : SV_TARGET
            {
                float2 res = _MainTex_TexelSize.xy;

                half4 originalColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                float screenDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, IN.uv);
                float linearDepth = LinearEyeDepth(screenDepth, _ZBufferParams) * length(IN.viewVector);

                float3 rayOrigin = _WorldSpaceCameraPos;
                float3 rayDir = normalize(IN.viewVector);
                //
                float3 emissionColor = _MainLightColor;

                _PlanetCenter = float3(rayOrigin.x, -(_PlanetRadius + _SurfaceOffset), rayOrigin.z);

                float2 hitInfo = raySphere(_PlanetCenter, _AtmosphereRadius, rayOrigin, rayDir);
                float dstToAtmosphere = hitInfo.x;
                float dstThroughAtmosphere = hitInfo.y;
                dstThroughAtmosphere = min(dstThroughAtmosphere, linearDepth-dstToAtmosphere);

                if(dstThroughAtmosphere > 0){
                    float3 pointInAtmosphere = rayOrigin + rayDir * dstToAtmosphere;//Get first point in atmosphere
                    half3 inScatteredLight = calculateInScatterLight(pointInAtmosphere, rayDir, dstThroughAtmosphere) * emissionColor;
                    //half3 inScatteredLight = SAMPLE_TEXTURE2D(_inScatterBaked, sampler_inScatterBaked, IN.uv);

                    return half4(inScatteredLight + originalColor * exp(-opticalDepth(rayOrigin, rayDir, dstThroughAtmosphere) * _GroundExtinction), 0);
                }
                return originalColor;
            }
            ENDHLSL
        }
    }
}