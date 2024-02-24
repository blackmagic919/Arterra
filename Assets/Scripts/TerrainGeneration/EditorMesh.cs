using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading;
using System.Linq;
using UnityEditor.Rendering;
using UnityEngine.Analytics;
using static UnityEngine.Mesh;

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
    public StructureGenerationData structureData;
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

    public class MeshInfo
    {
        public List<Vector3> vertices;
        public List<Vector3> normals;
        public List<Vector2> UVs;
        public List<Color> colorMap;
        public List<int> triangles;
        public List<UnityEngine.Rendering.SubMeshDescriptor> subMeshes;

        public MeshInfo()
        {
            vertices = new List<Vector3>();
            normals = new List<Vector3>();
            UVs = new List<Vector2>();
            triangles = new List<int>();
            colorMap = new List<Color>();
            subMeshes = new List<UnityEngine.Rendering.SubMeshDescriptor>();
        }

        public static Mesh GenerateMesh(MeshInfo meshData)
        {
            Mesh mesh = new Mesh();
            mesh.vertices = meshData.vertices.ToArray();
            mesh.normals = meshData.normals.ToArray();
            mesh.triangles = meshData.triangles.ToArray();
            mesh.colors = meshData.colorMap.ToArray();
            if (meshData.UVs.Count > 0)
                mesh.uv = meshData.UVs.ToArray();
            if(meshData.subMeshes.Count > 0)
                mesh.SetSubMeshes(meshData.subMeshes.ToArray());
            return mesh;
        }

        public Mesh GenerateMesh(UnityEngine.Rendering.IndexFormat meshIndexFormat)
        {
            Mesh mesh = new Mesh();
            mesh.indexFormat = meshIndexFormat;
            mesh.vertices = vertices.ToArray();
            mesh.normals = normals.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.colors = colorMap.ToArray();
            if (UVs.Count > 0)
                mesh.uv = UVs.ToArray();
            if (subMeshes.Count > 0)
                mesh.SetSubMeshes(subMeshes.ToArray());
            return mesh;
        }

        public void AddTriangle(int a, int b, int c)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }
    }
}
