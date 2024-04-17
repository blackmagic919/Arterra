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

struct matTerrain{
    float4 baseColor;
    float baseTextureScale;
    float baseColorStrength;
    int geoShaderInd;
};

StructuredBuffer<matTerrain> _MatTerrainData;

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
    o.uv = v.uv;
    return o;
}

int materialCount;
StructuredBuffer<int> inventoryMaterialIndexes;
StructuredBuffer<float> inventoryMaterialPercents;
uint BinarySearch(float key, uint arraySize) {
    uint left = 0;
    uint right = arraySize - 1;
    uint closestIndex = 0;

    while (left <= right) { 
        uint mid = (left + right) / 2;

        if (inventoryMaterialPercents[mid] <= key) {
            closestIndex = mid;
            left = mid + 1;
        } else {
            if(right == 0)//as using uint, right = -1 = uint.max
                return closestIndex;
            right = mid - 1;
        }
    }
               
    return closestIndex;
}

            
fixed3 frag (v2f IN) : SV_Target
{
    int index = inventoryMaterialIndexes[BinarySearch(IN.uv.x, materialCount)];

    matTerrain matData = _MatTerrainData[index];
    float3 color = matData.baseColor;
    float colorStrength = matData.baseColorStrength;
    

    float textureU = IN.uv.x / matData.baseTextureScale * (InventorySize*25);
    float textureV = IN.uv.y / matData.baseTextureScale;

    float3 textureColor = _Textures.Sample(sampler_Textures, float3(textureU, textureV, index));

    float3 OUT = color * colorStrength + textureColor * (1-colorStrength);

    if((int)index == selectedMat){
        float tintStrength = abs((_Time.y % _TintFrequency)/_TintFrequency * 2 - 1);
        return OUT*(1-tintStrength) + _Tint*tintStrength;
    }

    return OUT;
}