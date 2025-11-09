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
        _SelectedColor("Selected Color", Color) = (0.5, 0.5, 0.5, 0.5)
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
            float GridWidth;

            //A lot of global textures
            Texture2DArray _Textures;
            SamplerState sampler_Textures;
            Texture2D _LiquidFineWave;
            SamplerState sampler_LiquidFineWave;
            Texture2D _LiquidCoarseWave;
            SamplerState sampler_LiquidCoarseWave;
            float4 _SelectedColor;
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

            struct Ingredient {
                int Index;
                float Amount;
                uint Flags;
            };
            //
            StructuredBuffer<Ingredient> CraftingInfo;

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

            inline float sharpMap(float x, float pU, float pL) {
                if (x < 0.5) return 0.4 * pow(2.0 * x, pL);
                else return 1.0 - 0.6 * pow(2.0 * (1.0 - x), pU);
            }


            fixed3 frag (v2f IN) : SV_Target
            {
                float2 gridPos = min(IN.uv * GridWidth, GridWidth - 0.0001);
                float2 distToEdge = min(frac(gridPos), 1 - frac(gridPos));
                if(any(distToEdge < _GridSize))
                    return _GridColor;

                float densAmt = 0;
                Ingredient selIng = (Ingredient)0; selIng.Amount = -1.0f;
                int offset = (IN.offset.r * 255.0f) * (GridWidth + 1) * (GridWidth + 1);
                Influences2D blend = GetBlendInfo(gridPos);

                [unroll]for(int i = 0; i < 4; i++){
                    int2 cCoord = gridPos + int2(i % 2, i / 2);
                    int index = cCoord.x + cCoord.y * (GridWidth + 1);

                    Ingredient ing = CraftingInfo[index + offset];
                    ing.Amount = sharpMap(ing.Amount, 175.0f, 1);

                    densAmt += ing.Amount * blend.corner[i];
                    if (ing.Amount <= 0) continue;
                    ing.Amount -= distance(cCoord, gridPos);
                    if (ing.Amount > selIng.Amount) selIng = ing;
                }
                float blendWeight = smoothstep(0, 0.25, densAmt);
                float finalAmt = lerp(selIng.Amount, densAmt, blendWeight);
                if(finalAmt > 0){
                    fixed3 matColor = GetMatColor(IN.uv / _TexScale, selIng.Index);
                    if ((selIng.Flags & 0x1) && IN.offset.g > 0) 
                        return lerp(matColor, _SelectedColor.rgb, _SelectedColor.a);
                    else return matColor;
                } else discard;
                return 0;
            }

            ENDHLSL
        }
    }
}
