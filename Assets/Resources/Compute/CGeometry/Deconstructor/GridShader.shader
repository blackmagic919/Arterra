Shader "Unlit/GridShader"
{
    Properties
    {
        _WireframeWidth("WireframeWidth", float) = 0.005
        _WireframeColor("Wireframe front colour", Color) = (0.5, 0.5, 0.5, 1.0)
        _VertexColor("Vertex color", Color) = (1.0, 1.0, 1.0, 1.0)
        _SelectedColor("Selected color", Color) = (1.0, 0.5, 0.3, 1.0)
        _VertexSize("Vertex size", float) = 0.02
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline"  "Queue" = "Transparent+1" "RenderType"="Opaque" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Resources/Compute/Utility/GetIndex.hlsl"


            struct DrawVertex{
                int3 positionOS;
            };

            struct v2f
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
            };

            StructuredBuffer<DrawVertex> VertexBuffer;
            StructuredBuffer<uint> IndexBuffer;
            uint bSTART_index;
            uint bSTART_vertex;

            StructuredBuffer<uint> SelectionBuffer;
            int MapSizeX; int MapSizeY; int MapSizeZ;

            float4x4 _LocalToWorld;

            float _WireframeWidth;
            half4 _WireframeColor;

            float _VertexSize;
            half4 _VertexColor;
            half4 _SelectedColor;

            const static int3 cubeCorners[8] = {
                int3(0, 0, 0), int3(1, 0, 0), int3(0, 1, 0), int3(0, 0, 1),
                int3(1, 1, 0), int3(1, 0, 1), int3(0, 1, 1), int3(1, 1, 1)
            };

            v2f vert (uint vertexID: SV_VertexID)
            {
                v2f o = (v2f)0;

                DrawVertex input = VertexBuffer[bSTART_vertex + IndexBuffer[bSTART_index + vertexID]];
                float3 positionOS = float3(input.positionOS);
                float3 positionWS = mul(_LocalToWorld, float4(positionOS, 1.0)).xyz;

                o.positionCS = TransformWorldToHClip(positionWS);
                o.positionOS = input.positionOS;
                
                return o;
            }

            half4 frag (v2f IN) : SV_Target
            {
                float3 offset = frac(IN.positionOS);

                //Draw Vertex
                [unroll]for(uint i = 0; i < 8; i++){
                    float dist = distance(offset, cubeCorners[i]);
                    if(dist < _VertexSize){
                        uint index = indexFromCoordIrregular(floor(IN.positionOS) + cubeCorners[i], uint2(MapSizeY, MapSizeZ));
                        return ((SelectionBuffer[index/32] >> (index % 32)) & 0x1) ? _SelectedColor : _VertexColor;
                    }
                }

                //Draw Edges
                float distToXY = min(min(offset.x, offset.y), 1 - max(offset.x, offset.y));
                float distToXZ = min(min(offset.x, offset.z), 1 - max(offset.x, offset.z));
                float distToYZ = min(min(offset.y, offset.z), 1 - max(offset.y, offset.z));
                float distToEdge = max(max(distToXY, distToXZ), distToYZ);

                float alpha = step(distToEdge, _WireframeWidth);

                return half4(_WireframeColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
