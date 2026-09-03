using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Arterra.Engine.Rendering {
    public class DizzinessPass : ScriptableRenderPass {
        const string ProfilerTag = "Dizziness Pass";

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

        private class PassData {
            public TextureHandle source;
            public TextureHandle destination;
            public TextureHandle history1;
            public TextureHandle history2;
            public Material material;
        }

        public DizzinessPass(DizzinessFeature.PassSettings passSettings) {
            renderPassEvent = passSettings.renderPassEvent;
            ConfigureInput(ScriptableRenderPassInput.Color);
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

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
            if (!IsActive()) return;

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (cameraData.camera != Camera.main) return;
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer) return;

            RenderTextureDescriptor descriptor = cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            EnsureHistoryBuffers(descriptor);

            if (Time.unscaledTime > holdUntilTime) requestedStrength = 0f;

            float dt = Mathf.Max(Time.unscaledDeltaTime, 1f / 240f);
            smoothedDeltaTime = Mathf.Lerp(smoothedDeltaTime, dt, 0.1f);
            float t = 1f - Mathf.Exp(-StrengthSmooth * Mathf.Max(Time.unscaledDeltaTime, 0f));
            currentStrength = Mathf.Lerp(currentStrength, requestedStrength, t);

            TextureHandle source = resourceData.activeColorTexture;
            CaptureCurrentFrame(renderGraph, source);
            if (currentStrength < 0.0005f) return;

            RTHandle history1Buffer = GetDelayedHistory(DelaySeconds1);
            RTHandle history2Buffer = GetDelayedHistory(DelaySeconds2);
            if (history1Buffer == null || history2Buffer == null) return;

            float weight1 = Mathf.Lerp(0.08f, 1.65f, currentStrength);
            float weight2 = Mathf.Lerp(0.04f, 0.75f, currentStrength);
            material.SetFloat(StrengthID, currentStrength);
            material.SetTexture(History1TexID, history1Buffer.rt);
            material.SetTexture(History2TexID, history2Buffer.rt);
            material.SetFloat(HistoryWeight1ID, weight1);
            material.SetFloat(HistoryWeight2ID, weight2);

            TextureHandle destination = UniversalRenderer.CreateRenderGraphTexture(renderGraph, descriptor, "Dizziness Temporary", false);
            using (var builder = renderGraph.AddUnsafePass<PassData>(ProfilerTag, out var passData)) {
                passData.source = source;
                passData.destination = destination;
                passData.history1 = renderGraph.ImportTexture(history1Buffer);
                passData.history2 = renderGraph.ImportTexture(history2Buffer);
                passData.material = material;
                builder.UseTexture(passData.source, AccessFlags.Read);
                builder.UseTexture(passData.history1, AccessFlags.Read);
                builder.UseTexture(passData.history2, AccessFlags.Read);
                builder.UseTexture(destination, AccessFlags.Write);
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    context.cmd.SetRenderTarget(data.destination, 0, CubemapFace.Unknown, -1);
                    Blitter.BlitTexture(cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                });
            }

            resourceData.cameraColor = destination;
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
            for (int index = 0; index < targetCapacity; index++) {
                historyBuffers[index] = RTHandles.Alloc(descriptor, FilterMode.Bilinear);
                historyTimes[index] = float.NegativeInfinity;
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
                for (int index = 0; index < historyBuffers.Length; index++) historyBuffers[index]?.Release();
            }

            historyBuffers = null;
            historyTimes = null;
            historyCapacity = 0;
            historyWriteIndex = 0;
            historyCount = 0;
            historyReady = false;
            lastCaptureTime = float.NegativeInfinity;
        }

        static void CaptureCurrentFrame(RenderGraph renderGraph, TextureHandle source) {
            if (historyBuffers == null || historyCapacity == 0) return;

            float now = Time.unscaledTime;
            float captureInterval = 1f / Mathf.Max(HistoryCaptureHz, 1f);
            if (historyCount != 0 && now - lastCaptureTime < captureInterval) return;

            TextureHandle destination = renderGraph.ImportTexture(historyBuffers[historyWriteIndex]);
            RenderGraphUtils.AddBlitPass(renderGraph, source, destination, Vector2.one, Vector2.zero, passName: "Dizziness History Capture");
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
            for (int index = 0; index < historyCount; index++) {
                int historyIndex = (latestIndex - index + historyCapacity) % historyCapacity;
                if (historyTimes[historyIndex] <= targetTime) return historyBuffers[historyIndex];
                bestIndex = historyIndex;
            }

            return historyBuffers[bestIndex];
        }
    }
}