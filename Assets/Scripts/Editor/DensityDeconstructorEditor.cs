using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DensityDeconstructor)), CanEditMultipleObjects]
public class DensityDeconstructorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DensityDeconstructor deconstructor = (DensityDeconstructor)target;

        if (GUILayout.Button("Deconstruct"))
        {
            deconstructor.ExtractDensity();
        }

        if (GUILayout.Button("Reconstruct"))
        {
            deconstructor.BuildMesh();
        }

        if (GUILayout.Button("Set Material"))
        {
            deconstructor.SetMaterial();
        }

        if (GUILayout.Button("Visualize Material"))
        {
            deconstructor.VisualizeMaterial();
        }

        if (GUILayout.Button("Dispose Materials"))
        {
            deconstructor.DisposeMaterials();
        }

        if (GUILayout.Button("Save"))
        {
            deconstructor.SaveData();
        }

        base.OnInspectorGUI(); 
    }
}
