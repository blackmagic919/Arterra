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
        // Where/when the render pass should be injected during the rendering process.
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;

        // Used for any potential down-sampling we will do in the pass.
        [Range(1, 4)] public int downsample = 1;
        
        public Vector3 scatteringCoeffs = new Vector3(40, 60, 80);
        public float atmosphereRadius = 10;
        public float planetRadius = 5;
        public float surfaceOffset = 5;
        public float densityFalloffFactor = 1;
        public float extinctionFactor = 1;
        public int inScatterPoints = 10;
        public int opticalDepthPoints = 5;
        public ComputeShader RaySetupCompute;
        public ComputeShader InScatteringCompute;

        [HideInInspector]
        public Vector3 planetCenter
        {//
            get
            {
                Vector3 viewerPos = Camera.current.transform.position;
                viewerPos.y = -planetRadius;
                return viewerPos;
            }
        }
    }

    public void OnValidate()
    {
        passSettings.inScatterPoints = Mathf.Max(2, passSettings.inScatterPoints);
        passSettings.opticalDepthPoints = Mathf.Max(2, passSettings.opticalDepthPoints);
    }


    // References to our pass and its settings.
    TemplatePass pass;
    public PassSettings passSettings = new();


    // Gets called every time serialization happens.
    // Gets called when you enable/disable the renderer feature.
    // Gets called when you change a property in the inspector of the renderer feature.
    public override void Create()
    {//
        pass = new TemplatePass(passSettings);
    }
    //
    // Injects one or multiple render passes in the renderer.
    // Gets called when setting up the renderer, once per-camera.
    // Gets called every frame, once per-camera.
    // Will not be called if the renderer feature is disabled in the renderer inspector.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // Here you can queue up multiple passes after each other.
        renderer.EnqueuePass(pass);
    }
}