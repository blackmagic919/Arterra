// ScriptableRendererFeature template created for URP 12 and Unity 2021.2
// Made by Alexander Ameye 
// https://alexanderameye.github.io/

using UnityEngine;
using UnityEngine.Rendering.Universal;

public class TemplateFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class PassSettings
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;

        public GPUDensityManager densityManager;
        public AtmosphereBake luminanceBake;
        public Vector3 scatteringCoeffs = new Vector3(40, 60, 80);
        public float atmosphereRadius = 10;

        public float extinctionFactor = 1;
        public int inScatterPoints = 10;
        public int opticalDepthPoints = 5;
        public float densityMultiplier = 0.05f;

        /*
        public float planetRadius = 5;
        public float surfaceOffset = 5;
        public float densityFalloffFactor = 1;

        [HideInInspector]
        public Vector3 planetCenter
        {//
            get
            {
                Vector3 viewerPos = Camera.current.transform.position;
                viewerPos.y = -planetRadius;
                return viewerPos;
            }
        }*/
    }

    public void OnValidate()
    {
        passSettings.inScatterPoints = Mathf.Max(2, passSettings.inScatterPoints);
        passSettings.opticalDepthPoints = Mathf.Max(2, passSettings.opticalDepthPoints);
    }


    // References to our pass and its settings.
    TemplatePass pass;
    public PassSettings passSettings = new();

    public override void Create()
    {//
        pass = new TemplatePass(passSettings);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // Here you can queue up multiple passes after each other.
        renderer.EnqueuePass(pass);
    }
}