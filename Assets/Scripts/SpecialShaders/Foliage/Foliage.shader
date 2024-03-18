Shader "Unlit/Foliage"
{
    Properties
    {
        _LeafColor ("Leaf Color", Color) = (0, 1, 0, 1)
        _FresnelFalloff ("Highlight Falloff", float) = 0.5
        _AlphaMap ("Alpha Map", 2D) = "white" {}
        _AlphaClip ("Clip Value", float) = 0.2
        _WindNoiseTexture("Wind Noise Texture", 2D) = "white" {} 
        _WindTimeMult("Wind Frequency", Float) = 1 
        _WindAmplitude("Wind Strength", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags {"LightMode" = "UniversalForward"}
            
            Cull Back

            HLSLPROGRAM

            // Signal this shader requires geometry function support
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma target 5.0

            // Lighting and shadow keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT

            // Register our functions
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Foliage.hlsl"
            ENDHLSL
        }
    }
}
