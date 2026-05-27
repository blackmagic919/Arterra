using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Arterra.Engine.Rendering {
    public class NauseaPass : ScriptableRenderPass {
#pragma warning disable 0618
        const string ProfilerTag = "Nausea Pass";

        static RTHandle temporaryBuffer;
        static RTHandle colorBuffer;
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

        const float StrengthSmooth = 10f;

        public NauseaPass(NauseaFeature.PassSettings passSettings) {
            renderPassEvent = passSettings.renderPassEvent;
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

        [Obsolete]
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) {
            if (!IsActive()) return;

            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;

            colorBuffer = renderingData.cameraData.renderer.cameraColorTargetHandle;
            temporaryBuffer = RTHandles.Alloc(descriptor, FilterMode.Bilinear);
        }

        [Obsolete]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
            if (!IsActive()) return;
            if (renderingData.cameraData.camera != Camera.main) return;

            if (Time.unscaledTime > holdUntilTime) {
                requestedStrength = 0f;
            }

            float t = 1f - Mathf.Exp(-StrengthSmooth * Mathf.Max(Time.unscaledDeltaTime, 0f));
            currentStrength = Mathf.Lerp(currentStrength, requestedStrength, t);

            if (currentStrength < 0.0005f) return;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, new ProfilingSampler(ProfilerTag))) {
                material.SetFloat(StrengthID, currentStrength);
                material.SetFloat(NoiseScaleID, Mathf.Lerp(3f, 10f, currentStrength));
                material.SetFloat(ScrollSpeedID, Mathf.Lerp(0.25f, 1.8f, currentStrength));
                material.SetFloat(EdgePaddingID, Mathf.Lerp(0.03f, 0.12f, currentStrength));
                material.SetFloat(EdgeFeatherID, 0.08f);

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
