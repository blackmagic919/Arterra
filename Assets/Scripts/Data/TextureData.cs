using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using static GenerationHeightData;

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
    ComputeBuffer baseTextureIndexes;

    public void ApplyToMaterial()
    {
        Texture2DArray textures = GenerateTextureArray(MaterialDictionary.Select(x => x.texture).ToArray());
        Shader.SetGlobalTexture("_Textures", textures);
        Shader.SetGlobalFloatArray("_BaseColors", MaterialDictionary.SelectMany(e => new float[] { e.color.r, e.color.g, e.color.b, e.color.a }).ToArray());
        Shader.SetGlobalFloatArray("_BaseColorStrength", MaterialDictionary.Select(e => e.baseColorStrength).ToArray());
        Shader.SetGlobalFloatArray("_BaseTextureScales", MaterialDictionary.Select(e => e.textureScale).ToArray());

    }
    public void OnDisable()
    {
        baseColors?.Release();
        baseColorStrengths?.Release();
        baseTextureScales?.Release();
        baseTextureIndexes?.Release();
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
