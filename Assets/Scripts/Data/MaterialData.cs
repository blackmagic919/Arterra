using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(menuName = "Generation/MaterialData")]
public class MaterialData : ScriptableObject
{
    public string Name;
    public Texture2D texture;
    public TerrainData terrainData;
    public AtmosphericData AtmosphereScatter;
    public LiquidData liquidData;

    [System.Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct TerrainData{
        public Color color;
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
    }
}