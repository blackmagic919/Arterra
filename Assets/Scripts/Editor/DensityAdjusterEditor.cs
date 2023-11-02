using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DensityAdjuster)), CanEditMultipleObjects]
public class DensityAdjusterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DensityAdjuster deconstructor = (DensityAdjuster)target;

        if (GUILayout.Button("Transform Density"))
        {
            deconstructor.TransformDensity();
        }

        base.OnInspectorGUI();
    }
}
