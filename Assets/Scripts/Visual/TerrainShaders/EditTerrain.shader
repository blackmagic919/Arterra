Shader "Unlit/EditTerrain"
{
    Properties
    {
        _AddColor("Edit Add Color", Color) = (0, 0.5, 0, 0.5)
        _RemoveColor("Edit Remove Color", Color) = (0.5, 0, 0, 0.5)
        _Opacity("Opacity", float) = 0.5
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags{"LightMode" = "UniversalForward"}

            Cull Back

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ INDIRECT  //Try to use shader_feature--doesn't work with material instances, but less variants

            #include "Assets/Resources/Compute/MapData/WSLightSampler.hlsl"
            #include "Assets/Resources/Compute/Utility/LambertShade.hlsl"

            struct matTerrain{
                int textureIndex;
                float baseTextureScale;
                uint flipStateRendering;
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
                float3 normalOS;
                int2 material;
            };

            struct vInfo{
                uint axis[3];
            };

            half4 _AddColor;
            half4 _RemoveColor;
            float _Opacity;

            inline uint Flags(uint meta) { return (meta >> 24) & 0xFF;}
            inline uint TMaterial(uint meta) { return meta & 0x00FFFFFF;}
            StructuredBuffer<DrawVertex> Vertices;
            StructuredBuffer<vInfo> Triangles;
            StructuredBuffer<uint2> _AddressDict;
            uint triAddress;
            uint vertAddress;

            uint GetFlags(vInfo tri){
                uint flag = 0;
                flag |= Flags(Vertices[tri.axis[0] + _AddressDict[vertAddress].y].material.x);
                flag |= Flags(Vertices[tri.axis[1] + _AddressDict[vertAddress].y].material.x);
                flag |= Flags(Vertices[tri.axis[2] + _AddressDict[vertAddress].y].material.x);
                return flag;
            }

            v2f vert (uint vertexID: SV_VertexID){
                v2f o = (v2f)0;

                vInfo tri = Triangles[_AddressDict[triAddress].y + (vertexID/3)];
                uint vertInd = tri.axis[vertexID%3];
                DrawVertex input = Vertices[vertInd + _AddressDict[vertAddress].y];

                o.positionWS = mul(_LocalToWorld, float4(input.positionOS, 1)).xyz;
                o.normalWS = normalize(mul(_LocalToWorld, float4(input.normalOS, 0)).xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.material = ((GetFlags(tri) & 0xFF) << 24) | TMaterial(input.material.x);
                return o;
            }


            float3 triplanar(float3 worldPos, float scale, float3 blendAxes, int texInd){
                float3 scaledWorldPos = worldPos / scale;
                
                float3 xProjection = _Textures.Sample(sampler_Textures, float3(scaledWorldPos.y, scaledWorldPos.z, texInd)).xyz * blendAxes.x;
                float3 yProjection = _Textures.Sample(sampler_Textures, float3(scaledWorldPos.x, scaledWorldPos.z, texInd)).xyz * blendAxes.y;
                float3 zProjection = _Textures.Sample(sampler_Textures, float3(scaledWorldPos.x, scaledWorldPos.y, texInd)).xyz * blendAxes.z;

                return xProjection + yProjection + zProjection;
            }


            float4 frag (v2f IN) : SV_Target
            {
                float3 blendAxes = abs(IN.normalWS);
                blendAxes /= blendAxes.x + blendAxes.y + blendAxes.z;

                int material = TMaterial(IN.material);
                uint flags = Flags(IN.material);
                if (flags == 0) discard;

                matTerrain tInfo =  _MatTerrainData[material];
                uint light = SampleLight(IN.positionWS);
                float shadow = (1.0 - (light >> 30 & 0x3) / 3.0f);
                float3 normalWS = normalize(IN.normalWS);
                float3 albedo = triplanar(IN.positionWS, tInfo.baseTextureScale, blendAxes, tInfo.textureIndex);

                float3 DynamicLight = LambertShade(albedo, normalWS, shadow);
                float3 ObjectLight = float3(light & 0x3FF, (light >> 10) & 0x3FF, (light >> 20) & 0x3FF) / 1023.0f;
                ObjectLight = mad((1 - ObjectLight), unity_AmbientGround, ObjectLight * 2.5f); //linear interpolation
                ObjectLight *= albedo;

                if (flags & 0x2) albedo = lerp(max(DynamicLight, ObjectLight).rgb, _RemoveColor.rgb, _RemoveColor.a);
                else albedo = lerp(max(DynamicLight, ObjectLight).rgb, _AddColor.rgb, _AddColor.a);
                return float4(albedo, _Opacity);
            }
            ENDHLSL
        }
    }
}
