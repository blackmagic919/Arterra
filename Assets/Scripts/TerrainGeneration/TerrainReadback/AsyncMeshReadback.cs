using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using System.Linq;
using Unity.Collections;
using Utils;
using WorldConfig;

//Purpose: Render geometry directly from GPU until it is readback async.

/*Requirement: 
 * If readback called on same geometry while it is not readback
 * overide previous asyncReadbackTask with new readback task.
 */
namespace TerrainGeneration.Readback{
using static OctreeTerrain;
/// <summary>
/// The readback system is responsible for 
/// reading back meshes from the GPU and intermediately rendering the meshes
/// directly from the GPU while this is happening.
/// </summary>
public class AsyncMeshReadback 
{
    private Transform transform;
    private Bounds shaderBounds = default;

    //Dependencies
    private static ComputeShader meshDrawArgsCreator;
    private static ComputeShader triangleTranscriber;
    private static ComputeShader vertexTranscriber;

    const int MESH_VERTEX_STRIDE_WORD = 3 * 2 + 2;
    const int TRI_STRIDE_WORD = 3;

    private static uint numMeshes;

    private static WorldConfig.Intrinsic.Readback settings;
    public GeometryHandle[] triHandles = default;
    public GeometryHandle vertexHandle = default;

    //Readback Task

    public AsyncMeshReadback(Transform transform, Bounds boundsOS)
    {   
        this.transform = transform;
        this.triHandles = new GeometryHandle[numMeshes];
        this.shaderBounds = CustomUtility.TransformBounds(transform, boundsOS);
    }

    public static void PresetData(){
        settings = Config.CURRENT.System.ReadBack.value;
        numMeshes = (uint)settings.indirectTerrainMats.Length;
        meshDrawArgsCreator = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Readback/MeshDrawArgs");
        triangleTranscriber = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Readback/TranscribeTriangles");
        vertexTranscriber = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Readback/TranscribeVertices");

        DensityGenerator.GeoGenOffsets offsets = DensityGenerator.bufferOffsets;
        int kernel = meshDrawArgsCreator.FindKernel("CSMain");
        meshDrawArgsCreator.SetBuffer(kernel, "counter", UtilityBuffers.GenerationBuffer);
        meshDrawArgsCreator.SetBuffer(kernel, "_IndirectArgsBuffer", UtilityBuffers.ArgumentBuffer);
        

        kernel = triangleTranscriber.FindKernel("Transcribe");
        triangleTranscriber.SetBuffer(kernel, "BaseTriangles", UtilityBuffers.GenerationBuffer);
        triangleTranscriber.SetBuffer(kernel, "triDict", UtilityBuffers.GenerationBuffer);
        triangleTranscriber.SetBuffer(kernel, "counter", UtilityBuffers.GenerationBuffer);
        triangleTranscriber.SetInt("bSTART_Dict", offsets.dictStart);
        triangleTranscriber.SetBuffer(kernel, "_MemoryBuffer", GenerationPreset.memoryHandle.Storage);
        triangleTranscriber.SetBuffer(kernel, "_AddressDict", GenerationPreset.memoryHandle.Address);

        kernel = vertexTranscriber.FindKernel("Transcribe");
        vertexTranscriber.SetBuffer(kernel, "baseVertices", UtilityBuffers.GenerationBuffer);
        vertexTranscriber.SetBuffer(kernel, "counter", UtilityBuffers.GenerationBuffer);
        vertexTranscriber.SetInt("bCOUNTER", offsets.vertexCounter);
        vertexTranscriber.SetInt("bSTART", offsets.vertStart);
        vertexTranscriber.SetBuffer(kernel, "_MemoryBuffer", GenerationPreset.memoryHandle.Storage);
        vertexTranscriber.SetBuffer(kernel, "_AddressDict", GenerationPreset.memoryHandle.Address);
    }

    public void ReleaseAllGeometry()
    {
        for(int i = 0; i < numMeshes; i++)
            triHandles[i]?.Release();  
        this.vertexHandle?.Release();
    }

    public void OffloadVerticesToGPU(int vertexCounter)
    {
        this.vertexHandle?.Release();

        uint vertAddress = GenerationPreset.memoryHandle.AllocateMemory(UtilityBuffers.GenerationBuffer, MESH_VERTEX_STRIDE_WORD, vertexCounter);
        TranscribeVertices((int)vertAddress, vertexCounter);

        this.vertexHandle = new GeometryHandle{addressIndex = vertAddress, memory = GenerationPreset.memoryHandle};
    }


