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

    [Header("Other Textures")]
    public Texture2D liquidFineWave;
    public Texture2D liquidCoarseWave;

    Texture2DArray textureArray;
    ComputeBuffer terrainData;
    ComputeBuffer liquidData;
    ComputeBuffer atmosphericData;

    public void ApplyToMaterial()
    {
        OnDisable();

        int numMats = MaterialDictionary.Count;
        terrainData = new ComputeBuffer(numMats, sizeof(float) * 6 + sizeof(int), ComputeBufferType.Structured);
        atmosphericData = new ComputeBuffer(numMats, sizeof(float) * 6, ComputeBufferType.Structured);
        liquidData = new ComputeBuffer(numMats, sizeof(float) * (3 * 2 + 2 * 2 + 5), ComputeBufferType.Structured);

        terrainData.SetData(MaterialDictionary.Select(e => e.terrainData).ToArray());
        atmosphericData.SetData(MaterialDictionary.Select(e => e.AtmosphereScatter).ToArray());
        liquidData.SetData(MaterialDictionary.Select(e => e.liquidData).ToArray());

        Texture2DArray textures = GenerateTextureArray(MaterialDictionary.Select(x => x.texture).ToArray());
        Shader.SetGlobalTexture("_Textures", textures); 
        Shader.SetGlobalBuffer("_MatTerrainData", terrainData);
        Shader.SetGlobalBuffer("_MatAtmosphericData", atmosphericData);
        Shader.SetGlobalBuffer("_MatLiquidData", liquidData);

        Shader.SetGlobalTexture("_LiquidFineWave", liquidFineWave);
        Shader.SetGlobalTexture("_LiquidCoarseWave", liquidCoarseWave);
    }
    public void OnDisable()
    {
        terrainData?.Release();
        atmosphericData?.Release();
        liquidData?.Release();
    }

    public Texture2DArray GenerateTextureArray(Texture2D[] textures)
    {   
        textureArray = new Texture2DArray(textureSize, textureSize, textures.Length, textureFormat, true);
        for(int i = 0; i < textures.Length; i++)
        {
            textureArray.SetPixels32(textures[i].GetPixels32(), i);
        }
        textureArray.Apply();
        return textureArray;
    }


    public void OnEnable()
    {
        ApplyToMaterial();
    }
}
