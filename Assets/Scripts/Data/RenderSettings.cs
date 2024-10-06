using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(menuName = "Generation/Settings Wrapper")]
public class RenderSettings : ScriptableObject
{

    [UISetting(Alias = "Render Distances")]
    public Option<List<LODInfo>> detailLevels;
    [UISetting(Alias = "Update Frequency")]
    public float chunkUpdateThresh = 24f;
    [UISetting(Alias = "Terrain Scale")]
    public float lerpScale = 2f;
    [Range(0, 1)]
    public float IsoLevel;
    [UISetting(Ignore = true)]
    [Range(0, 64)]
    public int mapChunkSize = 64; //Number of cubes; Please don't change

    private void OnEnable()
    {
        IsoLevel = Mathf.Clamp(IsoLevel, 0.00001f, 0.99999f);
    }
}
