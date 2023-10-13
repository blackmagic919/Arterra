using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteInEditMode]
public class ProceduralGrassRenderer : MonoBehaviour
{
    [System.Serializable]
    public class GrassSettings
    {
        [Tooltip("Total height of grass layer stack")]
        public float grassHeight = 0.5f;
        [Tooltip("Maximum # of grass layers")]
        public int maxLayers = 16;
        [Tooltip("LOD Setting, Minimum Distance for LOD")]
        public float lodMinCameraDistance = 1;
        [Tooltip("LOD Setting, Maximum Distance for LOD")]
        public float lodMaxCameraDistance = 1;
        [Tooltip("LOD Setting, Power Applied to LOD Distance Falloff"), Range(0, 10)]
        public float lodFactor = 2;
        [Tooltip("Use world position XZ as the UV")]
        public bool useWorldPositionAsUV;
        [Tooltip("Multiplier on World Position if using world position as UV")]
        public float worldPositionUVScale;

        [Tooltip("The grass geometry creating compute shader")]
        public ComputeShader grassComputeShader = default;
        [Tooltip("The triangle count adjustment compute shader")]
        public ComputeShader triToVertComputeShader = default;
        [Tooltip("The material to render the grass mesh")]
        public Material material = default;
    }

    [Tooltip("A mesh to extrude the grass from")]
    [SerializeField] public Mesh sourceMesh = default;

    [SerializeField] public GrassSettings grassSettings = default;



    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SourceVertex
    {
        public Vector3 position;
        public Vector3 normal;
        public Color color;
        public Vector2 uv;
    }

    public bool initialized = false;//
    private bool isEnabled = false;
    private ComputeBuffer sourceVertexBuffer;
    private ComputeBuffer sourceTriBuffer;
    private ComputeBuffer drawBuffer;
    private ComputeBuffer argsBuffer;

    private ComputeShader instantiatedGrassComputeShader;
    private ComputeShader instantiatedTriToVertComputeShader;
    private Material instantiatedMaterial;


    private int idGrassKernel;
    private int idTriToVertKernel;
    private int dispatchSize;
    private Bounds localBounds;

    private const int SOURCE_VERT_STRIDE = sizeof(float) * (3 + 3 + 4 + 2);
    private const int SOURCE_TRI_STRIDE = sizeof(int);
    private const int DRAW_STRIDE = sizeof(float) * (2 + (3 + 3 + 2 + 1) * 3);
    private const int ARGS_STRIDE = sizeof(int) * 4;

    public void OnEnable()
    {
        initialized = false;
        isEnabled = false;
    }

