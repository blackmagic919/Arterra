using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Settings/StructureDict")]
public class StructureGenerationData : ScriptableObject
{
    [SerializeField]
    public List<Structure> StructureDictionary;

    ComputeBuffer indexBuffer; //Prefix sum
    ComputeBuffer mapBuffer;
    ComputeBuffer checksBuffer;
    ComputeBuffer settingsBuffer;

    public void ApplyToMaterial()
    {
        OnDisable();

        uint[] indexPrefixSum = new uint[(StructureDictionary.Count+1)*2];
        List<Structure.PointInfo> map = new List<Structure.PointInfo>();
        List<Structure.CheckPoint> checks = new List<Structure.CheckPoint>();
        Structure.Settings[] settings = new Structure.Settings[StructureDictionary.Count];

        for(int i = 0; i < StructureDictionary.Count; i++)
        {
            Structure.Data data = StructureDictionary[i].This;
            indexPrefixSum[2 * (i + 1)] = (uint)data.map.Length + indexPrefixSum[2*i]; //Density is same length as materials
            indexPrefixSum[2 * (i + 1) + 1] = (uint)data.checks.Count + indexPrefixSum[2 * i + 1];
            settings[i] = data.settings;
            map.AddRange(data.map);
            checks.AddRange(data.checks);
        }

        indexBuffer = new ComputeBuffer(StructureDictionary.Count + 1, sizeof(uint) * 2, ComputeBufferType.Structured); //By doubling stride, we compress the prefix sums
        mapBuffer = new ComputeBuffer(map.Count, sizeof(uint), ComputeBufferType.Structured);
        checksBuffer = new ComputeBuffer(checks.Count, sizeof(float) * 3 + sizeof(uint), ComputeBufferType.Structured);
        settingsBuffer = new ComputeBuffer(StructureDictionary.Count, sizeof(int) * 4 + sizeof(uint) * 2, ComputeBufferType.Structured);

        indexBuffer.SetData(indexPrefixSum);
        mapBuffer.SetData(map.ToArray());
        checksBuffer.SetData(checks.ToArray());
        settingsBuffer.SetData(settings);


        Shader.SetGlobalBuffer("_StructureIndexes", indexBuffer);
        Shader.SetGlobalBuffer("_StructureMap", mapBuffer);
        Shader.SetGlobalBuffer("_StructureChecks", checksBuffer);
        Shader.SetGlobalBuffer("_StructureSettings", settingsBuffer);
    }

    public void OnDisable()
    {
        indexBuffer?.Release();
        mapBuffer?.Release();
        checksBuffer?.Release();
        settingsBuffer?.Release();
    }

    public void OnEnable()
    {
        ApplyToMaterial();
    }

}
