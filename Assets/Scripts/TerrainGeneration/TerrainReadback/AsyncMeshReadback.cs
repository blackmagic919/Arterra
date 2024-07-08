using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using static EndlessTerrain;
using System.Linq;
using Unity.Collections;
using Utils;

//Purpose: Render geometry directly from GPU until it is readback async.

/*Requirement: 
 * If readback called on same geometry while it is not readback
 * overide previous asyncReadbackTask with new readback task.
 */

public class AsyncMeshReadback 
{
    private Queue<ComputeBuffer> tempBuffers = new Queue<ComputeBuffer>();
    private Transform transform;
    private Bounds shaderBounds = default;

    //Dependencies
    public static ComputeShader memorySizeCalculator;
    public static ComputeShader meshDrawArgsCreator;
    public static ComputeShader triangleTranscriber;
    public static ComputeShader vertexTranscriber;

    const int MESH_VERTEX_STRIDE_WORD = 3 * 2 + 2;
    const int TRI_STRIDE_WORD = 3;

    private readonly uint numMeshes;

    public ReadbackSettings settings;
    public GeometryHandle[] triHandles = default;
    public GeometryHandle vertexHandle = default;

    //Readback Task

    public AsyncMeshReadback(Transform transform, Bounds boundsOS)
    {   
        this.settings = WorldStorageHandler.WORLD_OPTIONS.ReadBackSettings.value;
        this.transform = transform;
        this.numMeshes = (uint)settings.indirectTerrainMats.Length;
        this.triHandles = new GeometryHandle[numMeshes];
        this.shaderBounds = CustomUtility.TransformBounds(transform, boundsOS);
    }

    static AsyncMeshReadback(){
        memorySizeCalculator = Resources.Load<ComputeShader>("TerrainGeneration/Readback/BaseMemorySize");
        meshDrawArgsCreator = Resources.Load<ComputeShader>("TerrainGeneration/Readback/MeshDrawArgs");
        triangleTranscriber = Resources.Load<ComputeShader>("TerrainGeneration/Readback/TranscribeTriangles");
        vertexTranscriber = Resources.Load<ComputeShader>("TerrainGeneration/Readback/TranscribeVertices");
    }

    public void ReleaseAllGeometry()
    {
        for(int i = 0; i < numMeshes; i++)
            triHandles[i]?.Release();  
        this.vertexHandle?.Release();
    }

    private void ReleaseTempBuffers()
    {
        while (tempBuffers.Count > 0)
            tempBuffers.Dequeue().Release();
    }

    public void OffloadVerticesToGPU(DensityGenerator.GeoGenOffsets bufferOffsets)
    {
        this.vertexHandle?.Release();

        uint vertAddress = GenerationPreset.memoryHandle.AllocateMemory(UtilityBuffers.GenerationBuffer, MESH_VERTEX_STRIDE_WORD, bufferOffsets.vertexCounter);
        TranscribeVertices(GenerationPreset.memoryHandle.AccessStorage(), GenerationPreset.memoryHandle.AccessAddresses(), (int)vertAddress, bufferOffsets.vertexCounter, bufferOffsets.vertStart);

        this.vertexHandle = new GeometryHandle{addressIndex = vertAddress, memory = GenerationPreset.memoryHandle};
    }


