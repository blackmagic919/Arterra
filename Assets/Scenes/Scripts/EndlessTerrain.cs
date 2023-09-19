using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EndlessTerrain : MonoBehaviour
{
    const float chunkUpdateThresh = 10f;
    const float sqrChunkUpdateThresh = chunkUpdateThresh * chunkUpdateThresh;
    Vector3 oldViewerPos;

    public Material mapMaterial;

    public static float renderDistance;
    public Transform viewer;
    public static Vector3 viewerPosition;
    static MapGenerator mapGenerator;
    public LODInfo[] detailLevels;

    const float lerpScale = 1f;

    int chunkSize;
    int chunksVisibleInViewDistance;
    static Queue<TerrainChunk> lastUpdateChunks = new Queue<TerrainChunk>();

    Dictionary<Vector3, TerrainChunk> terrainChunkDict = new Dictionary<Vector3, TerrainChunk>();

    void Start()
    {
        mapGenerator = FindObjectOfType<MapGenerator>();
        chunkSize = MapGenerator.mapChunkSize;
        renderDistance = detailLevels[detailLevels.Length - 1].distanceThresh;
        chunksVisibleInViewDistance = Mathf.RoundToInt(renderDistance / chunkSize);

        UpdateVisibleChunks();
    }

    private void Update()
    {
        viewerPosition = viewer.position / lerpScale;
        if ((oldViewerPos - viewerPosition).sqrMagnitude > sqrChunkUpdateThresh)
        {
            oldViewerPos = viewerPosition;
            UpdateVisibleChunks();
        }
    }

    void UpdateVisibleChunks()
    {

        while(lastUpdateChunks.Count > 0)
        {
            lastUpdateChunks.Dequeue().SetVisible(false);
        }

        int CCCoordX = Mathf.RoundToInt(viewerPosition.x / chunkSize); //CurrentChunkCoord
        int CCCoordY = Mathf.RoundToInt(viewerPosition.y / chunkSize);
        int CCCoordZ = Mathf.RoundToInt(viewerPosition.z / chunkSize);

        for(int xOffset = -chunksVisibleInViewDistance; xOffset <= chunksVisibleInViewDistance; xOffset++)
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
                        terrainChunkDict.Add(viewedCC, new TerrainChunk(viewedCC, chunkSize, transform, mapMaterial, detailLevels));
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

        LODMesh[] LODMeshes;
        LODInfo[] detailLevels;

        int prevLODInd = -1;

        public TerrainChunk(Vector3 coord, int size, Transform parent, Material material, LODInfo[] detailLevels)
        {
            position = coord * size;
            bounds = new Bounds(position, Vector3.one * size);
            this.detailLevels = detailLevels;

            meshObject = new GameObject("Terrain Chunk");
            meshObject.transform.position = position * lerpScale;
            meshObject.transform.localScale = Vector3.one * lerpScale;
            meshObject.transform.parent = parent;

            LODMeshes = new LODMesh[detailLevels.Length];
            for (int i = 0; i < detailLevels.Length; i++) { LODMeshes[i] = new LODMesh(detailLevels[i].LOD, this.position, Update); }

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

                for(int i = 0; i < detailLevels.Length-1; i++)
                {
                    if (closestDist > detailLevels[i].distanceThresh)
                        lodInd = i + 1;
                    else
                        break;
                }

                if(lodInd != prevLODInd)
                {
                    LODMesh lodMesh = LODMeshes[lodInd];
                    if (lodMesh.hasMesh)
                    {
                        prevLODInd = lodInd;
                        meshFilter.mesh = lodMesh.mesh;
                        meshCollider.sharedMesh = lodMesh.mesh;
                    }
                    else if(!lodMesh.hasRequestedMesh)
                    {
                        lodMesh.RequestMesh();
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

    class LODMesh
    {
        public Mesh mesh;
        public Vector3 position;
        public bool hasRequestedMesh;
        public bool hasMesh;

        System.Action UpdateCallback;
        int LOD;

        public LODMesh(int LOD, Vector3 position, System.Action UpdateCallback)
        {
            this.LOD = LOD;
            this.position = position;
            this.UpdateCallback = UpdateCallback;
        }

        void onMeshDataRecieved(MapGenerator.MeshData meshData)
        {
            this.hasMesh = true;
            this.mesh = meshData.GenerateMesh();
            UpdateCallback();
        }

        public void RequestMesh()
        {
            this.hasRequestedMesh = true;
            mapGenerator.RequestMapData(this.LOD, this.position, onMeshDataRecieved);
        }

    }

    [System.Serializable]
    public struct LODInfo
    {
        public int LOD;
        public float distanceThresh;
    }
}
