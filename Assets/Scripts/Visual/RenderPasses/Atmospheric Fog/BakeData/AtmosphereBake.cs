using System;
using UnityEngine;
using UnityEngine.Rendering;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.Engine.Rendering;

namespace Arterra.Configuration.Quality {
    /// <summary>
    /// Settings controlling the quality of the atmosphere. 
    /// The atmosphere is a purely visual effect and does not affect gameplay.
    /// </summary>
    [Serializable]
    public struct Atmosphere {
        /// <summary>
        /// The size of the baked texture in pixels. The baked texture is
        /// the resolution used to actually raymarch and sample the map information made
        /// available by <see cref="GPUDensityManager"/> which is an expensive operation.
        /// The result is upscaled to the screen resolution.
        /// </summary>
        public int BakedTextureSizePX; // 128
        /// <summary>
        /// The detail of the optical in-scatter. An in-scatter point is a point along the pixel's ray 
        /// in which the in-scattered light is calculated through calculating the optical depth along other
        /// rays with a resolution of <see cref="NumOpticalDepthPoints"/>. The amount of in-scatter points
        /// is 2^<see cref="InScatterDetail"/>. It must be a power of 2 for an acceleration step to work.
        /// </summary>
        public int InScatterDetail; // 6
        /// <summary>
        /// The number of optical depth points to sample along rays used to calculate in-scatter.
        /// The number of optical depth points is exactly <see cref="NumOpticalDepthPoints"/>. 
        /// </summary>
        public int NumOpticalDepthPoints; // 8
        /// <summary>
        /// The size of the luminance precompute texture in pixels.
        /// </summary>
        public int LuminanceTextureSizePX; // 128
        /// <summary>
        /// Exact number of depth layers used by the luminance precompute.
        /// This is used directly and does not use power-of-two expansion.
        /// </summary>
        public int LuminanceDetail; // 64
        /// <summary>
        /// Number of optical depth samples taken per luminance depth segment.
        /// Higher values reduce integration error at additional compute cost.
        /// </summary>
        public int LuminanceOpticalDepthPoints; // 8
    }

}

namespace Arterra.Engine.Rendering {
    public class AtmosphereBake {
        private ComputeBuffer OpticalInfo;
        private ComputeBuffer sunLuminance;
        private ComputeBuffer sunRayLengths;
        private ComputeBuffer singleOriginLuminanceAccum;

        private ComputeShader LuminanceCompute;
        private ComputeShader OpticalDataCompute;
        private bool singleOriginReadbackPending;
        private Vector3 singleOriginLuminanceCached = Vector3.one;
        private Arterra.Configuration.Quality.Atmosphere settings;
        public int NumInScatterPoints => 1 << settings.InScatterDetail;
        int LuminanceTextureSizePX => Mathf.Max(settings.LuminanceTextureSizePX, 1);
        int NumLuminancePoints => Mathf.Max(settings.LuminanceDetail, 1);
        int NumLuminanceOpticalDepthPoints => Mathf.Max(settings.LuminanceOpticalDepthPoints, 1);

        private float atmosphereRadius;
        public bool initialized = false;

        public AtmosphereBake(float atmosphereRadius) {
            this.settings = Config.CURRENT.Quality.Atmosphere.value;
            this.atmosphereRadius = atmosphereRadius;

            LuminanceCompute = Resources.Load<ComputeShader>("Compute/Atmosphere/Luminance");
            OpticalDataCompute = Resources.Load<ComputeShader>("Compute/Atmosphere/OpticalData");

            int numPixels = settings.BakedTextureSizePX * settings.BakedTextureSizePX;
            int luminancePixels = LuminanceTextureSizePX * LuminanceTextureSizePX;
            sunLuminance = new ComputeBuffer(luminancePixels * NumLuminancePoints, sizeof(float) * 3, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
            sunRayLengths = new ComputeBuffer(luminancePixels, sizeof(float), ComputeBufferType.Structured, ComputeBufferMode.Immutable);
            singleOriginLuminanceAccum = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.Raw, ComputeBufferMode.Immutable);

            int numOpticalSamples = numPixels * NumInScatterPoints;
            this.OpticalInfo = new ComputeBuffer(numOpticalSamples, sizeof(float) * (3 + 3), ComputeBufferType.Structured, ComputeBufferMode.Immutable);
            SetupData();
        }

        public void ReleaseData() {
            initialized = false;
            OpticalInfo?.Release();
            sunLuminance?.Release();
            sunRayLengths?.Release();
            singleOriginLuminanceAccum?.Release();
            singleOriginReadbackPending = false;
            singleOriginLuminanceCached = Vector3.one;
        }

