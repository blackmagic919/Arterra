using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(menuName = "Generation/Settings Wrapper")]
public class RenderSettings : ScriptableObject
{
    [Range(0, 1)]
    public float IsoLevel;
    public Option<List<LODInfo>> detailLevels;
    public int mapChunkSize = 64; //Number of cubes; Please don't change
    public float chunkUpdateThresh = 24f;
    public  float lerpScale = 2f;

    private void OnEnable()
    {
        IsoLevel = Mathf.Clamp(IsoLevel, 0.00001f, 0.99999f);
    }
}
