using System;
using System.Linq;
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
        /// The detail of the in-scatter. An in-scatter point is a point along the pixel's ray 
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
    }

}

namespace Arterra.Engine.Rendering {
    public class AtmosphereBake {
        private ComputeBuffer treeLocks;
        private ComputeBuffer rayInfo;
        private ComputeBuffer OpticalInfo;
        private ComputeBuffer sunLuminance;
        private ComputeBuffer sunRayLengths;

        private ComputeShader RaySetupCompute;
        private ComputeShader LuminanceCompute;
        private ComputeShader OpticalDataCompute;
        private Arterra.Configuration.Quality.Atmosphere settings;
        public int NumInScatterPoints => 1 << settings.InScatterDetail;

        private float atmosphereRadius;
        public bool initialized = false;

        public AtmosphereBake(float atmosphereRadius) {
            this.settings = Config.CURRENT.Quality.Atmosphere.value;
            this.atmosphereRadius = atmosphereRadius;

            RaySetupCompute = Resources.Load<ComputeShader>("Compute/Atmosphere/RayMarchSetup");
            LuminanceCompute = Resources.Load<ComputeShader>("Compute/Atmosphere/Luminance");
            OpticalDataCompute = Resources.Load<ComputeShader>("Compute/Atmosphere/OpticalData");

            int numPixels = settings.BakedTextureSizePX * settings.BakedTextureSizePX;
            rayInfo = new ComputeBuffer(numPixels, sizeof(float) * 3, ComputeBufferType.Structured, ComputeBufferMode.Immutable); //Floating point 3 channel
            sunLuminance = new ComputeBuffer(numPixels * NumInScatterPoints, sizeof(float) * 3, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
            sunRayLengths = new ComputeBuffer(numPixels, sizeof(float), ComputeBufferType.Structured, ComputeBufferMode.Immutable);

            //3D texture to store SunRayOpticalDepth
            //We can't use RenderTexture-Texture2DArray because SAMPLER2DARRAY does not terminate in a timely fashion
            int numTreeNodes = numPixels * (NumInScatterPoints * 2); //NumInScatterPoints should be a power of 2
            int numLocks = numPixels * Mathf.CeilToInt(NumInScatterPoints / 32.0f);
            this.OpticalInfo = new ComputeBuffer(numTreeNodes, sizeof(float) * (3 + 3), ComputeBufferType.Structured, ComputeBufferMode.Immutable);
            this.treeLocks = new ComputeBuffer(numLocks, sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.Immutable);
            treeLocks.SetData(Enumerable.Repeat(0, numLocks).ToArray()); //Clear once
            SetupData();
        }

        public void ReleaseData() {
            initialized = false;
            rayInfo?.Release();
            OpticalInfo?.Release();
            sunLuminance?.Release();
            sunRayLengths?.Release();
            treeLocks?.Release();
        }

        public void Execute(CommandBuffer cmd, Vector3 lightDirection) {
            if (!GPUMapManager.initialized)
                return;
            if (!initialized)
                return;
            if (settings.BakedTextureSizePX == 0)
                return;

            UpdateFrustumLightVolumeData(lightDirection);

            CalculateRayData(cmd);
            ExecuteLuminanceMarch(cmd);
            ExecuteOpticalMarch(cmd);
        }

        public void SetupData() {
            SetupRayData();
            SetupLuminanceMarch();
            SetupOpticalMarch();
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

        void SetupRayData() {
            RaySetupCompute.SetFloat("_AtmosphereRadius", atmosphereRadius);

            RaySetupCompute.SetInt("screenHeight", settings.BakedTextureSizePX);
            RaySetupCompute.SetInt("screenWidth", settings.BakedTextureSizePX);

            RaySetupCompute.SetBuffer(0, "rayInfo", rayInfo);
        }

        void SetupLuminanceMarch() {
            var tSettings = Config.CURRENT.Quality.Terrain.value;
            int IsoValue = Mathf.RoundToInt(tSettings.IsoLevel * 255.0f);

            LuminanceCompute.SetFloat("_AtmosphereRadius", atmosphereRadius);

            LuminanceCompute.SetInt("_NumInScatterPoints", NumInScatterPoints);

            LuminanceCompute.SetInt("screenHeight", settings.BakedTextureSizePX);
            LuminanceCompute.SetInt("screenWidth", settings.BakedTextureSizePX);
            LuminanceCompute.SetInt("IsoLevel", IsoValue);

            LuminanceCompute.SetBuffer(0, "luminance", sunLuminance);
            LuminanceCompute.SetBuffer(0, "sunRayLengths", sunRayLengths);

            GPUMapManager.SetDensitySampleData(LuminanceCompute);
        }

        void UpdateFrustumLightVolumeData(Vector3 lightDirection) {
            Camera camera = Camera.main;
            if (camera == null)
                return;

            FrustumLightVolumeCpuData volume = FrustumLightVolumeBuilder.Build(camera, lightDirection, atmosphereRadius);
            ApplyFrustumLightVolumeData(LuminanceCompute, volume);
            ApplyFrustumLightVolumeData(OpticalDataCompute, volume);
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

            OpticalDataCompute.SetInt("screenHeight", settings.BakedTextureSizePX);
            OpticalDataCompute.SetInt("screenWidth", settings.BakedTextureSizePX);
            OpticalDataCompute.SetInt("IsoLevel", IsoValue);

            OpticalDataCompute.SetBuffer(0, "treeLocks", treeLocks);
            OpticalDataCompute.SetBuffer(0, "rayInfo", rayInfo);
            OpticalDataCompute.SetBuffer(0, "mapData", OpticalInfo);
            OpticalDataCompute.SetBuffer(0, "sunLuminance", sunLuminance);
            OpticalDataCompute.SetBuffer(0, "sunRayLengths", sunRayLengths);

            LightBaker.SetupLightSampler(OpticalDataCompute, 0);
            GPUMapManager.SetDensitySampleData(OpticalDataCompute);
        }

        void CalculateRayData(CommandBuffer cmd) {
            RaySetupCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
            int numThreadsPerAxisX = Mathf.CeilToInt(settings.BakedTextureSizePX / (float)threadGroupSize);
            int numThreadsPerAxisY = Mathf.CeilToInt(settings.BakedTextureSizePX / (float)threadGroupSize);
            cmd.DispatchCompute(RaySetupCompute, 0, numThreadsPerAxisX, numThreadsPerAxisY, 1);
        }

        void ExecuteOpticalMarch(CommandBuffer cmd) {

            OpticalDataCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
            int numThreadsPerAxisX = Mathf.CeilToInt(settings.BakedTextureSizePX / (float)threadGroupSize);
            int numThreadsPerAxisY = Mathf.CeilToInt(settings.BakedTextureSizePX / (float)threadGroupSize);
            int numThreadsPerAxisZ = Mathf.CeilToInt(NumInScatterPoints / (float)threadGroupSize);
            cmd.DispatchCompute(OpticalDataCompute, 0, numThreadsPerAxisX, numThreadsPerAxisY, numThreadsPerAxisZ);
        }

        void ExecuteLuminanceMarch(CommandBuffer cmd) {
            LuminanceCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
            int numThreadsPerAxisX = Mathf.CeilToInt(settings.BakedTextureSizePX / (float)threadGroupSize);
            int numThreadsPerAxisY = Mathf.CeilToInt(settings.BakedTextureSizePX / (float)threadGroupSize);
            cmd.DispatchCompute(LuminanceCompute, 0, numThreadsPerAxisX, numThreadsPerAxisY, 1);
        }

    }
}