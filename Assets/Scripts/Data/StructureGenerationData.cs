using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Settings/StructureDict")]
public class StructureGenerationData : ScriptableObject
{
    [SerializeField]
    public List<Structure> StructureDictionary;

    ComputeBuffer indexBuffer; //Prefix sum
    ComputeBuffer densityBuffer;
    ComputeBuffer materialBuffer;
    ComputeBuffer checksBuffer;
    ComputeBuffer settingsBuffer;

    public void ApplyToMaterial()
    {
        OnDisable();

        uint[] indexPrefixSum = new uint[(StructureDictionary.Count+1)*2];
        List<float> density = new List<float>();
        List<int> material = new List<int>();
        List<StructureData.CheckPoint> checks = new List<StructureData.CheckPoint>();
        StructureSettings.StructSettingsCopy[] settings = new StructureSettings.StructSettingsCopy[StructureDictionary.Count];

        for(int i = 0; i < StructureDictionary.Count; i++)
        {
            indexPrefixSum[2 * (i + 1)] = (uint)StructureDictionary[i].data.density.Length + indexPrefixSum[2*i]; //Density is same length as materials
            indexPrefixSum[2 * (i + 1) + 1] = (uint)StructureDictionary[i].data.checks.Length + indexPrefixSum[2 * i + 1];
            settings[i] = new StructureSettings.StructSettingsCopy(StructureDictionary[i].settings);
            density.AddRange(StructureDictionary[i].data.density);
            material.AddRange(StructureDictionary[i].data.materials);
            checks.AddRange(StructureDictionary[i].data.checks);
        }

        indexBuffer = new ComputeBuffer(StructureDictionary.Count + 1, sizeof(uint) * 2, ComputeBufferType.Structured); //By doubling stride, we compress the prefix sums
        densityBuffer = new ComputeBuffer(density.Count, sizeof(float), ComputeBufferType.Structured);
        materialBuffer = new ComputeBuffer(material.Count, sizeof(int), ComputeBufferType.Structured);
        checksBuffer = new ComputeBuffer(checks.Count, sizeof(float) * 3 + sizeof(uint), ComputeBufferType.Structured);
        settingsBuffer = new ComputeBuffer(StructureDictionary.Count, sizeof(int) * 4 + sizeof(uint) * 2, ComputeBufferType.Structured);

        indexBuffer.SetData(indexPrefixSum);
        densityBuffer.SetData(density);
        materialBuffer.SetData(material);
        checksBuffer.SetData(checks.ToArray());
        settingsBuffer.SetData(settings);


        Shader.SetGlobalBuffer("_StructureIndexes", indexBuffer);
        Shader.SetGlobalBuffer("_StructureDensities", densityBuffer);
        Shader.SetGlobalBuffer("_StructureMaterials", materialBuffer);
        Shader.SetGlobalBuffer("_StructureChecks", checksBuffer);
        Shader.SetGlobalBuffer("_StructureSettings", settingsBuffer);
    }

    public void OnDisable()
    {
        indexBuffer?.Release();
        densityBuffer?.Release();
        materialBuffer?.Release();
        checksBuffer?.Release();
        settingsBuffer?.Release();
    }

    public void OnEnable()
    {
        ApplyToMaterial();
    }

    [System.Serializable]
    public class Structure
    {
        public StructureData data;
        public StructureSettings settings;
    }
}
