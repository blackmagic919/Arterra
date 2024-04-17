using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using static UtilityBuffers;
using static EndlessTerrain;
using System.Linq;

//Purpose: Render geometry directly from GPU until it is readback async.

/*Requirement: 
 * If readback called on same geometry while it is not readback
 * overide previous asyncReadbackTask with new readback task.
 */

public class AsyncMeshReadback 
{
    private Queue<ComputeBuffer> tempBuffers = new Queue<ComputeBuffer>();
    private MeshFilter meshFilter; //Keep this to disable meshfilter when on GPU
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

    public AsyncMeshReadback(ReadbackSettings settings, Transform transform, Bounds boundsOS, MeshFilter meshFilter)
    {   
        this.settings = settings;
        this.transform = transform;
        this.numMeshes = (uint)settings.indirectTerrainMats.Length;
        this.triHandles = new GeometryHandle[numMeshes];
        this.shaderBounds = TransformBounds(transform, boundsOS);
        this.meshFilter = meshFilter;
    }

    static AsyncMeshReadback(){
        memorySizeCalculator = Resources.Load<ComputeShader>("TerrainGeneration/Readback/BaseMemorySize");
        meshDrawArgsCreator = Resources.Load<ComputeShader>("TerrainGeneration/Readback/MeshDrawArgs");
        triangleTranscriber = Resources.Load<ComputeShader>("TerrainGeneration/Readback/TranscribeTriangles");
        vertexTranscriber = Resources.Load<ComputeShader>("TerrainGeneration/Readback/TranscribeVertices");
    }

