using System.Collections.Generic;
using System;
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Concurrent;
using Utils;

public class EndlessTerrain : MonoBehaviour
{

    [Header("Viewer Information")]
    public GameObject viewer;
    //Pause Viewer until terrain is generated
    public static int maxFrameLoad = 250; //GPU load
    public static Vector3 viewerPosition;
    Vector3 oldViewerPos = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);

    //Ideally specialShaders should be in materialData, but can't compile monobehavior in an asset 

    public static readonly int[] meshSkipTable = { 1, 2, 4, 8, 16 }; //has to be 2x for proper stitching
    public static readonly int[] taskLoadTable = { 5, 3, 2, 3, 0 };
    public static Queue<UpdateTask> MainLoopUpdateTasks;
    public static Queue<UpdateTask> MainLateUpdateTasks;
    public static Queue<UpdateTask> MainFixedUpdateTasks;
    public static ConcurrentQueue<GenTask> RequestQueue; //As GPU dispatch must happen linearly, queue to call them sequentially as prev is finished
    public static TerrainChunk[] TerrainChunks;
    public static SurfaceChunk[] SurfaceChunks;
    private RenderSettings rSettings;

    void OnEnable(){
        MainLoopUpdateTasks = new Queue<UpdateTask>();
        MainLateUpdateTasks = new Queue<UpdateTask>();
        MainFixedUpdateTasks = new Queue<UpdateTask>();
        RequestQueue = new ConcurrentQueue<GenTask>();
        
        RegisterBuilder.Initialize();
        GenerationPreset.Initialize();
        UtilityBuffers.Initialize();

        rSettings = WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value;
        viewerPosition = CPUDensityManager.WSToGS(viewer.transform.position);

        InputPoller.Initialize();
        UIOrigin.Initialize();

        ChunkStorageManager.Initialize();
        GPUDensityManager.Initialize();
        CPUDensityManager.Initialize();
        EntityManager.Initialize();
        TerrainUpdateManager.Initialize();
        StructureGenerator.PresetData();
        TerrainGenerator.PresetData();
        DensityGenerator.PresetData();
        ShaderGenerator.PresetData();
        AtmospherePass.Initialize();
        WorldStorageHandler.WORLD_OPTIONS.System.ReadBack.value.Initialize();
    }

    private void OnDisable()
    {
        foreach(TerrainChunk chunk in TerrainChunks)
            chunk.DestroyChunk();
        foreach (SurfaceChunk chunk in SurfaceChunks)
            chunk.DestroyChunk();

        UIOrigin.Release();
        UtilityBuffers.Release();
        GPUDensityManager.Release();
        CPUDensityManager.Release();
        EntityManager.Release();
        GenerationPreset.Release();
        AtmospherePass.Release();
        WorldStorageHandler.WORLD_OPTIONS.System.ReadBack.value.Release();
    }

    private void Start()
    {
        viewerPosition = CPUDensityManager.WSToGS(viewer.transform.position);
        InitializeAllChunks();
    }

    private void InitializeAllChunks(){
        int numChunksAxis = rSettings.detailLevels.value[^1].chunkDistThresh * 2;
        int numChunks2D = numChunksAxis * numChunksAxis;
        int numChunks = numChunks2D * numChunksAxis;
        TerrainChunks = new TerrainChunk[numChunks];
        SurfaceChunks = new SurfaceChunk[numChunks2D];

        for(int x = 0; x < numChunksAxis; x++){
            for(int z = 0; z < numChunksAxis; z++){
                int index2D = CustomUtility.indexFromCoord2D(x, z, numChunksAxis);
                SurfaceChunks[index2D] = new SurfaceChunk(new int2(x,z));
                for(int y = 0; y < numChunksAxis; y++){
                    int3 CCoord = new (x,y,z);
                    int index = CustomUtility.indexFromCoord(CCoord, numChunksAxis);
                    TerrainChunks[index] = new TerrainChunk(CCoord, transform, SurfaceChunks[index2D]);
                }
            }
        }
    }

    private void Update()
    {
        viewerPosition = CPUDensityManager.WSToGS(viewer.transform.position);
        UpdateChunks();
        StartGeneration();

        ProcessUpdateTasks(MainLoopUpdateTasks);
    }
    
    private void LateUpdate()
    {
        ProcessUpdateTasks(MainLateUpdateTasks);
    }

    private void FixedUpdate()
    {
        ProcessUpdateTasks(MainFixedUpdateTasks);
    }

    private void ProcessUpdateTasks(Queue<UpdateTask> taskQueue)
    {
        int UpdateTaskCount = taskQueue.Count;
        for(int i = 0; i < UpdateTaskCount; i++){
            UpdateTask task = taskQueue.Dequeue();
            if(!task.active)
                continue;
            task.Update(this);
            taskQueue.Enqueue(task);
        }
    }

    
    
    void OnDrawGizmos(){
        /*foreach(KeyValuePair<int3, TerrainChunk> chunk in terrainChunkDict){
            if(chunk.Value.prevMapLOD == 4) Gizmos.DrawWireCube(chunk.Value.position * lerpScale, mapChunkSize * lerpScale * Vector3.one);
        }*/
        EntityManager.OnDrawGizmos();
    }


    void StartGeneration()
    {
        int FrameGPULoad = 0;
        while(FrameGPULoad < maxFrameLoad)
        {
            if (!RequestQueue.TryDequeue(out GenTask gen))
                return;

            gen.task();
            FrameGPULoad += taskLoadTable[gen.id];
        }
    }

    void UpdateChunks()
    {
        if ((oldViewerPos - viewerPosition).magnitude > rSettings.chunkUpdateThresh)
        {
            oldViewerPos = viewerPosition;
            for(int i = 0; i < SurfaceChunks.Length; i++)
                SurfaceChunks[i].ValidateChunk();
            for(int i = 0; i < TerrainChunks.Length; i++)
                TerrainChunks[i].ValidateChunk();
        }
        for(int i = 0; i < TerrainChunks.Length; i++)
            TerrainChunks[i].Update();
    }

    public struct GenTask{
        public Action task;
        public int id;
        public GenTask(Action task, int id){
            this.task = task;
            this.id = id;
        }
    }

    public class MeshInfo
    {
        public List<Vector3> vertices;
        public List<Vector3> normals;
        public List<Vector2> UVs;
        public List<Color> colorMap;
        public List<int> triangles;
        public UnityEngine.Rendering.SubMeshDescriptor[] subMeshes;

        public MeshInfo()
        {
            vertices = new List<Vector3>();
            normals = new List<Vector3>();
            UVs = new List<Vector2>();
            triangles = new List<int>();
            colorMap = new List<Color>();
            subMeshes = new UnityEngine.Rendering.SubMeshDescriptor[0];
        }


        public Mesh GenerateMesh(UnityEngine.Rendering.IndexFormat meshIndexFormat)
        {
            Mesh mesh = new Mesh
            {
                indexFormat = meshIndexFormat,
                vertices = vertices.ToArray(),
                normals = normals.ToArray(),
                triangles = triangles.ToArray(),
                colors = colorMap.ToArray()
            };
            
            if (UVs.Count > 0)
                mesh.uv = UVs.ToArray();
            if (subMeshes.Length > 0)
                mesh.SetSubMeshes(subMeshes);
            return mesh;
        }

        public Mesh GetSubmesh(int submeshIndex, UnityEngine.Rendering.IndexFormat meshIndexFormat){
            Mesh mesh = new Mesh
            {
                indexFormat = meshIndexFormat,
                vertices = vertices.ToArray(),
                normals = normals.ToArray(),
                triangles = triangles.GetRange(subMeshes[submeshIndex].indexStart, subMeshes[submeshIndex].indexCount).ToArray(),
                colors = colorMap.ToArray()
            };
            
            if (UVs.Count > 0)
                mesh.uv = UVs.ToArray();
            return mesh.triangles.Length == 0 ? null : mesh;
        }

        public void AddTriangle(int a, int b, int c)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }
    }
}

