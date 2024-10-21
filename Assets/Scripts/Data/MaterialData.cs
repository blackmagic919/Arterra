using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Newtonsoft.Json;
using System;



public abstract class MaterialData : ScriptableObject
{
    public string Name;
    [UISetting(Ignore = true)][JsonIgnore]
    public Option<Sprite> texture;
    public TerrainData terrainData;
    public AtmosphericData AtmosphereScatter;
    public LiquidData liquidData;

    public abstract void UpdateMat(int3 GCoord);

    [System.Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct TerrainData{
        public Vec4 color;
        public float textureScale;
        [Range(0,1)]
        public float baseColorStrength;
        public int GeoShaderIndex;
    
    }

    [System.Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct AtmosphericData
    {
        public Vec3 ScatterCoeffs;
        public Vec3 GroundExtinction;
    }

    [System.Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct LiquidData{
        public Vec3 shallowCol;
        public Vec3 deepCol;
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
        public Vec2 waveScale;
        public Vec2 waveSpeed;
    }
}

