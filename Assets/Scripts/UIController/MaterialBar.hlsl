#include "UnityCG.cginc"
 #include "UnityUI.cginc"

struct appdata
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
};

struct v2f
{
    float2 uv : TEXCOORD0;
    float4 positionCS : SV_POSITION;
};

sampler2D _MaterialData;
float4 _MaterialData_ST;

const static int matAllocate = 8;
            
float _BaseColors[4*matAllocate];
float _BaseColorStrength[matAllocate];
float _BaseTextureScales[matAllocate];

int maxMatCount;
int selectedMat;
float InventorySize;

Texture2DArray _Textures;
SamplerState sampler_Textures;

float4 _Tint;
float _TintFrequency;

v2f vert (appdata v)
{
    v2f o;
    o.positionCS = UnityObjectToClipPos(v.positionOS);
    o.uv = TRANSFORM_TEX(v.uv, _MaterialData);
    return o;
}

float4 GetColor(uint ind){
    return float4(_BaseColors[4*ind], _BaseColors[4*ind+1], _BaseColors[4*ind+2], _BaseColors[4*ind+3]);
}
            
fixed3 frag (v2f IN) : SV_Target
{
    uint index = tex2D(_MaterialData, IN.uv).a * maxMatCount;

    float3 color = GetColor(index);
    float colorStrength = _BaseColorStrength[index];

    float textureU = IN.uv.x / _BaseTextureScales[index] * (InventorySize*25);
    float textureV = IN.uv.y / _BaseTextureScales[index];

    float3 textureColor = _Textures.Sample(sampler_Textures, float3(textureU, textureV, index));

    float3 OUT = color * colorStrength + textureColor * (1-colorStrength);

    if(index == selectedMat){
        float tintStrength = abs((_Time.y % _TintFrequency)/_TintFrequency * 2 - 1);
        return OUT*(1-tintStrength) + _Tint*tintStrength;
    }

    return OUT;
}