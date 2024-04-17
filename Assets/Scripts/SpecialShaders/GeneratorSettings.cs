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

}
