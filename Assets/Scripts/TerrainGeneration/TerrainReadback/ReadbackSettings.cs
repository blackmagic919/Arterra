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

    [Header("Buffer Space")]
    public MemoryBufferSettings memoryBuffer;

    public void OnEnable(){
        indirectTerrainMats = new Material[TerrainMats.Count];
        for(int i = 0; i < TerrainMats.Count; i++){
            indirectTerrainMats[i] = Object.Instantiate(TerrainMats[i]);
            indirectTerrainMats[i].EnableKeyword("INDIRECT");
        }
    }

    public void OnDisable(){
        for(int i = 0; i < indirectTerrainMats.Length; i++){
            if(Application.isPlaying) Object.Destroy(indirectTerrainMats[i]);
            else Object.DestroyImmediate(indirectTerrainMats[i]);
        }
    }
}
