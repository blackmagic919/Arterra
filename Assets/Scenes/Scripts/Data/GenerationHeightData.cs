using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

[CreateAssetMenu(menuName = "Generation/GenerationHeight")]
public class GenerationHeightData : ScriptableObject
{
    public List<BMaterial> Materials;

    [System.Serializable]
    public class BMaterial
    {
        public NoiseData generationNoise;

        public AnimationCurve generationPreference;
        public AnimationCurve heightPreference;

        public MaterialData mat;
    }
}
