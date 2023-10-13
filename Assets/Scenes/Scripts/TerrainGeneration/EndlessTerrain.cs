using System.Collections.Generic;
using System;
using UnityEngine;
using UnityStandardAssets.Characters.FirstPerson;
using Unity.Mathematics;
using System.Linq;

public class EndlessTerrain : MonoBehaviour
{
    [Header("Map Generic Information")]
    [Range(0, 1)]
    public float IsoLevel;
    public LODInfo[] detailLevels;
    public static float renderDistance;
    public const int mapChunkSize = 36;//Number of cubes, points-1;
    public int surfaceMaxDepth;
    const float chunkUpdateThresh = 20f;
    const float sqrChunkUpdateThresh = chunkUpdateThresh * chunkUpdateThresh;
    const float lerpScale = 2.5f;
    int chunksVisibleInViewDistance;
    int loadingChunks = 0;

    [Header("Viewer Information")]
    public Transform viewer;
    //Pause Viewer until terrain is generated
    public RigidbodyFirstPersonController viewerRigidBody;
    public float genTimePerFrameMs;
    public static Vector3 viewerPosition;
    Vector3 oldViewerPos = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    public bool viewerActive = false;


    [Header("Dependencies")]
    public Material mapMaterial;
    public MeshCreator meshCreator;
    public GenerationHeightData GenerationData;
    public TextureData texData;
    public ProceduralGrassRenderer.GrassSettings grassSettings;

    static Queue<TerrainChunk> lastUpdateChunks = new Queue<TerrainChunk>();
    static Queue<Action> timeRequestQueue = new Queue<Action>(); //As GPU dispatch must happen linearly, queue to call them sequentially as prev is finishe

    Dictionary<Vector3, TerrainChunk> terrainChunkDict = new Dictionary<Vector3, TerrainChunk>();

    void Start()
    {
        renderDistance = detailLevels[detailLevels.Length - 1].distanceThresh;
        chunksVisibleInViewDistance = Mathf.RoundToInt(renderDistance / mapChunkSize);
        meshCreator.GenerationData = GenerationData;//Will change, temporary
        //texData.ApplyToMaterial(mapMaterial, GenerationData.Materials);
        UpdateVisibleChunks();
    }

    private void Update()
    {
        viewerPosition = viewer.position / lerpScale;
        if ((oldViewerPos - viewerPosition).sqrMagnitude > sqrChunkUpdateThresh && timeRequestQueue.Count == 0)
        {
            oldViewerPos = viewerPosition;
            UpdateVisibleChunks();
        }
    }

