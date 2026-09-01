// ScriptableRenderPass template created for URP 12 and Unity 2021.2
// Made by Alexander Ameye
// https://alexanderameye.github.io/

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.GamePlay;

namespace Arterra.Engine.Rendering
{
    public class AtmospherePass : ScriptableRenderPass
    {
#pragma warning disable 0618
        const string ProfilerTag = "Atmosphere Pass";

        static AtmosphereBake AtmosphereSettings;
        static Material material;
        static bool initialized = false;
        static CopyDepthPass depthCopyPass;

        private class PassData
        {
            public AtmosphereBake atmosphereSettings;
            public Vector4 lightDirection;
            public Vector4 lightColor;
        }

        public AtmospherePass(AtmosphereFeature.PassSettings passSettings)
        {
            renderPassEvent = passSettings.renderPassEvent;
            ConfigureInput(ScriptableRenderPassInput.Color);
            initialized = false;
        }

        public static void Initialize()
        {
            if (depthCopyPass == null)
            {
                Shader copyDepthShader = Shader.Find("Hidden/Universal Render Pipeline/CopyDepth");
                if (copyDepthShader == null)
                {
                    Debug.LogError("AtmospherePass: URP CopyDepth shader was not found.");
                    return;
                }

                depthCopyPass = new CopyDepthPass(RenderPassEvent.AfterRenderingTransparents, copyDepthShader, customPassName: "Atmosphere Depth Copy");
            }

            if (material == null) material = CoreUtils.CreateEngineMaterial("Hidden/Fog");

            Arterra.Configuration.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain.value;
            float atmosphereRadius = rSettings.lerpScale * rSettings.mapChunkSize * GPUMapManager.numChunksRadius;
            AtmosphereSettings = new AtmosphereBake(atmosphereRadius);

            material.SetFloat("_AtmosphereRadius", atmosphereRadius);
            material.SetInt("_NumInScatterPoints", AtmosphereSettings.NumInScatterPoints);
            GPUMapManager.SetDensitySampleData(material);
            AtmosphereSettings.SetBakedData(material);
            initialized = true;
        }

        public static void Release()
        {
            if (material != null) UnityEngine.Object.Destroy(material);
            material = null;
            depthCopyPass?.Dispose();
            depthCopyPass = null;
            AtmosphereSettings?.ReleaseData();
            initialized = false;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!initialized || Camera.main == null) return;

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (cameraData.camera != Camera.main) return;
            if (!GPUMapManager.initialized || !AtmosphereSettings.initialized) return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer) return;

            RenderTextureDescriptor descriptor = cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;

            TextureHandle colorTexture = resourceData.activeColorTexture;
            TextureHandle activeDepthTexture = resourceData.activeDepthTexture;
            if (!activeDepthTexture.IsValid()) return;
            RenderTextureDescriptor depthDescriptor = cameraData.cameraTargetDescriptor;
            depthDescriptor.depthBufferBits = 0;
            depthDescriptor.depthStencilFormat = GraphicsFormat.None;
            depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
            depthDescriptor.msaaSamples = 1;
            TextureHandle depthTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, depthDescriptor, "Atmosphere Depth After Transparents", false);
            TextureHandle temporaryTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, descriptor, "Atmosphere Temporary", false);
            depthCopyPass.Render(renderGraph, frameData, depthTexture, activeDepthTexture, true, "Atmosphere Depth Copy");

            using (var builder = renderGraph.AddUnsafePass<PassData>(ProfilerTag, out var passData))
            {
                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                passData.atmosphereSettings = AtmosphereSettings;
                if (lightData.mainLightIndex >= 0 && lightData.mainLightIndex < lightData.visibleLights.Length)
                {
                    var mainLight = lightData.visibleLights[lightData.mainLightIndex];
                    passData.lightDirection = -mainLight.localToWorldMatrix.GetColumn(2);
                    passData.lightColor = mainLight.finalColor;
                }

                builder.UseTexture(colorTexture, AccessFlags.Read);
                builder.UseTexture(depthTexture, AccessFlags.Read);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    SetGlobalLightProperties(cmd, data);
                    data.atmosphereSettings.Execute(cmd);
                });
            }

            RenderGraphUtils.AddBlitPass(renderGraph, new RenderGraphUtils.BlitMaterialParameters(colorTexture, temporaryTexture, material, 0), "Atmosphere Fog");
            resourceData.cameraColor = temporaryTexture;
        }

        private static void SetGlobalLightProperties(CommandBuffer cmd, PassData passData)
        {
            cmd.SetGlobalVector("_LightDirection", passData.lightDirection);
            cmd.SetGlobalVector("_MainLightColor", passData.lightColor);
            cmd.SetGlobalVector("_MainLightPosition", passData.lightDirection);
        }
    }
}