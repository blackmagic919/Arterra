Shader "Unlit/ModelCustom" {
    SubShader {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        
        Pass {
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #define INDIRECT
            #define MODEL_DIRECT_BUFFER
            #define COMPACT_MODEL_VERTICES
            #include "Assets/Scripts/Visual/TerrainShaders/CustomModel.hlsl"
            ENDHLSL
        }
    }
}
