Shader "Unlit/ModelTerrain"
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
            Name "TerrainLit"
            Tags{"LightMode" = "UniversalForward"}

            Cull Back

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

#if UNITY_VERSION >= 202120
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
#else
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
#endif
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #pragma multi_compile _ INDIRECT  //Try to use shader_feature--doesn't work with material instances, but less variants

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct matTerrain{
                float4 baseColor;
                float baseTextureScale;
                float baseColorStrength;
                int geoShaderInd;
            };

            StructuredBuffer<matTerrain> _MatTerrainData;

            Texture2DArray _Textures;
            SamplerState sampler_Textures;

            struct v2f
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                nointerpolation int material: TEXCOORD2; //Materials are definate and can't be interpolated
            };


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
                o.material = input.material.x;

                float3 normalOS = o.normalWS = CalculateNormalOS(triAddress + (vertexID/3));
                o.normalWS = normalize(mul(_LocalToWorld, float4(CalculateNormalOS(triAddress + (vertexID/3)), 0)).xyz);
                return o;
            }
            
            float3 triplanar(float3 worldPos, float scale, float3 blendAxes, int texInd){
                float3 scaledWorldPos = worldPos / scale;
                
                float3 xProjection = _Textures.Sample(sampler_Textures, float3(scaledWorldPos.y, scaledWorldPos.z, texInd)).xyz * blendAxes.x;
                float3 yProjection = _Textures.Sample(sampler_Textures, float3(scaledWorldPos.x, scaledWorldPos.z, texInd)).xyz * blendAxes.y;
                float3 zProjection = _Textures.Sample(sampler_Textures, float3(scaledWorldPos.x, scaledWorldPos.y, texInd)).xyz * blendAxes.z;

                return xProjection + yProjection + zProjection;
            }


            float3 frag (v2f IN) : SV_Target
            {
                float3 blendAxes = abs(IN.normalWS);
                blendAxes /= blendAxes.x + blendAxes.y + blendAxes.z;

                int material = IN.material;

                float3 baseColor = _MatTerrainData[material].baseColor.xyz;

                float3 textureColor = triplanar(IN.positionWS, _MatTerrainData[material].baseTextureScale, blendAxes, material);

                float colorStrength = _MatTerrainData[material].baseColorStrength;

                InputData lightingInput = (InputData)0;
                lightingInput.positionWS = IN.positionWS;
                lightingInput.normalWS = normalize(IN.normalWS);
                lightingInput.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                lightingInput.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);

                SurfaceData surfaceInput = (SurfaceData)0;
                surfaceInput.albedo = baseColor * colorStrength + textureColor * (1-colorStrength);

                return UniversalFragmentPBR(lightingInput, surfaceInput).rgb;
            }
            ENDHLSL
        }
    }
}
