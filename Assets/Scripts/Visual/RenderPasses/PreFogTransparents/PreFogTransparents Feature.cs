using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Arterra.Engine.Rendering {
    public class PreFogTransparentsFeature : ScriptableRendererFeature {
        [Serializable]
        public class PassSettings {
            public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            public bool useLayerMask = false;
            public LayerMask preFogTransparentsLayerMask = ~0;
            public bool drawTransparentQueueOnly = true;
            public string primaryShaderPassTag = "PreFogTransparents";
            public bool overrideMaterialPassIndex = false;
            public int materialPassIndex = 0;
            public Material overrideMaterial = null;
        }

        class PreFogTransparentsPass : ScriptableRenderPass {
            static readonly List<ShaderTagId> FallbackShaderTags = new() {
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("SRPDefaultUnlit")
            };

            readonly PassSettings settings;
            List<ShaderTagId> shaderTags;
            FilteringSettings filteringSettings;

            private class PassData {
                public RendererListHandle rendererList;
            }

            public PreFogTransparentsPass(PassSettings passSettings) {
                settings = passSettings;
                renderPassEvent = settings.renderPassEvent;
                ConfigureInput(ScriptableRenderPassInput.Depth);
                shaderTags = BuildShaderTags();
                filteringSettings = BuildFiltering();
            }

            public void UpdateSettings() {
                renderPassEvent = settings.renderPassEvent;
                shaderTags = BuildShaderTags();
                filteringSettings = BuildFiltering();
            }

            List<ShaderTagId> BuildShaderTags() {
                if (!string.IsNullOrWhiteSpace(settings.primaryShaderPassTag)) {
                    return new List<ShaderTagId> { new ShaderTagId(settings.primaryShaderPassTag.Trim()) };
                }

                return FallbackShaderTags;
            }

            FilteringSettings BuildFiltering() {
                RenderQueueRange range = settings.drawTransparentQueueOnly ? RenderQueueRange.transparent : RenderQueueRange.all;
                int layerMask = settings.useLayerMask ? settings.preFogTransparentsLayerMask.value : ~0;
                return new FilteringSettings(range, layerMask);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
                if (settings.useLayerMask && settings.preFogTransparentsLayerMask.value == 0) return;
                if (shaderTags.Count == 0) return;

                SortingCriteria sort = settings.drawTransparentQueueOnly
                    ? SortingCriteria.CommonTransparent
                    : frameData.Get<UniversalCameraData>().defaultOpaqueSortFlags;

                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(shaderTags, renderingData, cameraData, lightData, sort);
                drawingSettings.overrideMaterial = settings.overrideMaterial;
                drawingSettings.overrideMaterialPassIndex = settings.overrideMaterialPassIndex ? Mathf.Max(settings.materialPassIndex, 0) : 0;
                RendererListParams rendererListParams = new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("PreFogTransparents Pass", out var passData)) {
                    builder.UseAllGlobalTextures(true);
                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
                    passData.rendererList = renderGraph.CreateRendererList(rendererListParams);
                    builder.UseRendererList(passData.rendererList);
                    builder.SetRenderFunc((PassData data, RasterGraphContext context) => context.cmd.DrawRendererList(data.rendererList));
                }
            }
        }

        public PassSettings passSettings = new();
        PreFogTransparentsPass pass;

        public override void Create() {
            pass = new PreFogTransparentsPass(passSettings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
            pass.UpdateSettings();
            renderer.EnqueuePass(pass);
        }
    }
}
