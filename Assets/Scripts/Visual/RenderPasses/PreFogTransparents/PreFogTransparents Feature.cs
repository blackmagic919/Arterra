using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
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
            static readonly ShaderTagId[] FallbackShaderTags = {
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("SRPDefaultUnlit")
            };

            readonly PassSettings settings;
            ShaderTagId[] shaderTags;
            FilteringSettings filteringSettings;

            public PreFogTransparentsPass(PassSettings passSettings) {
                settings = passSettings;
                renderPassEvent = settings.renderPassEvent;
                shaderTags = BuildShaderTags();
                filteringSettings = BuildFiltering();
            }

            public void UpdateSettings() {
                renderPassEvent = settings.renderPassEvent;
                shaderTags = BuildShaderTags();
                filteringSettings = BuildFiltering();
            }

            ShaderTagId[] BuildShaderTags() {
                if (!string.IsNullOrWhiteSpace(settings.primaryShaderPassTag)) {
                    return new[] { new ShaderTagId(settings.primaryShaderPassTag.Trim()) };
                }

                return FallbackShaderTags;
            }

            FilteringSettings BuildFiltering() {
                RenderQueueRange range = settings.drawTransparentQueueOnly ? RenderQueueRange.transparent : RenderQueueRange.all;
                int layerMask = settings.useLayerMask ? settings.preFogTransparentsLayerMask.value : ~0;
                return new FilteringSettings(range, layerMask);
            }

            [Obsolete]
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
                if (settings.useLayerMask && settings.preFogTransparentsLayerMask.value == 0) return;
                if (shaderTags.Length == 0) return;

                SortingCriteria sort = settings.drawTransparentQueueOnly
                    ? SortingCriteria.CommonTransparent
                    : renderingData.cameraData.defaultOpaqueSortFlags;

                var rendererListDesc = new RendererListDesc(shaderTags, renderingData.cullResults, renderingData.cameraData.camera) {
                    sortingCriteria = sort,
                    renderQueueRange = filteringSettings.renderQueueRange,
                    layerMask = filteringSettings.layerMask,
                    overrideMaterial = settings.overrideMaterial,
                    overrideMaterialPassIndex = settings.overrideMaterialPassIndex ? Mathf.Max(settings.materialPassIndex, 0) : 0
                };

                CommandBuffer cmd = CommandBufferPool.Get("PreFogTransparents Pass");
                RendererList rendererList = context.CreateRendererList(rendererListDesc);
                cmd.DrawRendererList(rendererList);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
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
