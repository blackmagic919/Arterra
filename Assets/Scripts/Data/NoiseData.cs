using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Unity.Mathematics;
using Newtonsoft.Json;

[CreateAssetMenu(menuName = "Generation/NoiseData")]
public class NoiseData : ScriptableObject
{
    public float noiseScale;
    public int octaves;
    [Range(0, 1)]
    public float persistance;
    public float lacunarity;
    public int seedOffset = 0;
    [SerializeField][UISetting(Ignore = true)][JsonIgnore]
    public Option<AnimationCurve> interpolation;

    private void OnValidate()
    {
        if (lacunarity < 1) lacunarity = 1;
        if (octaves < 0) octaves = 0;
        noiseScale = Mathf.Max(10E-9f, noiseScale);
    }

    [JsonIgnore][HideInInspector]
    public Vector4[] SplineKeys{
        get{
            return interpolation.value.keys.Select(e => new Vector4(e.time, e.value, e.inTangent, e.outTangent)).ToArray();
        }
    }
    
    [JsonIgnore][HideInInspector]
    public Vector3[] OctaveOffsets{
        get{
            System.Random prng = new System.Random(WorldOptions.CURRENT.Seed + seedOffset);
            Vector3[] octaveOffsets = new Vector3[octaves]; //Vector Array is processed as float4

            float maxPossibleHeight = 0;
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
}

