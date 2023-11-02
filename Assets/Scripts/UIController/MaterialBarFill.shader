Shader "Unlit/MaterialBarFill"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {} //this one is set by the UI panel
        _Tint("Tint Color", Color) = (0, 1, 0, 1)
        _TintFrequency("Tint Frequency", float) = 100
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "MaterialBar.hlsl"
            ENDHLSL
        }
    }
}
