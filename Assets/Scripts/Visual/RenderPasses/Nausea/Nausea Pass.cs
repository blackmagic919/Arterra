using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Arterra.Engine.Rendering {
    public class NauseaPass : ScriptableRenderPass {
        const string ProfilerTag = "Nausea Pass";

        static Material material;
        static bool initialized;
        static bool active;
        static float requestedStrength;
        static float currentStrength;
        static float holdUntilTime;

        static readonly int StrengthID = Shader.PropertyToID("_Strength");
        static readonly int NoiseScaleID = Shader.PropertyToID("_NoiseScale");
        static readonly int ScrollSpeedID = Shader.PropertyToID("_ScrollSpeed");
        static readonly int EdgePaddingID = Shader.PropertyToID("_EdgePadding");
        static readonly int EdgeFeatherID = Shader.PropertyToID("_EdgeFeather");
        static readonly int BlitTextureTexelSizeID = Shader.PropertyToID("_BlitTexture_TexelSize");

        const float StrengthSmooth = 10f;

        public NauseaPass(NauseaFeature.PassSettings passSettings) {
            renderPassEvent = passSettings.renderPassEvent;
            ConfigureInput(ScriptableRenderPassInput.Color);
            initialized = false;
        }

        public static void Initialize() {
            if (material == null) material = CoreUtils.CreateEngineMaterial("Hidden/NauseaOverlay");
            requestedStrength = 0f;
            currentStrength = 0f;
            holdUntilTime = 0f;
            active = false;
            initialized = true;
        }

        public static void Release() {
            if (material != null) UnityEngine.Object.Destroy(material);
            material = null;
            requestedStrength = 0f;
            currentStrength = 0f;
            holdUntilTime = 0f;
            active = false;
            initialized = false;
        }

        public static void SetActive(bool isActive) {
            active = isActive;
            if (active) return;

            requestedStrength = 0f;
            currentStrength = 0f;
            holdUntilTime = 0f;
        }

        public static bool IsActive() {
            return initialized && material != null && active;
        }

        public static void SetNausea(float strength, float holdTime = 0.1f) {
            if (!initialized || material == null) return;
            active = true;
            strength = Mathf.Clamp01(strength);
            requestedStrength = Mathf.Max(requestedStrength, strength);
            holdUntilTime = Mathf.Max(holdUntilTime, Time.unscaledTime + Mathf.Max(holdTime, 0f));
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
            if (!IsActive()) return;

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (cameraData.camera != Camera.main) return;
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer) return;

            if (Time.unscaledTime > holdUntilTime) requestedStrength = 0f;

            float t = 1f - Mathf.Exp(-StrengthSmooth * Mathf.Max(Time.unscaledDeltaTime, 0f));
            currentStrength = Mathf.Lerp(currentStrength, requestedStrength, t);
            if (currentStrength < 0.0005f) return;

            RenderTextureDescriptor descriptor = cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;

            material.SetFloat(StrengthID, currentStrength);
            material.SetFloat(NoiseScaleID, Mathf.Lerp(3f, 10f, currentStrength));
            material.SetFloat(ScrollSpeedID, Mathf.Lerp(0.25f, 1.8f, currentStrength));
            material.SetFloat(EdgePaddingID, Mathf.Lerp(0.03f, 0.12f, currentStrength));
            material.SetFloat(EdgeFeatherID, 0.08f);
            material.SetVector(BlitTextureTexelSizeID, new Vector4(1f / descriptor.width, 1f / descriptor.height, descriptor.width, descriptor.height));

            TextureHandle source = resourceData.activeColorTexture;
            TextureHandle destination = UniversalRenderer.CreateRenderGraphTexture(renderGraph, descriptor, "Nausea Temporary", false);
            RenderGraphUtils.AddBlitPass(renderGraph, new RenderGraphUtils.BlitMaterialParameters(source, destination, material, 0), ProfilerTag);
            resourceData.cameraColor = destination;
        }
    }
}