    public void OnStart()
    {
        if (sourceMesh.triangles.Length == 0) {
            isEnabled = false;
            return;
        }

        isEnabled = true;

        if (initialized)
            OnDisable();

        initialized = true;

        instantiatedGrassComputeShader = Instantiate(grassSettings.grassComputeShader);
        instantiatedTriToVertComputeShader = Instantiate(grassSettings.triToVertComputeShader);
        instantiatedMaterial = Instantiate(grassSettings.material);


        Vector3[] positions = sourceMesh.vertices;
        Vector3[] normals = sourceMesh.normals;
        Color[] colors = sourceMesh.colors;
        Vector2[] uvs = sourceMesh.uv;

        int[] tris = sourceMesh.triangles;

        SourceVertex[] vertices = new SourceVertex[positions.Length];
        for(int i = 0; i < vertices.Length; i++)
        {
            if (grassSettings.useWorldPositionAsUV) { 
                vertices[i] = new SourceVertex()
                {
                    position = positions[i],
                    normal = normals[i],
                    color = colors[i],
                };
            }
            else
            {
                vertices[i] = new SourceVertex()
                {
                    position = positions[i],
                    normal = normals[i],
                    color = colors[i],
                    uv = uvs[i]
                };
            }
        }
        int numTriangles = tris.Length / 3;

        sourceVertexBuffer = new ComputeBuffer(vertices.Length, SOURCE_VERT_STRIDE, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
        sourceVertexBuffer.SetData(vertices);

        sourceTriBuffer = new ComputeBuffer(tris.Length, SOURCE_TRI_STRIDE, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
        sourceTriBuffer.SetData(tris);

        drawBuffer = new ComputeBuffer(numTriangles * grassSettings.maxLayers, DRAW_STRIDE, ComputeBufferType.Append);
        drawBuffer.SetCounterValue(0);
        argsBuffer = new ComputeBuffer(1, ARGS_STRIDE, ComputeBufferType.IndirectArguments);

        argsBuffer.SetData(new int[] { 0, 1, 0, 0 });
        idGrassKernel = grassSettings.grassComputeShader.FindKernel("Main");
        idTriToVertKernel = instantiatedTriToVertComputeShader.FindKernel("Main");

        instantiatedGrassComputeShader.SetBuffer(idGrassKernel, "_SourceVertices", sourceVertexBuffer);
        instantiatedGrassComputeShader.SetBuffer(idGrassKernel, "_SourceTriangles", sourceTriBuffer);
        instantiatedGrassComputeShader.SetBuffer(idGrassKernel, "_DrawTriangles", drawBuffer);
        instantiatedGrassComputeShader.SetInt("_NumSourceTriangles", numTriangles);
        instantiatedGrassComputeShader.SetInt("_MaxLayers", grassSettings.maxLayers);
        instantiatedGrassComputeShader.SetFloat("_TotalHeight", grassSettings.grassHeight);
        instantiatedGrassComputeShader.SetFloat("_CameraDistanceMin", grassSettings.lodMinCameraDistance);
        instantiatedGrassComputeShader.SetFloat("_CameraDistanceMax", grassSettings.lodMaxCameraDistance);
        instantiatedGrassComputeShader.SetFloat("_CameraDistanceFactor", Mathf.Max(0, grassSettings.lodFactor));
        instantiatedGrassComputeShader.SetFloat("_WorldPositionToUVScale", grassSettings.worldPositionUVScale);
        if (grassSettings.useWorldPositionAsUV)
            instantiatedGrassComputeShader.EnableKeyword("USE_WORLD_POSITION_AS_UV");


        instantiatedTriToVertComputeShader.SetBuffer(idTriToVertKernel, "_IndirectArgsBuffer", argsBuffer);

        instantiatedMaterial.SetBuffer("_DrawTriangles", drawBuffer);

        instantiatedGrassComputeShader.GetKernelThreadGroupSizes(idGrassKernel, out uint threadGroupSize, out _, out _);
        dispatchSize = Mathf.CeilToInt((float)numTriangles / threadGroupSize);

        localBounds = sourceMesh.bounds;
        localBounds.Expand(grassSettings.grassHeight);
    }

    public void OnDisable()
    {
        if (initialized)
        {
            if (Application.isPlaying)
            {
                Destroy(instantiatedGrassComputeShader);
                Destroy(instantiatedTriToVertComputeShader);
                Destroy(instantiatedMaterial);
            }
            else
            {
                DestroyImmediate(instantiatedGrassComputeShader);
                DestroyImmediate(instantiatedTriToVertComputeShader);
                DestroyImmediate(instantiatedMaterial);
            }
            sourceVertexBuffer.Release();
            sourceTriBuffer.Release();
            drawBuffer.Release();
            argsBuffer.Release();
        }
        
        initialized = false;
    }

    public void Disable()
    {
        OnDisable();
        isEnabled = false;
    }

    public Bounds TransformBounds(Bounds boundsOS)
    {
        var center = transform.TransformPoint(boundsOS.center);

        var extents = boundsOS.size;
        var axisX = transform.TransformVector(extents.x, 0, 0);
        var axisY = transform.TransformVector(0, extents.y, 0);
        var axisZ = transform.TransformVector(0, 0, extents.z);

        extents.x = Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x);
        extents.y = Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y);
        extents.z = Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z);

        return new Bounds(center, extents);
    }

    private void LateUpdate()
    {
        if (!isEnabled)
            return;

        if (!Application.isPlaying)
        {
            OnDisable();
            OnStart();
        }

        drawBuffer.SetCounterValue(0);

        Bounds bounds = TransformBounds(localBounds);

        instantiatedGrassComputeShader.SetMatrix("_LocalToWorld", transform.localToWorldMatrix);
        instantiatedGrassComputeShader.SetVector("_CameraPosition", Camera.main.transform.position);
        instantiatedGrassComputeShader.Dispatch(idGrassKernel, dispatchSize, 1, 1);
        Shader.SetGlobalVector("_CameraPosition", Camera.main.transform.position);

        ComputeBuffer.CopyCount(drawBuffer, argsBuffer, 0);

        instantiatedTriToVertComputeShader.Dispatch(idTriToVertKernel, 1, 1, 1);

        Graphics.DrawProceduralIndirect(instantiatedMaterial, bounds, MeshTopology.Triangles, argsBuffer, 0, null, null, ShadowCastingMode.Off, true, gameObject.layer);
    }

}
