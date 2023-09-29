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
    public List<Texture2D> TextureDictionary;

    Texture2DArray textureArray;

    public Shader TerrainShader;

    public void ApplyToMaterial(Material mat, List<GenerationHeightData.BMaterial> materials)
    {
        mat.SetInt("baseColorCount", materials.Count);
        mat.SetColorArray("baseColors", materials.Select(x => x.materialData.color).ToArray());
        mat.SetFloatArray("baseColorStrength", materials.Select(x => x.materialData.baseColorStrength).ToArray());
        mat.SetFloatArray("baseTextureScales", materials.Select(x => x.materialData.textureScale).ToArray());
        mat.SetFloatArray("baseTextureIndexes", materials.Select(x => (float)x.materialData.textureInd).ToArray());
    }

    public void GenerateTextureArray(Texture2D[] textures)
    {   
        textureArray = new Texture2DArray(textureSize, textureSize, textures.Length, textureFormat, true);
        for(int i = 0; i < textures.Length; i++)
        {
            textureArray.SetPixels(textures[i].GetPixels(), i);
        }
        textureArray.Apply();
        Shader.SetGlobalTexture("_Textures", textureArray);
    }

    protected override void OnValidate()
    {
        GenerateTextureArray(TextureDictionary.ToArray());
        base.OnValidate();
    }
}
