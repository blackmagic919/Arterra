using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

[CreateAssetMenu(menuName = "Settings/TextureDict")]
public class TextureData : UpdatableData
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

    public void ApplyToMaterial()
    {
        OnDisable();

        int numMats = MaterialDictionary.Count;
        baseColors = new ComputeBuffer(numMats, sizeof(float) * 4, ComputeBufferType.Structured);
        baseColorStrengths = new ComputeBuffer(numMats, sizeof(float), ComputeBufferType.Structured);
        baseTextureScales = new ComputeBuffer(numMats, sizeof(float), ComputeBufferType.Structured);
        geoShaderIndexes = new ComputeBuffer(numMats, sizeof(int), ComputeBufferType.Structured);

        baseColors.SetData(MaterialDictionary.SelectMany(e => new float[] { e.color.r, e.color.g, e.color.b, e.color.a }).ToArray());
        baseColorStrengths.SetData(MaterialDictionary.Select(e => e.baseColorStrength).ToArray());
        baseTextureScales.SetData(MaterialDictionary.Select(e => e.textureScale).ToArray());
        geoShaderIndexes.SetData(MaterialDictionary.Select(e => e.GeoShaderIndex).ToArray());


        Texture2DArray textures = GenerateTextureArray(MaterialDictionary.Select(x => x.texture).ToArray());
        Shader.SetGlobalTexture("_Textures", textures);
        Shader.SetGlobalBuffer("_BaseColors", baseColors);
        Shader.SetGlobalBuffer("_BaseColorStrength", baseColorStrengths);
        Shader.SetGlobalBuffer("_BaseTextureScales", baseTextureScales);
        Shader.SetGlobalBuffer("_MaterialShaderIndex", geoShaderIndexes);

    }
    public void OnDisable()
    {
        baseColors?.Release();
        baseColorStrengths?.Release();
        baseTextureScales?.Release();
        geoShaderIndexes?.Release();
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


    protected override void OnValidate()
    {
        ApplyToMaterial();
        
        base.OnValidate();
    }
}
