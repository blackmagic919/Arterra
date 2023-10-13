using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading;
using System.Linq;

public class MapGenerator : MonoBehaviour
{
    public enum DrawMode { Voxel, March }
    public DrawMode drawMode;

    [HideInInspector]
    public const int mapChunkSize = 36;
    public static readonly int[] meshSkipTable = { 1, 2, 4, 6, 9, 12 };

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
        instance = FindObjectOfType<MapGenerator>();
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

            settings.meshCreator.GenerationData = settings.GenerationData;
            //settings.grassRenderer.GenerationData = settings.GenerationData;
            //settings.grassRenderer.filter = grassFilter;
            //settings.texData.GenerateTextureArray(settings.texData.TextureDictionary.ToArray());

            ChunkData newMap = settings.meshCreator.GenerateMapData(settings.IsoLevel, settings.surfaceMaxDepth, EditorOffset, EditorLoD);

            MeshFilter meshFilter = mesh.GetComponent<MeshFilter>();
            meshFilter.sharedMesh = newMap.GenerateMesh();

            settings.texData.ApplyToMaterial();
            
            grassRenderer.grassSettings = settings.grassSettings;
            grassRenderer.sourceMesh = newMap.GenerateGrass();
            grassRenderer.OnStart();
            //settings.grassRenderer.generatePoints(meshFilter.sharedMesh.triangles, meshFilter.sharedMesh.vertices, meshFilter.sharedMesh.colors, meshFilter.sharedMesh.normals, Vector3.zero, 2.5f);
            //ChunkData newMap = GenerateMapData(settings.TerrainNoise, settings.SurfaceNoise, settings.GenerationData, settings.IsoLevel, settings.surfaceMaxDepth, EditorOffset, EditorLoD);
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
        public MeshData grassMeshData;
        public float[][] heightCurves;
        public List<Vector3> vertexParents;

        public ChunkData()
        {
            vertexParents = new List<Vector3>();
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

        public Mesh GenerateGrass()
        {
            Mesh mesh = new Mesh();
            mesh.vertices = grassMeshData.vertices.ToArray();
            mesh.triangles = grassMeshData.triangles.ToArray();
            mesh.colors = grassMeshData.colorMap.ToArray();
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
