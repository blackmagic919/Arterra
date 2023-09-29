using System.Collections.Generic;
using System;
using UnityEngine;
using UnityStandardAssets.Characters.FirstPerson;

public class EndlessTerrain : MonoBehaviour
{
    const float chunkUpdateThresh = 20f;
    const float sqrChunkUpdateThresh = chunkUpdateThresh * chunkUpdateThresh;
    Vector3 oldViewerPos = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);

    public Material mapMaterial;

    public static float renderDistance;
    public Transform viewer;
    public static Vector3 viewerPosition;

    public LODInfo[] detailLevels;

    public MeshCreator meshCreator;

    const float lerpScale = 2.5f;
    public const int mapChunkSize = 36;
    public int surfaceMaxDepth;
    [Range(0, 1)]
    public float IsoLevel;

    int chunkSize;
    int chunksVisibleInViewDistance;
    static Queue<TerrainChunk> lastUpdateChunks = new Queue<TerrainChunk>();
    static Queue<Action> meshRequestQueue = new Queue<Action>(); //As GPU dispatch must happen linearly, queue to call them sequentially as prev is finished

    //Pause Viewer until terrain is generated
    public RigidbodyFirstPersonController viewerRigidBody;
    public bool viewerActive = false;
    public float genTimePerFrameMs;

    int loadingChunks = 0;

    Dictionary<Vector3, TerrainChunk> terrainChunkDict = new Dictionary<Vector3, TerrainChunk>();

    void Start()
    {
        chunkSize = mapChunkSize;
        renderDistance = detailLevels[detailLevels.Length - 1].distanceThresh;
        chunksVisibleInViewDistance = Mathf.RoundToInt(renderDistance / chunkSize);

        UpdateVisibleChunks();
    }

    private void Update()
    {
        viewerPosition = viewer.position / lerpScale;
        if ((oldViewerPos - viewerPosition).sqrMagnitude > sqrChunkUpdateThresh && meshRequestQueue.Count == 0)
        {
            oldViewerPos = viewerPosition;
            UpdateVisibleChunks();
        }
        StartGeneration();
    }

    private void onChunkRecieved(bool completedChunk)
    {
        if (viewerActive)
            return;

        loadingChunks += completedChunk ? -1 : 1;

        if (loadingChunks == 0)
        {
            viewerActive = true;
            viewerRigidBody.ActivateCharacter();
        }
    }

    void StartGeneration()
    {

        float startTime = Time.realtimeSinceStartup * 1000f;
        float endTime = startTime + genTimePerFrameMs;
        while (Time.realtimeSinceStartup * 1000f < endTime)
        {
            if (meshRequestQueue.Count == 0)
                return;

            meshRequestQueue.Dequeue().Invoke();
        }
    }

    void UpdateVisibleChunks()
    {

        while (lastUpdateChunks.Count > 0)
        {
            lastUpdateChunks.Dequeue().SetVisible(false);
        }

        int CCCoordX = Mathf.RoundToInt(viewerPosition.x / chunkSize); //CurrentChunkCoord
        int CCCoordY = Mathf.RoundToInt(viewerPosition.y / chunkSize);
        int CCCoordZ = Mathf.RoundToInt(viewerPosition.z / chunkSize);

        for (int xOffset = -chunksVisibleInViewDistance; xOffset <= chunksVisibleInViewDistance; xOffset++)
        {
            for (int yOffset = -chunksVisibleInViewDistance; yOffset <= chunksVisibleInViewDistance; yOffset++)
            {
                for (int zOffset = -chunksVisibleInViewDistance; zOffset <= chunksVisibleInViewDistance; zOffset++)
                {
                    Vector3 viewedCC = new Vector3(CCCoordX + xOffset, CCCoordY + yOffset, CCCoordZ + zOffset);
                    if (terrainChunkDict.ContainsKey(viewedCC))
                    {
                        TerrainChunk curChunk = terrainChunkDict[viewedCC];
                        curChunk.Update();

                    } else {
                        terrainChunkDict.Add(viewedCC, new TerrainChunk(viewedCC, chunkSize, surfaceMaxDepth, IsoLevel, transform, meshRequestQueue, meshCreator, mapMaterial, detailLevels, onChunkRecieved));
                    }
                }
            }
        }
    }

    public class TerrainChunk
    {
        GameObject meshObject;
        Vector3 position;
        Bounds bounds;

        MeshRenderer meshRenderer;
        MeshFilter meshFilter;
        MeshCollider meshCollider;

        MeshCreator meshCreator;
        Queue<Action> meshRequestQueue;

        LODMesh[] LODMeshes;
        LODInfo[] detailLevels;

        System.Action<bool> UpdateCallback;

        int prevLODInd = -1;

        public TerrainChunk(Vector3 coord, int size, int surfaceMaxDepth, float IsoLevel, Transform parent, Queue<Action> meshRequestQueue,
                            MeshCreator meshCreator, Material material, LODInfo[] detailLevels, Action<bool> UpdateCallback)
        {
            position = coord * size;
            bounds = new Bounds(position, Vector3.one * size);
            this.detailLevels = detailLevels;
            this.meshCreator = meshCreator;
            this.UpdateCallback = UpdateCallback;
            this.meshRequestQueue = meshRequestQueue;

            meshObject = new GameObject("Terrain Chunk");
            meshObject.transform.position = position * lerpScale;
            meshObject.transform.localScale = Vector3.one * lerpScale;
            meshObject.transform.parent = parent;

            LODMeshes = new LODMesh[detailLevels.Length];
            for (int i = 0; i < detailLevels.Length; i++) {
                LODMeshes[i] = new LODMesh(meshCreator, detailLevels[i].LOD, this.position, surfaceMaxDepth, IsoLevel, material, Update);
            }

            meshFilter = meshObject.AddComponent<MeshFilter>();
            meshRenderer = meshObject.AddComponent<MeshRenderer>();
            meshCollider = meshObject.AddComponent<MeshCollider>();
            meshRenderer.material = material;

            SetVisible(false);
        }

        public void Update()
        {
            float closestDist = Mathf.Sqrt(bounds.SqrDistance(viewerPosition));
            bool visible = closestDist <= renderDistance;

            if (visible)
            {
                int lodInd = 0;

                for (int i = 0; i < detailLevels.Length - 1; i++)
                {
                    if (closestDist > detailLevels[i].distanceThresh)
                        lodInd = i + 1;
                    else
                        break;
                }

                if (lodInd != prevLODInd)
                {
                    LODMesh lodMesh = LODMeshes[lodInd];
                    if (lodMesh.hasChunk)
                    {
                        prevLODInd = lodInd;
                        meshFilter.mesh = lodMesh.mesh;
                        if (detailLevels[lodInd].useForCollider) meshCollider.sharedMesh = lodMesh.mesh;
                        UpdateCallback(true);
                    }
                    else if (!lodMesh.hasRequestedChunk)
                    {
                        UpdateCallback(false);
                        lodMesh.hasRequestedChunk = true;
                        meshRequestQueue.Enqueue(lodMesh.RequestChunk);
                    }
                }

                lastUpdateChunks.Enqueue(this);
            }

            SetVisible(visible);
        }

        public void SetVisible(bool visible)
        {
            meshObject.SetActive(visible);
        }

        public bool isVisible() { return meshObject.activeSelf; }
    }

    public class LODMesh
    {
        public Mesh mesh;
        public Vector3 position;

        //Noise Data recieved by 
        MapGenerator.ChunkData chunkData;


        int surfaceMaxDepth;
        float IsoLevel;
        Material terrainMat;
        MeshCreator meshCreator;

        public bool hasChunk = false;
        public bool hasRequestedChunk = false;

        System.Action UpdateCallback;
        int LOD;

        //Temporary

        public LODMesh(MeshCreator meshCreator, int LOD, Vector3 position, int surfaceMaxDepth, float IsoLevel, Material terrainMat, System.Action UpdateCallback)
        {
            this.LOD = LOD;
            this.position = position;
            this.UpdateCallback = UpdateCallback;
            this.meshCreator = meshCreator;

            this.surfaceMaxDepth = surfaceMaxDepth;
            this.IsoLevel = IsoLevel;
            this.terrainMat = terrainMat;
        }


        public void RequestChunk()
        {
            hasChunk = true;
            this.chunkData = meshCreator.ComputeMapData(IsoLevel, surfaceMaxDepth, this.position, LOD, terrainMat);
            this.mesh = this.chunkData.GenerateMesh();
            UpdateCallback();
        }

    }

    [System.Serializable]
    public struct LODInfo
    {
        public int LOD;
        public float distanceThresh;
        public bool useForCollider;
    }
    
    void OnValuesUpdated()
    {
        if (!Application.isPlaying)
            return;
            //GenerateMapInEditor();
    }
}
