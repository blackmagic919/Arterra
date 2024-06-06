using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DensityDeconstructor)), CanEditMultipleObjects]
public class DensityDeconstructorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DensityDeconstructor deconstructor = (DensityDeconstructor)target;

        if(GUILayout.Button("Initialize Grid"))
        {
            deconstructor.InitializeGrid();
        }

        if(GUILayout.Button("Exit")){
            deconstructor.Release();
        }
        /*
        if (GUILayout.Button("Deconstruct"))
        {
            deconstructor.ExtractDensity();
        }

        if (GUILayout.Button("Reconstruct"))
        {
            deconstructor.BuildMesh();
        }*/

        base.OnInspectorGUI(); 
        
        GUILayout.Label("Transfer");

        if (GUILayout.Button("Save"))
        {
            deconstructor.SaveData();
        }
        if (GUILayout.Button("Load"))
        {
            deconstructor.LoadData();
        }
        if(GUILayout.Button("Convert"))
        {
            deconstructor.ConvertMesh();
        }
    }
}
