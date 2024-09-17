using System;
using System.Collections;
using System.Linq;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using static EndlessTerrain;

[CreateAssetMenu(menuName = "Settings/LuminanceBake")]
public class AtmosphereBake : ScriptableObject
{
    private ComputeBuffer treeLocks;
    private ComputeBuffer rayInfo;
    private ComputeBuffer OpticalInfo;

    private ComputeShader RaySetupCompute;
    private ComputeShader OpticalDataCompute;
    public int BakedTextureSizePX = 128;
    public int InScatterDetail = 6;
    public int NumInScatterPoints => 1 << InScatterDetail;
    public int NumOpticalDepthPoints = 8;

    private float atmosphereRadius;
    [HideInInspector] [UISetting(Ignore = true)]
    public bool initialized = false;

    public void Initialize(){
        ReleaseData();
        
        RaySetupCompute = Resources.Load<ComputeShader>("Atmosphere/RayMarchSetup");
        OpticalDataCompute = Resources.Load<ComputeShader>("Atmosphere/OpticalData");

        int numPixels = BakedTextureSizePX * BakedTextureSizePX;
        rayInfo = new ComputeBuffer(numPixels, sizeof(float) * 3, ComputeBufferType.Structured, ComputeBufferMode.Immutable); //Floating point 3 channel
        RenderSettings rSettings = WorldStorageHandler.WORLD_OPTIONS.Quality.value.Rendering.value;
        this.atmosphereRadius = rSettings.lerpScale * rSettings.mapChunkSize * rSettings.detailLevels.value[^1].chunkDistThresh;

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
        if(!GPUDensityManager.initialized)
            return;
        if(!initialized)
            return;
        if (BakedTextureSizePX == 0)
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
        material.SetInt("SampleTextureHeight", BakedTextureSizePX);
        material.SetInt("SampleTextureWidth", BakedTextureSizePX);
        material.SetInt("SampleDepth", NumInScatterPoints);
    }

    void SetupRayData(){
        RaySetupCompute.SetFloat("_AtmosphereRadius", atmosphereRadius);
        
        RaySetupCompute.SetInt("screenHeight", BakedTextureSizePX);
        RaySetupCompute.SetInt("screenWidth", BakedTextureSizePX);
        
        RaySetupCompute.SetBuffer(0, "rayInfo", rayInfo);
    }
    void SetupOpticalMarch(){
        RenderSettings rSettings = WorldStorageHandler.WORLD_OPTIONS.Quality.value.Rendering.value;
        OpticalDataCompute.SetFloat("_AtmosphereRadius", atmosphereRadius);
        OpticalDataCompute.SetFloat("_IsoLevel", rSettings.IsoLevel);

        OpticalDataCompute.SetInt("_NumInScatterPoints", NumInScatterPoints);
        OpticalDataCompute.SetInt("_NumOpticalDepthPoints", NumOpticalDepthPoints);

        OpticalDataCompute.SetInt("screenHeight", BakedTextureSizePX);
        OpticalDataCompute.SetInt("screenWidth", BakedTextureSizePX);

        OpticalDataCompute.SetBuffer(0, "treeLocks", treeLocks);
        OpticalDataCompute.SetBuffer(0, "rayInfo", rayInfo);
        OpticalDataCompute.SetBuffer(0, "mapData", OpticalInfo);

        GPUDensityManager.SetDensitySampleData(OpticalDataCompute);
    }

    void CalculateRayData(CommandBuffer cmd)
    {
        RaySetupCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxisX = Mathf.CeilToInt(BakedTextureSizePX / (float)threadGroupSize);
        int numThreadsPerAxisY = Mathf.CeilToInt(BakedTextureSizePX / (float)threadGroupSize);
        cmd.DispatchCompute(RaySetupCompute, 0, numThreadsPerAxisX, numThreadsPerAxisY, 1);
    }

    void ExecuteOpticalMarch(CommandBuffer cmd){

        OpticalDataCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxisX = Mathf.CeilToInt(BakedTextureSizePX / (float)threadGroupSize);
        int numThreadsPerAxisY = Mathf.CeilToInt(BakedTextureSizePX / (float)threadGroupSize);
        int numThreadsPerAxisZ = Mathf.CeilToInt(NumInScatterPoints / (float)threadGroupSize);
        cmd.DispatchCompute(OpticalDataCompute, 0, numThreadsPerAxisX, numThreadsPerAxisY, numThreadsPerAxisZ);
    }

    int RoundPow2(int x){
        if (x < 0) return 0;
        --x;
        x |= x >> 1;
        x |= x >> 2;
        x |= x >> 4;
        x |= x >> 8;
        x |= x >> 16;
        return x+1;
    }

}
