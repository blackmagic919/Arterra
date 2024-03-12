using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Settings/ShaderGenerator")]
public class GeneratorSettings : ScriptableObject
{
    [Header("Reference Shaders")]
    public List<SpecialShader> shaderDictionary;
    //At maximum LoD, we use 60 mB per vertex per base triangle per chunk
    public int maxVertexPerBase;

    [Header("Buffer Space")]
    public MemoryBufferSettings memoryBuffer;

    [HideInInspector]
    public ComputeShader matSizeCounter;
    [HideInInspector]
    public ComputeShader filterGeometry;
    [HideInInspector]
    public ComputeShader sizePrefixSum;
    [HideInInspector]
    public ComputeShader indirectThreads;
    [HideInInspector]
    public ComputeShader geoSizeCalculator;
    [HideInInspector]
    public ComputeShader geoTranscriber;
    [HideInInspector]
    public ComputeShader prefixShaderArgs;
    [HideInInspector]
    public ComputeShader shaderDrawArgs;

    public void OnEnable(){
        matSizeCounter = Resources.Load<ComputeShader>("GeoShader/Generation/ShaderMatSizeCounter");
        filterGeometry = Resources.Load<ComputeShader>("GeoShader/Generation/FilterShaderGeometry");
        sizePrefixSum = Resources.Load<ComputeShader>("GeoShader/Generation/ShaderPrefixConstructor");
        geoSizeCalculator = Resources.Load<ComputeShader>("GeoShader/Generation/GeometryMemorySize");
        geoTranscriber = Resources.Load<ComputeShader>("GeoShader/Generation/TranscribeGeometry");
        prefixShaderArgs = Resources.Load<ComputeShader>("GeoShader/Generation/PrefixShaderArgs");
        shaderDrawArgs = Resources.Load<ComputeShader>("GeoShader/Generation/GeoDrawArgs");

        indirectThreads = Resources.Load<ComputeShader>("Utility/DivideByThreads");
    }

    public Material[] GetMaterialInstances()
    {
        Material[] geoShaders = new Material[shaderDictionary.Count];
        for(int i = 0; i < shaderDictionary.Count; i++)
        {
            geoShaders[i] = Instantiate(shaderDictionary[i].GetMaterial());
        }
        return geoShaders;
    }

}
