using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Settings/Readback")]
public class ReadbackSettings : ScriptableObject
{
    [Header("Indirectly Drawn Shader")]
    public List<Material> indirectTerrainMats;

    [Header("Buffer Space")]
    public MemoryBufferSettings memoryBuffer;

    [Header("Dependencies")]
    public ComputeShader memorySizeCalculator;
    public ComputeShader baseGeoTranscriber;
    public ComputeShader memoryHandleCombiner;
    public ComputeShader meshDrawArgsCreator;
    public ComputeShader indirectThreads;
}
