using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

[CreateAssetMenu(menuName = "Generation/NoiseData")]
public class NoiseData : UpdatableData
{
    public float noiseScale;
    public int octaves;
    [Range(0, 1)]
    public float persistance;
    public float lacunarity;
    public float lerpScale;
    public int seed = 0;
    [HideInInspector]
    public Vector4[] splinePoints;

    [SerializeField]
    private AnimationCurve interpolation;

    protected override void OnValidate()
    {
        if (lacunarity < 1) lacunarity = 1;
        if (octaves < 0) octaves = 0;

        splinePoints = interpolation.keys.Select(e => new Vector4(e.time, e.value, e.inTangent, e.outTangent)).ToArray();

        base.OnValidate();
    }
}
