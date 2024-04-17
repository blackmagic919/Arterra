using UnityEditor;
using UnityEngine;
using Utils;

public class DensityAdjuster : MonoBehaviour
{
    public StructureData structure;
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
                    int index = CustomUtility.irregularIndexFromCoord(x, y, z, settings.sizeX, settings.sizeY);
                    if (structure.density[index] == 0)
                        continue;
                    structure.density[index] += deltaDensity;
                }
            }
        }
        EditorUtility.SetDirty(structure);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
