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
        public int inScatterPoints = 10;

        public GenerationSettings generationSettings;
    }

    public void OnValidate()
    {
        passSettings.inScatterPoints = Mathf.Max(2, passSettings.inScatterPoints);
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