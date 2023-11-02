using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading;
using System.Linq;
using static GenerationHeightData;
using UnityEditor.Rendering;
using UnityEngine.Analytics;

public class MapGenerator : MonoBehaviour
{
    public enum DrawMode { Voxel, March }
    public DrawMode drawMode;

    [HideInInspector]
    public const int mapChunkSize = 48;
    public static readonly int[] meshSkipTable = { 1, 2, 4, 8, 16 }; //has to be multiple

    [Header("Editor Information")]
    public Vector3 EditorOffset;
    [Range(0, 4)]
    public int EditorLoD;
    public bool editorAutoUpdate;

    [Header("Dependencies")]
    public GameObject mesh;
    public ProceduralGrassRenderer grassRenderer;
    static MapGenerator instance;

    Queue<ThreadInfo> ThreadInfoQueue = new Queue<ThreadInfo>();


    void Awake()
    {
        instance = FindAnyObjectByType<MapGenerator>();
    }
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
            Vector3 CCoord = new (Mathf.FloorToInt(EditorOffset.x / mapChunkSize), Mathf.FloorToInt(EditorOffset.y / mapChunkSize), Mathf.FloorToInt(EditorOffset.z / mapChunkSize));

            settings.meshCreator.ResetStructureDictionary();
            settings.meshCreator.PlanStructures(CCoord, EditorOffset, mapChunkSize, settings.surfaceMaxDepth, settings.IsoLevel);
            settings.meshCreator.GenerationData = settings.GenerationData;
            settings.meshCreator.GenerateDensity(EditorOffset, EditorLoD, settings.surfaceMaxDepth, mapChunkSize, settings.IsoLevel);
            settings.meshCreator.GenerateStructures(CCoord, settings.IsoLevel, EditorLoD, mapChunkSize);
            ChunkData chunkData = settings.meshCreator.GenerateMapData(settings.IsoLevel, EditorOffset, EditorLoD, mapChunkSize);
            settings.meshCreator.ReleaseBuffers();
 
            MeshFilter meshFilter = mesh.GetComponent<MeshFilter>();
            meshFilter.sharedMesh = chunkData.GenerateMesh();

            settings.texData.ApplyToMaterial();
        }
    }

    public static void RequestData(Func<object> generateDatum, Action<object> callback)
    {
        ThreadStart threadStart = delegate
        {
            instance.dataThread(generateDatum, callback);
        };

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


    public class ChunkData
    {
        public MeshData meshData;
        public float[][] heightCurves;
        public List<Vector3> vertexParents;

        public ChunkData()
        {
            vertexParents = new List<Vector3>();
        }

        public Mesh GenerateMesh(MeshData meshData)
        {
            Mesh mesh = new Mesh();
            mesh.vertices = meshData.vertices.ToArray();
            mesh.triangles = meshData.triangles.ToArray();
            mesh.colors = meshData.colorMap.ToArray();
            mesh.RecalculateNormals();
            return mesh;
        }

        public Mesh GenerateMesh()
        {
            Mesh mesh = new Mesh();
            mesh.vertices = meshData.vertices.ToArray();
            mesh.triangles = meshData.triangles.ToArray();
            mesh.colors = meshData.colorMap.ToArray();
            mesh.RecalculateNormals();
            return mesh;
        }
    }

    public class MeshData
    {
        public List<Vector3> vertices;
        public List<Color> colorMap;
        public List<int> triangles;

        public MeshData()
        {
            vertices = new List<Vector3>();
            triangles = new List<int>();
            colorMap = new List<Color>();
        }

        public void AddTriangle(int a, int b, int c)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

    }
}
