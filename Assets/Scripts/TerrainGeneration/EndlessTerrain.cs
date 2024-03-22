using System.Collections.Generic;
using System;
using UnityEngine;
using UnityStandardAssets.Characters.FirstPerson;
using UnityEngine.Profiling;
using Utils;

public class EndlessTerrain : MonoBehaviour
{
    [Header("Map Generic Information")]
    public GenerationSettings settings;
    public static float renderDistance;
    public const int mapChunkSize = 64; //Number of cubes;
    const float chunkUpdateThresh = 24f;
    const float sqrChunkUpdateThresh = chunkUpdateThresh * chunkUpdateThresh;
    public const float lerpScale = 2f;
    int chunksVisibleInViewDistance;

    [Header("Viewer Information")]
    public Transform viewer;
    //Pause Viewer until terrain is generated
    public RigidbodyFirstPersonController viewerRigidBody;
    public int maxFrameLoad = 50; //GPU load
    public static Vector3 viewerPosition;
    Vector3 oldViewerPos = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    public bool viewerActive = false;


    [Header("Dependencies")]
    public GenerationResources resources;
    //Ideally specialShaders should be in materialData, but can't compile monobehavior in an asset 

    public static readonly int[] meshSkipTable = { 1, 2, 4, 8, 16 }; //has to be 2x for proper stitching
    public static readonly int[] taskLoadTable = { 5, 5, 2, 5 };
    public static Queue<UpdateTask> MainLoopUpdateTasks = new Queue<UpdateTask>();
    public static PriorityQueue<GenTask, int> timeRequestQueue = new PriorityQueue<GenTask, int>(); //As GPU dispatch must happen linearly, queue to call them sequentially as prev is finished

    public static Queue<ChunkData> lastUpdateChunks = new Queue<ChunkData>();

    public static Dictionary<Vector3, TerrainChunk> terrainChunkDict = new Dictionary<Vector3, TerrainChunk>();
    public static Dictionary<Vector2, SurfaceChunk> surfaceChunkDict = new Dictionary<Vector2, SurfaceChunk>();


    void Start()
    {
        renderDistance = settings.detailLevels[settings.detailLevels.Length - 1].distanceThresh;
        chunksVisibleInViewDistance = Mathf.RoundToInt(renderDistance / mapChunkSize);

        resources.densityDict.InitializeManage(settings.detailLevels, mapChunkSize, lerpScale);
    }

    private void Update()
    {
        viewerPosition = viewer.position / lerpScale;
        if ((oldViewerPos - viewerPosition).sqrMagnitude > sqrChunkUpdateThresh)
        {
            oldViewerPos = viewerPosition;
            UpdateVisibleChunks();
        }
        StartGeneration();

        int UpdateTaskCount = MainLoopUpdateTasks.Count;
        for(int i = 0; i < UpdateTaskCount; i++){
            UpdateTask task = MainLoopUpdateTasks.Dequeue();
            
            if(!task.initialized){
                task.enqueued = false;
                continue;
            }

            task.Update();
            MainLoopUpdateTasks.Enqueue(task);
        }
    }
    

    private void OnDisable()
    {
        TerrainChunk[] chunks = new TerrainChunk[terrainChunkDict.Count];
        terrainChunkDict.Values.CopyTo(chunks, 0);
        foreach(TerrainChunk chunk in chunks)
        {
            chunk.DestroyChunk();
        }

        SurfaceChunk[] schunks = new SurfaceChunk[surfaceChunkDict.Count];
        surfaceChunkDict.Values.CopyTo(schunks, 0);
        foreach (SurfaceChunk chunk in schunks)
        {
            chunk.DestroyChunk();
        }
    }

    private void LateUpdate()
    {
        if (viewerActive)
            return;
        if (timeRequestQueue.Count > 0)
            return;
        viewerActive = true;
        viewerRigidBody.ActivateCharacter();
    }


    void StartGeneration()
    {
        int FrameGPULoad = 0;
        while(FrameGPULoad < maxFrameLoad)
        {
            if (!timeRequestQueue.TryDequeue(out GenTask gen, out int priority))
                return;

            Profiler.BeginSample($"Time Request Queue: {Enum.GetName(typeof(priorities), priority)}");
            gen.task.Invoke();
            Profiler.EndSample();

            FrameGPULoad += gen.genLoad;
        }
    }

