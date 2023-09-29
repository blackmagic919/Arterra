using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(menuName = "Generation/MaterialData")]
public class MaterialData : UpdatableData
{
    public string Name;
    public Color color;
    public int textureInd;
    public float textureScale;
    [Range(0,1)]
    public float baseColorStrength;
}