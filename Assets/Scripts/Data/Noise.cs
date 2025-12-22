using UnityEngine;
using System.Linq;
using Newtonsoft.Json;

namespace Arterra.Config.Generation {

/// <summary>
/// Noise Data contains settings which defines a distinct noise sampler function, which,
/// sampled across all space, creates a unique noise map that is processed and layered
/// together to generate the procedural terrain. 
/// <seealso href="https://blackmagic919.github.io/AboutMe/2024/07/13/Noise-Generation/"/>
/// </summary>
[CreateAssetMenu(menuName = "Generation/NoiseData")]
public class Noise : Category<Noise>
{
    /// <summary>
    /// The scale of the noise map relative to grid space; how much the noise varies as the sample point moves.
    /// A smaller noise scale will result in smaller features while (zooming out), and a larger noise scale will result
    /// in comparatively larger terrain features (zooming in).
    /// </summary>
    public float noiseScale;
    /// <summary>
    /// The amount of simple noise samples that are layered together to create the final noise map. A simple noise 
    /// sample is a single call to 3D Simplex Noise. Layering simple noise maps can create more complex noise maps
    /// with both large and small features.
    /// </summary>
    public int octaves;
    /// <summary>
    /// The influence strength of the noise output of each proceeding octave relative to the previous octave. 
    /// Persistance should be less than 1 such that the amplitude of each octave decreases creating less and less noticeable features.
    /// </summary>
    [Range(0, 1)]
    public float persistance;
    /// <summary>
    /// The inverse sample scale of each noise octave relative to the preivous octave. As lacunarity increases, the distance
    /// between samples increases, creating smaller features in the output map. Lacunarity should be greater than 1 such that
    /// the frequency(inverse scale) of each octave increases creating more smaller and smaller features.
    /// </summary>
    public float lacunarity;
    
    /// <summary>
    /// The seed offset that is added to the global seed to create a unique noise map. This allows for the generation of
    /// multiple noise maps anchored by the same global seed. Two noise maps with the same seed offset
    /// will sample an identical noise map (but at different scales/offsets).
    /// </summary>
    public int seedOffset = 0;
    /// <summary>
    /// The interpolation curve that is used to transform the sampled output of the noise function. Transforms a noise value
    /// from the range 0 to 1 to the same range. By modifying the curve, one can create localized regions of generation
    /// that conform to a specific shape or pattern. 
    /// </summary>
    [SerializeField][UISetting(Ignore = true)][JsonIgnore]
    public Option<AnimationCurve> interpolation;

    public override void OnValidate()
    {
        base.OnValidate();
        if (lacunarity < 1) lacunarity = 1;
        if (octaves < 0) octaves = 0;
        noiseScale = Mathf.Max(10E-9f, noiseScale);
    }

    /// <summary>
    /// The list of control points defining a 2D bezier curve that transforms a value from the x-axis to the y-axis.
    /// Each control point is a 4D vector describing its 2D coordinate followed by its tangent coming in and out of the point.
    /// </summary>
    [JsonIgnore][HideInInspector]
    public Vector4[] SplineKeys{
        get{
            return interpolation.value.keys.Select(e => new Vector4(e.time, e.value, e.inTangent, e.outTangent)).ToArray();
        }
    }
    
    /// <summary>
    /// The list of offsets that each octave samples noise from. Given the same seed offset and global seed, 
    /// the same list of offsets will be generated. By default offsets are 3D coordinates, 
    /// but lower dimensions may use fewer components of the vector.
    /// </summary>
    [JsonIgnore][HideInInspector]
    public Vector3[] OctaveOffsets{
        get{
            System.Random prng = new System.Random(Config.CURRENT.Seed + seedOffset);
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
}

