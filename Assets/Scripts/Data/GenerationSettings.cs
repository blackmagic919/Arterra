using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(menuName = "Generation/Settings Wrapper")]
public class GenerationSettings : ScriptableObject
{
    [Range(0, 1)]
    public float IsoLevel;
    public LODInfo[] detailLevels;

    private void OnEnable()
    {
        IsoLevel = Mathf.Clamp(IsoLevel, 0.00001f, 0.99999f);
    }
}
