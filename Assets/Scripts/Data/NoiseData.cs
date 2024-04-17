using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Unity.Mathematics;

[CreateAssetMenu(menuName = "Generation/NoiseData")]
public class NoiseData : ScriptableObject
{
    public float noiseScale;
    public int octaves;
    [Range(0, 1)]
    public float persistance;
    public float lacunarity;
    public int seed = 0;
    [SerializeField]
    private AnimationCurve interpolation;

    [HideInInspector]
    public Vector4[] splinePoints;
    [HideInInspector]
    public Vector3[] offsets;
    [HideInInspector]
    public float maxPossibleHeight;

    private void OnValidate()
    {
        if (lacunarity < 1) lacunarity = 1;
        if (octaves < 0) octaves = 0;

        splinePoints = interpolation.keys.Select(e => new Vector4(e.time, e.value, e.inTangent, e.outTangent)).ToArray();
        offsets = CalculateOctaveOffsets(out maxPossibleHeight);
        noiseScale = Mathf.Max(10E-9f, noiseScale);
    }

    Vector3[] CalculateOctaveOffsets(out float maxPossibleHeight){
        System.Random prng = new System.Random(seed);
        Vector3[] octaveOffsets = new Vector3[octaves]; //Vector Array is processed as float4

        maxPossibleHeight = 0;
        float amplitude = 1;
        
        for (int i = 0; i < octaves; i++)
        {
            float offsetX = prng.Next((int)-10E5, (int)10E5);
            float offsetY = prng.Next((int)-10E5, (int)10E5);
            float offsetZ = prng.Next((int)-10E5, (int)10E5);
            octaveOffsets[i] = new Vector4(offsetX, offsetY, offsetZ, 0);

            maxPossibleHeight += amplitude;
            amplitude *= persistance;
        }
        return octaveOffsets;
    }
}
