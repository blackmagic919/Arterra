using System.Collections.Generic;
using System;
using UnityEngine;
using UnityStandardAssets.Characters.FirstPerson;
using UnityEngine.Profiling;
using Utils;


public class EndlessTerrain : MonoBehaviour
{
    [Header("Map Generic Information")]
    [Range(0, 1)]
    public float IsoLevel;
    public LODInfo[] detailLevels;
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
    public int actionsPerFrame = 50;
    public static Vector3 viewerPosition;
    Vector3 oldViewerPos = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    public bool viewerActive = false;


    [Header("Dependencies")]
    public GenerationResources generation;
    //Ideally specialShaders should be in materialData, but can't compile monobehavior in an asset 


    public static Queue<TerrainChunk> lastUpdateTerrainChunks = new Queue<TerrainChunk>();
    public static Queue<SurfaceChunk> lastUpdateSurfaceChunks = new Queue<SurfaceChunk>();

    public static PriorityQueue<Action, int> timeRequestQueue = new PriorityQueue<Action, int>(); //As GPU dispatch must happen linearly, queue to call them sequentially as prev is finished

    public static Dictionary<Vector3, TerrainChunk> terrainChunkDict = new Dictionary<Vector3, TerrainChunk>();
    public static Dictionary<Vector2, SurfaceChunk> surfaceChunkDict = new Dictionary<Vector2, SurfaceChunk>();


    void Start()
    {
        renderDistance = detailLevels[detailLevels.Length - 1].distanceThresh;
        chunksVisibleInViewDistance = Mathf.RoundToInt(renderDistance / mapChunkSize);
        generation.meshCreator.biomeData = generation.biomeData;//Will change, temporary
        generation.mapCreator.biomeData = generation.biomeData;
        generation.texData.ApplyToMaterial();
        generation.structData.ApplyToMaterial();

        generation.densityDict.InitializeManage(chunksVisibleInViewDistance, mapChunkSize, lerpScale);
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
        generation.atmosphereBake.Execute();
    }


    void StartGeneration()
    {
        for(int i = 0; i < actionsPerFrame; i++)
        {
            if (!timeRequestQueue.TryDequeue(out Action action, out int priority))
                return;

            Profiler.BeginSample($"Time Request Queue: {Enum.GetName(typeof(priorities), priority)}");
            action.Invoke();
            Profiler.EndSample();
        }
    }

    void UpdateVisibleChunks()
    {
        int CCCoordX = Mathf.RoundToInt(viewerPosition.x / mapChunkSize); //CurrentChunkCoord
        int CCCoordY = Mathf.RoundToInt(viewerPosition.y / mapChunkSize);
        int CCCoordZ = Mathf.RoundToInt(viewerPosition.z / mapChunkSize);
        Vector3 CCCoord = new Vector3(CCCoordX, CCCoordY, CCCoordZ);
        Vector2 CSCoord = new Vector2(CCCoordX, CCCoordZ);

        while (lastUpdateTerrainChunks.Count > 0)
        {
            lastUpdateTerrainChunks.Dequeue().UpdateVisibility(CCCoord, chunksVisibleInViewDistance);
        }
        while (lastUpdateSurfaceChunks.Count > 0)
        {
            lastUpdateSurfaceChunks.Dequeue().UpdateVisibility(CSCoord, chunksVisibleInViewDistance);
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
                    curSChunk = new SurfaceChunk(Instantiate(generation.mapCreator), viewedSC);
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
                        terrainChunkDict.Add(viewedCC, new TerrainChunk(viewedCC, IsoLevel, transform, curSChunk, detailLevels, generation));
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

    public void Terraform(Vector3 terraformPoint, float terraformRadius, Func<Vector2, float, Vector2> handleTerraform)
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
}
