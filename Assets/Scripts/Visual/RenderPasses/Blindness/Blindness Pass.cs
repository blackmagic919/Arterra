using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Arterra.Engine.Rendering {
#pragma warning disable CS1591
    public class BlindnessPass : ScriptableRenderPass {
#pragma warning disable 0618
        const string ProfilerTag = "Blindness Pass";

        static RTHandle temporaryBuffer;
        static RTHandle colorBuffer;
        static RTHandle depthBuffer;
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

        const float StrengthSmooth = 10f;

        public BlindnessPass(BlindnessFeature.PassSettings passSettings) {
            renderPassEvent = passSettings.renderPassEvent;
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

        [Obsolete]
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) {
            if (!IsActive()) return;

            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;

            colorBuffer = renderingData.cameraData.renderer.cameraColorTargetHandle;
            depthBuffer = renderingData.cameraData.renderer.cameraDepthTargetHandle;
            temporaryBuffer = RTHandles.Alloc(descriptor, FilterMode.Bilinear);
        }

        [Obsolete]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
            if (!IsActive()) return;
            if (renderingData.cameraData.camera != Camera.main) return;

            if (Time.unscaledTime > holdUntilTime) {
                requestedStrength = 0f;
            }

            float dt = Mathf.Max(Time.unscaledDeltaTime, 0f);
            float t = 1f - Mathf.Exp(-StrengthSmooth * dt);
            currentStrength = Mathf.Lerp(currentStrength, requestedStrength, t);
            currentDepthStart = Mathf.Lerp(currentDepthStart, requestedDepthStart, t);
            currentDepthEnd = Mathf.Lerp(currentDepthEnd, requestedDepthEnd, t);

            if (currentStrength < 0.0005f) return;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, new ProfilingSampler(ProfilerTag))) {
                cmd.SetGlobalTexture("_CameraDepthTexture", depthBuffer);

                material.SetFloat(StrengthID, currentStrength);
                material.SetFloat(DepthStartID, currentDepthStart);
                material.SetFloat(DepthEndID, currentDepthEnd);
                material.SetFloat(MaxBlurPixelsID, Mathf.Lerp(4f, 24f, currentStrength));
                material.SetInt(KernelRadiusID, Mathf.RoundToInt(Mathf.Lerp(2f, 6f, currentStrength)));

                cmd.Blit(colorBuffer.rt, temporaryBuffer.rt, material, 0);
                cmd.Blit(temporaryBuffer.rt, colorBuffer.rt);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd) {
            if (cmd == null) throw new ArgumentNullException(nameof(cmd));
            temporaryBuffer?.Release();
        }
    }
}
