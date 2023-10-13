using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Generation/GenerationHeight")]
public class GenerationHeightData : UpdatableData
{
    public List<BMaterial> Materials;

    //Will use in the future
    /*
    [System.Serializable]
    public class GrassData
    {
        public Color baseColor;
        public Color topColor;
        public Texture2D grainyNoiseTex;
        [Range(0,1)]
        public float grainyDepthScale;
        public Texture2D smoothNoiseTex;
        [Range(0, 1)]
        public float smoothDepthScale;
        public Texture2D windNoiseTex;
        public float windFrequency;
        public float windStrength;
    }*/

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
        public int materialIndex;

        public List<DensityGrad> VerticalPreference;
        public AnimationCurve generationPref;
    }
}
