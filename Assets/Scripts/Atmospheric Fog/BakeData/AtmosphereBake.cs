using System;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using static EndlessTerrain;

[CreateAssetMenu(menuName = "Settings/LuminanceBake")]
public class AtmosphereBake : ScriptableObject
{
    private ComputeBuffer rayInfo;
    private ComputeBuffer OpticalInfo;

    private TemplateFeature.PassSettings passSettings;

    private ComputeShader RaySetupCompute;
    private ComputeShader OpticalDataCompute;
    
    public int BakedTextureSizePX = 128;
    public int NumInScatterPoints = 5;
    public int NumOpticalDepthPoints = 5;

    [HideInInspector]
    public bool initialized = false;
    [HideInInspector]

    public void SetSettings(TemplateFeature.PassSettings passSettings)
    {
        this.passSettings = passSettings;
    }

    public void OnEnable(){
        ReleaseData();
        
        RaySetupCompute = Resources.Load<ComputeShader>("Atmosphere/RayMarchSetup");
        OpticalDataCompute = Resources.Load<ComputeShader>("Atmosphere/OpticalData");

        int numPixels = BakedTextureSizePX * BakedTextureSizePX;
        rayInfo = new ComputeBuffer(numPixels, sizeof(float) * (3 + 2), ComputeBufferType.Structured, ComputeBufferMode.Immutable); //Floating point 3 channel

        //3D texture to store SunRayOpticalDepth
        //We can't use RenderTexture-Texture2DArray because SAMPLER2DARRAY does not terminate in a timely fashion
        int numCubicTexels = BakedTextureSizePX * BakedTextureSizePX * NumInScatterPoints;
        this.OpticalInfo = new ComputeBuffer(numCubicTexels, sizeof(float) * (2 + 3 + 3 + 3), ComputeBufferType.Structured, ComputeBufferMode.Immutable);
    }

    public void OnDisable(){
        ReleaseData();
    }

    private void ReleaseData(){
        initialized = false;
        rayInfo?.Release();
        OpticalInfo?.Release();
    }

    public void Execute()
    {
        if(!GPUDensityManager.initialized)
            return;
        if(!initialized)
            SetupData();
        if (Shader.GetGlobalTexture("_CameraDepthTexture") == null)
            return;
        if (BakedTextureSizePX == 0)
            return;
        
        CalculateRayData();
        ExecuteOpticalMarch();
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
        float atmosphereRadius = lerpScale * passSettings.generationSettings.detailLevels[^1].distanceThresh;
        RaySetupCompute.SetFloat("_AtmosphereRadius", atmosphereRadius);
        
        RaySetupCompute.SetInt("screenHeight", BakedTextureSizePX);
        RaySetupCompute.SetInt("screenWidth", BakedTextureSizePX);
        
        RaySetupCompute.SetBuffer(0, "rayInfo", rayInfo);
    }
    void SetupOpticalMarch(){
        float atmosphereRadius = lerpScale * passSettings.generationSettings.detailLevels[^1].distanceThresh;
        OpticalDataCompute.SetFloat("_AtmosphereRadius", atmosphereRadius);
        OpticalDataCompute.SetFloat("_IsoLevel", passSettings.generationSettings.IsoLevel);

        OpticalDataCompute.SetInt("_NumInScatterPoints", NumInScatterPoints);
        OpticalDataCompute.SetInt("_NumOpticalDepthPoints", NumOpticalDepthPoints);

        OpticalDataCompute.SetInt("screenHeight", BakedTextureSizePX);
        OpticalDataCompute.SetInt("screenWidth", BakedTextureSizePX);

        OpticalDataCompute.SetBuffer(0, "rayInfo", rayInfo);
        OpticalDataCompute.SetBuffer(0, "mapData", OpticalInfo);

        GPUDensityManager.SetDensitySampleData(OpticalDataCompute);
    }

    void CalculateRayData()
    {
        RaySetupCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxisX = Mathf.CeilToInt(BakedTextureSizePX / (float)threadGroupSize);
        int numThreadsPerAxisY = Mathf.CeilToInt(BakedTextureSizePX / (float)threadGroupSize);
        RaySetupCompute.Dispatch(0, numThreadsPerAxisX, numThreadsPerAxisY, 1);
    }

    void ExecuteOpticalMarch(){

        OpticalDataCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxisX = Mathf.CeilToInt(BakedTextureSizePX / (float)threadGroupSize);
        int numThreadsPerAxisY = Mathf.CeilToInt(BakedTextureSizePX / (float)threadGroupSize);
        int numThreadsPerAxisZ = Mathf.CeilToInt(NumInScatterPoints / (float)threadGroupSize);
        OpticalDataCompute.Dispatch(0, numThreadsPerAxisX, numThreadsPerAxisY, numThreadsPerAxisZ);

    }

}
