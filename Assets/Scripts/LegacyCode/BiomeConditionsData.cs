using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Generation/Biome Conditions")]
public class BiomeConditionsData : ScriptableObject
{
    public int biome;

    [Space(10)]
    [Range(0, 1)]
    public float TerrainStart;
    [Range(0, 1)]
    public float TerrainEnd;
    
    [Space(10)]
    [Range(0, 1)]
    public float ContinentalStart;
    [Range(0, 1)]
    public float ContinentalEnd;//

    [Range(0, 1)]
    public float ErosionStart;
    [Range(0, 1)]
    public float ErosionEnd;

    [Space(10)]
    [Range(0, 1)]
    public float SquashStart;
    [Range(0, 1)]
    public float SquashEnd;

    [Space(10)]
    [Range(0, 1)]
    public float AtmosphereStart;
    [Range(0, 1)]
    public float AtmosphereEnd;

    [Space(10)]
    [Range(0, 1)]
    public float HumidStart;
    [Range(0, 1)]
    public float HumidEnd;

    private void OnValidate()
    {
        ContinentalEnd = Mathf.Max(ContinentalStart, ContinentalEnd);
        ErosionEnd = Mathf.Max(ErosionStart, ErosionEnd);
        TerrainEnd = Mathf.Max(TerrainStart, TerrainEnd);
        SquashEnd = Mathf.Max(SquashStart, SquashEnd);
        AtmosphereEnd = Mathf.Max(AtmosphereStart, AtmosphereEnd);
        HumidEnd = Mathf.Max(HumidStart, HumidEnd);
    }

}
