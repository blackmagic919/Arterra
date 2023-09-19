using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

[CreateAssetMenu(menuName = "Generation/GenerationHeight")]
public class GenerationHeightData : ScriptableObject
{
    public List<BMaterial> Materials;

    [System.Serializable]
    public class DensityGrad
    {
        public AnimationCurve DensityCurve;
        public int upperLimit;
        public int lowerLimit;
    }

    [System.Serializable]
    public class BMaterial
    {
        public NoiseData generationNoise;

        public List<DensityGrad> VerticalPreference;
        public AnimationCurve generationPref;

        public MaterialData mat;
    }
}
