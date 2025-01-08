using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WorldConfig.Intrinsic{
/// <summary> Settings for the readback system. 
/// <see cref="TerrainGeneration.Readback.AsyncMeshReadback"/> 
/// for more information. </summary>
[CreateAssetMenu(menuName = "Settings/Readback")]
public class Readback: ScriptableObject
{
    /// <summary> The list of all materials normally used to render the terrain. </summary>
    [Header("Indirectly Drawn Shader")]
    public List<Material> TerrainMats; 
    /// <summary> Variants of all normal materials used to intermediately draw the terrain
    /// directly from the GPU. Each material in <see cref="TerrainMats"/> should define a compiler
    /// keyword that creates a variant allowing it to render information directly from a custom buffer. </summary>
    [HideInInspector]
    public Material[] indirectTerrainMats;

    /// <summary> Populates the <see cref="indirectTerrainMats"/> array with variants of materials in <see cref="TerrainMats"/>. </summary>
    public void Initialize(){
        indirectTerrainMats = new Material[TerrainMats.Count];
        for(int i = 0; i < TerrainMats.Count; i++){
            if(indirectTerrainMats[i] != null) continue;
            indirectTerrainMats[i] = Object.Instantiate(TerrainMats[i]);
            indirectTerrainMats[i].EnableKeyword("INDIRECT");
        }
    }

    /// <summary> Releases all variant materials created in <see cref="indirectTerrainMats"/> </summary>
    public void Release(){
        for(int i = 0; i < indirectTerrainMats.Length; i++){
            if(indirectTerrainMats[i] == null) continue;
            Object.Destroy(indirectTerrainMats[i]);
        }
    }
}
}
