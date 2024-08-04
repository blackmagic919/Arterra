using System.Collections.Generic;
using System;
using UnityEngine;
using System.Diagnostics;
using Unity.Mathematics;
using System.Collections.Concurrent;

public class EndlessTerrain : MonoBehaviour
{
    int chunksVisibleInViewDistance;

    [Header("Viewer Information")]
    public GameObject viewer;
    //Pause Viewer until terrain is generated
    public static int maxFrameLoad = 250; //GPU load
    public static Vector3 viewerPosition;
    Vector3 oldViewerPos = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);

    //Ideally specialShaders should be in materialData, but can't compile monobehavior in an asset 

    public static readonly int[] meshSkipTable = { 1, 2, 4, 8, 16 }; //has to be 2x for proper stitching
    public static readonly int[] taskLoadTable = { 5, 3, 2, 3 };
    public static Queue<UpdateTask> MainLateUpdateTasks = new Queue<UpdateTask>();
    public static Queue<UpdateTask> MainFixedUpdateTasks = new Queue<UpdateTask>();
    public static ConcurrentQueue<GenTask> RequestQueue = new ConcurrentQueue<GenTask>(); //As GPU dispatch must happen linearly, queue to call them sequentially as prev is finished

    public static Queue<ChunkData> lastUpdateChunks = new Queue<ChunkData>();

    public static Dictionary<int3, TerrainChunk> terrainChunkDict = new Dictionary<int3, TerrainChunk>();
    public static Dictionary<int2, SurfaceChunk> surfaceChunkDict = new Dictionary<int2, SurfaceChunk>();
    private RenderSettings rSettings;

    void OnEnable(){
        GenerationPreset.Initialize();
        UtilityBuffers.Initialize();

        rSettings = WorldStorageHandler.WORLD_OPTIONS.Rendering.value;
        chunksVisibleInViewDistance = rSettings.detailLevels.value[^1].chunkDistThresh;

        ChunkStorageManager.Initialize();
        GPUDensityManager.Initialize();
        CPUDensityManager.Intiialize();
        EntityManager.Initialize();
        StructureGenerator.PresetData();
        TerrainGenerator.PresetData();
        DensityGenerator.PresetData();
        ShaderGenerator.PresetData();
        WorldStorageHandler.WORLD_OPTIONS.Atmosphere.value.pass.Initialize();
    }

    private void Update()
    {
        viewerPosition = CPUDensityManager.WSToGS(viewer.transform.position);
        if ((oldViewerPos - viewerPosition).magnitude > rSettings.chunkUpdateThresh)
        {
            oldViewerPos = viewerPosition;
            UpdateVisibleChunks();
        }
        StartGeneration();
    }
    
    private void LateUpdate()
    {
        int UpdateTaskCount = MainLateUpdateTasks.Count;
        for(int i = 0; i < UpdateTaskCount; i++){
            UpdateTask task = MainLateUpdateTasks.Dequeue();
            if(!task.active)
                continue;
            task.Update(this);
            MainLateUpdateTasks.Enqueue(task);
        }
    }

    private void FixedUpdate()
    {
        int UpdateTaskCount = MainFixedUpdateTasks.Count;
        for(int i = 0; i < UpdateTaskCount; i++){
            UpdateTask task = MainFixedUpdateTasks.Dequeue();
            if(!task.active)
                continue;
            task.Update(this);
            MainFixedUpdateTasks.Enqueue(task);
        }
    }

    
    
    void OnDrawGizmos(){
        /*foreach(KeyValuePair<int3, TerrainChunk> chunk in terrainChunkDict){
            if(chunk.Value.prevMapLOD == 4) Gizmos.DrawWireCube(chunk.Value.position * lerpScale, mapChunkSize * lerpScale * Vector3.one);
        }*/
        EntityManager.OnDrawGizmos();
    }
    private void OnDisable()
    {
        TerrainChunk[] chunks = new TerrainChunk[terrainChunkDict.Count];
        terrainChunkDict.Values.CopyTo(chunks, 0);
        foreach(TerrainChunk chunk in chunks)
            chunk.DestroyChunk();

        SurfaceChunk[] schunks = new SurfaceChunk[surfaceChunkDict.Count];
        surfaceChunkDict.Values.CopyTo(schunks, 0);
        foreach (SurfaceChunk chunk in schunks)
            chunk.DestroyChunk();

        UtilityBuffers.Release();
        GPUDensityManager.Release();
        CPUDensityManager.Release();
        EntityManager.Release();
        GenerationPreset.Release();
        WorldStorageHandler.WORLD_OPTIONS.Atmosphere.value.pass.Release();
    }


    void StartGeneration()
    {
        int FrameGPULoad = 0;
        while(FrameGPULoad < maxFrameLoad)
        {
            if (!RequestQueue.TryDequeue(out GenTask gen))
                return;

            if(gen.valid()) gen.task();

            FrameGPULoad += gen.load;
        }
    }

    void UpdateVisibleChunks()
    {
        int CCCoordX = Mathf.RoundToInt(viewerPosition.x / rSettings.mapChunkSize); //CurrentChunkCoord
        int CCCoordY = Mathf.RoundToInt(viewerPosition.y / rSettings.mapChunkSize);
        int CCCoordZ = Mathf.RoundToInt(viewerPosition.z / rSettings.mapChunkSize);
        int3 CCCoord = new int3(CCCoordX, CCCoordY, CCCoordZ);
        int2 CSCoord = new int2(CCCoordX, CCCoordZ);

        while (lastUpdateChunks.Count > 0)
        {
            lastUpdateChunks.Dequeue().UpdateVisibility(CCCoord, chunksVisibleInViewDistance);
        }

        for (int xOffset = -chunksVisibleInViewDistance; xOffset <= chunksVisibleInViewDistance; xOffset++)
        {
            for (int zOffset = -chunksVisibleInViewDistance; zOffset <= chunksVisibleInViewDistance; zOffset++)
            {
                int2 viewedSC = new int2(xOffset, zOffset) + CSCoord;
                SurfaceChunk curSChunk;
                if (surfaceChunkDict.TryGetValue(viewedSC, out curSChunk)) {
                    curSChunk.Update();
                }
                else {
                    curSChunk = new SurfaceChunk(viewedSC);
                    surfaceChunkDict.Add(viewedSC, curSChunk);
                }

                for (int yOffset = -chunksVisibleInViewDistance; yOffset <= chunksVisibleInViewDistance; yOffset++)
                {
                    int3 viewedCC = new int3(xOffset, yOffset, zOffset) + CCCoord;
                    if (terrainChunkDict.ContainsKey(viewedCC)) terrainChunkDict[viewedCC].Update();
                    else terrainChunkDict.Add(viewedCC, new TerrainChunk(viewedCC, transform, curSChunk));
                }
            }
        }
    }


    public struct GenTask{
        public Func<bool> valid;
        public Action task;
        public int load;
        public GenTask(Func<bool> valid, Action task, int genLoad){
            this.valid = valid;
            this.task = task;
            this.load = genLoad;
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

