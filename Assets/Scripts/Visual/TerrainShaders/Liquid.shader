Shader "Unlit/LiquidShader"
{
    Properties
    {
        //None all data from buffers
    }   

    SubShader//
    {
        Tags {"RenderPipeline" = "UniversalPipeline"  "Queue" = "Transparent" "RenderType"="Transparent" }
        ZWrite On

        Pass
        {
            Name "ForwardLit"
            Tags {"LightMode" = "PreFogTransparents"}
            Cull Back
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define _SPECULAR_COLOR
            #pragma multi_compile _ INDIRECT 
            #pragma multi_compile _ NO_EDITORLIGHTING
            
            #include "LiquidShader.hlsl"
            ENDHLSL
        }
    }
}