using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class TerrainRenderer : MonoBehaviour
{
    [HideInInspector]
    public bool initialized = false;

    private ChunkBuffers meshBuffers;

    private Material instantiatedMaterial;

    private Bounds bounds;

    public void CreateRenderer(Material material, Bounds bounds)
    {
        this.bounds = bounds;
        this.instantiatedMaterial = Instantiate(material);
    }

    public void OnEnable()
    {
        initialized = false;
    }

    public void Render(ChunkBuffers info)
    {
        if (initialized)
            Release();
        initialized = true;

        this.meshBuffers = info;

        instantiatedMaterial.SetBuffer("_DrawTriangles", this.meshBuffers.sourceMeshBuffer);
        instantiatedMaterial.SetMatrix("_LocalToWorld", this.transform.localToWorldMatrix);
    }

    public void OnDisable()
    {
        Release();
    }

    public void Release()
    {
        if (!initialized)
            return;
        meshBuffers?.Release();
        initialized = false;
    }

    // Update is called once per frame
    void LateUpdate()
    {
        if (!initialized)
            return;

        Graphics.DrawProceduralIndirect(instantiatedMaterial, bounds, MeshTopology.Triangles, this.meshBuffers.argsBuffer, 0, null, null, ShadowCastingMode.On, true, gameObject.layer);
    }
}

public class ChunkBuffers
{
    public bool released;
    public ComputeBuffer sourceMeshBuffer;
    public ComputeBuffer argsBuffer;

    ~ChunkBuffers()
    {
        Release();
    }

    public void Release()
    {
        released = true;
        sourceMeshBuffer?.Release();
        argsBuffer?.Release();
    }
}
