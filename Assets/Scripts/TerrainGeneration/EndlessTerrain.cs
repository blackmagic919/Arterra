using System.Collections.Generic;
using System;
using UnityEngine;
using UnityStandardAssets.Characters.FirstPerson;
using Unity.Mathematics;
using System.Linq;
using static MapGenerator;
using static GenerationHeightData;

public class EndlessTerrain : MonoBehaviour
{
    [Header("Map Generic Information")]
    [Range(0, 1)]
    public float IsoLevel;
    public LODInfo[] detailLevels;
    public static float renderDistance;
    public const int mapChunkSize = 48;//Number of cubes, points-1;
    public int surfaceMaxDepth;
    const float chunkUpdateThresh = 20f;
    const float sqrChunkUpdateThresh = chunkUpdateThresh * chunkUpdateThresh;
    public const float lerpScale = 2.5f;
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
    public List<SpecialShaderData> specialShaders;
    //Ideally specialShaders should be in materialData, but can't compile monobehavior in an asset 

    public static Queue<TerrainChunk> lastUpdateChunks = new Queue<TerrainChunk>();
    public static Queue<Action> timeRequestQueue = new Queue<Action>(); //As GPU dispatch must happen linearly, queue to call them sequentially as prev is finishe

    Dictionary<Vector3, TerrainChunk> terrainChunkDict = new Dictionary<Vector3, TerrainChunk>();

    void Start()
    {
        renderDistance = detailLevels[detailLevels.Length - 1].distanceThresh;
        chunksVisibleInViewDistance = Mathf.RoundToInt(renderDistance / mapChunkSize);
        meshCreator.GenerationData = GenerationData;//Will change, temporary
        //texData.ApplyToMaterial(mapMaterial, GenerationData.Materials);
    }

    private void Update()
    {
        viewerPosition = viewer.position / lerpScale;
        if ((oldViewerPos - viewerPosition).sqrMagnitude > sqrChunkUpdateThresh && timeRequestQueue.Count == 0)
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
            if (timeRequestQueue.Count == 0)
                return;

            timeRequestQueue.Dequeue().Invoke();
        }
    }

    void UpdateVisibleChunks()
    {
        int CCCoordX = Mathf.RoundToInt(viewerPosition.x / mapChunkSize); //CurrentChunkCoord
        int CCCoordY = Mathf.RoundToInt(viewerPosition.y / mapChunkSize);
        int CCCoordZ = Mathf.RoundToInt(viewerPosition.z / mapChunkSize);
        Vector3 CCCoord = new Vector3(CCCoordX, CCCoordY, CCCoordZ);

        while (lastUpdateChunks.Count > 0)
        {
            lastUpdateChunks.Dequeue().UpdateVisibility(CCCoord, chunksVisibleInViewDistance);
        }

        for (int xOffset = -chunksVisibleInViewDistance; xOffset <= chunksVisibleInViewDistance; xOffset++)
        {
            for (int zOffset = -chunksVisibleInViewDistance; zOffset <= chunksVisibleInViewDistance; zOffset++)
            {
                for (int yOffset = -chunksVisibleInViewDistance; yOffset <= chunksVisibleInViewDistance; yOffset++)
                {
                    Vector3 viewedCC = new Vector3(xOffset, yOffset, zOffset) + CCCoord;
                    if (terrainChunkDict.ContainsKey(viewedCC))
                    {
                        TerrainChunk curChunk = terrainChunkDict[viewedCC];
                        curChunk.Update();
                    } else {
                        terrainChunkDict.Add(viewedCC, new TerrainChunk(viewedCC, mapChunkSize, surfaceMaxDepth, IsoLevel, transform, specialShaders, meshCreator, mapMaterial, detailLevels, onChunkRecieved));
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
                    if (SphereIntersectsBox(terraformPoint, (terraformRadius+1), mapChunkSize * lerpScale * viewedCC, mapChunkSize * lerpScale * Vector3.one)) { 
                        TerrainChunk curChunk = terrainChunkDict[viewedCC];
                        curChunk.TerraformChunk(terraformPoint, terraformRadius, handleTerraform);
                    }
                }
            }
        }
    }
    

    
    
    void OnValuesUpdated()
    {
        if (!Application.isPlaying)
            return;
            //GenerateMapInEditor();
    }
}