    public void OffloadTrisToGPU(int triCounter, int triStart, int dictStart, int matIndex)
    {
        triHandles[matIndex]?.Release();

        //Transcribe data to memory heap for GPU-forward render
        uint geoHeapMemoryAddress = GenerationPreset.memoryHandle.AllocateMemory(UtilityBuffers.GenerationBuffer, TRI_STRIDE_WORD, triCounter);

        uint drawArgsAddress = UtilityBuffers.AllocateArgs(); //Allocates 4 bytes
        CreateDispArg(UtilityBuffers.ArgumentBuffer, triCounter, (int)drawArgsAddress);

        TranscribeTriangles(GenerationPreset.memoryHandle.AccessStorage(), GenerationPreset.memoryHandle.AccessAddresses(),
                           (int)geoHeapMemoryAddress, triCounter, triStart, dictStart);

        //All buffers that are created by helper functions must enqueue to a buffer handle for consistency
        //Thus to indicate that they aren't being handled, initialize persistant buffers here
        RenderParams rp = GetRenderParams(GenerationPreset.memoryHandle.AccessStorage(), GenerationPreset.memoryHandle.AccessAddresses(), (int)geoHeapMemoryAddress, (int)this.vertexHandle.addressIndex, matIndex);
        triHandles[matIndex] = new GeometryHandle(rp, GenerationPreset.memoryHandle, geoHeapMemoryAddress, drawArgsAddress, matIndex);

        MainLoopUpdateTasks.Enqueue(triHandles[matIndex]);
        ReleaseTempBuffers();
    }

    public void BeginMeshReadback(Action<SharedMeshInfo> callback){

        ReadbackTask RBTask = new ReadbackTask((SharedMeshInfo ret) => {ReleaseAllGeometry(); callback(ret);}, (int)numMeshes);

        //Readback shared vertices
        GeometryHandle vertHandle = this.vertexHandle; //Get reference here so that it doesn't change when lambda evaluates
        if(vertHandle == null || !vertHandle.active)
            return;
        AsyncGPUReadback.Request(GenerationPreset.memoryHandle.AccessAddresses(), size: 8, offset: 8*(int)vertHandle.addressIndex, ret => onVertAddressRecieved(ret, vertHandle, RBTask));
        RBTask.AddTask();

        //Readback mesh triangles
        for(int matIndex = 0; matIndex < numMeshes; matIndex++){
            GeometryHandle geoHandle = triHandles[matIndex];
            if(geoHandle == null || !geoHandle.active)
                continue;
            //Begin readback of data
            AsyncGPUReadback.Request(GenerationPreset.memoryHandle.AccessAddresses(), size: 8, offset: 8*(int)geoHandle.addressIndex, ret => onTriAddressRecieved(ret, geoHandle, RBTask));
            RBTask.AddTask();
        }
    }

