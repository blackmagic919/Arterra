Shader "Unlit/LiquidShader"
{
    Properties
    {
        //None all data from buffers
    }   

    SubShader//
    {
        Tags {"RenderPipeline" = "UniversalPipeline"  "Queue" = "Transparent" "RenderType"="Opaque" }
        ZWrite On

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
            
            #include "LiquidShader.hlsl"
            ENDHLSL
        }
    }
}