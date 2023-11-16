using UnityEngine;
using UnityEngine.Rendering;
using Utils;

[ExecuteInEditMode]
public class ProceduralFoliageRenderer : SpecialShader
{
    [Tooltip("A mesh to create foliage from")]
    [SerializeField] private Mesh sourceMesh = default;
    [SerializeField] private FoliageSettings foliageSettings = default; 

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SourceVertex
    {
        public Vector3 position;
        public Vector3 normal;
    }

    private bool initialized = false;

    private int idFoliageKernel;
    private int idTriToVertKernel;
    private int dispatchSize;
    private Bounds localBounds;

    private ComputeShader instantiatedFoliageComputeShader;
    private ComputeShader instantiatedTriToVertComputeShader;
    private Material instantiatedMaterial;

    private ComputeBuffer sourceVertexBuffer;
    private ComputeBuffer sourceTriBuffer;
    private ComputeBuffer drawBuffer;
    private ComputeBuffer argsBuffer;

    private const int SOURCE_VERT_STRIDE = sizeof(float) * (3 + 3);
    private const int SOURCE_TRI_STRIDE = sizeof(int);
    private const int DRAW_STRIDE = sizeof(float) * ((3 + 3 + 2) * 3);
    private const int ARGS_STRIDE = sizeof(int) * 4;

    public void OnEnable()
    {
        initialized = false;
    }

    public override void SetMesh(Mesh mesh)
    {
        this.sourceMesh = mesh;
    }

    public override void SetSettings(ShaderSettings settings)
    {
        this.foliageSettings = (FoliageSettings)settings;
    }

    public override void Render()
    {
        if (sourceMesh.triangles.Length == 0)
        {
            Release();
            return;
        }

        if (initialized)
            Release();

        initialized = true;

        instantiatedFoliageComputeShader = Instantiate(foliageSettings.foliageComputeShader);
        instantiatedTriToVertComputeShader = Instantiate(foliageSettings.triToVertComputeShader);
        instantiatedMaterial = Instantiate(foliageSettings.material);

        Vector3[] positions = sourceMesh.vertices;
        Vector3[] normals = sourceMesh.normals;
        int[] tris = sourceMesh.triangles;

        SourceVertex[] vertices = new SourceVertex[positions.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = new SourceVertex()
            {
                position = positions[i],
                normal = normals[i]
            };
        }

        int numTriangles = tris.Length / 3;

        sourceVertexBuffer = new ComputeBuffer(vertices.Length, SOURCE_VERT_STRIDE, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
        sourceVertexBuffer.SetData(vertices);

        sourceTriBuffer = new ComputeBuffer(tris.Length, SOURCE_TRI_STRIDE, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
        sourceTriBuffer.SetData(tris);

        drawBuffer = new ComputeBuffer(numTriangles * 2 * 3, DRAW_STRIDE, ComputeBufferType.Append); //Quad is two triangles
        drawBuffer.SetCounterValue(0);
        argsBuffer = new ComputeBuffer(1, ARGS_STRIDE, ComputeBufferType.IndirectArguments);

        argsBuffer.SetData(new int[] { 0, 1, 0, 0 });

        idFoliageKernel = instantiatedFoliageComputeShader.FindKernel("Main");
        idTriToVertKernel = instantiatedFoliageComputeShader.FindKernel("Main");

        instantiatedFoliageComputeShader.SetBuffer(idFoliageKernel, "_SourceVertices", sourceVertexBuffer);
        instantiatedFoliageComputeShader.SetBuffer(idFoliageKernel, "_SourceTriangles", sourceTriBuffer);
        instantiatedFoliageComputeShader.SetBuffer(idFoliageKernel, "_DrawTriangles", drawBuffer);
        instantiatedFoliageComputeShader.SetInt("_NumSourceTriangles", numTriangles);
        instantiatedFoliageComputeShader.SetFloat("_QuadSize", foliageSettings.QuadSize);
        instantiatedFoliageComputeShader.SetFloat("_InflationFactor", foliageSettings.Inflation);

        instantiatedTriToVertComputeShader.SetBuffer(idTriToVertKernel, "_IndirectArgsBuffer", argsBuffer);
        instantiatedMaterial.SetBuffer("_DrawTriangles", drawBuffer);

        instantiatedFoliageComputeShader.GetKernelThreadGroupSizes(idFoliageKernel, out uint threadGroupSize, out _, out _);
        dispatchSize = Mathf.CeilToInt((float)numTriangles / threadGroupSize);

        localBounds = sourceMesh.bounds;
        localBounds.Expand(foliageSettings.QuadSize/2.0f);

        ComputeGeometry();
    }

    public void OnDisable()
    {
        Release();
    }

    public override void Release()
    {
        if (initialized)
        {
            if (Application.isPlaying)
            {
                Destroy(instantiatedFoliageComputeShader);
                Destroy(instantiatedTriToVertComputeShader);
                Destroy(instantiatedMaterial);
            }
            else
            {
                DestroyImmediate(instantiatedFoliageComputeShader);
                DestroyImmediate(instantiatedTriToVertComputeShader);
                DestroyImmediate(instantiatedMaterial);
            }
            sourceVertexBuffer?.Release();
            sourceTriBuffer?.Release();
            drawBuffer?.Release();
            argsBuffer?.Release();
        }
        initialized = false;
    }

    public Bounds TransformBounds(Bounds boundsOS)
    {
        var center = transform.TransformPoint(boundsOS.center);

        var extents = boundsOS.size; //Don't use boundsOS.extents, this is object space
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
        if (!initialized)
            return;

        if (!Application.isPlaying)
        {
            Release();
            Render();
        }

        Bounds bounds = TransformBounds(localBounds);

        Graphics.DrawProceduralIndirect(instantiatedMaterial, bounds, MeshTopology.Triangles, argsBuffer, 0, null, null, ShadowCastingMode.Off, true, gameObject.layer);
    }

    private void ComputeGeometry()
    {
        drawBuffer.SetCounterValue(0);

        instantiatedFoliageComputeShader.SetMatrix("_LocalToWorld", transform.localToWorldMatrix);

        instantiatedFoliageComputeShader.Dispatch(idFoliageKernel, dispatchSize, 1, 1);

        ComputeBuffer.CopyCount(drawBuffer, argsBuffer, 0);

        instantiatedTriToVertComputeShader.Dispatch(idTriToVertKernel, 1, 1, 1);
    }
}

