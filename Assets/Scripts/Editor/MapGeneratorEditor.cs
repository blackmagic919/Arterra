using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor (typeof (EditorMesh))]
public class MapGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorMesh mapGen = (EditorMesh)target;

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
            mapGen.GenerateMapInEditor();
            //mapGen.voxels.DeleteMap();

        }
        
        if (GUILayout.Button("Delete"))
        {
            //mapGen.voxels.DeleteMap();
        }

        //base.OnInspectorGUI(); THis causes duplicate GUI
    }
}
