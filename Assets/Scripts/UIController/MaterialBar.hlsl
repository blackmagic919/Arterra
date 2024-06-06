#include "UnityCG.cginc"
#include "UnityUI.cginc"

struct appdata
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
};

struct v2f
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
};

sampler2D _MaterialData;
float4 _MaterialData_ST;

struct matTerrain{
    float4 baseColor;
    float baseTextureScale;
    float baseColorStrength;
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

int selectedMat;
float InventorySize;

//A lot of global textures
Texture2DArray _Textures;
SamplerState sampler_Textures;
Texture2D _LiquidFineWave;
SamplerState sampler_LiquidFineWave;
Texture2D _LiquidCoarseWave;
SamplerState sampler_LiquidCoarseWave;

float4 _Tint;
float _TintFrequency;
float _AuxFadeEnd;

v2f vert (appdata v)
{
    v2f o;
    o.positionCS = UnityObjectToClipPos(v.positionOS);
    o.uv = v.uv;
    return o;
}

struct invMat {
    int material;
    float percent;
};


StructuredBuffer<invMat> MainInventoryMaterial;
StructuredBuffer<invMat> AuxInventoryMaterial;
int MainMaterialCount;
int AuxMaterialCount;
uint UseSolid;

int BinarySearch(float key, int arraySize, StructuredBuffer<invMat> inventory) {
    int left = 0;
    int right = max(arraySize - 1, 0);
    uint closestIndex = 0;

    while (left <= right) { 
        int mid = (left + right) / 2;

        if (inventory[mid].percent <= key) {
            closestIndex = mid;
            left = mid + 1;
        } else {
            if(right == 0)//as using uint, right = -1 = uint.max
                return closestIndex;
            right = mid - 1;
        }
    }
               
    return inventory[closestIndex].material;
}

fixed3 GetMatColor(float2 uv, int index){
    matTerrain matData = _MatTerrainData[index];
    float3 color = matData.baseColor;
    float colorStrength = matData.baseColorStrength;

    float textureU = uv.x / matData.baseTextureScale * (InventorySize*25);
    float textureV = uv.y / matData.baseTextureScale;

    float3 textureColor = _Textures.Sample(sampler_Textures, float3(textureU, textureV, index));

    return color * colorStrength + textureColor * (1-colorStrength);
}

fixed3 GetLiquidColor(float2 uv, int index){
    liquidMat matData = _MatLiquidData[index];
    
    uv = float2(uv.x * (InventorySize*25), uv.y);

    float2 fineUV = float2(_Time.x * matData.WaveSpeed.x, _Time.x * matData.WaveSpeed.x * 0.75);
    float2 coarseUV = float2(_Time.x * matData.WaveSpeed.y * -0.5, _Time.x * matData.WaveSpeed.y * -0.25);

    fineUV = uv * matData.WaveScale + fineUV;
    coarseUV = uv * matData.WaveScale + coarseUV;

    float3 fineNormal = UnpackNormal(_LiquidFineWave.Sample(sampler_LiquidFineWave, fineUV));
    float3 coarseNormal = UnpackNormal(_LiquidCoarseWave.Sample(sampler_LiquidCoarseWave, coarseUV));
    float3 waveNormal = lerp(coarseNormal, fineNormal, matData.WaveBlend);

    return lerp(matData.WaterShallowCol, matData.WaterDeepCol, abs(waveNormal.y)); //Give illusion of shadows
}

            
fixed3 frag (v2f IN) : SV_Target
{
    fixed3 MainColor; fixed3 AuxColor;

    int mainIndex = BinarySearch(IN.uv.x, MainMaterialCount, MainInventoryMaterial);
    MainColor = UseSolid == 1 ? GetMatColor(IN.uv, mainIndex) : GetLiquidColor(IN.uv, mainIndex);

    if(mainIndex == selectedMat){
        float tintStrength = abs((_Time.y % _TintFrequency)/_TintFrequency * 2 - 1) * _Tint.a;
        MainColor =  MainColor*(1-tintStrength) + _Tint.rgb*tintStrength;
    }
    
    if(AuxMaterialCount == 0) 
        AuxColor = MainColor;
    else
    {
        int auxIndex = BinarySearch(IN.uv.x, AuxMaterialCount, AuxInventoryMaterial);
        AuxColor = UseSolid == 1 ? GetLiquidColor(IN.uv, auxIndex) : GetMatColor(IN.uv, auxIndex);
    }

    fixed3 OUT;
    if(IN.uv.y < _AuxFadeEnd) OUT = AuxColor;
    else OUT = MainColor;

    return OUT;
}