    private void LateUpdate()
    {
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
            if (timeRequestQueue.Count == 0)
                return;

            timeRequestQueue.Dequeue().Invoke();
        }
    }

    void UpdateVisibleChunks()
    {

        while (lastUpdateChunks.Count > 0)
        {
            lastUpdateChunks.Dequeue().UpdateVisibility();
        }

        int CCCoordX = Mathf.RoundToInt(viewerPosition.x / (mapChunkSize*lerpScale)); //CurrentChunkCoord
        int CCCoordY = Mathf.RoundToInt(viewerPosition.y / (mapChunkSize*lerpScale));
        int CCCoordZ = Mathf.RoundToInt(viewerPosition.z / (mapChunkSize*lerpScale));

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
                        terrainChunkDict.Add(viewedCC, new TerrainChunk(viewedCC, mapChunkSize, surfaceMaxDepth, IsoLevel, transform, timeRequestQueue, grassSettings, meshCreator, mapMaterial, detailLevels, onChunkRecieved));
                    }
                }
            }
        }
    }

    public static bool SphereIntersectsBox(Vector3 sphereCentre, float sphereRadius, Vector3 boxCentre, Vector3 boxSize)
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

    public void Terraform(Vector3 terraformPoint, float weight, float terraformRadius)
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
                    if (SphereIntersectsBox(terraformPoint, (terraformRadius+1), mapChunkSize * lerpScale * viewedCC, mapChunkSize * lerpScale * Vector3.one)) { 
                        TerrainChunk curChunk = terrainChunkDict[viewedCC];
                        curChunk.TerraformChunk(terraformPoint, weight, terraformRadius);
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
        ProceduralGrassRenderer grassRenderer;

        Queue<Action> meshRequestQueue;

        LODMesh[] LODMeshes;
        LODInfo[] detailLevels;

        System.Action<bool> UpdateCallback;

        float[] storedDensity = null;
        float[] storedMaterial = null;
        bool hasDensityMap = false;
        bool compeltedRequest = false;

        float IsoLevel;
        int surfaceMaxDepth;
        int prevLODInd = -1;

        public TerrainChunk(Vector3 coord, int size, int surfaceMaxDepth, float IsoLevel, Transform parent, Queue<Action> meshRequestQueue,
                            ProceduralGrassRenderer.GrassSettings grassSettings, MeshCreator meshCreator, Material material, LODInfo[] detailLevels, Action<bool> UpdateCallback)
        {
            position = coord * size - Vector3.one * (size/2f); //Shift mesh so it is aligned with center
            bounds = new Bounds(position, Vector3.one * size);
            this.IsoLevel = IsoLevel;
            this.surfaceMaxDepth = surfaceMaxDepth;
            this.detailLevels = detailLevels;
            this.UpdateCallback = UpdateCallback;
            this.meshRequestQueue = meshRequestQueue;
            this.meshCreator = meshCreator;

            meshObject = new GameObject("Terrain Chunk");
            meshObject.transform.position = position * lerpScale;
            meshObject.transform.localScale = Vector3.one * lerpScale;
            meshObject.transform.parent = parent;
            //meshObject.transform.localToWorldMatrix;

            LODMeshes = new LODMesh[detailLevels.Length];
            for (int i = 0; i < detailLevels.Length; i++) {
                LODMeshes[i] = new LODMesh(meshCreator, detailLevels[i].LOD, this.position, surfaceMaxDepth, IsoLevel, Update);
            }

            meshFilter = meshObject.AddComponent<MeshFilter>();
            meshRenderer = meshObject.AddComponent<MeshRenderer>();
            meshCollider = meshObject.AddComponent<MeshCollider>();
            grassRenderer = meshObject.AddComponent<ProceduralGrassRenderer>();
            grassRenderer.grassSettings = grassSettings;

            meshRenderer.material = material;

            SetVisible(false);
        }

        public void TerraformChunk(Vector3 targetPosition, float weight, float terraformRadius)
        {
            if (!hasDensityMap) {
                int numPoints = (mapChunkSize + 1) * (mapChunkSize + 1) * (mapChunkSize + 1);
                storedDensity = meshCreator.GetDensity(IsoLevel, surfaceMaxDepth, this.position);
                storedMaterial = Enumerable.Repeat(-1.0f, numPoints).ToArray();
                hasDensityMap = true;
            }

            float worldScale = lerpScale;

            Vector3 targetPointLocal = meshObject.transform.worldToLocalMatrix.MultiplyPoint3x4(targetPosition);
            int closestX = Mathf.Max(0, Mathf.Min(Mathf.RoundToInt(targetPointLocal.x), mapChunkSize));
            int closestY = Mathf.Max(0, Mathf.Min(Mathf.RoundToInt(targetPointLocal.y), mapChunkSize));
            int closestZ = Mathf.Max(0, Mathf.Min(Mathf.RoundToInt(targetPointLocal.z), mapChunkSize));
            int localRadius = Mathf.CeilToInt((1.000f/worldScale) * terraformRadius);

            meshCreator.GetFocusedMaterials(localRadius, new Vector3(closestX, closestY, closestZ), this.position, ref storedMaterial);

            Queue<int3> effectedPoints = new Queue<int3>();
            effectedPoints.Enqueue(new int3(closestX, closestY, closestZ));

            float material = 2.0f; //temporary test


            for(int x = -localRadius; x <= localRadius; x++)
            {
                for(int y = -localRadius; y <= localRadius; y++)
                {
                    for(int z = -localRadius; z <= localRadius; z++)
                    {
                        int3 vertPosition = new(closestX + x, closestY + y, closestZ + z);
                        if (Mathf.Max(vertPosition.x, vertPosition.y, vertPosition.z) > mapChunkSize)
                            continue;
                        if (Mathf.Min(vertPosition.x, vertPosition.y, vertPosition.z) < 0)
                            continue;

                        int index = Utility.indexFromCoord(vertPosition.x, vertPosition.y, vertPosition.z, mapChunkSize + 1);

                        Vector3 dR = new Vector3(vertPosition.x, vertPosition.y, vertPosition.z) - targetPointLocal;
                        float sqrDistWS = worldScale * (dR.x * dR.x + dR.y * dR.y + dR.z * dR.z);

                        
                        float brushStrength = 1.0f - Mathf.InverseLerp(0, terraformRadius * terraformRadius, sqrDistWS);

                        if(weight >= 0) { //Add
                            if (storedDensity[index] < IsoLevel || storedMaterial[index] == material)
                            {
                                storedDensity[index] = Mathf.Clamp(storedDensity[index] + brushStrength * weight, 0, 1);
                                storedMaterial[index] = material;
                            }
                        }
                        else
                        {
                            if (storedDensity[index] > IsoLevel)
                            {
                                storedDensity[index] = Mathf.Clamp(storedDensity[index] + brushStrength * weight, 0, 1);
                            }
                        }
                    }
                }
            }


            foreach (LODMesh mesh in LODMeshes)
            {
                mesh.depreceated = true;
            }
            
            Update();
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

                LODMesh lodMesh = LODMeshes[lodInd];
                if (lodInd != prevLODInd || lodMesh.depreceated)
                {
                    if (lodMesh.depreceated && compeltedRequest)
                    {
                        compeltedRequest = false;
                        lodMesh.hasChunk = false;
                        lodMesh.hasRequestedChunk = false;
                        lodMesh.depreceated = false;
                        prevLODInd = -1;
                    }

                    if (lodMesh.hasChunk)
                    {
                        prevLODInd = lodInd;
                        meshFilter.mesh = lodMesh.mesh;
                        compeltedRequest = true;
                        if (detailLevels[lodInd].useForCollider)
                            meshCollider.sharedMesh = lodMesh.mesh;

                        if (closestDist < grassRenderer.grassSettings.lodMaxCameraDistance)
                        {
                            grassRenderer.sourceMesh = lodMesh.grassMesh;
                            grassRenderer.OnStart();
                        }

                        else
                            grassRenderer.Disable();

                        if(lodMesh.depreceated) //was depreceated while chunk was regenerating
                            meshRequestQueue.Enqueue(Update);

                        UpdateCallback(true);
                    }
                    else if (!lodMesh.hasRequestedChunk)
                    {
                        lodMesh.hasRequestedChunk = true;
                        if (hasDensityMap)
                            meshRequestQueue.Enqueue(() => lodMesh.ComputeChunk(ref storedDensity, ref storedMaterial)); 
                        else
                            meshRequestQueue.Enqueue(lodMesh.GetChunk);

                        UpdateCallback(false);
                    }
                }
                lastUpdateChunks.Enqueue(this);
            }
            SetVisible(visible);
        }

        public void UpdateVisibility()
        {
            float closestDist = Mathf.Sqrt(bounds.SqrDistance(viewerPosition));
            bool visible = closestDist <= renderDistance;
            SetVisible(visible);
        }

        public void SetVisible(bool visible)
        {
            if (visible == meshObject.activeSelf)
                return;
            meshObject.SetActive(visible);
        }

        public bool isVisible() { return meshObject.activeSelf; }
    }

    public class LODMesh
    {
        public Mesh mesh;
        public Mesh grassMesh;
        public Vector3 position;

        //Noise Data recieved by 
        MapGenerator.ChunkData chunkData;


        int surfaceMaxDepth;
        float IsoLevel;
        MeshCreator meshCreator;

        public bool hasChunk = false;
        public bool hasRequestedChunk = false;
        public bool excludeGen = false;
        public bool depreceated = false;

        System.Action UpdateCallback;
        int LOD;

        //Temporary

        public LODMesh(MeshCreator meshCreator, int LOD, Vector3 position, int surfaceMaxDepth, float IsoLevel, System.Action UpdateCallback)
        {
            this.LOD = LOD;
            this.position = position;
            this.UpdateCallback = UpdateCallback;
            this.meshCreator = meshCreator;

            this.surfaceMaxDepth = surfaceMaxDepth;
            this.IsoLevel = IsoLevel;
        }


        public async void GetChunk()
        {
            chunkData = meshCreator.GenerateMapData(IsoLevel, surfaceMaxDepth, this.position, LOD);
            this.mesh = this.chunkData.GenerateMesh();
            this.grassMesh = this.chunkData.GenerateGrass();
            hasChunk = true;
            UpdateCallback();
        }

        public void ComputeChunk(ref float[] density, ref float[] material)
        {
            this.chunkData = meshCreator.GenerateMapData(IsoLevel, surfaceMaxDepth, this.position, LOD, density: density, material: material, hasDensity: true);
            this.mesh = this.chunkData.GenerateMesh();
            this.grassMesh = this.chunkData.GenerateGrass();
            hasChunk = true;
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
