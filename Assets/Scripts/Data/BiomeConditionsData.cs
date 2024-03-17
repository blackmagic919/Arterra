using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Generation/Biome Conditions")]
public class BiomeConditionsData : ScriptableObject
{
    [Space(10)]
    [Range(0, 1)]
    public float ContinentalStart;
    [Range(0, 1)]
    public float ContinentalEnd;

    [Range(0, 1)]
    public float ErosionStart;
    [Range(0, 1)]
    public float ErosionEnd;

    [Space(10)]
    [Range(0, 1)]
    public float PVStart;
    [Range(0, 1)]
    public float PVEnd;

    [Space(10)]
    [Range(0, 1)]
    public float SquashStart;
    [Range(0, 1)]
    public float SquashEnd;

    [Space(10)]
    [Range(0, 1)]
    public float TempStart;
    [Range(0, 1)]
    public float TempEnd;

    [Space(10)]
    [Range(0, 1)]
    public float HumidStart;
    [Range(0, 1)]
    public float HumidEnd;

    private void OnValidate()
    {
        ErosionEnd = Mathf.Max(ErosionStart, ErosionEnd);
        ContinentalEnd = Mathf.Max(ContinentalStart, ContinentalEnd);
        PVEnd = Mathf.Max(PVStart, PVEnd);
        SquashEnd = Mathf.Max(SquashStart, SquashEnd);
        TempEnd = Mathf.Max(TempStart, TempEnd);
        HumidEnd = Mathf.Max(HumidStart, HumidEnd);
    }

}
