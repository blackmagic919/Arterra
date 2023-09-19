using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor (typeof (MapGenerator))]
public class MapGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        MapGenerator mapGen = (MapGenerator)target;

        if (DrawDefaultInspector())
        {
            if (mapGen.editorAutoUpdate)
            {
                //mapGen.voxels.DeleteMap();
                mapGen.GenerateMapInEditor();
            }
        }

        if (GUILayout.Button("Generate"))
        {
            //mapGen.voxels.DeleteMap();
            mapGen.GenerateMapInEditor();
        }

        if (GUILayout.Button("Delete"))
        {
            //mapGen.voxels.DeleteMap();
        }

        base.OnInspectorGUI();
    }
}
