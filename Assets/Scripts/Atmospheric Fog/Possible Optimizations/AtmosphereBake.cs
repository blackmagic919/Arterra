using UnityEngine;

public class AtmosphereBake
{
    private RenderTexture rayDirs;
    private RenderTexture rayLengths;
    public RenderTexture inScattering;

    public TemplateFeature.PassSettings passSettings;
    public RenderTextureDescriptor targetDescriptor;

    public ComputeShader RaySetupCompute;
    public ComputeShader InScatteringCompute;

    const uint threadGroupSize = 8;

    public AtmosphereBake(TemplateFeature.PassSettings passSettings)
    {
        this.passSettings = passSettings;
    }

    public void SetScreenTarget(RenderTextureDescriptor targetDescriptor)
    {
        rayDirs?.Release();
        rayLengths?.Release();
        inScattering?.Release();

        this.targetDescriptor = targetDescriptor;

        this.RaySetupCompute = passSettings.RaySetupCompute;
        this.InScatteringCompute = passSettings.InScatteringCompute;

        rayDirs = new RenderTexture(targetDescriptor.width, targetDescriptor.height, 0, RenderTextureFormat.RGB111110Float); //This format is 3 channel and doesn't normalize vectors
        rayLengths = new RenderTexture(targetDescriptor.width, targetDescriptor.height, 0, RenderTextureFormat.RGFloat);

        rayDirs.enableRandomWrite = true;
        rayLengths.enableRandomWrite = true;

        inScattering = new RenderTexture(targetDescriptor.width, targetDescriptor.height, 0, RenderTextureFormat.RGB111110Float);
        inScattering.enableRandomWrite = true;
        rayDirs.Create();
        rayLengths.Create();
        inScattering.Create();
    }

    public void Execute()
    {
        if (Shader.GetGlobalTexture("_CameraDepthTexture") == null)
            return;
        if (targetDescriptor.width == 0)
            return;
        if (targetDescriptor.height == 0)
            return;

        InitializeSetup();
        ExecuteSetup();
        InitializeInScattering();
        ExecuteInScattering();
    }

    public RenderTexture GetInScattering()
    {
        return inScattering;
    }

    public void InitializeSetup()
    {
        RaySetupCompute.SetVector("_PlanetCenter", passSettings.planetCenter);
        RaySetupCompute.SetFloat("_AtmosphereRadius", passSettings.atmosphereRadius);
        RaySetupCompute.SetInt("screenHeight", targetDescriptor.height);
        RaySetupCompute.SetInt("screenWidth", targetDescriptor.width);

        RaySetupCompute.SetTexture(0, "rayDirs", rayDirs);
        RaySetupCompute.SetTexture(0, "rayLengths", rayLengths);
        RaySetupCompute.SetTexture(0, "inScattering", inScattering);
    }

    public void ExecuteSetup()
    {
        int numThreadsPerAxisX = Mathf.CeilToInt(targetDescriptor.width / (float)threadGroupSize);
        int numThreadsPerAxisY = Mathf.CeilToInt(targetDescriptor.height / (float)threadGroupSize);
        RaySetupCompute.Dispatch(0, numThreadsPerAxisX, numThreadsPerAxisY, 1);//
    }
    //
    public void InitializeInScattering()
    {
        InScatteringCompute.SetVector("_ScatteringCoeffs", passSettings.scatteringCoeffs);
        InScatteringCompute.SetVector("_PlanetCenter", passSettings.planetCenter);
        InScatteringCompute.SetFloat("_PlanetRadius", passSettings.planetRadius);
        InScatteringCompute.SetFloat("_AtmosphereRadius", passSettings.atmosphereRadius);
        InScatteringCompute.SetFloat("_DensityFalloff", passSettings.densityFalloffFactor);
        InScatteringCompute.SetInt("_NumInScatterPoints", passSettings.inScatterPoints);
        InScatteringCompute.SetInt("_NumOpticalDepthPoints", passSettings.opticalDepthPoints);

        InScatteringCompute.SetInt("screenHeight", targetDescriptor.height);
        InScatteringCompute.SetInt("screenWidth", targetDescriptor.width);

        InScatteringCompute.SetTexture(0, "rayDirs", rayDirs);
        InScatteringCompute.SetTexture(0, "rayLengths", rayLengths);
        InScatteringCompute.SetTexture(0, "inScattering", inScattering);
    }

    public void ExecuteInScattering()
    {
        int numThreadsPerAxisX = Mathf.CeilToInt(targetDescriptor.width / (float)threadGroupSize);
        int numThreadsPerAxisY = Mathf.CeilToInt(targetDescriptor.height / (float)threadGroupSize);
        //int numThreadsPerAxisZ = Mathf.CeilToInt(passSettings.inScatterPoints / (float)threadGroupSize);

        //InScatteringCompute.Dispatch(0, numThreadsPerAxisX, numThreadsPerAxisY, numThreadsPerAxisZ);
        // This causes conflicts as multiple threads add to same pixel, can be done using IntelockedAdd(), but need to convert to int and back
        
        for (int i = 0; i < passSettings.inScatterPoints; i++)
        {
            InScatteringCompute.SetInt("depth", i);
            InScatteringCompute.Dispatch(0, numThreadsPerAxisX, numThreadsPerAxisY, 1);
        }
    }
}
