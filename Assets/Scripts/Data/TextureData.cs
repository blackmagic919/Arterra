using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

[CreateAssetMenu(menuName = "Settings/TextureDict")]
public class TextureData : ScriptableObject
{

    const int textureSize = 512;
    const TextureFormat textureFormat = TextureFormat.RGB565;

    [SerializeField]
    public List<MaterialData> MaterialDictionary;

    Texture2DArray textureArray;
    ComputeBuffer baseColors;
    ComputeBuffer baseColorStrengths;
    ComputeBuffer baseTextureScales;
    ComputeBuffer geoShaderIndexes;
    ComputeBuffer atmosphericData;

    public void ApplyToMaterial()
    {
        OnDisable();

        int numMats = MaterialDictionary.Count;
        baseColors = new ComputeBuffer(numMats, sizeof(float) * 4, ComputeBufferType.Structured);
        baseColorStrengths = new ComputeBuffer(numMats, sizeof(float), ComputeBufferType.Structured);
        baseTextureScales = new ComputeBuffer(numMats, sizeof(float), ComputeBufferType.Structured);
        geoShaderIndexes = new ComputeBuffer(numMats, sizeof(int), ComputeBufferType.Structured);
        atmosphericData = new ComputeBuffer(numMats, sizeof(float)*4, ComputeBufferType.Structured);

        baseColors.SetData(MaterialDictionary.SelectMany(e => new float[] { e.color.r, e.color.g, e.color.b, e.color.a }).ToArray());
        baseColorStrengths.SetData(MaterialDictionary.Select(e => e.baseColorStrength).ToArray());
        baseTextureScales.SetData(MaterialDictionary.Select(e => e.textureScale).ToArray());
        geoShaderIndexes.SetData(MaterialDictionary.Select(e => e.GeoShaderIndex).ToArray());
        atmosphericData.SetData(MaterialDictionary.Select(e => e.AtmosphereScatter).ToArray());


        Texture2DArray textures = GenerateTextureArray(MaterialDictionary.Select(x => x.texture).ToArray());
        Shader.SetGlobalTexture("_Textures", textures);
        Shader.SetGlobalBuffer("_BaseColors", baseColors);
        Shader.SetGlobalBuffer("_BaseColorStrength", baseColorStrengths);
        Shader.SetGlobalBuffer("_BaseTextureScales", baseTextureScales);
        Shader.SetGlobalBuffer("_MaterialShaderIndex", geoShaderIndexes);
        Shader.SetGlobalBuffer("_MatAtmosphericData", atmosphericData);
    }
    public void OnDisable()
    {
        baseColors?.Release();
        baseColorStrengths?.Release();
        baseTextureScales?.Release();
        geoShaderIndexes?.Release();
        atmosphericData?.Release();
    }

    public Texture2DArray GenerateTextureArray(Texture2D[] textures)
    {   
        textureArray = new Texture2DArray(textureSize, textureSize, textures.Length, textureFormat, true);
        for(int i = 0; i < textures.Length; i++)
        {
            textureArray.SetPixels(textures[i].GetPixels(), i);
        }
        textureArray.Apply();
        return textureArray;
    }


    public void OnEnable()
    {
        ApplyToMaterial();
    }
}
