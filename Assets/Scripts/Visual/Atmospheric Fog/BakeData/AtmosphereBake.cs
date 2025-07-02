using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using WorldConfig;
using MapStorage;

namespace WorldConfig.Quality{
    /// <summary>
    /// Settings controlling the quality of the atmosphere. 
    /// The atmosphere is a purely visual effect and does not affect gameplay.
    /// </summary>
    [Serializable]
    public struct Atmosphere{
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
public class AtmosphereBake
{
    private ComputeBuffer treeLocks;
    private ComputeBuffer rayInfo;
    private ComputeBuffer OpticalInfo;

    private ComputeShader RaySetupCompute;
    private ComputeShader OpticalDataCompute;
    private WorldConfig.Quality.Atmosphere settings;
    public int NumInScatterPoints => 1 << settings.InScatterDetail;

    private float atmosphereRadius;
    public bool initialized = false;

    public AtmosphereBake(float atmosphereRadius){
        this.settings = Config.CURRENT.Quality.Atmosphere.value;
        this.atmosphereRadius = atmosphereRadius;
        
        RaySetupCompute = Resources.Load<ComputeShader>("Compute/Atmosphere/RayMarchSetup");
        OpticalDataCompute = Resources.Load<ComputeShader>("Compute/Atmosphere/OpticalData");

        int numPixels = settings.BakedTextureSizePX * settings.BakedTextureSizePX;
        rayInfo = new ComputeBuffer(numPixels, sizeof(float) * 3, ComputeBufferType.Structured, ComputeBufferMode.Immutable); //Floating point 3 channel

        //3D texture to store SunRayOpticalDepth
        //We can't use RenderTexture-Texture2DArray because SAMPLER2DARRAY does not terminate in a timely fashion
        int numTreeNodes = numPixels * (NumInScatterPoints * 2); //NumInScatterPoints should be a power of 2
        int numLocks = numPixels * Mathf.CeilToInt(NumInScatterPoints / 32.0f);
        this.OpticalInfo = new ComputeBuffer(numTreeNodes, sizeof(float) * (3 + 3), ComputeBufferType.Structured, ComputeBufferMode.Immutable);
        this.treeLocks = new ComputeBuffer(numLocks, sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.Immutable);
        treeLocks.SetData(Enumerable.Repeat(0, numLocks).ToArray()); //Clear once
        SetupData();
    }

    public void ReleaseData(){
        initialized = false;
        rayInfo?.Release();
        OpticalInfo?.Release();
        treeLocks?.Release();
    }

    public void Execute(CommandBuffer cmd)
    {
        if(!GPUMapManager.initialized)
            return;
        if(!initialized)
            return;
        if (settings.BakedTextureSizePX == 0)
            return;
        
        CalculateRayData(cmd);
        ExecuteOpticalMarch(cmd);
    }

    public void SetupData(){
        SetupRayData();
        SetupOpticalMarch();
        initialized = true;
    }

    public void SetBakedData(Material material)
    {
        InitializeTextureInterpHelper(material);
        material.SetBuffer("_OpticalInfo", OpticalInfo);
    }

    void InitializeTextureInterpHelper(Material material){
        material.SetInt("SampleTextureHeight", settings.BakedTextureSizePX);
        material.SetInt("SampleTextureWidth", settings.BakedTextureSizePX);
        material.SetInt("SampleDepth", NumInScatterPoints);
    }

    void SetupRayData(){
        RaySetupCompute.SetFloat("_AtmosphereRadius", atmosphereRadius);
        
        RaySetupCompute.SetInt("screenHeight", settings.BakedTextureSizePX);
        RaySetupCompute.SetInt("screenWidth", settings.BakedTextureSizePX);
        
        RaySetupCompute.SetBuffer(0, "rayInfo", rayInfo);
    }
    void SetupOpticalMarch(){
        WorldConfig.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain.value;
        OpticalDataCompute.SetFloat("_AtmosphereRadius", atmosphereRadius);

        OpticalDataCompute.SetInt("_NumInScatterPoints", NumInScatterPoints);
        OpticalDataCompute.SetInt("_NumOpticalDepthPoints", settings.NumOpticalDepthPoints);

        OpticalDataCompute.SetInt("screenHeight", settings.BakedTextureSizePX);
        OpticalDataCompute.SetInt("screenWidth", settings.BakedTextureSizePX);

        OpticalDataCompute.SetBuffer(0, "treeLocks", treeLocks);
        OpticalDataCompute.SetBuffer(0, "rayInfo", rayInfo);
        OpticalDataCompute.SetBuffer(0, "mapData", OpticalInfo);

        LightBaker.SetupLightSampler(OpticalDataCompute, 0);
        GPUMapManager.SetDensitySampleData(OpticalDataCompute);
    }

    void CalculateRayData(CommandBuffer cmd)
    {
        RaySetupCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxisX = Mathf.CeilToInt(settings.BakedTextureSizePX / (float)threadGroupSize);
        int numThreadsPerAxisY = Mathf.CeilToInt(settings.BakedTextureSizePX / (float)threadGroupSize);
        cmd.DispatchCompute(RaySetupCompute, 0, numThreadsPerAxisX, numThreadsPerAxisY, 1);
    }

    void ExecuteOpticalMarch(CommandBuffer cmd){

        OpticalDataCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxisX = Mathf.CeilToInt(settings.BakedTextureSizePX / (float)threadGroupSize);
        int numThreadsPerAxisY = Mathf.CeilToInt(settings.BakedTextureSizePX / (float)threadGroupSize);
        int numThreadsPerAxisZ = Mathf.CeilToInt(NumInScatterPoints / (float)threadGroupSize);
        cmd.DispatchCompute(OpticalDataCompute, 0, numThreadsPerAxisX, numThreadsPerAxisY, numThreadsPerAxisZ);
    }

}