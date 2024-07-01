// ScriptableRenderPass template created for URP 12 and Unity 2021.2
// Made by Alexander Ameye 
// https://alexanderameye.github.io/

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static EndlessTerrain;

public class AtmospherePass : ScriptableRenderPass
{
#pragma warning disable 0618
    const string ProfilerTag = "Atmosphere Pass";

    AtmosphereFeature.PassSettings passSettings;

    RenderTargetIdentifier temporaryBuffer;
    RTHandle colorBuffer; RTHandle depthBuffer;
    int temporaryBufferID = Shader.PropertyToID("_TemporaryBuffer");

    Material material;

    // It is good to cache the shader property IDs here.
    static readonly int AtmosphereRadiusProperty = Shader.PropertyToID("_AtmosphereRadius");
    static readonly int inScatterProperty = Shader.PropertyToID("_NumInScatterPoints");
    static bool initialized = false;

    // The constructor of the pass. Here you can set any material properties that do not need to be updated on a per-frame basis.
    public AtmospherePass(AtmosphereFeature.PassSettings passSettings)
    {
        this.passSettings = passSettings;
        renderPassEvent = passSettings.renderPassEvent;
        initialized = false;
    }

    public void Initialize(){
        if (material == null) material = CoreUtils.CreateEngineMaterial("Hidden/Fog");
        float atmosphereRadius = lerpScale * passSettings.generationSettings.detailLevels[^1].distanceThresh;
        material.SetFloat(AtmosphereRadiusProperty, atmosphereRadius);
        material.SetInt(inScatterProperty, passSettings.luminanceBake.NumInScatterPoints);
        GPUDensityManager.SetDensitySampleData(material);
        passSettings.luminanceBake.Initialize(passSettings);
        passSettings.luminanceBake.SetBakedData(material);
        initialized = true;
    }

    public void Release(){
        if(material != null) UnityEngine.Object.Destroy(material);
        passSettings.luminanceBake.ReleaseData();
        initialized = false;
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        if(!initialized) return;
        RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
        descriptor.depthBufferBits = 0;

        ConfigureInput(ScriptableRenderPassInput.Color);
        colorBuffer = renderingData.cameraData.renderer.cameraColorTargetHandle;
        //We need copy from depth buffer because transparent pass needs depth texture of opaque pass, and fog needs depth texture of transparent pass
        depthBuffer = renderingData.cameraData.renderer.cameraDepthTargetHandle; 

        cmd.GetTemporaryRT(temporaryBufferID, descriptor, FilterMode.Bilinear);
        temporaryBuffer = new RenderTargetIdentifier(temporaryBufferID);
    }

    // The actual execution of the pass. This is where custom rendering occurs.
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if(!initialized) return;
        passSettings.luminanceBake.Execute();
        if(GPUDensityManager.initialized && passSettings.luminanceBake.initialized){
            CommandBuffer cmd = CommandBufferPool.Get();
            
            using (new ProfilingScope(cmd, new ProfilingSampler(ProfilerTag)))
            {
                // Blit from the color buffer to a temporary buffer and back. This is needed for a two-pass shader.
                Blit(cmd, depthBuffer, Shader.GetGlobalTexture("_CameraDepthTexture")); //Make sure camera depth is available in shader
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