    public void OffloadTrisToGPU(int triCounter, int triStart, int matIndex)
    {
        triHandles[matIndex]?.Release();

        //Transcribe data to memory heap for GPU-forward render
        uint geoHeapMemoryAddress = GenerationPreset.memoryHandle.AllocateMemory(UtilityBuffers.GenerationBuffer, TRI_STRIDE_WORD, triCounter);

        uint drawArgsAddress = UtilityBuffers.AllocateArgs(); //Allocates 4 bytes
        CreateDispArg(triCounter, (int)drawArgsAddress);

        TranscribeTriangles((int)geoHeapMemoryAddress, triCounter, triStart);

        //All buffers that are created by helper functions must enqueue to a buffer handle for consistency
        //Thus to indicate that they aren't being handled, initialize persistant buffers here
        RenderParams rp = GetRenderParams((int)geoHeapMemoryAddress, (int)this.vertexHandle.addressIndex, matIndex);
        triHandles[matIndex] = new GeometryHandle(rp, GenerationPreset.memoryHandle, geoHeapMemoryAddress, drawArgsAddress, matIndex);

        MainLateUpdateTasks.Enqueue(triHandles[matIndex]);
    }

    public void BeginMeshReadback(Action<ReadbackTask<IVertFormat.TVert>.SharedMeshInfo> callback){
        ReadbackTask<IVertFormat.TVert> RBTask = new ReadbackTask<IVertFormat.TVert>((ReadbackTask<IVertFormat.TVert>.SharedMeshInfo ret) => { callback(ret); ReleaseAllGeometry();}, (int)numMeshes);

        //Readback shared vertices
        GeometryHandle vertHandle = this.vertexHandle; //Get reference here so that it doesn't change when lambda evaluates
        if(vertHandle == null || !vertHandle.active)
            return;
        AsyncGPUReadback.Request(GenerationPreset.memoryHandle.Address, size: 8, offset: 8*(int)vertHandle.addressIndex, ret => OnAddressRecieved(ret, vertHandle, RBTask, onVertSizeRecieved));
        RBTask.AddTask();

        //Readback mesh triangles
        for(int matIndex = 0; matIndex < numMeshes; matIndex++){
            GeometryHandle geoHandle = triHandles[matIndex];
            if(geoHandle == null || !geoHandle.active)
                continue;
            //Begin readback of data
            AsyncGPUReadback.Request(GenerationPreset.memoryHandle.Address, size: 8, offset: 8*(int)geoHandle.addressIndex, ret => OnAddressRecieved(ret, geoHandle, RBTask, onTriSizeRecieved));
            RBTask.AddTask();
        }
    }

    void OnAddressRecieved(AsyncGPUReadbackRequest request, GeometryHandle geoHandle, ReadbackTask<IVertFormat.TVert> RBTask, ReadbackSizeRecieved onSizeRecieved)
    {
        if (geoHandle == null || !geoHandle.active) //Info was depreceated
            return;

        uint2 memAddress = request.GetData<uint2>().ToArray()[0];

        if (memAddress.x == 0) { //No geometry to readback
            geoHandle.Release();
            RBTask.onRBRecieved();
            return; 
        }

        //AsyncGPUReadback.Request size and offset are in units of bytes... 
        AsyncGPUReadback.Request(GenerationPreset.memoryHandle.Storage, size: 4, offset: 4 * ((int)memAddress.x - 1), ret => onSizeRecieved(ret, memAddress, geoHandle, RBTask));
    }
    private delegate void ReadbackSizeRecieved(AsyncGPUReadbackRequest request, uint2 address, GeometryHandle geoHandle, ReadbackTask<IVertFormat.TVert> RBTask);
    void onTriSizeRecieved(AsyncGPUReadbackRequest request, uint2 address, GeometryHandle geoHandle, ReadbackTask<IVertFormat.TVert> RBTask)
    {
        if (geoHandle == null || !geoHandle.active)  //Info was depreceated
            return;

        int memSize = (int)(request.GetData<uint>().ToArray()[0] - TRI_STRIDE_WORD); //subtract one triangle for padding
        int triStartWord = (int)(address.y * TRI_STRIDE_WORD);

        RBTask.RBMesh.IndexBuffer[geoHandle.matIndex] = new NativeArray<uint>(memSize, Allocator.Persistent);
        //AsyncGPUReadback.Request size and offset are in units of bytes... 
        AsyncGPUReadback.RequestIntoNativeArray(ref RBTask.RBMesh.IndexBuffer[geoHandle.matIndex], GenerationPreset.memoryHandle.Storage, size: 4 * memSize, offset: 4 * triStartWord, ret => onDataRecieved(geoHandle, RBTask));
    }

