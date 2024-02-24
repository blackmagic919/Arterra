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

    [Header("Dependencies")]
    public ComputeShader matSizeCounter;
    public ComputeShader filterGeometry;
    public ComputeShader sizePrefixSum;
    public ComputeShader indirectThreads;
    public ComputeShader geoSizeCalculator;
    public ComputeShader geoTranscriber;
    public ComputeShader prefixShaderArgs;
    public ComputeShader shaderDrawArgs;

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
