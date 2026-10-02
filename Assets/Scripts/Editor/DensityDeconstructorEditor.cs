using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Arterra.Editor {
    [CustomEditor(typeof(DensityDeconstructor)), CanEditMultipleObjects]
    public class DensityDeconstructorEditor : UnityEditor.Editor
    {
        private bool loading;

        private async void RunOperation(Func<Task> operation) {
            if (loading) return;
            loading = true;
            try { await operation(); }
            catch (Exception exception) { Debug.LogException(exception); }
            finally {
                loading = false;
                if (this) Repaint();
            }
        }

        public override void OnInspectorGUI()
        {
            DensityDeconstructor deconstructor = (DensityDeconstructor)target;
            if (loading) EditorGUILayout.HelpBox("Loading world configuration…", MessageType.Info);
            using (new EditorGUI.DisabledScope(loading)) {

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
                RunOperation(deconstructor.LoadData);
            }
            if(GUILayout.Button("Convert"))
            {
                RunOperation(deconstructor.ConvertMesh);
            }
            if(GUILayout.Button("Resize"))
            {
                RunOperation(deconstructor.ResizeStructure);
            }
            if(GUILayout.Button("Shift"))
            {
                RunOperation(deconstructor.ShiftStructure);
            }
            if(GUILayout.Button("LoadChunk"))
            {
                RunOperation(deconstructor.LoadChunk);
            }
            }
        }
    }
}