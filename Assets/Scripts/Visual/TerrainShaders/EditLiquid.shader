Shader "Unlit/EditLiquid"
{
    Properties {
        _BackgroundColor("Background color", Color) = (0, 0.5, 0, 0.5)
        _Opacity("Opacity", float) = 0.5
    }   

    SubShader//
    {
        Tags {"RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay" "RenderType"="Opaque" }
        ZWrite On

        Pass
        {
            Name "ForwardLit"
            Tags {"LightMode" = "UniversalForward"}
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define _SPECULAR_COLOR
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ INDIRECT 
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Resources/Compute/MapData/WSLightSampler.hlsl"
            #include "Assets/Resources/Compute/Utility/LambertShade.hlsl"

            struct v2f
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                nointerpolation int material: TEXCOORD3;
            };


            struct liquidMat{
                float4 WaterShallowCol;
                float4 WaterDeepCol;
                float WaterColFalloff;
                float WaveBlend;
                float WaveStrength;
                float2 WaveScale;
                float2 WaveSpeed;
            };

            StructuredBuffer<liquidMat> _MatLiquidData;

            float4 _BackgroundColor;
            float _Opacity;
            TEXTURE2D(_LiquidFineWave);
            SAMPLER(sampler_LiquidFineWave);
            TEXTURE2D(_LiquidCoarseWave);
            SAMPLER(sampler_LiquidCoarseWave);
            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            float4x4 _LocalToWorld;

            struct DrawVertex{
                float3 positionOS;
                float3 normalOS;
                int2 material;
            };

            struct vInfo{
                uint axis[3];
            };

            StructuredBuffer<DrawVertex> Vertices;
            StructuredBuffer<vInfo> Triangles;
            StructuredBuffer<uint2> _AddressDict;
            uint triAddress;
            uint vertAddress;


            v2f vert (uint vertexID: SV_VertexID){
                v2f o = (v2f)0;

                uint vertInd = Triangles[_AddressDict[triAddress].y + (vertexID/3)].axis[vertexID%3];
                DrawVertex input = Vertices[vertInd + _AddressDict[vertAddress].y];

                o.positionWS = mul(_LocalToWorld, float4(input.positionOS, 1)).xyz;
                o.normalWS = normalize(mul(_LocalToWorld, float4(input.normalOS, 0)).xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.material = input.material.y; //y is liquid
                o.screenPos = ComputeScreenPos(o.positionCS);

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


                float2 waveOffsetFine = float2(_Time.x * matData.WaveSpeed.x, _Time.x * matData.WaveSpeed.x * 0.75);
                float2 waveOffsetCoarse = float2(_Time.x * matData.WaveSpeed.y * -0.5, _Time.x * matData.WaveSpeed.y * -0.25);

                float3 waveNormalFine = triplanar(_LiquidFineWave, sampler_LiquidFineWave, IN.positionWS, matData.WaveScale.x, IN.normalWS, waveOffsetFine);
                float3 waveNormalCoarse = triplanar(_LiquidCoarseWave, sampler_LiquidCoarseWave, IN.positionWS, matData.WaveScale.y, IN.normalWS, waveOffsetCoarse);
                float3 waveNormal = lerp(waveNormalCoarse, waveNormalFine, matData.WaveBlend);
                waveNormal = normalize(blend_rnm(IN.normalWS, waveNormal));

                UV += waveNormal * matData.WaveStrength;
                float screenDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, UV);
                float linearDepth = LinearEyeDepth(screenDepth, _ZBufferParams) * length(viewVector);
                float dstToWater = IN.positionCS.w;

                float waterDepth = max(linearDepth - dstToWater, 0);
                float4 waterCol = lerp(matData.WaterShallowCol, matData.WaterDeepCol, 1 - exp(-waterDepth * matData.WaterColFalloff));

                uint light = SampleLight(IN.positionWS);
                float shadow = 1.0 - (light >> 30 & 0x3) / 3.0f;
                float3 DynamicLight = LambertShade(waterCol, IN.normalWS, shadow);
                float3 ObjectLight = float3(light & 0x3FF, (light >> 10) & 0x3FF, (light >> 20) & 0x3FF) / 1023.0f;
                ObjectLight = mad((1 - ObjectLight), unity_AmbientGround, ObjectLight * 2.5f); //linear interpolation
                ObjectLight *= waterCol.rgb;

                float3 albedo = lerp(max(DynamicLight, ObjectLight), _BackgroundColor.rgb, _BackgroundColor.a);
                return float4(albedo, waterCol.a * _Opacity);
            }
            ENDHLSL
        }
    }
}