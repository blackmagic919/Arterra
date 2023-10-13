#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
//#include "UnityCG.cginc"


const static int maxMatCount = 8;

//int baseColorCount;
float _BaseColors[maxMatCount];
float _BaseColorStrength[maxMatCount];
float _BaseTextureScales[maxMatCount];
//float4x4 _LocalToWorld; 

Texture2DArray _Textures;
SamplerState sampler_Textures;

struct appdata
{
    float4 vertex : POSITION;
    float4 normal : NORMAL;
    float4 color: COLOR;
};

struct v2f
{
    float2 uv : TEXCOORD0;
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD1;
    float3 normalWS : TEXCOORD2;
    float4 color: COLOR;
};

v2f vert (appdata v)
{
    v2f o;

    VertexPositionInputs posInputs = GetVertexPositionInputs(v.vertex.xyz);
	VertexNormalInputs normInputs = GetVertexNormalInputs(v.normal.xyz);

    o.positionCS = posInputs.positionCS;
    o.positionWS = posInputs.positionWS;
    o.normalWS = normInputs.normalWS;
    o.color = v.color;
    return o;
}

float4 lerp(float4 a, float4 b, float value){
    return (a*value) + (b*(1-value));
}

float3 lerp(float3 a, float3 b, float value){
    return (a*value) + (b*(1-value));
}

float lerp(float a, float b, float value){
    return (a*value) + (b*(1-value));
}

float4 GetColor(float ind){
    return float4(_BaseColors[4*ind], _BaseColors[4*ind+1], _BaseColors[4*ind+2], _BaseColors[4*ind+3]);
}


float3 triplanar(float3 worldPos, float scale, float3 blendAxes, int texInd){
    float3 scaledWorldPos = worldPos / scale;
    
    float4 xProjection = _Textures.Sample(sampler_Textures, float3(scaledWorldPos.y, scaledWorldPos.z, texInd)) * blendAxes.x;
    float4 yProjection = _Textures.Sample(sampler_Textures, float3(scaledWorldPos.x, scaledWorldPos.z, texInd)) * blendAxes.y;
    float4 zProjection = _Textures.Sample(sampler_Textures, float3(scaledWorldPos.x, scaledWorldPos.y, texInd)) * blendAxes.z;

    return xProjection + yProjection + zProjection;
}


float3 frag (v2f IN) : SV_Target
{
    float3 blendAxes = abs(IN.normalWS);
    blendAxes /= blendAxes.x + blendAxes.y + blendAxes.z;

    int a = (int)IN.color.r;
    int b = (int)IN.color.g;
    float interpFactor = IN.color.b/((float)255);

    float3 baseColor = lerp(GetColor(a), GetColor(b), interpFactor);
    float3 texA = triplanar(IN.positionWS, _BaseTextureScales[a], blendAxes, a);
    float3 texB = triplanar(IN.positionWS, _BaseTextureScales[b], blendAxes, b);

    float3 textureColor = lerp(texA, texB, interpFactor);

    float colorStrength = lerp(_BaseColorStrength[a], _BaseColorStrength[b], interpFactor);

    InputData lightingInput = (InputData)0;
	lightingInput.positionWS = IN.positionWS;
	lightingInput.normalWS = normalize(IN.normalWS);
    lightingInput.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
	lightingInput.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);

	SurfaceData surfaceInput = (SurfaceData)0;
	surfaceInput.albedo = baseColor * colorStrength + textureColor*(1-colorStrength);
	surfaceInput.alpha = 1;

    #if UNITY_VERSION >= 202120
	return UniversalFragmentBlinnPhong(lightingInput, surfaceInput);
#else
	return UniversalFragmentBlinnPhong(lightingInput, surfaceInput.albedo, float4(surfaceInput.specular, 1), surfaceInput.smoothness, 0, surfaceInput.alpha);
#endif
}