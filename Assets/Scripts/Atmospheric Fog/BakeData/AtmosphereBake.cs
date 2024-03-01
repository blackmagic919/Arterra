using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using static EndlessTerrain;

[CreateAssetMenu(menuName = "Settings/LuminanceBake")]
public class AtmosphereBake : ScriptableObject
{
    private ComputeBuffer rayDirs;
    private ComputeBuffer rayLengths;
    private ComputeBuffer Luminance;
    private ComputeBuffer OpticalInfo;

    private TemplateFeature.PassSettings passSettings;

    public ComputeShader RaySetupCompute;
    public ComputeShader OpticalDepthCompute;
    public ComputeShader OpticalDataCompute;
    
    public int BakedTextureSizePX = 128;
    public int SunRayInScatterPoints = 5;
    public int SunRayOpticalDepthPoints = 5;

    [HideInInspector]
    public bool initialized = false;
    [HideInInspector]

    const uint threadGroupSize = 8;

    public void SetSettings(TemplateFeature.PassSettings passSettings)
    {
        this.passSettings = passSettings;
    }

    public void OnEnable(){
        ReleaseData();

        int numPixels = BakedTextureSizePX * BakedTextureSizePX;
        rayDirs = new ComputeBuffer(numPixels, sizeof(float) * 3, ComputeBufferType.Structured, ComputeBufferMode.Immutable); //Floating point 3 channel
        rayLengths = new ComputeBuffer(numPixels, sizeof(float) * 2, ComputeBufferType.Structured, ComputeBufferMode.Immutable);//Floating point 2 channel

        //3D texture to store SunRayOpticalDepth
        //We can't use RenderTexture-Texture2DArray because SAMPLER2DARRAY does not terminate in a timely fashion
        int numCubicTexels = BakedTextureSizePX * BakedTextureSizePX * SunRayInScatterPoints;
        this.Luminance = new ComputeBuffer(numCubicTexels, sizeof(float) * 3, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
        this.OpticalInfo = new ComputeBuffer(numCubicTexels, sizeof(float) * (1 + 3 + 1), ComputeBufferType.Structured, ComputeBufferMode.Immutable);

        initialized = true;
    }

    public void OnDisable(){
        ReleaseData();
    }

    private void ReleaseData(){
        initialized = false;
        rayDirs?.Release();
        rayLengths?.Release();
        Luminance?.Release();
        OpticalInfo?.Release();
    }

    public void Execute()
    {
        if (Shader.GetGlobalTexture("_CameraDepthTexture") == null)
            return;
        if (BakedTextureSizePX == 0)
            return;
        if(!initialized)
            return;
        if(!passSettings.densityManager.initialized)
            return;
        
        CalculateRayData();
        ExecuteRaymarch();
    }

    public void SetBakedData(Material material)
    {
        InitializeTextureInterpHelper(material);
        material.SetBuffer("_LuminanceLookup", Luminance);
    }

    void InitializeTextureInterpHelper(Material material){
        material.SetInt("SampleTextureHeight", BakedTextureSizePX);
        material.SetInt("SampleTextureWidth", BakedTextureSizePX);
        material.SetInt("SampleDepth", SunRayInScatterPoints);
    }

    void CalculateRayData()
    {
        float atmosphereRadius = lerpScale * passSettings.generationSettings.detailLevels[^1].distanceThresh;
        RaySetupCompute.SetFloat("_AtmosphereRadius", atmosphereRadius);
        
        RaySetupCompute.SetInt("screenHeight", BakedTextureSizePX);
        RaySetupCompute.SetInt("screenWidth", BakedTextureSizePX);
        
        RaySetupCompute.SetBuffer(0, "rayDirs", rayDirs);
        RaySetupCompute.SetBuffer(0, "rayLengths", rayLengths);

        int numThreadsPerAxisX = Mathf.CeilToInt(BakedTextureSizePX / (float)threadGroupSize);
        int numThreadsPerAxisY = Mathf.CeilToInt(BakedTextureSizePX / (float)threadGroupSize);
        RaySetupCompute.Dispatch(0, numThreadsPerAxisX, numThreadsPerAxisY, 1);
    }

    void ExecuteRaymarch()
    {
        float atmosphereRadius = lerpScale * passSettings.generationSettings.detailLevels[^1].distanceThresh;
        OpticalDepthCompute.SetFloat("_AtmosphereRadius", atmosphereRadius);
        OpticalDepthCompute.SetFloat("_IsoLevel", passSettings.generationSettings.IsoLevel);

        OpticalDepthCompute.SetInt("_NumInScatterPoints", SunRayInScatterPoints);
        OpticalDepthCompute.SetInt("_NumOpticalDepthPoints", SunRayOpticalDepthPoints);

        OpticalDepthCompute.SetInt("screenHeight", BakedTextureSizePX);
        OpticalDepthCompute.SetInt("screenWidth", BakedTextureSizePX);

        OpticalDepthCompute.SetBuffer(0, "rayDirs", rayDirs);
        OpticalDepthCompute.SetBuffer(0, "rayLengths", rayLengths);
        OpticalDepthCompute.SetBuffer(0, "luminance", Luminance);

        this.passSettings.densityManager.SetDensitySampleData(OpticalDepthCompute);

        int numThreadsPerAxisX = Mathf.CeilToInt(BakedTextureSizePX / (float)threadGroupSize);
        int numThreadsPerAxisY = Mathf.CeilToInt(BakedTextureSizePX / (float)threadGroupSize);
        int numThreadsPerAxisZ = Mathf.CeilToInt(SunRayInScatterPoints / (float)threadGroupSize);
        OpticalDepthCompute.Dispatch(0, numThreadsPerAxisX, numThreadsPerAxisY, numThreadsPerAxisZ);
    }

    void ExecuteOpticMarch(){
        float atmosphereRadius = lerpScale * passSettings.generationSettings.detailLevels[^1].distanceThresh;
        OpticalDepthCompute.SetFloat("_AtmosphereRadius", atmosphereRadius);
        OpticalDepthCompute.SetFloat("_IsoLevel", passSettings.generationSettings.IsoLevel);

        OpticalDepthCompute.SetInt("_NumInScatterPoints", SunRayInScatterPoints);
        OpticalDepthCompute.SetInt("_NumOpticalDepthPoints", SunRayOpticalDepthPoints);

        OpticalDataCompute.SetInt("screenHeight", BakedTextureSizePX);
        OpticalDataCompute.SetInt("screenWidth", BakedTextureSizePX);

        OpticalDataCompute.SetBuffer(0, "rayDirs", rayDirs);
        OpticalDataCompute.SetBuffer(0, "rayLengths", rayLengths);
        OpticalDataCompute.SetBuffer(0, "mapData", OpticalInfo);

        this.passSettings.densityManager.SetDensitySampleData(OpticalDataCompute);

        int numThreadsPerAxisX = Mathf.CeilToInt(BakedTextureSizePX / (float)threadGroupSize);
        int numThreadsPerAxisY = Mathf.CeilToInt(BakedTextureSizePX / (float)threadGroupSize);
        int numThreadsPerAxisZ = Mathf.CeilToInt(SunRayInScatterPoints / (float)threadGroupSize);
        OpticalDataCompute.Dispatch(0, numThreadsPerAxisX, numThreadsPerAxisY, numThreadsPerAxisZ);
    }

}
