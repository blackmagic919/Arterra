using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Arterra.Engine.Rendering {
    public class DizzinessPass : ScriptableRenderPass {
#pragma warning disable 0618
        const string ProfilerTag = "Dizziness Pass";

        static RTHandle temporaryBuffer;
        static RTHandle colorBuffer;
        static RTHandle[] historyBuffers;
        static float[] historyTimes;
        static int historyWriteIndex;
        static int historyCount;
        static int historyCapacity;
        static Material material;

        static bool initialized;
        static bool active;

        static float requestedStrength;
        static float currentStrength;
        static float holdUntilTime;
        static bool historyReady;
        static int historyWidth;
        static int historyHeight;
        static float smoothedDeltaTime;
        static float lastCaptureTime;

        static readonly int StrengthID = Shader.PropertyToID("_Strength");
        static readonly int History1TexID = Shader.PropertyToID("_History1Tex");
        static readonly int History2TexID = Shader.PropertyToID("_History2Tex");
        static readonly int HistoryWeight1ID = Shader.PropertyToID("_HistoryWeight1");
        static readonly int HistoryWeight2ID = Shader.PropertyToID("_HistoryWeight2");

        const float StrengthSmooth = 10f;
        const float DelaySeconds1 = 0.25f;
        const float DelaySeconds2 = 0.75f;
        const float MaxHistorySeconds = 1f;
        const float HistoryCaptureHz = 15f;
        const int HistoryDownsample = 4;
        const int MinHistoryFrames = 8;
        const int MaxHistoryFrames = 120;

        public DizzinessPass(DizzinessFeature.PassSettings passSettings) {
            renderPassEvent = passSettings.renderPassEvent;
            initialized = false;
        }

        public static void Initialize() {
            if (material == null) material = CoreUtils.CreateEngineMaterial("Hidden/DizzinessOverlay");
            requestedStrength = 0f;
            currentStrength = 0f;
            holdUntilTime = 0f;
            active = false;
            historyReady = false;
            historyWidth = 0;
            historyHeight = 0;
            smoothedDeltaTime = 1f / 60f;
            historyWriteIndex = 0;
            historyCount = 0;
            historyCapacity = 0;
            lastCaptureTime = float.NegativeInfinity;
            initialized = true;
        }

        public static void Release() {
            if (material != null) UnityEngine.Object.Destroy(material);
            material = null;
            ReleaseHistoryBuffers();
            requestedStrength = 0f;
            currentStrength = 0f;
            holdUntilTime = 0f;
            active = false;
            historyReady = false;
            historyWidth = 0;
            historyHeight = 0;
            smoothedDeltaTime = 1f / 60f;
            lastCaptureTime = float.NegativeInfinity;
            initialized = false;
        }

        public static void SetActive(bool isActive) {
            active = isActive;
            if (active) return;

            requestedStrength = 0f;
            currentStrength = 0f;
            holdUntilTime = 0f;
            historyReady = false;
            historyWriteIndex = 0;
            historyCount = 0;
            lastCaptureTime = float.NegativeInfinity;
        }

        public static bool IsActive() {
            return initialized && material != null && active;
        }

        public static void SetDizziness(float strength, float holdTime = 0.1f) {
            if (!initialized || material == null) return;
            active = true;
            strength = Mathf.Clamp01(strength);
            requestedStrength = Mathf.Max(requestedStrength, strength);
            holdUntilTime = Mathf.Max(holdUntilTime, Time.unscaledTime + Mathf.Max(holdTime, 0f));
        }

        [Obsolete]
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) {
            if (!IsActive()) return;
            if (renderingData.cameraData.camera != Camera.main) return;

            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;

            colorBuffer = renderingData.cameraData.renderer.cameraColorTargetHandle;
            temporaryBuffer = RTHandles.Alloc(descriptor, FilterMode.Bilinear);

            EnsureHistoryBuffers(descriptor);
        }

        [Obsolete]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
            if (!IsActive()) return;
            if (renderingData.cameraData.camera != Camera.main) return;

            if (Time.unscaledTime > holdUntilTime) {
                requestedStrength = 0f;
            }

            float dt = Mathf.Max(Time.unscaledDeltaTime, 1f / 240f);
            smoothedDeltaTime = Mathf.Lerp(smoothedDeltaTime, dt, 0.1f);

            float t = 1f - Mathf.Exp(-StrengthSmooth * Mathf.Max(Time.unscaledDeltaTime, 0f));
            currentStrength = Mathf.Lerp(currentStrength, requestedStrength, t);

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, new ProfilingSampler(ProfilerTag))) {
                CaptureCurrentFrame(cmd);

                if (currentStrength >= 0.0005f) {
                    float w1 = Mathf.Lerp(0.08f, 1.65f, currentStrength);
                    float w2 = Mathf.Lerp(0.04f, 0.75f, currentStrength);
                    RTHandle delayed1 = GetDelayedHistory(DelaySeconds1);
                    RTHandle delayed2 = GetDelayedHistory(DelaySeconds2);

                    if (delayed1 == null) delayed1 = colorBuffer;
                    if (delayed2 == null) delayed2 = delayed1;

                    material.SetFloat(StrengthID, currentStrength);
                    material.SetTexture(History1TexID, delayed1);
                    material.SetTexture(History2TexID, delayed2);
                    material.SetFloat(HistoryWeight1ID, w1);
                    material.SetFloat(HistoryWeight2ID, w2);

                    cmd.Blit(colorBuffer.rt, temporaryBuffer.rt, material, 0);
                    cmd.Blit(temporaryBuffer.rt, colorBuffer.rt);
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd) {
            if (cmd == null) throw new ArgumentNullException(nameof(cmd));
            temporaryBuffer?.Release();
            temporaryBuffer = null;
        }

        static void EnsureHistoryBuffers(RenderTextureDescriptor descriptor) {
            float captureInterval = 1f / Mathf.Max(HistoryCaptureHz, 1f);
            int targetCapacity = Mathf.Clamp(Mathf.CeilToInt(MaxHistorySeconds / captureInterval) + 2, MinHistoryFrames, MaxHistoryFrames);
            descriptor.width = Mathf.Max(1, descriptor.width / Mathf.Max(HistoryDownsample, 1));
            descriptor.height = Mathf.Max(1, descriptor.height / Mathf.Max(HistoryDownsample, 1));
            bool sizeChanged = historyWidth != descriptor.width || historyHeight != descriptor.height;
            bool countChanged = historyBuffers == null || targetCapacity != historyCapacity;

            if (!sizeChanged && !countChanged) return;

            ReleaseHistoryBuffers();

            historyBuffers = new RTHandle[targetCapacity];
            historyTimes = new float[targetCapacity];
            for (int i = 0; i < targetCapacity; i++) {
                historyBuffers[i] = RTHandles.Alloc(descriptor, FilterMode.Bilinear);
                historyTimes[i] = float.NegativeInfinity;
            }

            historyCapacity = targetCapacity;
            historyWidth = descriptor.width;
            historyHeight = descriptor.height;
            historyReady = false;
            historyWriteIndex = 0;
            historyCount = 0;
            lastCaptureTime = float.NegativeInfinity;
        }

        static void ReleaseHistoryBuffers() {
            if (historyBuffers != null) {
                for (int i = 0; i < historyBuffers.Length; i++) {
                    historyBuffers[i]?.Release();
                }
            }

            historyBuffers = null;
            historyTimes = null;
            historyCapacity = 0;
            historyWriteIndex = 0;
            historyCount = 0;
            historyReady = false;
            lastCaptureTime = float.NegativeInfinity;
        }

        static void CaptureCurrentFrame(CommandBuffer cmd) {
            if (historyBuffers == null || historyCapacity == 0) return;

            float now = Time.unscaledTime;
            float captureInterval = 1f / Mathf.Max(HistoryCaptureHz, 1f);
            bool shouldCapture = historyCount == 0 || (now - lastCaptureTime) >= captureInterval;
            if (!shouldCapture) return;

            RTHandle writeBuffer = historyBuffers[historyWriteIndex];
            cmd.Blit(colorBuffer.rt, writeBuffer.rt);
            historyTimes[historyWriteIndex] = now;

            historyWriteIndex = (historyWriteIndex + 1) % historyCapacity;
            historyCount = Mathf.Min(historyCount + 1, historyCapacity);
            historyReady = historyCount > 0;
            lastCaptureTime = now;
        }

        static RTHandle GetDelayedHistory(float delaySeconds) {
            if (!historyReady || historyBuffers == null || historyCount == 0) return null;

            float targetTime = Time.unscaledTime - Mathf.Max(delaySeconds, 0f);
            int latestIndex = (historyWriteIndex - 1 + historyCapacity) % historyCapacity;
            int bestIndex = latestIndex;

            // Walk backwards through valid samples and pick the first frame at or before targetTime.
            for (int i = 0; i < historyCount; i++) {
                int idx = (latestIndex - i + historyCapacity) % historyCapacity;
                if (historyTimes[idx] <= targetTime) {
                    bestIndex = idx;
                    return historyBuffers[bestIndex];
                }
                bestIndex = idx;
            }

            // Not enough history yet: return oldest available sample.
            return historyBuffers[bestIndex];
        }
    }
}
