using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using static UnityEditor.Searcher.SearcherWindow.Alignment;

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

    public NoiseData TerrainNoise; //For underground terrain generation
    public GenerationHeightData GenerationData;
    public NoiseData SurfaceNoise; //For surface generation

    const float lerpScale = 2.5f;
    public const int mapChunkSize = 48;
    public int surfaceMaxDepth;
    [Range(0,1)]
    public float IsoLevel;

    int chunkSize;
    int chunksVisibleInViewDistance;
    static Queue<TerrainChunk> lastUpdateChunks = new Queue<TerrainChunk>();

    Dictionary<Vector3, TerrainChunk> terrainChunkDict = new Dictionary<Vector3, TerrainChunk>();

    void Start()
    {
        mapGenerator = FindObjectOfType<MapGenerator>();
        chunkSize = mapChunkSize;
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
                        terrainChunkDict.Add(viewedCC, new TerrainChunk(viewedCC, chunkSize, surfaceMaxDepth, IsoLevel, transform, TerrainNoise, SurfaceNoise, GenerationData, mapMaterial, detailLevels));
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

        public NoiseData TerrainNoise; //For underground terrain generation
        public GenerationHeightData GenerationData;
        public NoiseData SurfaceNoise; //For surface generation

        int prevLODInd = -1;

        public TerrainChunk(Vector3 coord, int size, int surfaceMaxDepth, float IsoLevel, Transform parent, NoiseData TerrainNoise, NoiseData SurfaceNoise, GenerationHeightData GenerationData,
                            Material material, LODInfo[] detailLevels)
        {
            position = coord * size;
            bounds = new Bounds(position, Vector3.one * size);
            this.detailLevels = detailLevels;

            this.TerrainNoise = TerrainNoise;
            this.SurfaceNoise = SurfaceNoise;
            this.GenerationData = GenerationData;

            meshObject = new GameObject("Terrain Chunk");
            meshObject.transform.position = position * lerpScale;
            meshObject.transform.localScale = Vector3.one * lerpScale;
            meshObject.transform.parent = parent;

            LODMeshes = new LODMesh[detailLevels.Length];
            for (int i = 0; i < detailLevels.Length; i++) {
                LODMeshes[i] = new LODMesh(detailLevels[i].LOD, this.position, surfaceMaxDepth, IsoLevel, GenerationData, TerrainNoise, SurfaceNoise, material, Update);
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
                    if (lodMesh.hasChunk)
                    {
                        prevLODInd = lodInd;
                        meshFilter.mesh = lodMesh.mesh;
                        if(detailLevels[lodInd].useForCollider) meshCollider.sharedMesh = lodMesh.mesh;
                    }
                    else if(!lodMesh.hasRequestedChunk)
                    {
                        lodMesh.RequestChunk();
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

        //Noise Data recieved by 
        MapGenerator.ChunkData chunkData;

        GenerationHeightData generationData;
        NoiseData TerrainNoise;
        NoiseData SurfaceNoise;
        int surfaceMaxDepth;
        float IsoLevel;
        Material terrainMat;

        /*
         * 0 Underground Noise
         * 1 Surface Noise
         * 2 material Height Pref
         * 3 Terrain Noise  <- 0 & 1
         * 4 mesh <- 3
         * 5 Partial gen noise <- 4
         * 6 Partial material map <- 5 & 2
         * 7 Color Map <- 6
         * 8 Resize <- 7
         * 9 Resize <- 7
         */

        public bool hasChunk = false;
        public bool hasRequestedChunk = false;

        System.Action UpdateCallback;
        int LOD;
        int meshSkipInc;

        //Temporary

        public LODMesh(int LOD, Vector3 position, int surfaceMaxDepth, float IsoLevel, GenerationHeightData generationData,
                       NoiseData TerrainNoise, NoiseData SurfaceNoise, Material terrainMat, System.Action UpdateCallback)
        {
            this.LOD = LOD;
            this.meshSkipInc = ((LOD == 0) ? 1 : LOD * 2);
            this.position = position;
            this.UpdateCallback = UpdateCallback;

            this.generationData = generationData;
            this.TerrainNoise = TerrainNoise;
            this.SurfaceNoise = SurfaceNoise;
            this.surfaceMaxDepth = surfaceMaxDepth;
            this.IsoLevel = IsoLevel;
            this.terrainMat = terrainMat;
        }


        public void RequestChunk()
        {
            hasRequestedChunk = true;
            mapGenerator.RequestData(() => (MapGenerator.GenerateMapData(TerrainNoise, SurfaceNoise, generationData, IsoLevel, surfaceMaxDepth, this.position, LOD)), onChunkRecieved);
        }

        void onChunkRecieved(object chunkData)
        {
            hasChunk = true;
            this.chunkData = (MapGenerator.ChunkData)chunkData;
            this.mesh = this.chunkData.GenerateMesh();
            TextureData.ApplyToMaterial(terrainMat, generationData.Materials);
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

    private void OnValidate()
    {
        if (TerrainNoise != null)
        {
            TerrainNoise.OnValuesUpdated -= OnValuesUpdated;
            TerrainNoise.OnValuesUpdated += OnValuesUpdated;
        }
        if (GenerationData != null)
        {
            GenerationData.OnValuesUpdated -= OnValuesUpdated;
            GenerationData.OnValuesUpdated += OnValuesUpdated;
        }
        if (SurfaceNoise != null)
        {
            SurfaceNoise.OnValuesUpdated -= OnValuesUpdated;
            SurfaceNoise.OnValuesUpdated += OnValuesUpdated;
        }
    }
}
