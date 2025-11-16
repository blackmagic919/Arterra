Shader "Unlit/OutlineShader"
{
    Properties
    {
        _WireframeWidth("Wireframe width threshold", float) = 0.005
        _WireframeColor("Wireframe front colour", Color) = (0.5, 0.5, 0.5, 1.0)
        _BackgroundColor("Background color", Color) = (0, 0.5, 0, 0.25)
        _VertexColor("Vertex Color", Color) = (0.6, 0.25, 0, 0.25)
        _VertexSize("Vertex size", float) = 0.02
        _DepthOpacity("Depth Opacity", float) = 0.5
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay" "RenderType"="Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Cull Off
            ZWrite On

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
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            float4x4 _LocalToWorld;

            float _WireframeWidth;
            half4 _WireframeColor;
            
            float _DepthOpacity;
            float _VertexSize;
            half4 _VertexColor;
            half4 _BackgroundColor;
            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            const static int3 cubeCorners[8] = {
                int3(0, 0, 0), int3(1, 0, 0), int3(0, 1, 0), int3(0, 0, 1),
                int3(1, 1, 0), int3(1, 0, 1), int3(0, 1, 1), int3(1, 1, 1)
            };

            struct appdata
            {
                float3 vertex : POSITION;
                float3 normal : NORMAL;
            };

            v2f vert (appdata v)
            {
                v2f o = (v2f)0;
                VertexPositionInputs posInputs = GetVertexPositionInputs(v.vertex.xyz);
                o.positionCS = posInputs.positionCS;
                o.positionWS = posInputs.positionWS;
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag (v2f IN) : SV_Target
            {
                float3 offset = frac(IN.positionWS + 0.5f);
                //Draw Vertex
                [unroll]for(uint i = 0; i < 8; i++){
                    float dist = distance(offset, cubeCorners[i]);
                    if(dist < _VertexSize){
                        return _VertexColor;
                    }
                }
                

                //Draw Edges
                float distToXY = min(min(offset.x, offset.y), 1 - max(offset.x, offset.y));
                float distToXZ = min(min(offset.x, offset.z), 1 - max(offset.x, offset.z));
                float distToYZ = min(min(offset.y, offset.z), 1 - max(offset.y, offset.z));
                float distToEdge = max(max(distToXY, distToXZ), distToYZ);

                float4 albedo;
                if(distToEdge > _WireframeWidth)
                    albedo = _BackgroundColor;
                else 
                    albedo = _WireframeColor;

                float2 UV = IN.screenPos.xy / IN.screenPos.w;
                float3 viewVector = mul(unity_CameraInvProjection, float4(UV.xy * 2 - 1, 0, -1)).xyz;
                float screenDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, UV);
                float linearDepth = LinearEyeDepth(screenDepth, _ZBufferParams) * length(viewVector);
                float dstToOverlay = IN.positionCS.w;
                float overlayDepth = linearDepth - dstToOverlay;
                overlayDepth = exp(-overlayDepth * _DepthOpacity);
                albedo.a = albedo.a * overlayDepth + (1 - overlayDepth);
                return albedo;
            }
            ENDHLSL
        }
    }
}
