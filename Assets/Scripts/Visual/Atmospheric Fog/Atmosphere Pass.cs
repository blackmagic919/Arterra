// ScriptableRenderPass template created for URP 12 and Unity 2021.2
// Made by Alexander Ameye 
// https://alexanderameye.github.io/

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using WorldConfig;

public class AtmospherePass : ScriptableRenderPass
{
#pragma warning disable 0618
    const string ProfilerTag = "Atmosphere Pass";

    static AtmosphereBake AtmosphereSettings;

    static RTHandle temporaryBuffer;
    static RTHandle colorBuffer; static RTHandle depthBuffer;

    static Material material;

    // It is good to cache the shader property IDs here.
    static bool initialized = false;

    // The constructor of the pass. Here you can set any material properties that do not need to be updated on a per-frame basis.
    public AtmospherePass(AtmosphereFeature.PassSettings passSettings)
    {
        renderPassEvent = passSettings.renderPassEvent;
        initialized = false;
    }

    public static void Initialize(){
        if (material == null) material = CoreUtils.CreateEngineMaterial("Hidden/Fog");

        WorldConfig.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain.value;
        float atmosphereRadius = rSettings.lerpScale * rSettings.mapChunkSize * GPUDensityManager.numChunksRadius;
        AtmosphereSettings = new AtmosphereBake(atmosphereRadius);

        material.SetFloat("_AtmosphereRadius", atmosphereRadius);
        material.SetInt("_NumInScatterPoints", AtmosphereSettings.NumInScatterPoints);
        GPUDensityManager.SetDensitySampleData(material);
        AtmosphereSettings.SetBakedData(material);
        initialized = true;
    }

    public static void Release(){
        if(material != null) UnityEngine.Object.Destroy(material);
        AtmosphereSettings.ReleaseData();
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
        temporaryBuffer = RTHandles.Alloc(descriptor, FilterMode.Bilinear);
    }

    // The actual execution of the pass. This is where custom rendering occurs.
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if(!initialized) return;
        if(GPUDensityManager.initialized && AtmosphereSettings.initialized){
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, new ProfilingSampler(ProfilerTag)))
            {
                AtmosphereSettings.Execute(cmd);
                // Blit from the color buffer to a temporary buffer and back. This is needed for a two-pass shader.
                Blit(cmd, depthBuffer, Shader.GetGlobalTexture("_CameraDepthTexture")); //Make sure camera depth is available in shader
                Blit(cmd, colorBuffer, temporaryBuffer.rt, material, 0); // shader pass 0
                Blit(cmd, temporaryBuffer.rt, colorBuffer);
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
        temporaryBuffer?.Release();
    }
}