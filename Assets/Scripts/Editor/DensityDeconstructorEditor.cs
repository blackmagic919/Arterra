using UnityEditor;
using UnityEngine;

namespace Arterra.Editor {
    [CustomEditor(typeof(DensityDeconstructor)), CanEditMultipleObjects]
    public class DensityDeconstructorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DensityDeconstructor deconstructor = (DensityDeconstructor)target;

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
            if(GUILayout.Button("Resize"))
            {
                deconstructor.ResizeStructure();
            }
            if(GUILayout.Button("Shift"))
            {
                deconstructor.ShiftStructure();
            }
            if(GUILayout.Button("LoadChunk"))
            {
                deconstructor.LoadChunk();
            }
        }
    }
}