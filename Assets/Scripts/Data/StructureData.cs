using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

public class StructureData : ScriptableObject
{
    public Option<Settings> settings;
    public Option<List<PointInfo>> map;
    [SerializeField]
    public Option<List<CheckPoint>> checks;
    
    public void Initialize(){
        map.value ??= new List<PointInfo>((int)(settings.value.GridSize.x * settings.value.GridSize.y * settings.value.GridSize.z));
        checks.value ??= new List<CheckPoint>();
    }

    [System.Serializable]
    public struct CheckPoint
    {
        public float3 position;
        public CheckInfo checkInfo; //bool is not yet blittable in shader 5.0

        public CheckPoint(float3 position, CheckInfo checkInfo)
        {
            this.position = position;
            this.checkInfo = checkInfo;
        }
    }

    [System.Serializable] 
    public struct Settings{
        public uint3 GridSize;
        public int minimumLOD;
        [Range(0, 1)]
        public uint randThetaRot;
        [Range(0, 1)]
        public uint randPhiRot;
    }
    [System.Serializable]
    public struct CheckInfo{
        public uint data;
        public uint MinDensity{
            readonly get => data & 0xFF;
            set => data = (data & 0xFFFFFF00) | (value & 0xFF);
        }

        public uint MaxDensity{
            readonly get => (data >> 8) & 0xFF;
            set => data = (data & 0xFFFF00FF) | ((value & 0xFF) << 8);
        }

        public uint MinViscosity{
            readonly get => (data >> 16) & 0xFF;
            set => data = (data & 0xFF00FFFF) | ((value & 0xFF) << 16);
        }

        public uint MaxViscosity{
            readonly get => (data >> 24) & 0xFF;
            set => data = (data & 0x00FFFFFF) | ((value & 0xFF) << 24);
        }
    }

    [System.Serializable]
    public struct PointInfo
    {
        public uint data;

        public bool preserve{ 
            readonly get => (data & 0x80000000) != 0;
            //Should not edit, but some functions need to
            set => data = value ? data | 0x80000000 : data & 0x7FFFFFFF;
        }

        public int density
        {
            readonly get => (int)data & 0xFF;
            set => data = (data & 0xFFFFFF00) | ((uint)value & 0xFF);
        }

        public int viscosity
        {
            readonly get => (int)(data >> 8) & 0xFF;
            set => data = (data & 0xFFFF00FF) | (((uint)value & 0xFF) << 8);
        }

        public int material
        {
            readonly get => (int)((data >> 16) & 0x7FFF);
            set => data = (data & 0x8000FFFF) | (uint)((value & 0x7FFF) << 16);
        }
    }
}