        public void ExecuteLuminance(CommandBuffer cmd, Vector3 lightDirection) {
            if (!GPUMapManager.initialized)
                return;
            if (!initialized)
                return;
            if (settings.BakedTextureSizePX == 0)
                return;

            UpdateFrustumLightVolumeData(lightDirection);
            SetSunLuminanceGlobalData();

            ExecuteLuminanceMarch(cmd);
        }

        public void ExecuteOptical(CommandBuffer cmd, Vector3 lightDirection) {
            if (!GPUMapManager.initialized)
                return;
            if (!initialized)
                return;
            if (settings.BakedTextureSizePX == 0)
                return;

            UpdateFrustumLightVolumeData(lightDirection);

            ExecuteOpticalMarch(cmd);
        }

        public void SetupData() {
            SetupLuminanceMarch();
            SetupOpticalMarch();
            SetSunLuminanceGlobalData();
            initialized = true;
        }

        public void SetBakedData(Material material) {
            InitializeTextureInterpHelper(material);
            material.SetBuffer("_OpticalInfo", OpticalInfo);
        }

        void InitializeTextureInterpHelper(Material material) {
            material.SetInt("SampleTextureHeight", settings.BakedTextureSizePX);
            material.SetInt("SampleTextureWidth", settings.BakedTextureSizePX);
            material.SetInt("SampleDepth", NumInScatterPoints);
        }

        void SetupLuminanceMarch() {
            var tSettings = Config.CURRENT.Quality.Terrain.value;
            int IsoValue = Mathf.RoundToInt(tSettings.IsoLevel * 255.0f);

            LuminanceCompute.SetFloat("_AtmosphereRadius", atmosphereRadius);

            LuminanceCompute.SetInt("_NumSunDepthPoints", NumLuminancePoints);
            LuminanceCompute.SetInt("_NumLuminanceOpticalDepthPoints", NumLuminanceOpticalDepthPoints);

            LuminanceCompute.SetInt("sunHeight", LuminanceTextureSizePX);
            LuminanceCompute.SetInt("sunWidth", LuminanceTextureSizePX);
            LuminanceCompute.SetInt("IsoLevel", IsoValue);

            int luminanceBakeKernel = LuminanceCompute.FindKernel("Bake");
            int sampleSingleOriginKernel = LuminanceCompute.FindKernel("SampleSingleOrigin");

            LuminanceCompute.SetBuffer(luminanceBakeKernel, "luminance", sunLuminance);
            LuminanceCompute.SetBuffer(luminanceBakeKernel, "sunRayLengths", sunRayLengths);
            LuminanceCompute.SetBuffer(sampleSingleOriginKernel, "luminance", sunLuminance);
            LuminanceCompute.SetBuffer(sampleSingleOriginKernel, "sunRayLengths", sunRayLengths);
            LuminanceCompute.SetBuffer(sampleSingleOriginKernel, "singleOriginLuminanceAccum", singleOriginLuminanceAccum);

            GPUMapManager.SetDensitySampleData(LuminanceCompute, 0);
            GPUMapManager.SetDensitySampleData(LuminanceCompute, 1);
        }

        void UpdateFrustumLightVolumeData(Vector3 lightDirection) {
            Camera camera = Camera.main;
            if (camera == null)
                return;

            FrustumLightVolumeCpuData volume = FrustumLightVolumeBuilder.Build(camera, lightDirection, atmosphereRadius);
            ApplyFrustumLightVolumeData(LuminanceCompute, volume);
            ApplyFrustumLightVolumeData(OpticalDataCompute, volume);

            Shader.SetGlobalMatrix("_FrustumLightWSToFS", volume.wsToFs);
            Shader.SetGlobalMatrix("_FrustumLightFSToWS", volume.fsToWs);
        }

        void SetSunLuminanceGlobalData() {
            Shader.SetGlobalBuffer("sunLuminance", sunLuminance);
            Shader.SetGlobalBuffer("sunRayLengths", sunRayLengths);
            Shader.SetGlobalInt("sunHeight", LuminanceTextureSizePX);
            Shader.SetGlobalInt("sunWidth", LuminanceTextureSizePX);
            Shader.SetGlobalInt("_NumSunDepthPoints", NumLuminancePoints);
        }

        static void ApplyFrustumLightVolumeData(ComputeShader compute, FrustumLightVolumeCpuData volume) {
            compute.SetMatrix("_FrustumLightWSToFS", volume.wsToFs);
            compute.SetMatrix("_FrustumLightFSToWS", volume.fsToWs);
        }

