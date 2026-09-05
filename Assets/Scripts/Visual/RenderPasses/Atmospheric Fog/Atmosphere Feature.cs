using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Arterra.Engine.Rendering
{
    public class AtmosphereFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class PassSettings
        {
            public RenderPassEvent deferredSetupRenderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        class AtmosphereDeferredSetupPass : ScriptableRenderPass
        {
            private class PassData
            {
                public bool enableDeferred;
                public Vector3 lightDirection;
                public Vector4 lightColor;
            }

            readonly PassSettings settings;

            public AtmosphereDeferredSetupPass(PassSettings passSettings)
            {
                settings = passSettings;
                renderPassEvent = settings.deferredSetupRenderPassEvent;
            }

            public void UpdateSettings()
            {
                renderPassEvent = settings.deferredSetupRenderPassEvent;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                using (var builder = renderGraph.AddUnsafePass<PassData>("Atmosphere Deferred Setup", out var passData))
                {
                    passData.enableDeferred = AtmospherePass.ShouldEnableDeferredForCamera(cameraData.camera);
                    passData.lightDirection = Vector3.up;
                    passData.lightColor = Color.white;
                    if (lightData.mainLightIndex >= 0 && lightData.mainLightIndex < lightData.visibleLights.Length)
                    {
                        var mainLight = lightData.visibleLights[lightData.mainLightIndex];
                        passData.lightDirection = -mainLight.localToWorldMatrix.GetColumn(2);
                        passData.lightColor = mainLight.finalColor;
                    }

                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                    {
                        CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        if (data.enableDeferred) AtmospherePass.EnableDeferredState(cmd, data.lightDirection, data.lightColor);
                        else AtmospherePass.DisableDeferredState(cmd);
                    });
                }
            }
        }


        // References to our pass and its settings.
        AtmosphereDeferredSetupPass setupPass;
        public AtmospherePass pass;
        public PassSettings passSettings = new();

        public override void Create()
        {
            setupPass = new AtmosphereDeferredSetupPass(passSettings);
            pass = new AtmospherePass(passSettings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            setupPass.UpdateSettings();
            renderer.EnqueuePass(setupPass);
            renderer.EnqueuePass(pass);
        }
    }
}