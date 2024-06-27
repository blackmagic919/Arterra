using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

[CreateAssetMenu(menuName = "Settings/ShaderGenerator")]
public class GeneratorSettings : ScriptableObject
{
    [Header("Reference Shaders")]
    public Option<List<Option<SpecialShader> > > shaderDictionary;
    //At maximum LoD, we use 60 mB per vertex per base triangle per chunk
    public int maxVertexPerBase;

    [Header("Buffer Space")][UIgnore][JsonIgnore]
    public Option<MemoryBufferSettings> memoryBuffer;
}
