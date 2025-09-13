Shader "Unlit/CraftingShader"
{
    Properties
    {
        _TexScale("Texture scale", float) = 0.25
        _GridSize("Grid size", float) = 0.025
        _GridColor("Grid color", Color) = (0.5, 0.5, 0.5, 1.0)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        _SelectedColor("Selected Color", Color) = (0.1, 0.8, 0.1, 1.0)
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

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "Assets/Resources/Compute/Utility/BlendHelper.hlsl"

            struct appdata
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 offset : TEXCOORD1;
            };

            struct matTerrain{
                int texIndex;
                float baseTextureScale;
                uint flipStateRendering;
                int geoShaderInd;
            };

            struct liquidMat{
                float3 WaterShallowCol;
                float3 WaterDeepCol;
                float WaterColFalloff;
                float DepthOpacity;
                float Smoothness;
                float WaveBlend;
                float WaveStrength;
                float2 WaveScale;
                float2 WaveSpeed;
            };

            StructuredBuffer<matTerrain> _MatTerrainData;
            StructuredBuffer<liquidMat> _MatLiquidData;
            uint IsoValue; float GridWidth;

            //A lot of global textures
            Texture2DArray _Textures;
            SamplerState sampler_Textures;
            Texture2D _LiquidFineWave;
            SamplerState sampler_LiquidFineWave;
            Texture2D _LiquidCoarseWave;
            SamplerState sampler_LiquidCoarseWave;
            float3 _SelectedColor;
            fixed3 _GridColor;
            float _GridSize;
            float _TexScale;

            v2f vert (appdata v)
            {
                v2f o;
                o.positionCS = UnityObjectToClipPos(v.positionOS);
                o.uv = v.uv;
                o.offset = v.color.rg;
                return o;
            }

            StructuredBuffer<uint> CraftingInfo;

            fixed3 GetMatColor(float2 uv, int index){
                matTerrain matData = _MatTerrainData[index];
            
                float textureU = uv.x / matData.baseTextureScale;
                float textureV = uv.y / matData.baseTextureScale;
            
                return _Textures.Sample(sampler_Textures, float3(textureU, textureV, matData.texIndex));
            }
            
            fixed3 GetLiquidColor(float2 uv, int index){
                liquidMat matData = _MatLiquidData[index];
                
                uv = float2(uv.x, uv.y);
            
                float2 fineUV = float2(_Time.x * matData.WaveSpeed.x, _Time.x * matData.WaveSpeed.x * 0.75);
                float2 coarseUV = float2(_Time.x * matData.WaveSpeed.y * -0.5, _Time.x * matData.WaveSpeed.y * -0.25);
            
                fineUV = uv * matData.WaveScale + fineUV;
                coarseUV = uv * matData.WaveScale + coarseUV;
            
                float3 fineNormal = UnpackNormal(_LiquidFineWave.Sample(sampler_LiquidFineWave, fineUV));
                float3 coarseNormal = UnpackNormal(_LiquidCoarseWave.Sample(sampler_LiquidCoarseWave, coarseUV));
                float3 waveNormal = lerp(coarseNormal, fineNormal, matData.WaveBlend);
            
                return lerp(matData.WaterShallowCol, matData.WaterDeepCol, abs(waveNormal.y)); //Give illusion of shadows
            }
            
            uint density(uint data) { return data & 0x000000FF; }
            uint viscosity(uint data) { return (data & 0x0000FF00) >> 8; }
            uint material(uint data) { return (data & 0x7FFF0000) >> 16; }
            bool isDirty(uint data) { return (data & 0x80000000) != 0; }

            fixed3 frag (v2f IN) : SV_Target
            {
                float2 gridPos = min(IN.uv * GridWidth, GridWidth - 0.0001);
                float2 distToEdge = min(frac(gridPos), 1 - frac(gridPos));
                if(any(distToEdge < _GridSize))
                    return _GridColor;

                
                float2 solidParent = 0; 
                uint solidMat = 0;
                float2 liquidParent = 0;
                uint liquidMat = 0;

                int offset = (IN.offset.r * 255.0f) * (GridWidth + 1) * (GridWidth + 1);
                Influences2D blend = GetBlendInfo(gridPos);

                [unroll]for(int i = 0; i < 4; i++){
                    int2 cCoord = blend.origin + int2(i % 2, i / 2);
                    int index = cCoord.x * (GridWidth + 1) + cCoord.y;

                    uint mapData = CraftingInfo[index + offset];
                    float solidDensity = (int)viscosity(mapData) * blend.corner[i];
                    float liquidDensity = (int)density(mapData) * blend.corner[i] - solidDensity;
                    if(solidDensity >= solidParent.y && viscosity(mapData) >= IsoValue) {
                        solidParent.y = solidDensity;
                        solidMat = mapData;
                    } if(liquidDensity >= liquidParent.y && density(mapData) 
                        - viscosity(mapData) >= IsoValue) {
                        liquidParent.y = liquidDensity;
                        liquidMat = mapData;
                    } 
                    solidParent.x += solidDensity;
                    liquidParent.x += liquidDensity;
                }

                if(solidParent.x >= IsoValue){
                    if (isDirty(solidMat) && IN.offset.g > 0) return _SelectedColor;
                    else return GetMatColor(IN.uv / _TexScale, material(solidMat));
                } else if(liquidParent.x >= IsoValue){
                    if (isDirty(liquidMat) && IN.offset.g > 0) return _SelectedColor;
                    else return GetLiquidColor(IN.uv / _TexScale, material(liquidMat));
                } else discard;
                return 0;
            }

            ENDHLSL
        }
    }
}
