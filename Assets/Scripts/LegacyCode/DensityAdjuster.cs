using UnityEditor;
using UnityEngine;
using Utils;

public class DensityAdjuster : MonoBehaviour
{
    #if UNITY_EDITOR
    public Structure structure;
    public StructureSettings settings;
    public float deltaDensity;

    public void TransformDensity()
    {
        for(int x = 0; x < settings.sizeX; x++)
        {
            for (int y = 0; y < settings.sizeY; y++)
            {
                for (int z = 0; z < settings.sizeZ; z++)
                {
                    int index = CustomUtility.irregularIndexFromCoord(x, y, z, settings.sizeY, settings.sizeZ);
                    if (structure.This.map[index].density == 0)
                        continue;
                    structure.This.map[index].density += (int)(deltaDensity * 255);
                }
            }
        }
        EditorUtility.SetDirty(structure);
        AssetDatabase.SaveAssets();//
        AssetDatabase.Refresh();
    }
    #endif
}