    void onTriAddressRecieved(AsyncGPUReadbackRequest request, GeometryHandle geoHandle, ReadbackTask RBTask)
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
        AsyncGPUReadback.Request(GenerationPreset.memoryHandle.AccessStorage(), size: 4, offset: 4 * ((int)memAddress.x - 1), ret => onTriSizeRecieved(ret, memAddress, geoHandle, RBTask));
    }

    void onVertAddressRecieved(AsyncGPUReadbackRequest request, GeometryHandle geoHandle, ReadbackTask RBTask){
        if (geoHandle == null || !geoHandle.active) //Info was depreceated
            return;
        
        uint2 memAddress = request.GetData<uint2>().ToArray()[0];

        if(memAddress.x == 0){
            geoHandle.Release();
            RBTask.onRBRecieved();
            return;
        }

        AsyncGPUReadback.Request(GenerationPreset.memoryHandle.AccessStorage(), size: 4, offset: 4 * ((int)memAddress.x - 1), ret => onVertSizeRecieved(ret, memAddress, geoHandle, RBTask));
    }

    void onTriSizeRecieved(AsyncGPUReadbackRequest request, uint2 address, GeometryHandle geoHandle, ReadbackTask RBTask)
    {
        if (geoHandle == null || !geoHandle.active)  //Info was depreceated
            return;

        int memSize = (int)(request.GetData<uint>().ToArray()[0] - TRI_STRIDE_WORD); //subtract one triangle for padding
        int triStartWord = (int)(address.y * TRI_STRIDE_WORD);

        RBTask.RBMesh.IndexBuffer[geoHandle.matIndex] = new NativeArray<uint>(memSize, Allocator.Persistent);
        //AsyncGPUReadback.Request size and offset are in units of bytes... 
        AsyncGPUReadback.RequestIntoNativeArray(ref RBTask.RBMesh.IndexBuffer[geoHandle.matIndex], GenerationPreset.memoryHandle.AccessStorage(), size: 4 * memSize, offset: 4 * triStartWord, ret => onTriDataRecieved(geoHandle, RBTask));
    }

    void onVertSizeRecieved(AsyncGPUReadbackRequest request, uint2 address, GeometryHandle geoHandle, ReadbackTask RBTask){
        if (geoHandle == null || !geoHandle.active) 
            return;

        int memSize = (int)(request.GetData<uint>().ToArray()[0] - MESH_VERTEX_STRIDE_WORD); //subtract one triangle for padding
        int vertCount = memSize / MESH_VERTEX_STRIDE_WORD;
        int vertStartWord = (int)(address.y * MESH_VERTEX_STRIDE_WORD);

        RBTask.RBMesh.VertexBuffer = new NativeArray<Vertex>(vertCount, Allocator.Persistent);
        //AsyncGPUReadback.Request size and offset are in units of bytes... 
        //Async says async but is run on main thread(kind of confusing)
        AsyncGPUReadback.RequestIntoNativeArray(ref RBTask.RBMesh.VertexBuffer, GenerationPreset.memoryHandle.AccessStorage(), size: 4 * memSize, offset: 4 * vertStartWord, ret => onVertDataRecieved(geoHandle, RBTask));
    }

    void onTriDataRecieved(GeometryHandle geoHandle, ReadbackTask RBTask)
    {
        if (geoHandle == null || !geoHandle.active)  //Info was depreceated
            return;
            
        RBTask.onRBRecieved();
    }

    void onVertDataRecieved(GeometryHandle geoHandle, ReadbackTask RBTask){
        if (geoHandle == null || !geoHandle.active)  //Info was depreceated
            return;
        
        RBTask.onRBRecieved();
    }


    void CreateDispArg(GraphicsBuffer argumentBuffer, int triCounter, int address)
    {
        meshDrawArgsCreator.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        meshDrawArgsCreator.SetInt("bCOUNTER", triCounter);
        meshDrawArgsCreator.SetInt("argOffset", address);

        meshDrawArgsCreator.SetBuffer(0, "_IndirectArgsBuffer", argumentBuffer);
        meshDrawArgsCreator.Dispatch(0, 1, 1, 1);
    }

    RenderParams GetRenderParams(ComputeBuffer storage, ComputeBuffer addresses, int triAddress, int vertAddress, int matIndex)
    {
        RenderParams rp = new RenderParams(this.settings.indirectTerrainMats[matIndex])
        {
            worldBounds = this.shaderBounds,
            shadowCastingMode = ShadowCastingMode.On,
            matProps = new MaterialPropertyBlock()
        };

        rp.matProps.SetBuffer("Vertices", storage);
        rp.matProps.SetBuffer("Triangles", storage);
        rp.matProps.SetBuffer("_AddressDict", addresses);

        rp.matProps.SetInt("triAddress", triAddress);
        rp.matProps.SetInt("vertAddress", vertAddress);
        
        rp.matProps.SetInt("_Vertex4ByteStride", MESH_VERTEX_STRIDE_WORD);
        rp.matProps.SetMatrix("_LocalToWorld", this.transform.localToWorldMatrix);

        return rp;
    }

    void TranscribeVertices(ComputeBuffer memoryBuffer, ComputeBuffer addressBuffer, int addressIndex, int vertCounter, int vertStart){
        ComputeBuffer args = UtilityBuffers.CountToArgs(vertexTranscriber, UtilityBuffers.GenerationBuffer, countOffset: vertCounter);
        
        vertexTranscriber.SetBuffer(0, "baseVertices", UtilityBuffers.GenerationBuffer);
        vertexTranscriber.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        vertexTranscriber.SetInt("bCOUNTER", vertCounter);
        vertexTranscriber.SetInt("bSTART", vertStart);

        vertexTranscriber.SetBuffer(0, "_MemoryBuffer", memoryBuffer);
        vertexTranscriber.SetBuffer(0, "_AddressDict", addressBuffer);
        vertexTranscriber.SetInt("addressIndex", addressIndex);
        vertexTranscriber.DispatchIndirect(0, args);
    }

    void TranscribeTriangles(ComputeBuffer memoryBuffer, ComputeBuffer addressBuffer, int addressIndex, int triCounter, int triStart, int dictStart)
    {
        ComputeBuffer args = UtilityBuffers.CountToArgs(triangleTranscriber, UtilityBuffers.GenerationBuffer, countOffset: triCounter);

        triangleTranscriber.SetBuffer(0, "BaseTriangles", UtilityBuffers.GenerationBuffer);
        triangleTranscriber.SetBuffer(0, "triDict", UtilityBuffers.GenerationBuffer);
        triangleTranscriber.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        triangleTranscriber.SetInt("bCOUNTER_Tri", triCounter);
        triangleTranscriber.SetInt("bSTART_Tri", triStart);
        triangleTranscriber.SetInt("bSTART_Dict", dictStart);

        triangleTranscriber.SetBuffer(0, "_MemoryBuffer", memoryBuffer);
        triangleTranscriber.SetBuffer(0, "_AddressDict", addressBuffer);
        triangleTranscriber.SetInt("triAddress", addressIndex);

        triangleTranscriber.DispatchIndirect(0, args);
    }

    public struct Vertex
    {
        public Vector3 pos;
        public Vector3 norm;
        public int2 material;

        //Data is packed into one struct, so read to first stream
        public static void SetVertexBufferParams(Mesh mesh, int count){
            mesh.SetVertexBufferParams(count, 
            new [] {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, stream: 0),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, stream: 0),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.SInt32, 2, stream: 0)
            });
        }
    }

    //As data is read this way, reading to native array is significantly faster
    public class SharedMeshInfo
    {
        public NativeArray<uint>[] IndexBuffer;
        public NativeArray<Vertex> VertexBuffer;

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
            Vertex.SetVertexBufferParams(mesh, VertexBuffer.Length);
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
            
            mesh.SetSubMeshes(subMeshes);
            return mesh;
        }

        public Mesh GetSubmesh(int submeshIndex, UnityEngine.Rendering.IndexFormat meshIndexFormat){
            if(submeshIndex >= IndexBuffer.Length || IndexBuffer[submeshIndex].Length == 0) return null;
            if(VertexBuffer == null || VertexBuffer.Length == 0) return null;

            Mesh mesh = new Mesh();
            NativeArray<uint> indices = IndexBuffer[submeshIndex];
            Vertex.SetVertexBufferParams(mesh, VertexBuffer.Length);
            mesh.SetIndexBufferParams(indices.Length, meshIndexFormat);

            mesh.SetVertexBufferData(VertexBuffer, 0, 0, VertexBuffer.Length, 0, MeshUpdateFlags.DontValidateIndices);
            mesh.SetIndexBufferData(indices, 0, 0, indices.Length, MeshUpdateFlags.DontValidateIndices);
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, indices.Length, MeshTopology.Triangles));
            
            return mesh;
        }
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

        public override void Update()
        {
            if (!active)
                return;
                
            //Offset in bytes = address * 4 args per address * 4 bytes per arg
            Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, UtilityBuffers.ArgumentBuffer, 1, (int)argsAddress);
        }
    }

    class ReadbackTask{
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
    }

    /*
    ComputeBuffer CalculateGeoSize(ComputeBuffer geoSize, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer memSize = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
        bufferHandle.Enqueue(memSize);

        this.settings.memorySizeCalculator.SetBuffer(0, "geoSize", geoSize);
        this.settings.memorySizeCalculator.SetInt("stride4Bytes", MESH_VERTEX_STRIDE_4BYTE * 3);

        this.settings.memorySizeCalculator.SetBuffer(0, "byteLength", memSize);

        this.settings.memorySizeCalculator.Dispatch(0, 1, 1, 1);

        return memSize;
    }
    */
}