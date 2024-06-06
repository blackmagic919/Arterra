using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;


public class Structure : ScriptableObject{
    public Data This;

    [System.Serializable] 
    public struct Data
    {
        public Settings settings;
        public PointInfo[] map;
        [SerializeField]
        public List<CheckPoint> checks;
        
        public void Initialize(){
            map ??= new PointInfo[settings.GridSize.x * settings.GridSize.y * settings.GridSize.z];
            checks ??= new List<CheckPoint>();
        }

        public void Clear(){
            map = null;
            checks = null;
        }
    }

    [System.Serializable]
    public struct CheckPoint
    {
        public float3 position;
        [Range(0, 1)]
        public uint isUnderGround; //bool is not yet blittable in shader 5.0

        public CheckPoint(float3 position, uint isUnderGround)
        {
            this.position = position;
            this.isUnderGround = isUnderGround;
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

