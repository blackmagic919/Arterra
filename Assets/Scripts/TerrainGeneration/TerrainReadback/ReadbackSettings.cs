using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Settings/Readback")]
public class ReadbackSettings : ScriptableObject
{
    [Header("Indirectly Drawn Shader")]
    public List<Material> TerrainMats; 
    [HideInInspector]
    public Material[] indirectTerrainMats;

    public void Initialize(){
        indirectTerrainMats = new Material[TerrainMats.Count];
        for(int i = 0; i < TerrainMats.Count; i++){
            if(indirectTerrainMats[i] != null) continue;
            indirectTerrainMats[i] = Object.Instantiate(TerrainMats[i]);
            indirectTerrainMats[i].EnableKeyword("INDIRECT");
        }
    }

    public void Release(){
        for(int i = 0; i < indirectTerrainMats.Length; i++){
            if(indirectTerrainMats[i] == null) continue;
            Object.Destroy(indirectTerrainMats[i]);
        }
    }
}
