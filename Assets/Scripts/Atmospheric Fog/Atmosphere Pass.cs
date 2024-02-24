// ScriptableRenderPass template created for URP 12 and Unity 2021.2
// Made by Alexander Ameye 
// https://alexanderameye.github.io/

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class TemplatePass : ScriptableRenderPass
{
#pragma warning disable 0618
    const string ProfilerTag = "Atmosphere Pass";

    TemplateFeature.PassSettings passSettings;

    RenderTargetIdentifier temporaryBuffer;
    RTHandle colorBuffer;
    int temporaryBufferID = Shader.PropertyToID("_TemporaryBuffer");

    Material material;

    /*static readonly int PlanetRadiusProperty = Shader.PropertyToID("_PlanetRadius");
    static readonly int SurfaceOffsetProperty = Shader.PropertyToID("_SurfaceOffset");
    static readonly int DensityFalloffProperty = Shader.PropertyToID("_DensityFalloff");*/

    // It is good to cache the shader property IDs here.
    static readonly int ScatteringProperty = Shader.PropertyToID("_ScatteringCoeffs");
    static readonly int AtmosphereRadiusProperty = Shader.PropertyToID("_AtmosphereRadius");
    static readonly int GroundExtinctionProperty = Shader.PropertyToID("_GroundExtinction");
    static readonly int inScatterProperty = Shader.PropertyToID("_NumInScatterPoints");
    static readonly int opticalDepthProperty = Shader.PropertyToID("_NumOpticalDepthPoints");
    static readonly int DensityMultiplier = Shader.PropertyToID("_DensityMultiplier");

    // The constructor of the pass. Here you can set any material properties that do not need to be updated on a per-frame basis.
    public TemplatePass(TemplateFeature.PassSettings passSettings)
    {
        this.passSettings = passSettings;

        renderPassEvent = passSettings.renderPassEvent;
        
        if (material == null) material = CoreUtils.CreateEngineMaterial("Hidden/Fog");

        // Set any material properties based on our pass settings. 
        material.SetVector(ScatteringProperty, passSettings.scatteringCoeffs);
        material.SetFloat(AtmosphereRadiusProperty, passSettings.atmosphereRadius);
        material.SetFloat(GroundExtinctionProperty, passSettings.extinctionFactor);
        material.SetInt(inScatterProperty, passSettings.inScatterPoints);
        material.SetInt(opticalDepthProperty, passSettings.opticalDepthPoints);
        material.SetFloat(DensityMultiplier, passSettings.densityMultiplier);

        passSettings.densityManager.SetDensitySampleData(material);
        passSettings.luminanceBake.SetSettings(passSettings);
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;

        descriptor.depthBufferBits = 0;
        
        ConfigureInput(ScriptableRenderPassInput.Depth);

        colorBuffer = renderingData.cameraData.renderer.cameraColorTargetHandle;

        cmd.GetTemporaryRT(temporaryBufferID, descriptor, FilterMode.Bilinear);
        temporaryBuffer = new RenderTargetIdentifier(temporaryBufferID);
        
        passSettings.luminanceBake.SetBakedData(material);
    }

    // The actual execution of the pass. This is where custom rendering occurs.
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if(passSettings.densityManager.initialized && passSettings.luminanceBake.initialized){
            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, new ProfilingSampler(ProfilerTag)))
            {
                // Blit from the color buffer to a temporary buffer and back. This is needed for a two-pass shader.
                Blit(cmd, colorBuffer, temporaryBuffer, material, 0); // shader pass 0
                Blit(cmd, temporaryBuffer, colorBuffer);
            }

            // Execute the command buffer and release it.
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    // Called when the camera has finished rendering.
    // Here we release/cleanup any allocated resources that were created by this pass.
    // Gets called for all cameras i na camera stack.
    public override void OnCameraCleanup(CommandBuffer cmd)
    {
        if (cmd == null) throw new ArgumentNullException("cmd");

        // Since we created a temporary render texture in OnCameraSetup, we need to release the memory here to avoid a leak.
        cmd.ReleaseTemporaryRT(temporaryBufferID);
    }
}