    void onVertSizeRecieved(AsyncGPUReadbackRequest request, uint2 address, GeometryHandle geoHandle,  ReadbackTask<IVertFormat.TVert> RBTask){
        if (geoHandle == null || !geoHandle.active) 
            return;

        int memSize = (int)(request.GetData<uint>().ToArray()[0] - MESH_VERTEX_STRIDE_WORD); //subtract one triangle for padding
        int vertCount = memSize / MESH_VERTEX_STRIDE_WORD;
        int vertStartWord = (int)(address.y * MESH_VERTEX_STRIDE_WORD);

        RBTask.RBMesh.VertexBuffer = new NativeArray<IVertFormat.TVert>(vertCount, Allocator.Persistent);
        //AsyncGPUReadback.Request size and offset are in units of bytes... 
        //Async says async but is run on main thread(kind of confusing)
        AsyncGPUReadback.RequestIntoNativeArray(ref RBTask.RBMesh.VertexBuffer, GenerationPreset.memoryHandle.Storage, size: 4 * memSize, offset: 4 * vertStartWord, ret => onDataRecieved(geoHandle, RBTask));
    }

    void onDataRecieved(GeometryHandle geoHandle, ReadbackTask<IVertFormat.TVert> RBTask)
    {
        if (geoHandle == null || !geoHandle.active)  //Info was depreceated
            return;
            
        RBTask.onRBRecieved();
    }

    void CreateDispArg(int triCounter, int address)
    {
        meshDrawArgsCreator.SetInt("bCOUNTER", triCounter);
        meshDrawArgsCreator.SetInt("argOffset", address);
        meshDrawArgsCreator.Dispatch(0, 1, 1, 1);
    }

    RenderParams GetRenderParams(int triAddress, int vertAddress, int matIndex)
    {
        RenderParams rp = new RenderParams(settings.indirectTerrainMats[matIndex])
        {
            worldBounds = this.shaderBounds,
            shadowCastingMode = ShadowCastingMode.On,
            matProps = new MaterialPropertyBlock()
        };
        rp.matProps.SetBuffer("Vertices", GenerationPreset.memoryHandle.Storage);
        rp.matProps.SetBuffer("Triangles", GenerationPreset.memoryHandle.Storage);
        rp.matProps.SetBuffer("_AddressDict", GenerationPreset.memoryHandle.Address);

        rp.matProps.SetInt("triAddress", triAddress);
        rp.matProps.SetInt("vertAddress", vertAddress);
        
        rp.matProps.SetInt("_Vertex4ByteStride", MESH_VERTEX_STRIDE_WORD);
        rp.matProps.SetMatrix("_LocalToWorld", this.transform.localToWorldMatrix);

        return rp;
    }

    void TranscribeVertices(int addressIndex, int vertCounter){
        ComputeBuffer args = UtilityBuffers.CountToArgs(vertexTranscriber, UtilityBuffers.GenerationBuffer, countOffset: vertCounter);

        vertexTranscriber.SetInt("addressIndex", addressIndex);
        vertexTranscriber.DispatchIndirect(0, args);
    }

    void TranscribeTriangles(int addressIndex, int triCounter, int triStart)
    {
        ComputeBuffer args = UtilityBuffers.CountToArgs(triangleTranscriber, UtilityBuffers.GenerationBuffer, countOffset: triCounter);
        triangleTranscriber.SetInt("bCOUNTER_Tri", triCounter);
        triangleTranscriber.SetInt("bSTART_Tri", triStart);
        triangleTranscriber.SetInt("triAddress", addressIndex);

        triangleTranscriber.DispatchIndirect(0, args);
    }
    
}

public enum ReadbackMaterial{
    terrain = 0,
    water = 1,
}


public class GeometryHandle : UpdateTask
{
    public RenderParams rp = default;
    public GenerationPreset.MemoryHandle memory = default;
    public int matIndex = -1;
    public uint addressIndex = 0;
    public uint argsAddress = 0;

    public GeometryHandle(RenderParams rp, GenerationPreset.MemoryHandle memory, uint addressIndex, uint argsAddress, int matIndex)
    {
        this.addressIndex = addressIndex;
        this.memory = memory;
        this.rp = rp;
        this.matIndex = matIndex;
        this.argsAddress = argsAddress;
        this.active = true;
    }

    public GeometryHandle()
    {
        this.active = true;
    }

    ~GeometryHandle(){
        Release();
    }

    public void Release(){
        if(!this.active) return;
        this.active = false;

        //Release geometry memory
        if(this.addressIndex != 0)
            GenerationPreset.memoryHandle.ReleaseMemory(this.addressIndex);
        if(this.argsAddress != 0)
            UtilityBuffers.ReleaseArgs(this.argsAddress);
    }

    public void Disable(){ this.active = false;}

    public override void Update(MonoBehaviour mono = null)
    {
        if (!active)
            return;

        //Offset in bytes = address * 4 args per address * 4 bytes per arg
        Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, UtilityBuffers.ArgumentBuffer, 1, (int)argsAddress);
    }
}