        void SetupOpticalMarch() {
            var tSettings = Config.CURRENT.Quality.Terrain.value;
            int IsoValue = Mathf.RoundToInt(tSettings.IsoLevel * 255.0f);

            OpticalDataCompute.SetFloat("_AtmosphereRadius", atmosphereRadius);

            OpticalDataCompute.SetInt("_NumInScatterPoints", NumInScatterPoints);
            OpticalDataCompute.SetInt("_NumOpticalDepthPoints", settings.NumOpticalDepthPoints);
            OpticalDataCompute.SetInt("_NumSunDepthPoints", NumLuminancePoints);

            OpticalDataCompute.SetInt("screenHeight", settings.BakedTextureSizePX);
            OpticalDataCompute.SetInt("screenWidth", settings.BakedTextureSizePX);
            OpticalDataCompute.SetInt("sunHeight", LuminanceTextureSizePX);
            OpticalDataCompute.SetInt("sunWidth", LuminanceTextureSizePX);
            OpticalDataCompute.SetInt("IsoLevel", IsoValue);

            OpticalDataCompute.SetBuffer(0, "mapData", OpticalInfo);
            OpticalDataCompute.SetBuffer(0, "sunLuminance", sunLuminance);
            OpticalDataCompute.SetBuffer(0, "sunRayLengths", sunRayLengths);
            GPUMapManager.SetDensitySampleData(OpticalDataCompute);
        }

        void ExecuteOpticalMarch(CommandBuffer cmd) {

            OpticalDataCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
            int numThreadsPerAxisX = Mathf.CeilToInt(settings.BakedTextureSizePX / (float)threadGroupSize);
            int numThreadsPerAxisY = Mathf.CeilToInt(settings.BakedTextureSizePX / (float)threadGroupSize);
            cmd.DispatchCompute(OpticalDataCompute, 0, numThreadsPerAxisX, numThreadsPerAxisY, 1);
        }

        void ExecuteLuminanceMarch(CommandBuffer cmd) {
            int luminanceBakeKernel = LuminanceCompute.FindKernel("Bake");
            LuminanceCompute.GetKernelThreadGroupSizes(luminanceBakeKernel, out uint threadGroupSize, out _, out _);
            int numThreadsPerAxisX = Mathf.CeilToInt(LuminanceTextureSizePX / (float)threadGroupSize);
            int numThreadsPerAxisY = Mathf.CeilToInt(LuminanceTextureSizePX / (float)threadGroupSize);
            cmd.DispatchCompute(LuminanceCompute, luminanceBakeKernel, numThreadsPerAxisX, numThreadsPerAxisY, 1);
        }

        public bool TrySampleSingleOriginLuminance(Vector3 rayOriginWS, Vector3 lightDirection, out Vector3 luminanceSample) {
            luminanceSample = singleOriginLuminanceCached;
            if (!initialized || !GPUMapManager.initialized)
                return false;

            GraphicsResourceContext renderContext = SharedResourceManager.GraphicsRendering;
            if (renderContext == null)
                return false;

            if (singleOriginReadbackPending)
                return true;

            renderContext.SetVector(LuminanceCompute, "_SingleRayOriginWS", new Vector4(rayOriginWS.x, rayOriginWS.y, rayOriginWS.z, 1.0f));
            renderContext.SetBufferData(singleOriginLuminanceAccum, new uint[3]);

            int totalSamples = Mathf.Max(NumLuminancePoints + NumLuminanceOpticalDepthPoints, 1);
            int sampleSingleOriginKernel = LuminanceCompute.FindKernel("SampleSingleOrigin");
            LuminanceCompute.GetKernelThreadGroupSizes(sampleSingleOriginKernel, out uint threadGroupSize, out _, out _);
            int threadGroupsX = Mathf.CeilToInt(totalSamples / (float)threadGroupSize);
            renderContext.Dispatch(LuminanceCompute, sampleSingleOriginKernel, threadGroupsX, 1, 1);

            singleOriginReadbackPending = true;
            renderContext.RequestAsyncReadback(singleOriginLuminanceAccum, sizeof(uint) * 3, 0, OnSingleOriginLuminanceReadback);
            luminanceSample = singleOriginLuminanceCached;
            return true;
        }

        void OnSingleOriginLuminanceReadback(AsyncGPUReadbackRequest request) {
            singleOriginReadbackPending = false;
            if (!initialized || request.hasError)
                return;

            var data = request.GetData<uint>();
            if (data.Length < 3)
                return;

            Vector3 sunDepth = new Vector3(data[0], data[1], data[2]) / 65536.0f;
            singleOriginLuminanceCached = new Vector3(
                Mathf.Exp(-sunDepth.x),
                Mathf.Exp(-sunDepth.y),
                Mathf.Exp(-sunDepth.z)
            );
        }

    }
}