    void UpdateVisibleChunks()
    {
        int CCCoordX = Mathf.RoundToInt(viewerPosition.x / mapChunkSize); //CurrentChunkCoord
        int CCCoordY = Mathf.RoundToInt(viewerPosition.y / mapChunkSize);
        int CCCoordZ = Mathf.RoundToInt(viewerPosition.z / mapChunkSize);
        Vector3 CCCoord = new Vector3(CCCoordX, CCCoordY, CCCoordZ);
        Vector2 CSCoord = new Vector2(CCCoordX, CCCoordZ);

        while (lastUpdateChunks.Count > 0)
        {
            lastUpdateChunks.Dequeue().UpdateVisibility(CCCoord, chunksVisibleInViewDistance);
        }

        for (int xOffset = -chunksVisibleInViewDistance; xOffset <= chunksVisibleInViewDistance; xOffset++)
        {
            for (int zOffset = -chunksVisibleInViewDistance; zOffset <= chunksVisibleInViewDistance; zOffset++)
            {
                Vector3 viewedSC = new Vector2(xOffset, zOffset) + CSCoord;
                SurfaceChunk curSChunk;
                if (surfaceChunkDict.TryGetValue(viewedSC, out curSChunk)) {
                    curSChunk.Update();
                }
                else {
                    curSChunk = new SurfaceChunk(resources.surfaceSettings, viewedSC);
                    surfaceChunkDict.Add(viewedSC, curSChunk);
                }

                for (int yOffset = -chunksVisibleInViewDistance; yOffset <= chunksVisibleInViewDistance; yOffset++)
                {
                    Vector3 viewedCC = new Vector3(xOffset, yOffset, zOffset) + CCCoord;
                    if (terrainChunkDict.ContainsKey(viewedCC))
                    {
                        TerrainChunk curChunk = terrainChunkDict[viewedCC];
                        curChunk.Update();
                    } else {
                        terrainChunkDict.Add(viewedCC, new TerrainChunk(viewedCC, settings.IsoLevel, transform, curSChunk, settings.detailLevels, resources));
                    }
                }
            }
        }
    }


    static bool SphereIntersectsBox(Vector3 sphereCentre, float sphereRadius, Vector3 boxCentre, Vector3 boxSize)
    {
        float closestX = Mathf.Clamp(sphereCentre.x, boxCentre.x - boxSize.x / 2, boxCentre.x + boxSize.x / 2);
        float closestY = Mathf.Clamp(sphereCentre.y, boxCentre.y - boxSize.y / 2, boxCentre.y + boxSize.y / 2);
        float closestZ = Mathf.Clamp(sphereCentre.z, boxCentre.z - boxSize.z / 2, boxCentre.z + boxSize.z / 2);

        float dx = closestX - sphereCentre.x;
        float dy = closestY - sphereCentre.y;
        float dz = closestZ - sphereCentre.z;

        float sqrDstToBox = dx * dx + dy * dy + dz * dz;
        return sqrDstToBox < sphereRadius * sphereRadius;
    }

    public void Terraform(Vector3 terraformPoint, float terraformRadius, Func<TerrainChunk.MapData, float, TerrainChunk.MapData> handleTerraform)
    {
        int CCCoordX = Mathf.RoundToInt(terraformPoint.x / (mapChunkSize*lerpScale));
        int CCCoordY = Mathf.RoundToInt(terraformPoint.y / (mapChunkSize*lerpScale));
        int CCCoordZ = Mathf.RoundToInt(terraformPoint.z / (mapChunkSize*lerpScale));

        int chunkTerraformRadius = Mathf.CeilToInt(terraformRadius / (mapChunkSize* lerpScale));

        for(int x = -chunkTerraformRadius; x <= chunkTerraformRadius; x++)
        {
            for (int y = -chunkTerraformRadius; y <= chunkTerraformRadius; y++)
            {
                for (int z = -chunkTerraformRadius; z <= chunkTerraformRadius; z++)
                {
                    Vector3 viewedCC = new Vector3(x + CCCoordX, y + CCCoordY, z + CCCoordZ);

                    if (!terrainChunkDict.ContainsKey(viewedCC))
                        continue;
                    //For some reason terraformRadius itself isn't updating all the chunks properly
                    if (SphereIntersectsBox(terraformPoint, (terraformRadius+1), mapChunkSize * lerpScale * viewedCC, (mapChunkSize+1) * lerpScale * Vector3.one)) { 
                        TerrainChunk curChunk = terrainChunkDict[viewedCC];
                        curChunk.TerraformChunk(terraformPoint, terraformRadius, handleTerraform);
                    }
                }
            }
        }
    }

    public struct GenTask{
        public Action task;
        public int genLoad;
        public GenTask(Action task, int genLoad){
            this.task = task;
            this.genLoad = genLoad;
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

