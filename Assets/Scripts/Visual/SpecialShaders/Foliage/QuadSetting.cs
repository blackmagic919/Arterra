using System;
using System.Collections.Generic;
using UnityEngine;
using Arterra.Configuration;

[CreateAssetMenu(menuName = "ShaderData/QuadShader/Setting")]
public class QuadSetting : Category<QuadSetting>
{
    public static int DataSize => sizeof(float) * 6 + sizeof(int) * 2;
    /// <summary>The registry names of all entries referencing registries within <see cref="info"/>. When an element such as
    /// a material, structure, or entry needs to reference an entry in an external registry, they can indicate the index
    /// within this list of the name of the entry within the registry that they are referencing. </summary>
    public Option<List<string> > Names;
    //This is seperated so we have just the raw data independent of 
    //anything we might have inherited and so-on
    public Data info;
    [Serializable]
    public struct Data {
        [Tooltip("Size of Quad Images")]
        public float QuadSize; //1.0f
        [Tooltip("Distance Extruded Along Normal")]
        public float Inflation; //0f   
        public Color LeafColor;
        [RegistryReference("Textures")]
        public int TextureIndex;
        // 0 = Triple
        // 1 = Quads Along Normal
        // 2 = Quad facing normal
        public int QuadType; //0 = Triple
    }
    public Data GetInfo()
    {
        Data serial = info;
        Catalogue<TextureContainer> texReg = Config.CURRENT.Generation.Textures;
        serial.TextureIndex = texReg.RetrieveIndex(Names.value[serial.TextureIndex]);
        return serial;
    }
}

[Serializable]
public struct QuadLevel {
    public float cullPercentage;
    public float sizeInflate;
    public static int DataSize => sizeof(float) * 2;
}
