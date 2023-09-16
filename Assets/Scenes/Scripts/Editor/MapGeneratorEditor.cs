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
                mapGen.DeleteMap();
                mapGen.GenerateMap();
            }
        }

        if (GUILayout.Button("Generate"))
        {
            mapGen.DeleteMap();
            mapGen.GenerateMap();
        }

        if (GUILayout.Button("Delete"))
        {
            mapGen.DeleteMap();
        }

        base.OnInspectorGUI();
    }
}
