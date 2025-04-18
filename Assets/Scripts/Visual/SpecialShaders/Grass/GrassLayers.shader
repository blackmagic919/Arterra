// MIT License
Shader "Grass/GrassLayers" {
    Properties {
        _BaseColor("Base color", Color) = (0, 0.5, 0, 1) // Color of the lowest layer
        _TopColor("Top color", Color) = (0, 1, 0, 1) // Color of the highest layer
        _DetailNoiseTexture("Grainy noise", 2D) = "white" {} // Texture A used to clip layers
        _DetailDepthScale("Grainy depth scale", Range(0, 1)) = 1 // The influence of Texture A
        _SmoothNoiseTexture("Smooth noise", 2D) = "white" {} // Texture B used to clip layers
        _SmoothDepthScale("Smooth depth scale", Range(0, 1)) = 1 // The influence of Texture B
        _WindNoiseTexture("Wind noise texture", 2D) = "white" {} // A wind noise texture
        _WindTimeMult("Wind frequency", Float) = 1 // Wind noise offset by time
        _WindAmplitude("Wind strength", Float) = 1 // The largest UV offset of wind
        _CameraHeight("Camera Height", Float) = 2
        _WSToUVScale("UV Scale", Float) = 1
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