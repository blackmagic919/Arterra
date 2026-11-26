Shader "Hidden/Arterra/PanoramaProjection"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            UNITY_DECLARE_TEX2DARRAY(_Faces);
            float4x4 _FaceWorldToClip[6];

            float4 frag(v2f_img input) : SV_Target {
                // Match Unity's Skybox/Panoramic longitude-latitude convention.
                float longitude = (0.5f - input.uv.x) * (2.0f * UNITY_PI);
                float latitude = (input.uv.y - 0.5f) * UNITY_PI;
                float3 direction = float3(cos(latitude) * cos(longitude), sin(latitude), cos(latitude) * sin(longitude));
                float3 axis = abs(direction);
                uint face = axis.x >= axis.y && axis.x >= axis.z ? (direction.x >= 0 ? 0u : 1u)
                    : axis.y >= axis.z ? (direction.y >= 0 ? 2u : 3u) : (direction.z >= 0 ? 4u : 5u);
                float4 clip = mul(_FaceWorldToClip[face], float4(direction, 0));
                float2 uv = clip.xy / clip.w * 0.5f + 0.5f;
                #if UNITY_UV_STARTS_AT_TOP
                    uv.y = 1.0f - uv.y;
                #endif
                return UNITY_SAMPLE_TEX2DARRAY(_Faces, float3(uv, face));
            }
            ENDHLSL
        }
    }
}
