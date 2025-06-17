// MIT License
Shader "Grass/GrassLayers" {
    Properties {
        _WindNoiseTexture("Wind noise texture", 2D) = "white" {} // A wind noise texture
        _WindTimeMult("Wind frequency", Float) = 1 // Wind noise offset by time
        _WindAmplitude("Wind strength", Float) = 1 // The largest UV offset of wind
    }

    SubShader {
        Tags{"RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True"}
        // Forward Lit Pass
        Pass {

            Name "ForwardLit"
            Tags {"LightMode" = "UniversalForward"}
            Cull Back

            HLSLPROGRAM

            // Register our functions
            #pragma vertex Vertex
            #pragma fragment Fragment

            // Incude our logic file
            #include "GrassLayers.hlsl"    

            ENDHLSL
        }

        Pass {

            Name "ShadowCaster"
            Tags {"LightMode" = "ShadowCaster"}

            HLSLPROGRAM
            // Signal this shader requires geometry function support
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma target 5.0  

            // Support all the various light types and shadow paths
            #pragma multi_compile_shadowcaster

            // Register our functions
            #pragma vertex Vertex
            #pragma fragment Fragment

            // A custom keyword to modify logic during the shadow caster pass
            #define SHADOW_CASTER_PASS
            // Incude our logic file
            #include "GrassLayers.hlsl"

            ENDHLSL
        }

        
    }
}