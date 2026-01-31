using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading;


namespace Arterra.Editor {
    public class EditorMesh : MonoBehaviour {
        public enum DrawMode { Voxel, March }
        public DrawMode drawMode;

        [HideInInspector]

        [Header("Editor Information")]
        public Vector3 EditorOffset;
        [Range(0, 4)]
        public int EditorLoD;
        public bool editorAutoUpdate;

        [Header("Dependencies")]
        public GameObject mesh;
        //public StructureGenerationData structureData;
        static EditorMesh instance;

        Queue<ThreadInfo> ThreadInfoQueue = new Queue<ThreadInfo>();


        void Awake() {
            instance = FindAnyObjectByType<EditorMesh>();
        }
        //[HideInInspector]
        //public VoxelMesh voxels = new VoxelMesh(); Voxel mesh crashes the game atm someone can fix this if they want


        public void GenerateMapInEditor() {

            if (drawMode == DrawMode.Voxel) {
                //voxels.GenerateVoxelMesh(terrainNoiseMap); 
            } else {

            }
        }

        public static void RequestData(Func<object> generateDatum, Action<object> callback) {
            ThreadStart threadStart = delegate {
                instance.dataThread(generateDatum, callback);
            };

            new Thread(threadStart).Start();//int LoD, Vector3 center,
        }

        void dataThread(Func<object> generateDatum, Action<object> callback) {
            object data = generateDatum();

            lock (ThreadInfoQueue) {
                ThreadInfoQueue.Enqueue(new ThreadInfo(callback, data));
            }
        }

        private void Update() {
            if (ThreadInfoQueue.Count > 0) {
                while (ThreadInfoQueue.Count > 0) {
                    ThreadInfo threadInfo = ThreadInfoQueue.Dequeue();
                    //if (threadInfo.callback == null) continue;
                    threadInfo.callback(threadInfo.parameter);

                }
            }
        }


        struct ThreadInfo {
            public readonly Action<object> callback;
            public readonly object parameter;

            public ThreadInfo(Action<object> callback, object parameter) {
                this.callback = callback;
                this.parameter = parameter;
            }
        }
    }
}