public interface IVertFormat {
    //Like to be static, but .NET 5 doesn't support
    public void SetVertexBufferParams(Mesh mesh, int count);
    
    public struct TVert : IVertFormat
{
    public Vector3 pos;
    public Vector3 norm;
    public int2 material;

    //Data is packed into one struct, so read to first stream
    public void SetVertexBufferParams(Mesh mesh, int count){
            mesh.SetVertexBufferParams(count, 
            new [] {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, stream: 0),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, stream: 0),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.SInt32, 2, stream: 0)
            });
        }
    }

    public struct SVert : IVertFormat
    {
        public Vector3 pos;
        public int material;
        public int uv;

        //Data is packed into one struct, so read to first stream
        public void SetVertexBufferParams(Mesh mesh, int count){
            mesh.SetVertexBufferParams(count, 
            new [] {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, stream: 0),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.UInt32, 1, stream: 0),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.UInt32, 1, stream: 0)
            });
        }
    }
}

public class ReadbackTask<Vert> where Vert : struct, IVertFormat{
    private int numRBTasks;
    private Action<SharedMeshInfo> RBMeshCallback;
    public SharedMeshInfo RBMesh;

    public ReadbackTask(Action<SharedMeshInfo> RBMeshCallback, int numMeshes){
        this.numRBTasks = 0;
        this.RBMeshCallback = RBMeshCallback;
        this.RBMesh = new SharedMeshInfo(numMeshes);
    }

    public void AddTask(){ numRBTasks++; }

    public void onRBRecieved(){
        numRBTasks--;
        if(numRBTasks == 0) RBMeshCallback(RBMesh);
    }

    //As data is read this way, reading to native array is significantly faster
    public class SharedMeshInfo
    {
        public NativeArray<uint>[] IndexBuffer;
        public NativeArray<Vert> VertexBuffer;

        public SharedMeshInfo(int numSubMeshes)
        {
            IndexBuffer = new NativeArray<uint>[numSubMeshes];
        }

        ~SharedMeshInfo(){
            Release();
        }

        public void Release(){
            for(int i = 0; i < IndexBuffer.Length; i++)
                IndexBuffer[i].Dispose();
            VertexBuffer.Dispose();
        }

        public Mesh GenerateMesh(UnityEngine.Rendering.IndexFormat meshIndexFormat)
        {
            Mesh mesh = new();
            if(VertexBuffer == null || VertexBuffer.Length == 0) return null;

            //Set Layouts
            int totalIndices = IndexBuffer.Sum(x => x.Length);
            new Vert().SetVertexBufferParams(mesh, VertexBuffer.Length);
            mesh.SetIndexBufferParams(totalIndices, meshIndexFormat);

            //Set Shared Vertex Data
            mesh.SetVertexBufferData(VertexBuffer, 0, 0, VertexBuffer.Length, 0, MeshUpdateFlags.DontValidateIndices);
            
            //Set Indices and Submesh Data
            int meshIndexStart = 0;
            SubMeshDescriptor[] subMeshes = new SubMeshDescriptor[IndexBuffer.Length];
            for(int i = 0; i < IndexBuffer.Length; i++){
                NativeArray<uint> indices = IndexBuffer[i];
                if(indices == null || indices.Length == 0) continue;

                mesh.SetIndexBufferData(indices, 0, meshIndexStart, indices.Length, MeshUpdateFlags.DontValidateIndices);
                subMeshes[i] = new SubMeshDescriptor(meshIndexStart, indices.Length, MeshTopology.Triangles);
                meshIndexStart += indices.Length;
            }
            mesh.SetIndexBufferParams(meshIndexStart, meshIndexFormat);
            
            mesh.SetSubMeshes(subMeshes);
            return mesh;
        }

        public Mesh GetSubmesh(int submeshIndex, UnityEngine.Rendering.IndexFormat meshIndexFormat){
            if(submeshIndex >= IndexBuffer.Length || IndexBuffer[submeshIndex].Length == 0) return null;
            if(VertexBuffer == null || VertexBuffer.Length == 0) return null;

            Mesh mesh = new Mesh();
            NativeArray<uint> indices = IndexBuffer[submeshIndex];
            new Vert().SetVertexBufferParams(mesh, VertexBuffer.Length);
            mesh.SetIndexBufferParams(indices.Length, meshIndexFormat);

            mesh.SetVertexBufferData(VertexBuffer, 0, 0, VertexBuffer.Length, 0, MeshUpdateFlags.DontValidateIndices);
            mesh.SetIndexBufferData(indices, 0, 0, indices.Length, MeshUpdateFlags.DontValidateIndices);
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, indices.Length, MeshTopology.Triangles));
            
            return mesh;
        }
    }
}}