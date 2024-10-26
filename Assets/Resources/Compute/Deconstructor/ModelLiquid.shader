Shader "Unlit/ModelLiquid"
{
    Properties
    {
    }
    SubShader
    {
        Tags {"RenderPipeline" = "UniversalPipeline"  "Queue" = "Transparent" "RenderType"="Transparent" }
        ZWrite On 
		Blend SrcAlpha OneMinusSrcAlpha
        
        Pass
        {
            Name "LiquidLit"
            Tags {"LightMode" = "UniversalForward"}
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define _SPECULAR_COLOR
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ INDIRECT 
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct v2f
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                nointerpolation int material: TEXCOORD3;
            };


            struct liquidMat{
                float3 WaterShallowCol;
                float3 WaterDeepCol;
                float WaterColFalloff;
                float DepthOpacity;
                float Smoothness;
                float WaveBlend;
                float WaveStrength;
                float2 WaveScale;
                float2 WaveSpeed;
            };

            StructuredBuffer<liquidMat> _MatLiquidData;


            TEXTURE2D(_LiquidFineWave);
            SAMPLER(sampler_LiquidFineWave);
            TEXTURE2D(_LiquidCoarseWave);
            SAMPLER(sampler_LiquidCoarseWave);
            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            float4x4 _LocalToWorld;

            struct DrawVertex{
                float3 positionOS;
                int2 material;
            };

            struct vInfo{
                uint axis[3];
            };

            StructuredBuffer<DrawVertex> Vertices;
            StructuredBuffer<vInfo> Triangles;
            uint triAddress;
            uint vertAddress;

            float3 CalculateNormalOS(int triIndex){
                float3 vertA = Vertices[vertAddress + Triangles[triIndex].axis[0]].positionOS;
                float3 vertB = Vertices[vertAddress + Triangles[triIndex].axis[1]].positionOS;
                float3 vertC = Vertices[vertAddress + Triangles[triIndex].axis[2]].positionOS;
                return cross(vertB - vertA, vertC - vertB);
            }

            v2f vert (uint vertexID: SV_VertexID){
                v2f o = (v2f)0;

                uint vertInd = Triangles[triAddress + (vertexID/3)].axis[vertexID%3];
                DrawVertex input = Vertices[vertAddress + vertInd];

                o.positionWS = mul(_LocalToWorld, float4(input.positionOS, 1)).xyz;
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.material = input.material.y;

                float3 normalOS = o.normalWS = CalculateNormalOS(triAddress + (vertexID/3));
                o.normalWS = normalize(mul(_LocalToWorld, float4(CalculateNormalOS(triAddress + (vertexID/3)), 0)).xyz);
                return o;
            }

            float3 triplanar(Texture2D waveText, SamplerState waveSampler, float3 worldPos, float scale, float3 normal, float2 offset = float2(0, 0)){
                float3 blendAxes = abs(normal);
                blendAxes /= dot(blendAxes, 1.0);
    
                float2 uvX = worldPos.zy * scale + offset;
                float2 uvY = worldPos.xz * scale + offset;
                float2 uvZ = worldPos.xy * scale + offset;
                
                float3 xProjection = UnpackNormal(SAMPLE_TEXTURE2D(waveText, waveSampler, uvX)) * blendAxes.x;
                float3 yProjection = UnpackNormal(SAMPLE_TEXTURE2D(waveText, waveSampler, uvY)) * blendAxes.y;
                float3 zProjection = UnpackNormal(SAMPLE_TEXTURE2D(waveText, waveSampler, uvZ)) * blendAxes.z;
    
                return xProjection + yProjection + zProjection;
            }
    
    
            float3 blend_rnm(float3 n1, float3 n2)
            {
                n1.z += 1;
                n2.xy = -n2.xy;
    
                return (n1 * dot(n1, n2) / n1.z - n2);
            }
    
            half4 frag (v2f IN) : SV_Target
            {
                float2 UV = IN.screenPos.xy / IN.screenPos.w;
                float3 viewVector = mul(unity_CameraInvProjection, float4(UV.xy * 2 - 1, 0, -1)).xyz;
                viewVector = mul(unity_CameraToWorld, float4(viewVector,0)).xyz;
                //https://forum.unity.com/threads/what-does-the-function-computescreenpos-in-unitycg-cginc-do.294470/
                //SSUV and ViewVector need to be parallel to screen--so have to be done in pixel shader
    
                liquidMat matData = _MatLiquidData[IN.material];
    
                float screenDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, UV);
                float linearDepth = LinearEyeDepth(screenDepth, _ZBufferParams) * length(viewVector);
                float dstToWater = IN.positionCS.w;
    
                float3 viewDir = normalize(viewVector);
                float2 waveOffsetFine = float2(_Time.x * matData.WaveSpeed.x, _Time.x * matData.WaveSpeed.x * 0.75);
                float2 waveOffsetCoarse = float2(_Time.x * matData.WaveSpeed.y * -0.5, _Time.x * matData.WaveSpeed.y * -0.25);
    
                float3 waveNormalFine = triplanar(_LiquidFineWave, sampler_LiquidFineWave, IN.positionWS, matData.WaveScale.x, IN.normalWS, waveOffsetFine);
                float3 waveNormalCoarse = triplanar(_LiquidCoarseWave, sampler_LiquidCoarseWave, IN.positionWS, matData.WaveScale.y, IN.normalWS, waveOffsetCoarse);
                float3 waveNormal = lerp(waveNormalCoarse, waveNormalFine, matData.WaveBlend);
                float3 specWaveNormal = normalize(lerp(IN.normalWS, blend_rnm(IN.normalWS, waveNormal), matData.WaveStrength));
    
                float waterDepth = linearDepth - dstToWater;
                
                float3 waterCol = lerp(matData.WaterShallowCol, matData.WaterDeepCol, 1 - exp(-waterDepth * matData.WaterColFalloff));
                float waterAlpha = 1 - exp(-waterDepth * matData.DepthOpacity);
    
                InputData lightingInput = (InputData)0;                                       
                lightingInput.positionWS = IN.positionWS;
                lightingInput.normalWS = normalize(specWaveNormal);
                lightingInput.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
    
                SurfaceData surfaceInput = (SurfaceData)0;
                surfaceInput.albedo = waterCol;
                surfaceInput.alpha = waterAlpha;
                surfaceInput.smoothness = matData.Smoothness;
                surfaceInput.specular = 1;
    
            #if UNITY_VERSION >= 202120
                return UniversalFragmentBlinnPhong(lightingInput, surfaceInput);
            #else
                return UniversalFragmentBlinnPhong(lightingInput, surfaceInput.albedo, float4(surfaceInput.specular, 1), surfaceInput.smoothness, 0, surfaceInput.alpha);
            #endif
            }
            ENDHLSL
        }
    }
}
