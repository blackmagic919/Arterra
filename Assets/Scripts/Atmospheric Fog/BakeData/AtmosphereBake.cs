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
    public int NumInScatterPoints = 30;
    public int NumOpticalDepthPoints = 10;

    [HideInInspector]
    public bool initialized = false;
    [HideInInspector]

    const uint threadGroupSize = 8;

    public void SetSettings(TemplateFeature.PassSettings passSettings)
    {
        this.passSettings = passSettings;
    }

    public void OnValidate()
    {
        NumInScatterPoints = Mathf.Max(2, NumInScatterPoints);
        NumOpticalDepthPoints = Mathf.Max(1, NumOpticalDepthPoints);
    }

    public void OnEnable(){
        ReleaseData();

        int numPixels = BakedTextureSizePX * BakedTextureSizePX;
        rayDirs = new ComputeBuffer(numPixels, sizeof(float) * 3, ComputeBufferType.Structured, ComputeBufferMode.Immutable); //Floating point 3 channel
        rayLengths = new ComputeBuffer(numPixels, sizeof(float) * 2, ComputeBufferType.Structured, ComputeBufferMode.Immutable);//Floating point 2 channel

        //3D texture to store SunRayOpticalDepth
        //We can't use RenderTexture-Texture2DArray because SAMPLER2DARRAY does not terminate in a timely fashion
        int numCubicTexels = BakedTextureSizePX * BakedTextureSizePX * NumInScatterPoints;
        this.Luminance = new ComputeBuffer(numCubicTexels, sizeof(float) * 3, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
        this.OpticalInfo = new ComputeBuffer(numCubicTexels, sizeof(float) * (1 + 3 + 1), ComputeBufferType.Structured, ComputeBufferMode.Immutable);
        Camera.main.depthTextureMode = DepthTextureMode.Depth;

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
        if (BakedTextureSizePX == 0)
            return;
        if(!initialized)
            return;
        if(!passSettings.densityManager.initialized)
            return;
        
        CalculateRayData();
        ExecuteRaymarch();
        ExecuteOpticalMarch();
    }

    public void SetBakedData(Material material)
    {
        InitializeTextureInterpHelper(material);
        material.SetBuffer("_LuminanceLookup", Luminance);
        material.SetBuffer("_OpticalInfoLookup", OpticalInfo);
    }

    void InitializeTextureInterpHelper(Material material){
        material.SetInt("SampleTextureHeight", BakedTextureSizePX);
        material.SetInt("SampleTextureWidth", BakedTextureSizePX);
        material.SetInt("SampleDepth", NumInScatterPoints);
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

        OpticalDepthCompute.SetInt("_NumInScatterPoints", NumInScatterPoints);
        OpticalDepthCompute.SetInt("_NumOpticalDepthPoints", NumOpticalDepthPoints);

        OpticalDepthCompute.SetInt("screenHeight", BakedTextureSizePX);
        OpticalDepthCompute.SetInt("screenWidth", BakedTextureSizePX);

        OpticalDepthCompute.SetBuffer(0, "rayDirs", rayDirs);
        OpticalDepthCompute.SetBuffer(0, "rayLengths", rayLengths);
        OpticalDepthCompute.SetBuffer(0, "luminance", Luminance);

        this.passSettings.densityManager.SetDensitySampleData(OpticalDepthCompute);

        int numThreadsPerAxisX = Mathf.CeilToInt(BakedTextureSizePX / (float)threadGroupSize);
        int numThreadsPerAxisY = Mathf.CeilToInt(BakedTextureSizePX / (float)threadGroupSize);
        int numThreadsPerAxisZ = Mathf.CeilToInt(NumInScatterPoints / (float)threadGroupSize);
        OpticalDepthCompute.Dispatch(0, numThreadsPerAxisX, numThreadsPerAxisY, numThreadsPerAxisZ);
    }

    void ExecuteOpticalMarch(){
        float atmosphereRadius = lerpScale * passSettings.generationSettings.detailLevels[^1].distanceThresh;
        OpticalDataCompute.SetFloat("_AtmosphereRadius", atmosphereRadius);
        OpticalDataCompute.SetFloat("_IsoLevel", passSettings.generationSettings.IsoLevel);

        OpticalDataCompute.SetInt("_NumInScatterPoints", NumInScatterPoints);
        OpticalDataCompute.SetInt("_NumOpticalDepthPoints", NumOpticalDepthPoints);

        OpticalDataCompute.SetInt("screenHeight", BakedTextureSizePX);
        OpticalDataCompute.SetInt("screenWidth", BakedTextureSizePX);

        OpticalDataCompute.SetBuffer(0, "rayDirs", rayDirs);
        OpticalDataCompute.SetBuffer(0, "rayLengths", rayLengths);
        OpticalDataCompute.SetBuffer(0, "mapData", OpticalInfo);

        this.passSettings.densityManager.SetDensitySampleData(OpticalDataCompute);

        int numThreadsPerAxisX = Mathf.CeilToInt(BakedTextureSizePX / (float)threadGroupSize);
        int numThreadsPerAxisY = Mathf.CeilToInt(BakedTextureSizePX / (float)threadGroupSize);
        int numThreadsPerAxisZ = Mathf.CeilToInt(NumInScatterPoints / (float)threadGroupSize);
        OpticalDataCompute.Dispatch(0, numThreadsPerAxisX, numThreadsPerAxisY, numThreadsPerAxisZ);

    }

}
