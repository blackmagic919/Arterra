using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class TextureData
{

    const int textureSize = 512;
    const TextureFormat textureFormat = TextureFormat.RGB565;

    public static void ApplyToMaterial(Material mat, List<GenerationHeightData.BMaterial> materials)
    {
        mat.SetInt("baseColorCount", materials.Count);
        mat.SetColorArray("baseColors", materials.Select(x => x.materialData.color).ToArray());
        mat.SetFloatArray("baseColorStrength", materials.Select(x => x.materialData.baseColorStrength).ToArray());
        mat.SetFloatArray("baseTextureScales", materials.Select(x => x.materialData.textureScale).ToArray());
        Texture2DArray texturesArray = GenerateTextureArray(materials.Select(x => x.materialData.texture).ToArray());
        mat.SetTexture("baseTextures", texturesArray);
    }

    public static Texture2DArray GenerateTextureArray(Texture2D[] textures)
    {   
        Texture2DArray textureArray = new Texture2DArray(textureSize, textureSize, textures.Length, textureFormat, true);
        for(int i = 0; i < textures.Length; i++)
        {
            textureArray.SetPixels(textures[i].GetPixels(), i);
        }
        textureArray.Apply();
        return textureArray;
    }
}
