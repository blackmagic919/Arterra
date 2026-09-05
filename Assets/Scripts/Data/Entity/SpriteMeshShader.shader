Shader "Unlit/SpriteMeshShader"
{
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags{"LightMode" = "UniversalForward"}

            Cull Back

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ NO_EDITORLIGHTING
            #pragma multi_compile _ ATMOSPHERE_DEFERRED
            #pragma multi_compile _ INDIRECT  //Try to use shader_feature--doesn't work with material instances, but less variants

            #include "SpriteMesh.hlsl"
            ENDHLSL
        }
    }
}
