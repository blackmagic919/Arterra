Shader "Arterra/Custom Model" {
    SubShader {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ INDIRECT
            #pragma multi_compile _ NO_EDITORLIGHTING
            #pragma multi_compile _ ATMOSPHERE_DEFERRED
            #include "CustomModel.hlsl"
            ENDHLSL
        }
        Pass {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment depthFrag
            #pragma multi_compile _ INDIRECT
            #include "CustomModel.hlsl"
            ENDHLSL
        }
        Pass {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ INDIRECT
            #include "TerrainShaderShadowCast.hlsl"
            ENDHLSL
        }
    }
}
