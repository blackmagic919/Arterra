using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(menuName = "Generation/MaterialData")]
public class MaterialData : ScriptableObject
{
    public string Name;
    public Color color;
    public Texture2D texture;
    public float textureScale;
    [Range(0,1)]
    public float baseColorStrength;
    public int GeoShaderIndex = -1;

    public Vector4 AtmosphereScatter;

    [System.Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct AtmosphericData
    {
        public Vector3 ScatterCoeffs;
        public float GroundExtinction;
    }
}