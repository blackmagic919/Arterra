using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Arterra.Engine.Rendering {
#pragma warning disable CS1591
    public class BlindnessPass : ScriptableRenderPass {
#pragma warning disable 0618
        const string ProfilerTag = "Blindness Pass";

        static Material material;

        static bool initialized;
        static bool active;

        static float requestedStrength;
        static float currentStrength;
        static float requestedDepthStart;
        static float requestedDepthEnd;
        static float currentDepthStart;
        static float currentDepthEnd;
        static float holdUntilTime;

        static readonly int StrengthID = Shader.PropertyToID("_Strength");
        static readonly int DepthStartID = Shader.PropertyToID("_DepthStart");
        static readonly int DepthEndID = Shader.PropertyToID("_DepthEnd");
        static readonly int MaxBlurPixelsID = Shader.PropertyToID("_MaxBlurPixels");
        static readonly int KernelRadiusID = Shader.PropertyToID("_KernelRadius");
        static readonly int BlitTextureTexelSizeID = Shader.PropertyToID("_BlitTexture_TexelSize");

        const float StrengthSmooth = 10f;

        private class PassData {
            public TextureHandle source;
            public Material material;
        }

        public BlindnessPass(BlindnessFeature.PassSettings passSettings) {
            renderPassEvent = passSettings.renderPassEvent;
            ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);
            initialized = false;
        }

        public static void Initialize() {
            if (material == null) material = CoreUtils.CreateEngineMaterial("Hidden/BlindnessOverlay");
            requestedStrength = 0f;
            currentStrength = 0f;
            requestedDepthStart = 1.5f;
            requestedDepthEnd = 14f;
            currentDepthStart = requestedDepthStart;
            currentDepthEnd = requestedDepthEnd;
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

        public static void SetBlindness(float strength, float depthStart = 1.5f, float depthEnd = 14f, float holdTime = 0.1f) {
            if (!initialized || material == null) return;
            active = true;
            strength = Mathf.Clamp01(strength);
            requestedStrength = Mathf.Max(requestedStrength, strength);
            requestedDepthStart = Mathf.Max(depthStart, 0.01f);
            requestedDepthEnd = Mathf.Max(depthEnd, requestedDepthStart + 0.01f);
            holdUntilTime = Mathf.Max(holdUntilTime, Time.unscaledTime + Mathf.Max(holdTime, 0f));
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
            if (!IsActive()) return;
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (cameraData.camera != Camera.main) return;
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer) return;

            TextureHandle depthTexture = resourceData.cameraDepthTexture;
            if (!depthTexture.IsValid()) return;

            RenderTextureDescriptor descriptor = cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            TextureHandle source = resourceData.activeColorTexture;
            TextureHandle destination = UniversalRenderer.CreateRenderGraphTexture(renderGraph, descriptor, "Blindness Temporary", false);

            if (Time.unscaledTime > holdUntilTime) {
                requestedStrength = 0f;
            }

            float dt = Mathf.Max(Time.unscaledDeltaTime, 0f);
            float t = 1f - Mathf.Exp(-StrengthSmooth * dt);
            currentStrength = Mathf.Lerp(currentStrength, requestedStrength, t);
            currentDepthStart = Mathf.Lerp(currentDepthStart, requestedDepthStart, t);
            currentDepthEnd = Mathf.Lerp(currentDepthEnd, requestedDepthEnd, t);

            if (currentStrength < 0.0005f) return;

            material.SetFloat(StrengthID, currentStrength);
            material.SetFloat(DepthStartID, currentDepthStart);
            material.SetFloat(DepthEndID, currentDepthEnd);
            material.SetFloat(MaxBlurPixelsID, Mathf.Lerp(4f, 24f, currentStrength));
            material.SetInt(KernelRadiusID, Mathf.RoundToInt(Mathf.Lerp(2f, 6f, currentStrength)));
            material.SetVector(BlitTextureTexelSizeID, new Vector4(1f / descriptor.width, 1f / descriptor.height, descriptor.width, descriptor.height));

            using (var builder = renderGraph.AddUnsafePass<PassData>(ProfilerTag, out var passData)) {
                passData.source = source;
                passData.material = material;
                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(depthTexture, AccessFlags.Read);
                builder.UseTexture(destination, AccessFlags.Write);
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => {
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                });
            }

            resourceData.cameraColor = destination;
        }
    }
}
