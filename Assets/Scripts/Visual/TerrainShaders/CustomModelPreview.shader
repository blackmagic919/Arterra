Shader "Arterra/Custom Model Preview" {
    SubShader {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Overlay" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Pass {
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ INDIRECT
            #define MODEL_PREVIEW
            #include "Assets/Scripts/Visual/TerrainShaders/CustomModel.hlsl"
            ENDHLSL
        }
    }
}
