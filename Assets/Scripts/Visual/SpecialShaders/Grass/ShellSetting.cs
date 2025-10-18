using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using WorldConfig;

[CreateAssetMenu(menuName = "ShaderData/ShellTexture/Setting")]
public class ShellSetting : Category<ShellSetting> {
    public static int DataSize => sizeof(float) * 13 + sizeof(int) * 2;
    /// <summary>The registry names of all entries referencing registries within <see cref="info"/>. When an element such as
    /// a material, structure, or entry needs to reference an entry in an external registry, they can indicate the index
    /// within this list of the name of the entry within the registry that they are referencing. </summary>
    public Option<List<string>> Names;
    public Data info;
    [Serializable]
    public struct Data {
        public float grassHeight; //0.5f
        public int maxLayers; //15f  
        [RegistryReference("Textures")]
        public int TextureIndex;
        public Color BaseColor;
        public Color TopColor;
        public float Scale;
        public float CenterHeight;
        public float WindFrequency;
        public float WindStrength;
    }

    public Data GetInfo() {
        Data serial = info;
        Catalogue<TextureContainer> texReg = Config.CURRENT.Generation.Textures;
        serial.TextureIndex = texReg.RetrieveIndex(Names.value[serial.TextureIndex]);
        return serial;
    }
}

[Serializable]
public struct ShellLevel {
    public int skipFactor;
    public static int DataSize => sizeof(int);
}
