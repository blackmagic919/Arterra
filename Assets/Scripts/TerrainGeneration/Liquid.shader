Shader "Unlit/LiquidShader"
{
    Properties
    {
        _WaveNormalFine ("Wave Normal", 2D) = "white" {}
        _WaveNormalCoarse ("Wave Normal Coarse", 2D) = "white" {}

        _WaveScaleFine ("Wave Scale Fine", Range(0, 1)) = 0.5
        _WaveScaleCoarse ("Wave Scale Coarse", Range(0, 1)) = 0.5

        _WaveStrength("Wave Normal Strength", Range(0, 1)) = 0.5
        _WaveBlend("Wave Blend", Range(0, 1)) = 0.5

        _WaveSpeedFine("Wave Speed Fine", Range(0, 20)) = 0.5
        _WaveSpeedCoarse("Wave Speed Coarse", Range(0, 20)) = 0.5
    }   

    SubShader
    {
        Tags {"RenderPipeline" = "UniversalPipeline"  "Queue" = "Transparent" "RenderType"="Transparent" }
        ZWrite On
		Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "ForwardLit"
            Tags {"LightMode" = "UniversalForward"}
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define _SPECULAR_COLOR
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ INDIRECT 
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            #include "LiquidShader.hlsl"
            ENDHLSL
        }
    }
}