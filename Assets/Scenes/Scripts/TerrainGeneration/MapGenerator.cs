using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading;

public class MapGenerator : MonoBehaviour
{
    public enum DrawMode { Voxel, March }
    public DrawMode drawMode;

    public MeshFilter meshFilter;

    [HideInInspector]
    public const int mapChunkSize = 36;
    public static readonly int[] meshSkipTable = { 1, 2, 4, 6, 9, 12 };
    public Vector3 EditorOffset;
    [Range(0, 4)]
    public int EditorLoD;

    Queue<ThreadInfo> ThreadInfoQueue = new Queue<ThreadInfo>();
    
    public bool editorAutoUpdate;
    //[HideInInspector]
    //public VoxelMesh voxels = new VoxelMesh(); Voxel mesh crashes the game atm someone can fix this if they want


    public void GenerateMapInEditor()
    {

        if (drawMode == DrawMode.Voxel)
        {
            //voxels.GenerateVoxelMesh(terrainNoiseMap); 
        }
        else
        {
            EndlessTerrain settings = transform.GetComponent<EndlessTerrain>();
            ChunkData newMap = settings.meshCreator.ComputeMapData(settings.IsoLevel, settings.surfaceMaxDepth, EditorOffset, EditorLoD, settings.mapMaterial);
            meshFilter.sharedMesh = newMap.GenerateMesh();
            //ChunkData newMap = GenerateMapData(settings.TerrainNoise, settings.SurfaceNoise, settings.GenerationData, settings.IsoLevel, settings.surfaceMaxDepth, EditorOffset, EditorLoD);

        }
    }

    public void RequestData(Func<object> generateDatum, Action<object> callback)
    {
        ThreadStart threadStart = delegate
        {
            dataThread(generateDatum, callback);
        }; ;

        new Thread(threadStart).Start();//int LoD, Vector3 center,
    }

    void dataThread(Func<object> generateDatum, Action<object> callback)
    {
        object data = generateDatum();

        lock (ThreadInfoQueue)
        {
            ThreadInfoQueue.Enqueue(new ThreadInfo(callback, data));
        }
    }

    private void Update()
    {
        if (ThreadInfoQueue.Count > 0)
        {
            while (ThreadInfoQueue.Count > 0)
            {
                ThreadInfo threadInfo = ThreadInfoQueue.Dequeue();
                //if (threadInfo.callback == null) continue;
                threadInfo.callback(threadInfo.parameter);
                
            }
        }
    }
    

    struct ThreadInfo
    {
        public readonly Action<object> callback;
        public readonly object parameter;

        public ThreadInfo(Action<object> callback, object parameter)
        {
            this.callback = callback;
            this.parameter = parameter;
        }
    }


    public struct ChunkData
    {
        public float[,,] undergroundNoise;
        public float[,,] surfaceNoise;
        public float[,,] terrainNoiseMap;
        public MeshData meshData;
        public float[][] partialGenerationNoises;
        public Color[] colorMap;
        public float[][] heightCurves;
        public int[,,] materialMap;

        public Mesh GenerateMesh()
        {
            Mesh mesh = new Mesh();
            mesh.vertices = meshData.vertices.ToArray();
            mesh.triangles = meshData.triangles.ToArray();
            mesh.colors = colorMap;
            mesh.RecalculateNormals();
            return mesh;
        }

    }
}
