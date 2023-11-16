using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(menuName = "Generation/MaterialData")]
public class MaterialData : UpdatableData
{
    public string Name;
    public Color color;
    public Texture2D texture;
    public float textureScale;
    [Range(0,1)]
    public float baseColorStrength;
}

[System.Serializable]
public class SpecialShaderData
{
    public ShaderSettings settings;
    public SpecialShader shader;
    public int materialIndex;
    public int detailLevel;
    [Range(0,1)]
    public float cuttoffThreshold;
    public bool replaceOriginal;

    //Use this to copy shader data when instantiating the shader
    public SpecialShaderData(SpecialShaderData originalShaderData)
    {
        this.settings = originalShaderData.settings;
        this.materialIndex = originalShaderData.materialIndex;
        this.detailLevel = originalShaderData.detailLevel;
    }
}