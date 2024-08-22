Shader "Unlit/LiquidShader"
{
    Properties
    {
        //None all data from buffers
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