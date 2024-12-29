using UnityEngine;
using Unity.Mathematics;
using Newtonsoft.Json;
using System;



public abstract class MaterialData : ScriptableObject
{
    public string SolidItem;
    public string LiquidItem;
    public TerrainData terrainData;
    public AtmosphericData AtmosphereScatter;
    public LiquidData liquidData;

    public abstract void UpdateMat(int3 GCoord);

    [System.Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct TerrainData{
        [HideInInspector][UISetting(Ignore = true)]
        public int SolidTextureIndex;
        public Vector4 color;
        public float textureScale;
        [Range(0,1)]
        public float baseColorStrength;
        public int GeoShaderIndex;
    
    }

    [System.Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct AtmosphericData
    {
        public Vector3 ScatterCoeffs;
        public Vector3 GroundExtinction;
    }

    [System.Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct LiquidData{
        public Vector3 shallowCol;
        public Vector3 deepCol;
        [Range(0,1)]
        public float colFalloff;
        [Range(0,1)]
        public float depthOpacity;
        [Range(0,1)]
        public float smoothness;
        [Range(0,1)]
        public float waveBlend;
        [Range(0,1)]
        public float waveStrength;
        public Vector2 waveScale;
        public Vector2 waveSpeed;
    }
}

