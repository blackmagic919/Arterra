Shader "Unlit/Snow"
{
    Properties
    {
        _OffsetTexture("Detail Noise Texture", 2D) = "white" {}
        _OffsetHeight("Offset Height", Range(0,1)) = 0.5

        _SnowNormal("Snow Normal", 2D) = "white" {}
        _SnowColor("Snow Color", Color) = (1, 1, 1, 1)
        _TexOpacity("Texture Opacity", Range(0, 1)) = 1
        
        _SparkleTexture("Sparkle Texture", 2D) = "white" {}
        _SparkleCutoff("Sparkle Cutoff", float) = 0.5
        _NormalStrength("Normal Strength", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        Cull[_Cull]

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma skip_variants SHADOWS_*

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Resources/Compute/GeoShader/VertexPacker.hlsl"
            #include "Assets/Resources/Compute/MapData/WSLightSampler.hlsl"
            #include "Assets/Resources/Compute/Utility/LambertShade.hlsl"
            
            struct DrawTriangle{
                uint3 vertex[3];
            };
            
            
            StructuredBuffer<DrawTriangle> _StorageMemory;
            StructuredBuffer<uint2> _AddressDict;
            float4x4 _LocalToWorld;
            uint addressIndex;

            TEXTURE2D(_OffsetTexture); SAMPLER(sampler_OffsetTexture); float4 _OffsetTexture_ST;
            TEXTURE2D(_SparkleTexture); SAMPLER(sampler_SparkleTexture); float4 _SparkleTexture_ST;
            TEXTURE2D(_SnowNormal); SAMPLER(sampler_SnowNormal); float4 _SnowNormal_ST;
            float _NormalStrength;  float _OffsetHeight; float4 _SnowColor;
            float _TexOpacity; float _SparkleCutoff;

            struct VertexOutput
            {
                float3 positionWS : TEXCOORD0;
                float3 normalWS     : TEXCOORD1; // Normal vector in world space
                float4 positionCS   : SV_POSITION;
            };

            float2 mapCoordinates(float3 worldPos)
            {
                float2 projXY = worldPos.xy;
                float2 projXZ = worldPos.xz;
                float2 projYZ = worldPos.yz;

                float2 worldUV = (projXY + projXZ + projYZ) / 3;

                return worldUV;
            }

            float3 triplanar(Texture2D text, SamplerState sample, float4 text_ST, float3 worldPos, float3 blendAxes){
                
                float3 xProjection = SAMPLE_TEXTURE2D(text, sample, TRANSFORM_TEX(worldPos.yz, text)).xyz * blendAxes.x;
                float3 yProjection = SAMPLE_TEXTURE2D(text, sample, TRANSFORM_TEX(worldPos.xz, text)).xyz * blendAxes.y;
                float3 zProjection = SAMPLE_TEXTURE2D(text, sample, TRANSFORM_TEX(worldPos.xy, text)).xyz * blendAxes.z;
            
                return xProjection + yProjection + zProjection;
            }

            float3 triplanarNorm(Texture2D text, SamplerState sample, float4 text_ST, float3 worldPos, float3 blendAxes){
                    float3 xProjection = UnpackNormal(SAMPLE_TEXTURE2D(text, sample, TRANSFORM_TEX(worldPos.yz, text))) * blendAxes.x;
                    float3 yProjection = UnpackNormal(SAMPLE_TEXTURE2D(text, sample, TRANSFORM_TEX(worldPos.xz, text))) * blendAxes.y;
                    float3 zProjection = UnpackNormal(SAMPLE_TEXTURE2D(text, sample, TRANSFORM_TEX(worldPos.xy, text))) * blendAxes.z;
                
                    return normalize(xProjection + yProjection + zProjection);
            }
            
            float3 blend_rnm(float3 n1, float3 n2)
            {
                n1.z += 1;
                n2.xy = -n2.xy;

                return (n1 * dot(n1, n2) / n1.z - n2);
            }
            

            VertexOutput vert (uint vertexID: SV_VertexID)
            {
                VertexOutput output = (VertexOutput)0;
                if(_AddressDict[addressIndex].x == 0)
                    return output;

                uint triAddress = vertexID / 3 + _AddressDict[addressIndex].y;
                uint vertexIndex = vertexID % 3;
                uint3 input = _StorageMemory[triAddress].vertex[vertexIndex];  
                VertexInfo v = UnpackVertex(input);
                float snowHeight = (uint)((input.z >> 24) & 0xFF) / 255.0f;
                snowHeight *= _OffsetHeight;
                
                float3 positionWS = mul(_LocalToWorld, float4(v.positionOS, 1)).xyz;
                float3 normalWS = normalize(mul(_LocalToWorld, float4(v.normalOS, 0)).xyz);
                
                float2 snowUV = TRANSFORM_TEX(mapCoordinates(positionWS), _OffsetTexture);
                float snowNoise = SAMPLE_TEXTURE2D_LOD(_OffsetTexture, sampler_OffsetTexture, snowUV, 0).r * 2 - 1;
                snowHeight += snowNoise * snowHeight;
                
                output.positionWS = positionWS + normalWS * snowHeight;
                output.normalWS = normalWS;

                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            float3 frag (VertexOutput IN) : SV_Target
            {
                float3 blendAxes = abs(IN.normalWS); blendAxes /= dot(blendAxes, 1);
                float3 albedo = triplanar(_OffsetTexture, sampler_OffsetTexture, _OffsetTexture_ST, IN.positionWS, blendAxes);
                albedo = lerp(_SnowColor.rgb, _SnowColor.rgb * albedo, _TexOpacity);

                float3 sparkle = triplanar(_SparkleTexture, sampler_SparkleTexture, _SparkleTexture_ST, IN.positionWS, blendAxes);
                albedo += step(sparkle, _SparkleCutoff);

                float3 normal = triplanarNorm(_SnowNormal, sampler_SnowNormal, _SnowNormal_ST, IN.positionWS, blendAxes);
                normal = lerp(IN.normalWS, normal, _NormalStrength);

                uint light = SampleLight(IN.positionWS);//
                float shadow = (1.0 - (light >> 30 & 0x3) / 3.0f);
                float3 DynamicLight = LambertShade(albedo, NormalizeNormalPerPixel(normal), shadow);
                float3 ObjectLight = float3(light & 0x3FF, (light >> 10) & 0x3FF, (light >> 20) & 0x3FF) / 1023.0f;
                ObjectLight = mad((1 - ObjectLight), unity_AmbientEquator, ObjectLight * 2.5f); //linear interpolation
                ObjectLight *= albedo;

                return max(DynamicLight, ObjectLight).rgb;
            }
            ENDHLSL
        }
    }
}