using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using static MapGenerator;
using static GenerationHeightData;
using UnityEditor;


public class DensityDeconstructor : MonoBehaviour
{
    [Header("Density Settings")]
    public LayerMask meshLayer;
    public float MaxDensityDifference;
    public uint skipIncrementWS;
    public float IsoLevel;

    [Header("MaterialSettings")]
    public LayerMask materialLayer;
    public int materialIndex;

    readonly int[] xD = new int[6] { -1, 1, 0, 0, 0, 0 };
    readonly int[] yD = new int[6] { 0, 0, -1, 1, 0, 0 };
    readonly int[] zD = new int[6] { 0, 0, 0, 0, -1, 1 };

    [Header("Misc")]
    public Material recreationMaterial;
    public string savePath;
    public List<CheckPoint> CheckPoints;

    float[,,] density;
    int[,,] materials;
    Renderer boxRenderer;

    GameObject recreatdObject;
    MeshFilter meshFilter;
    MeshRenderer meshRenderer;

    float Epsilon = 1E-5f;

    [Serializable]
    public struct CheckPoint
    {
        public Transform transform;
        public bool isUnderGround;
    }

    void OnValidate()
    {
        boxRenderer = this.transform.gameObject.GetComponent<Renderer>();
        MaxDensityDifference = Mathf.Min(IsoLevel, MaxDensityDifference);
    }

    public void SetMaterial()
    {
        Bounds bounds = boxRenderer.bounds;
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;

        Vector3 origin = center - extents;
        uint pointsX = (uint)Mathf.CeilToInt((2 * extents.x) / skipIncrementWS);
        uint pointsY = (uint)Mathf.CeilToInt((2 * extents.y) / skipIncrementWS);
        uint pointsZ = (uint)Mathf.CeilToInt((2 * extents.z) / skipIncrementWS);
        uint sizeX = pointsX * skipIncrementWS;
        uint sizeY = pointsY * skipIncrementWS;
        uint sizeZ = pointsZ * skipIncrementWS;

        materials ??= Utility.InitializeArray3D(-1, pointsX, pointsY, pointsZ);

        bool[,,] isOutside = new bool[pointsX, pointsY, pointsZ];
        isOutside[0, 0, 0] = true;
        for (uint x = 0; x < sizeX; x += skipIncrementWS)
        {
            for (uint y = 0; y < sizeY; y += skipIncrementWS)
            {
                for (uint z = 0; z < sizeZ; z += skipIncrementWS)
                {
                    Vector3 positionOS = new Vector3(x, y, z);
                    Vector3 positionWS = origin + positionOS;
                    uint3 index = new uint3(x / skipIncrementWS, y / skipIncrementWS, z / skipIncrementWS);
                    for (int i = 0; i < 6; i++)
                    {
                        Vector3 direction = new Vector3(xD[i], yD[i], zD[i]);
                        Vector3 endPoint = positionOS + direction * skipIncrementWS;
                        uint3 endIndex = new uint3((uint)endPoint.x / skipIncrementWS, (uint)endPoint.y / skipIncrementWS, (uint)endPoint.z / skipIncrementWS);

                        if (Mathf.Min(endPoint.x, endPoint.y, endPoint.z) < 0)
                            continue;
                        if (endPoint.x >= sizeX || endPoint.y >= sizeY || endPoint.z >= sizeZ)
                            continue;


                        Ray ray = new Ray(positionWS, direction);
                        if (Physics.Raycast(ray, skipIncrementWS, materialLayer))
                            isOutside[endIndex.x, endIndex.y, endIndex.z] = !isOutside[index.x, index.y, index.z] || isOutside[endIndex.x, endIndex.y, endIndex.z];
                        else
                            isOutside[endIndex.x, endIndex.y, endIndex.z] = isOutside[index.x, index.y, index.z] || isOutside[endIndex.x, endIndex.y, endIndex.z];
                    }
                    materials[index.x, index.y, index.z] = isOutside[index.x, index.y, index.z] ? materials[index.x, index.y, index.z] : materialIndex;
                }
            }
        }
    }

    public void DisposeMaterials()
    {
        materials = null;
    }

