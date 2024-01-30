using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading;
using System.Linq;
using UnityEditor.Rendering;
using UnityEngine.Analytics;

public class EditorMesh : MonoBehaviour
{
    public enum DrawMode { Voxel, March }
    public DrawMode drawMode;

    [HideInInspector]
    public static readonly int[] meshSkipTable = { 1, 2, 4, 8, 16 }; //has to be multiple

    [Header("Editor Information")]
    public Vector3 EditorOffset;
    [Range(0, 4)]
    public int EditorLoD;
    public bool editorAutoUpdate;

    [Header("Dependencies")]
    public GameObject mesh;
    public ProceduralGrassRenderer grassRenderer;
    static EditorMesh instance;

    Queue<ThreadInfo> ThreadInfoQueue = new Queue<ThreadInfo>();


    void Awake()
    {
        instance = FindAnyObjectByType<EditorMesh>();
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
            Vector3 CCoord = new (Mathf.FloorToInt(EditorOffset.x / EndlessTerrain.mapChunkSize), Mathf.FloorToInt(EditorOffset.y / EndlessTerrain.mapChunkSize), Mathf.FloorToInt(EditorOffset.z / EndlessTerrain.mapChunkSize));
            SurfaceChunk.LODMap surfaceData = new SurfaceChunk.LODMap(settings.mapCreator, new Vector2(CCoord.x, CCoord.z), EditorLoD);
            surfaceData.GetChunk(() => { });

            settings.meshCreator.biomeData = settings.biomeData;
            settings.mapCreator.biomeData = settings.biomeData;

            settings.meshCreator.GenerateDensity(surfaceData, EditorOffset, EditorLoD, EndlessTerrain.mapChunkSize, settings.IsoLevel);
            settings.meshCreator.GenerateMaterials(surfaceData, EditorOffset, EditorLoD, EndlessTerrain.mapChunkSize);
            //Chunkbuffers chunkData = settings.meshCreator.GenerateMapData(settings.IsoLevel, EditorLoD, EndlessTerrain.mapChunkSize);
            settings.meshCreator.ReleaseTempBuffers();
 
            MeshFilter meshFilter = mesh.GetComponent<MeshFilter>();
            //meshFilter.sharedMesh = chunkData.GenerateMesh();

            settings.texData.ApplyToMaterial();
            settings.structData.ApplyToMaterial();
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
        public MeshInfo meshData;
        public List<Vector3> vertexParents;

        public ChunkData()
        {
            vertexParents = new List<Vector3>();
            meshData = new MeshInfo();
        }

        public static Mesh GenerateMesh(MeshInfo meshData)
        {
            Mesh mesh = new Mesh();
            mesh.vertices = meshData.vertices.ToArray();
            mesh.normals = meshData.normals.ToArray();
            mesh.triangles = meshData.triangles.ToArray();
            mesh.colors = meshData.colorMap.ToArray();
            return mesh;
        }

        public Mesh GenerateMesh()
        {
            Mesh mesh = new Mesh();
            mesh.vertices = meshData.vertices.ToArray();
            mesh.normals = meshData.normals.ToArray();
            mesh.triangles = meshData.triangles.ToArray();
            mesh.colors = meshData.colorMap.ToArray();
            return mesh;
        }
    }

    public class MeshInfo
    {
        public List<Vector3> vertices;
        public List<Vector3> normals;
        public List<Color> colorMap;
        public List<int> triangles;

        public MeshInfo()
        {
            vertices = new List<Vector3>();
            normals = new List<Vector3>();
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
