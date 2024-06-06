Shader "Unlit/MaterialBarFill"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {} //this one is set by the UI panel
        _Tint("Tint Color", Color) = (0, 1, 0, 1)
        _TintFrequency("Tint Frequency", float) = 100
        _AuxFadeEnd("Aux Fade End", float) = 0.5

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            Cull Off
            ZWrite Off

            Stencil{
                Ref [_Stencil]
                Pass[_StencilOp]
                Comp[_StencilComp]
                ReadMask[_StencilReadMask]
                WriteMask[_StencilWriteMask]
            }

            ColorMask[_ColorMask]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag


            #include "MaterialBar.hlsl"
            ENDHLSL
        }
    }
}
