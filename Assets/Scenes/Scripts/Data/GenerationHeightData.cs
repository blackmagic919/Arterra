using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

[CreateAssetMenu(menuName = "Generation/GenerationHeight")]
public class GenerationHeightData : UpdatableData
{
    public List<BMaterial> Materials;

    [HideInInspector]
    public TextureData textureData = new TextureData();

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
        public MaterialData materialData;//

        public List<DensityGrad> VerticalPreference;
        public AnimationCurve generationPref;
    }
}
