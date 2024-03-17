using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

[CreateAssetMenu(menuName = "Settings/NoiseDict")]
public class NoiseGenerationData : ScriptableObject
{
    //This is also done using global buffers because setting data is costly
    //And setting data also decreases parallelization of shaders
    [SerializeField]
    List<NoiseData> NoiseSamplerDictionary;

    ComputeBuffer indexBuffer;
    ComputeBuffer settingsBuffer;
    ComputeBuffer offsetsBuffer;
    ComputeBuffer splinePointsBuffer;

    void ApplyToMaterial(){
        OnDisable();

        uint[] indexPrefixSum = new uint[(NoiseSamplerDictionary.Count + 1) * 2];
        NoiseSettings[] settings = new NoiseSettings[NoiseSamplerDictionary.Count];
        List<Vector3> offsets = new List<Vector3>();
        List<Vector4> splinePoints = new List<Vector4>();
        for(int i = 0; i < NoiseSamplerDictionary.Count; i++){
            indexPrefixSum[2 * (i+1)] = (uint)NoiseSamplerDictionary[i].offsets.Length + indexPrefixSum[2*i];
            indexPrefixSum[2 * (i+1) + 1] = (uint)NoiseSamplerDictionary[i].splinePoints.Length + indexPrefixSum[2*i+1];
            settings[i] = new NoiseSettings(NoiseSamplerDictionary[i]);
            offsets.AddRange(NoiseSamplerDictionary[i].offsets);
            splinePoints.AddRange(NoiseSamplerDictionary[i].splinePoints);
        }
        
        indexBuffer = new ComputeBuffer(NoiseSamplerDictionary.Count + 1, sizeof(uint) * 2, ComputeBufferType.Structured);
        settingsBuffer = new ComputeBuffer(NoiseSamplerDictionary.Count, sizeof(float) * 4, ComputeBufferType.Structured);
        offsetsBuffer = new ComputeBuffer(offsets.Count, sizeof(float) * 3, ComputeBufferType.Structured);
        splinePointsBuffer = new ComputeBuffer(splinePoints.Count, sizeof(float) * 4, ComputeBufferType.Structured);

        indexBuffer.SetData(indexPrefixSum);
        settingsBuffer.SetData(settings);
        offsetsBuffer.SetData(offsets);
        splinePointsBuffer.SetData(splinePoints);

        Shader.SetGlobalBuffer("_NoiseIndexes", indexBuffer);
        Shader.SetGlobalBuffer("_NoiseSettings", settingsBuffer);
        Shader.SetGlobalBuffer("_NoiseOffsets", offsetsBuffer);
        Shader.SetGlobalBuffer("_NoiseSplinePoints", splinePointsBuffer);
    }
    public void OnDisable()
    {
        indexBuffer?.Release();
        settingsBuffer?.Release();
        offsetsBuffer?.Release();
        splinePointsBuffer?.Release();
    }

    public void OnEnable()
    {
        ApplyToMaterial();
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct NoiseSettings
    {
        public float noiseScale;
        public float persistance;
        public float lacunarity;
        public float maxPossibleHeight;
        public NoiseSettings(NoiseData noise){
            noiseScale = noise.noiseScale;
            persistance = noise.persistance;
            lacunarity = noise.lacunarity;
            maxPossibleHeight = noise.maxPossibleHeight;
        }
    }
}