    public void ExtractDensity()
    {
        Bounds bounds = boxRenderer.bounds;
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;

        Vector3 origin = center - extents;
        uint pointsX = (uint)Mathf.CeilToInt((2 * extents.x) / skipIncrementWS);
        uint pointsY = (uint)Mathf.CeilToInt((2 * extents.y) / skipIncrementWS);
        uint pointsZ = (uint)Mathf.CeilToInt((2 * extents.z) / skipIncrementWS);
        uint sizeX = pointsX * skipIncrementWS;
        uint sizeY = pointsY * skipIncrementWS;
        uint sizeZ = pointsZ * skipIncrementWS;

        density = new float[pointsX, pointsY, pointsZ];

        //Cannot simply use odd point test because ray doesn't hit backface if it first hits frontface
        bool[,,] isOutside = new bool[pointsX, pointsY, pointsZ];
        isOutside[0,0,0] = true;

        Physics.queriesHitBackfaces = true;

        for (uint x = 0; x < sizeX; x += skipIncrementWS)
        {
            for (uint y = 0; y < sizeY; y += skipIncrementWS)
            {
                for (uint z = 0; z < sizeZ; z += skipIncrementWS)
                {
                    Vector3 positionOS = new Vector3(x, y, z);
                    Vector3 positionWS = origin + positionOS;
                    uint3 index = new uint3(x / skipIncrementWS, y / skipIncrementWS, z / skipIncrementWS);

                    //I was averaging distances, then realized the existance of SDFs lol
                    //Method creates a pseudo distance field only on major axises and near mesh surface(saves time)
                    float minDistance = skipIncrementWS;
                    for (int i = 0; i < 6; i++)
                    {
                        Vector3 direction = new Vector3(xD[i], yD[i], zD[i]);
                        Vector3 endPoint = positionOS + direction * skipIncrementWS;
                        uint3 endIndex = new uint3((uint)endPoint.x / skipIncrementWS, (uint)endPoint.y / skipIncrementWS, (uint)endPoint.z / skipIncrementWS);

                        if (Mathf.Min(endPoint.x, endPoint.y, endPoint.z) < 0)
                            continue;
                        if (endPoint.x >= sizeX || endPoint.y >= sizeY || endPoint.z >= sizeZ)
                            continue;
                        
                        
                        Ray ray = new Ray(positionWS, direction);
                        RaycastHit hit;
                        if (Physics.Raycast(ray, out hit, skipIncrementWS, meshLayer)){
                            minDistance = Mathf.Min(hit.distance, minDistance);
                            //Sometimes there are issues with backface detection so I take a backface detection from all directions
                            isOutside[endIndex.x, endIndex.y, endIndex.z] = !isOutside[index.x, index.y, index.z] || isOutside[endIndex.x, endIndex.y, endIndex.z];
                        }
                        else{
                            isOutside[endIndex.x, endIndex.y, endIndex.z] = isOutside[index.x, index.y, index.z]; //|| isOutside[endIndex.x, endIndex.y, endIndex.z];
                        }
                    }

                    minDistance /= skipIncrementWS;
                    if (!isOutside[index.x, index.y, index.z])
                        density[index.x, index.y, index.z] = minDistance * MaxDensityDifference + IsoLevel;
                    else
                        density[index.x, index.y, index.z] = (minDistance == 1) ? 0 : IsoLevel - minDistance * MaxDensityDifference;
                    
                }
            }
        }
    }

    public void SaveData()
    {
        StructureData obj = ScriptableObject.CreateInstance<StructureData>();

        obj.sizeX = density.GetLength(0);
        obj.sizeY = density.GetLength(1);
        obj.sizeZ = density.GetLength(2);

        obj.density = obj.Flatten(density);
        obj.materials = obj.Flatten(materials);

        Bounds bounds = boxRenderer.bounds;
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;

        Vector3 origin = center - extents;
        obj.SetChecks(CheckPoints.Select(e => (e.transform.position - origin)/ skipIncrementWS).ToArray(), CheckPoints.Select(e => e.isUnderGround).ToArray());

        AssetDatabase.CreateAsset(obj, "Assets/" + savePath + "/Saved_Data.asset");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }


    public void BuildMesh()
    {
        if (recreatdObject != null) DestroyImmediate(recreatdObject);
        if (meshFilter != null) DestroyImmediate(meshFilter);
        if (meshRenderer != null) DestroyImmediate(meshRenderer);

        recreatdObject = new GameObject("Reconstructed Mesh");
        recreatdObject.transform.position = this.transform.position;
        recreatdObject.transform.parent = this.transform.parent;
        meshFilter = recreatdObject.AddComponent<MeshFilter>();
        meshRenderer = recreatdObject.AddComponent<MeshRenderer>();

        meshRenderer.material = recreationMaterial;

        ChunkData newChunk = MeshGenerator.GenerateMesh(density, IsoLevel);
        meshFilter.sharedMesh = newChunk.GenerateMesh();
    }

    public void VisualizeMaterial()
    {
        if (recreatdObject != null) DestroyImmediate(recreatdObject);
        if (meshFilter != null) DestroyImmediate(meshFilter);
        if (meshRenderer != null) DestroyImmediate(meshRenderer);

        recreatdObject = new GameObject("Applied Materials");
        recreatdObject.transform.position = this.transform.position;
        recreatdObject.transform.parent = this.transform.parent;
        meshFilter = recreatdObject.AddComponent<MeshFilter>();
        meshRenderer = recreatdObject.AddComponent<MeshRenderer>();

        meshRenderer.material = recreationMaterial;

        
        int sizeX = materials.GetLength(0);
        int sizeY = materials.GetLength(1);
        int sizeZ = materials.GetLength(2);
        float[,,] materialView = new float[sizeX, sizeY, sizeZ];
        for (int x = 0; x < sizeX; x++)
        {
            for (int y = 0; y < sizeY; y++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    materialView[x, y, z] = materials[x, y, z] == materialIndex ? 1 : 0;
                }
            }
        }

        ChunkData newChunk = MeshGenerator.GenerateMesh(materialView, 0.5f);
        meshFilter.sharedMesh = newChunk.GenerateMesh();
    }
}