    public Bounds TransformBounds(Transform transform, Bounds boundsOS)
    {
        var center = transform.TransformPoint(boundsOS.center);

        var size = boundsOS.size;
        var axisX = transform.TransformVector(size.x, 0, 0);
        var axisY = transform.TransformVector(0, size.y, 0);
        var axisZ = transform.TransformVector(0, 0, size.z);

        size.x = Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x);
        size.y = Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y);
        size.z = Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z);

        return new Bounds(center, size);
    }

    public void ReleaseAllGeometry()
    {
        for(int i = 0; i < numMeshes; i++)
            ReleaseGeometry(triHandles[i]);
        ReleaseGeometry(this.vertexHandle);
    }

    private void ReleaseGeometry(GeometryHandle handle)
    {
        if (handle == null || !handle.initialized) return;

        handle.Release(this.settings.memoryBuffer);
    }

    private void ReleaseTempBuffers()
    {
        while (tempBuffers.Count > 0)
            tempBuffers.Dequeue().Release();
    }

    public void OffloadVerticesToGPU(int vertCounter, int vertStart)
    {
        ReleaseGeometry(this.vertexHandle);

        meshFilter.mesh = null;//Clear CPU-forward rendered mesh
        uint vertAddress = this.settings.memoryBuffer.AllocateMemory(UtilityBuffers.GenerationBuffer, MESH_VERTEX_STRIDE_WORD, vertCounter);
        TranscribeVertices(this.settings.memoryBuffer.AccessStorage(), this.settings.memoryBuffer.AccessAddresses(), (int)vertAddress, vertCounter, vertStart);

        this.vertexHandle = new GeometryHandle{addressIndex = vertAddress};
    }


    public void OffloadTrisToGPU(int triCounter, int triStart, int dictStart, int matIndex)
    {
        ReleaseGeometry(triHandles[matIndex]);

        //Transcribe data to memory heap for GPU-forward render
        uint geoHeapMemoryAddress = this.settings.memoryBuffer.AllocateMemory(UtilityBuffers.GenerationBuffer, TRI_STRIDE_WORD, triCounter);

        uint drawArgsAddress = UtilityBuffers.AllocateArgs(); //Allocates 4 bytes
        CreateDispArg(UtilityBuffers.ArgumentBuffer, triCounter, (int)drawArgsAddress);

        TranscribeTriangles(this.settings.memoryBuffer.AccessStorage(), this.settings.memoryBuffer.AccessAddresses(),
                           (int)geoHeapMemoryAddress, triCounter, triStart, dictStart);

        //All buffers that are created by helper functions must enqueue to a buffer handle for consistency
        //Thus to indicate that they aren't being handled, initialize persistant buffers here
        RenderParams rp = GetRenderParams(this.settings.memoryBuffer.AccessStorage(), this.settings.memoryBuffer.AccessAddresses(), (int)geoHeapMemoryAddress, (int)this.vertexHandle.addressIndex, matIndex);
        triHandles[matIndex] = new GeometryHandle(rp, geoHeapMemoryAddress, drawArgsAddress, matIndex);

        MainLoopUpdateTasks.Enqueue(triHandles[matIndex]);
        ReleaseTempBuffers();
    }


    public void BeginMeshReadback(Action<MeshInfo> callback){

        ReadbackTask RBTask = new ReadbackTask((MeshInfo ret) => {ReleaseAllGeometry(); callback(ret);});

        //Readback shared vertices
        GeometryHandle vertHandle = this.vertexHandle; //Get reference here so that it doesn't change when lambda evaluates
        if(vertHandle == null || !vertHandle.initialized)
            return;
        AsyncGPUReadback.Request(this.settings.memoryBuffer.AccessAddresses(), size: 8, offset: 8*(int)vertHandle.addressIndex, ret => onVertAddressRecieved(ret, vertHandle, RBTask));
        RBTask.AddTask();

        //Readback mesh triangles
        for(int matIndex = 0; matIndex < numMeshes; matIndex++){
            GeometryHandle geoHandle = triHandles[matIndex];
            if(geoHandle == null || !geoHandle.initialized)
                continue;
            //Begin readback of data
            AsyncGPUReadback.Request(this.settings.memoryBuffer.AccessAddresses(), size: 8, offset: 8*(int)geoHandle.addressIndex, ret => onTriAddressRecieved(ret, geoHandle, RBTask));
            RBTask.AddTask();
        }
    }

    void onTriAddressRecieved(AsyncGPUReadbackRequest request, GeometryHandle geoHandle, ReadbackTask RBTask)
    {
        if (geoHandle == null || !geoHandle.initialized) //Info was depreceated
            return;

        uint2 memAddress = request.GetData<uint2>().ToArray()[0];

        if (memAddress.x == 0) { //No geometry to readback
            ReleaseGeometry(geoHandle);
            RBTask.onRBRecieved();
            return; 
        }

        //AsyncGPUReadback.Request size and offset are in units of bytes... 
        AsyncGPUReadback.Request(this.settings.memoryBuffer.AccessStorage(), size: 4, offset: 4 * ((int)memAddress.x - 1), ret => onTriSizeRecieved(ret, memAddress, geoHandle, RBTask));
    }

    void onVertAddressRecieved(AsyncGPUReadbackRequest request, GeometryHandle geoHandle, ReadbackTask RBTask){
        if (geoHandle == null || !geoHandle.initialized) //Info was depreceated
            return;
        
        uint2 memAddress = request.GetData<uint2>().ToArray()[0];

        if(memAddress.x == 0){
            ReleaseGeometry(geoHandle);
            RBTask.onRBRecieved();
            return;
        }

        AsyncGPUReadback.Request(this.settings.memoryBuffer.AccessStorage(), size: 4, offset: 4 * ((int)memAddress.x - 1), ret => onVertSizeRecieved(ret, memAddress, geoHandle, RBTask));
    }

    void onTriSizeRecieved(AsyncGPUReadbackRequest request, uint2 address, GeometryHandle geoHandle, ReadbackTask RBTask)
    {
        if (geoHandle == null || !geoHandle.initialized)  //Info was depreceated
            return;

        int memSize = (int)(request.GetData<uint>().ToArray()[0] - TRI_STRIDE_WORD); //subtract one triangle for padding
        int triStartWord = (int)(address.y * TRI_STRIDE_WORD);

        //AsyncGPUReadback.Request size and offset are in units of bytes... 
        AsyncGPUReadback.Request(this.settings.memoryBuffer.AccessStorage(), size: 4 * memSize, offset: 4 * triStartWord, ret => onTriDataRecieved(ret, memSize, geoHandle, RBTask));
    }

    void onVertSizeRecieved(AsyncGPUReadbackRequest request, uint2 address, GeometryHandle geoHandle, ReadbackTask RBTask){
        if (geoHandle == null || !geoHandle.initialized) 
            return;

        int memSize = (int)(request.GetData<uint>().ToArray()[0] - MESH_VERTEX_STRIDE_WORD); //subtract one triangle for padding
        int vertStartWord = (int)(address.y * MESH_VERTEX_STRIDE_WORD);

        //AsyncGPUReadback.Request size and offset are in units of bytes... 
        AsyncGPUReadback.Request(this.settings.memoryBuffer.AccessStorage(), size: 4 * memSize, offset: 4 * vertStartWord, ret => onVertDataRecieved(ret, memSize, geoHandle, RBTask));
    }

    void onTriDataRecieved(AsyncGPUReadbackRequest request, int size, GeometryHandle geoHandle, ReadbackTask RBTask)
    {
        if (geoHandle == null || !geoHandle.initialized)  //Info was depreceated
            return;

        int numTris = size / TRI_STRIDE_WORD;
        uint[] tris = request.GetData<uint>().ToArray();

        RBTask.RBMesh.subMeshes[geoHandle.matIndex].indexStart = RBTask.RBMesh.triangles.Count;
        RBTask.RBMesh.subMeshes[geoHandle.matIndex].indexCount = numTris * 3;

        for (int i = 0; i < 3 * numTris; i++)
            RBTask.RBMesh.triangles.Add((int)tris[i]);

        RBTask.onRBRecieved();
    }

    void onVertDataRecieved(AsyncGPUReadbackRequest request, int size, GeometryHandle geoHandle, ReadbackTask RBTask){
        if (geoHandle == null || !geoHandle.initialized)  //Info was depreceated
            return;
        
        int numVerts = size / MESH_VERTEX_STRIDE_WORD;
        Vertex[] vertices = request.GetData<Vertex>().ToArray();

        for(int i = 0; i < numVerts; i++){
            RBTask.RBMesh.vertices.Add(vertices[i].pos);
            RBTask.RBMesh.normals.Add(vertices[i].norm);
            RBTask.RBMesh.colorMap.Add(new Color(vertices[i].material.x, vertices[i].material.y, 0));
        }

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
    }


    public class GeometryHandle : UpdateTask
    {
        public RenderParams rp = default;
        public int matIndex = -1;
        public uint addressIndex = 0;
        public uint argsAddress = 0;

        public GeometryHandle(RenderParams rp, uint addressIndex, uint argsAddress, int matIndex)
        {
            this.addressIndex = addressIndex;
            this.rp = rp;
            this.matIndex = matIndex;
            this.argsAddress = argsAddress;
            this.initialized = true;
        }

        public GeometryHandle()
        {
            this.initialized = true;
        }

        public void Release(MemoryBufferSettings memory){
            this.initialized = false;

            //Release geometry memory
            if(this.addressIndex != 0)
                memory.ReleaseMemory(this.addressIndex);
            if(this.argsAddress != 0)
                UtilityBuffers.ReleaseArgs(this.argsAddress);
        }

        public override void Update()
        {
            if (!initialized)
                return;
                
            //Offset in bytes = address * 4 args per address * 4 bytes per arg
            Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, UtilityBuffers.ArgumentBuffer, 1, (int)argsAddress);
        }
    }

    class ReadbackTask{
        private int numRBTasks;
        private Action<MeshInfo> RBMeshCallback;
        public MeshInfo RBMesh;

        public ReadbackTask(Action<MeshInfo> RBMeshCallback){
            this.numRBTasks = 0;
            this.RBMeshCallback = RBMeshCallback;
            this.RBMesh = new MeshInfo{subMeshes = Enumerable.Repeat<SubMeshDescriptor>(new SubMeshDescriptor(0, 0, MeshTopology.Triangles), 2).ToArray()};
        }

        public void AddTask(){ numRBTasks++; }

        public void onRBRecieved(){
            numRBTasks--;
            if(numRBTasks == 0){
                RBMeshCallback(RBMesh);
            }
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