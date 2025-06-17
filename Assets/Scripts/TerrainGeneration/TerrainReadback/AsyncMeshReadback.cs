using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using System.Linq;
using Unity.Collections;
using Utils;
using WorldConfig;

namespace TerrainGeneration.Readback{
using static OctreeTerrain;
/// <summary>
/// The readback system is responsible for reading back meshes from the GPU and 
/// intermediately rendering the meshes directly from the GPU while this is happening.
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
    /// <summary> The <see cref="GeometryHandle">geometry handles</see> for all the unqiue materials that are used to render the chunk. Each
    /// chunk will have a unique handle for each material in <see cref="WorldConfig.Intrinsic.Readback.indirectTerrainMats"/>
    /// regardless of whether any geometry in the chunk uses that material. </summary>
    public GeometryHandle[] triHandles = default;
    /// <summary> The <see cref="GeometryHandle">geometry handles</see> for the shared vertices of the chunk. This
    /// holds all the vertex information used by geometry linked through <see cref="triHandles"/> in the chunk. </summary>
    public GeometryHandle vertexHandle = default;

    /// <summary> Creates an instance of the readback system specific for a chunk. Each readback 
    /// instance is responsible for managing a single chunk and its associated geometry. </summary>
    /// <param name="transform">The orientation of the chunk. Used for rendering the chunk indirectly</param>
    /// <param name="boundsOS">The bounds in the chunk's Object Space(local space) of the extents of the geometry. </param>
    public AsyncMeshReadback(Transform transform, Bounds boundsOS)
    {   
        this.transform = transform;
        this.triHandles = new GeometryHandle[numMeshes];
        this.shaderBounds = CustomUtility.TransformBounds(transform, boundsOS);
    }

    /// <summary> Presets static data shared by the readback system. This should be called before any 
    /// instances of the readback system are used. Referenced in <see cref="SystemProtocol.Startup"/>.  </summary>
    public static void PresetData(){
        settings = Config.CURRENT.System.ReadBack.value;
        numMeshes = (uint)settings.indirectTerrainMats.Length;
        meshDrawArgsCreator = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Readback/MeshDrawArgs");
        triangleTranscriber = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Readback/TranscribeTriangles");
        vertexTranscriber = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Readback/TranscribeVertices");
        settings.Initialize();

        Map.Generator.GeoGenOffsets offsets = Map.Generator.bufferOffsets;
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

    /// <summary> Releases all static data shared by the readback system. This should be called when the 
    /// readback system is no longer needed (when the world is closed). Referenced in <see cref="SystemProtocol.Shutdown"/>. </summary>
    public static void Release(){
        settings.Release();
    }
    /// <summary> Releases all <see cref="GeometryHandle">geometry handles</see> associated with the specific chunk's readback.
    /// This should be called to ensure a chunk is not holding onto any GPU memory upon being unloaded. </summary>
    public void ReleaseAllGeometry()
    {
        for(int i = 0; i < numMeshes; i++)
            triHandles[i]?.Release();  
        this.vertexHandle?.Release();
    }

    /// <summary> Offloads(copies) vertices to long term GPU storage. This can be used to store vertices for rendering or to hold onto them
    /// until instructions are flushed so it may be read back to the CPU. A Handle to the stored vertices will be 
    /// saved in <see cref="vertexHandle"/>, with any possible previous handles being released. </summary>
    /// <param name="vertexCounter">The location within the <see cref="UtilityBuffers.GenerationBuffer">working buffer</see> 
    /// storing the amount of vertices to be copied. </param>
    public void OffloadVerticesToGPU(int vertexCounter)
    {
        this.vertexHandle?.Release();

        uint vertAddress = GenerationPreset.memoryHandle.AllocateMemory(UtilityBuffers.GenerationBuffer, MESH_VERTEX_STRIDE_WORD, vertexCounter);
        TranscribeVertices((int)vertAddress, vertexCounter);

        this.vertexHandle = new GeometryHandle{addressIndex = vertAddress, memory = GenerationPreset.memoryHandle};
    }

    /// <summary> Offloads(copies) triangles(index buffer) to long term GPU storage. This can be used to store triangles for 
    /// rendering or to hold onto them until instructions are flushed so it may be read back to the CPU. A Handle to the 
    /// stored triangles will be saved in <see cref="triHandles"/>, with any possible previous handles being released. </summary>
    /// <param name="triCounter">The location within the <see cref="UtilityBuffers.GenerationBuffer">working buffer</see> 
    /// storing the amount of triangles to be copied. </param>
    /// <param name="triStart">The location within the <see cref="UtilityBuffers.GenerationBuffer">working buffer</see> 
    /// of the start of the triangles(index buffer).</param>
    /// <param name="matIndex">The index within <see cref="WorldConfig.Intrinsic.Readback.indirectTerrainMats"/> of the material to use
    /// for geometry referenced by these triangles if it is to be indirectly rendered. </param>
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

    /// <summary> Creates a request to readback all geometry referenced by <see cref="GeometryHandle"/>s of the chunk's instance
    /// to a <see cref="ReadbackTask{T}"> CPU mesh object </see>. This is an asynchronous task that retrieves the data once
    /// the CPU and GPU states are synchronized. </summary>
    /// <param name="callback"> The callback to be called once the readback is complete. The callback will be provided the readback <see cref="ReadbackTask{T}.SharedMeshInfo"/> </param>
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

    private void OnAddressRecieved(AsyncGPUReadbackRequest request, GeometryHandle geoHandle, ReadbackTask<IVertFormat.TVert> RBTask, ReadbackSizeRecieved onSizeRecieved)
    {
        if (geoHandle == null || !geoHandle.active) //Info was depreceated
            return;

        uint2 memAddress = request.GetData<uint2>().ToArray()[0];

        if (memAddress.x == 0) { //No geometry to readback
            geoHandle.Release();
            RBTask.OnRBRecieved();
            return; 
        }

        //AsyncGPUReadback.Request size and offset are in units of bytes... 
        AsyncGPUReadback.Request(GenerationPreset.memoryHandle.Storage, size: 4, offset: 4 * ((int)memAddress.x - 1), ret => onSizeRecieved(ret, memAddress, geoHandle, RBTask));
    }
    private delegate void ReadbackSizeRecieved(AsyncGPUReadbackRequest request, uint2 address, GeometryHandle geoHandle, ReadbackTask<IVertFormat.TVert> RBTask);
    private void onTriSizeRecieved(AsyncGPUReadbackRequest request, uint2 address, GeometryHandle geoHandle, ReadbackTask<IVertFormat.TVert> RBTask)
    {
        if (geoHandle == null || !geoHandle.active)  //Info was depreceated
            return;

        int memSize = (int)(request.GetData<uint>().ToArray()[0] - TRI_STRIDE_WORD); //subtract one triangle for padding
        int triStartWord = (int)(address.y * TRI_STRIDE_WORD);

        RBTask.RBMesh.IndexBuffer[geoHandle.matIndex] = new NativeArray<uint>(memSize, Allocator.Persistent);
        //AsyncGPUReadback.Request size and offset are in units of bytes... 
        AsyncGPUReadback.RequestIntoNativeArray(ref RBTask.RBMesh.IndexBuffer[geoHandle.matIndex], GenerationPreset.memoryHandle.Storage, size: 4 * memSize, offset: 4 * triStartWord, ret => onDataRecieved(geoHandle, RBTask));
    }

    private void onVertSizeRecieved(AsyncGPUReadbackRequest request, uint2 address, GeometryHandle geoHandle,  ReadbackTask<IVertFormat.TVert> RBTask){
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

    private void onDataRecieved(GeometryHandle geoHandle, ReadbackTask<IVertFormat.TVert> RBTask)
    {
        if (geoHandle == null || !geoHandle.active)  //Info was depreceated
            return;
            
        RBTask.OnRBRecieved();
    }

    private void CreateDispArg(int triCounter, int address)
    {
        meshDrawArgsCreator.SetInt("bCOUNTER", triCounter);
        meshDrawArgsCreator.SetInt("argOffset", address);
        meshDrawArgsCreator.Dispatch(0, 1, 1, 1);
    }

    private RenderParams GetRenderParams(int triAddress, int vertAddress, int matIndex)
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

    private void TranscribeVertices(int addressIndex, int vertCounter){
        ComputeBuffer args = UtilityBuffers.CountToArgs(vertexTranscriber, UtilityBuffers.GenerationBuffer, countOffset: vertCounter);
        vertexTranscriber.SetInt("addressIndex", addressIndex);
        vertexTranscriber.DispatchIndirect(0, args);
    }

    private void TranscribeTriangles(int addressIndex, int triCounter, int triStart)
    {
        ComputeBuffer args = UtilityBuffers.CountToArgs(triangleTranscriber, UtilityBuffers.GenerationBuffer, countOffset: triCounter);
        triangleTranscriber.SetInt("bCOUNTER_Tri", triCounter);
        triangleTranscriber.SetInt("bSTART_Tri", triStart);
        triangleTranscriber.SetInt("triAddress", addressIndex);

        triangleTranscriber.DispatchIndirect(0, args);
    }
    
}

/// <summary> The types of materials that can be read back from the GPU. Associates
/// a name to the materials defined in <see cref="WorldConfig.Intrinsic.Readback.indirectTerrainMats"/>. </summary>
public enum ReadbackMaterial{
    /// <summary> The index of the material used for terrain geometry. </summary>
    terrain = 0,
    /// <summary> The index of the material used for liquid geometry. </summary>
    water = 1,
}

/// <summary> A handle to geometry that is stored in <see cref="GenerationPreset.MemoryHandle._GPUMemorySource"> long term GPU storage </see>. 
/// If the geometry is triangles(an index buffer), the handle will also store information for rendering the geometry indirectly,
/// by using vertex information stored in a <see cref="AsyncMeshReadback.vertexHandle"> seperate geometry handle </see>.  </summary>
public class GeometryHandle : UpdateTask
{
    /// <summary> The <see cref="RenderParams"/> describing how the geometry should be rendered indirectly if it is to be rendered. </summary>
    public RenderParams rp = default;
    /// <summary> The memory handle of the buffer where the geometry is stored. Specifically, the geometry will be stored in 
    /// <see cref="GenerationPreset.MemoryHandle._GPUMemorySource"/> of this memory handle. </summary>
    public GenerationPreset.MemoryHandle memory = default;
    /// <summary> The index of the material used for rendering the geometry indirectly. This index is used to reference the material in
    /// <see cref="WorldConfig.Intrinsic.Readback.indirectTerrainMats"/>. </summary>
    public int matIndex = -1;
    /// <summary> The address of the geometry within the <see cref="GenerationPreset.MemoryHandle.Address"> address buffer </see> of the <see cref="memory"> memory handle </see>
    /// of the location that stores the real address of the geometry in <see cref="GenerationPreset.MemoryHandle._GPUMemorySource"/>. See <see cref="GenerationPreset.MemoryHandle.Address"/> for more information.
    /// </summary>
    public uint addressIndex = 0;
    /// <summary> If the geometry is to be rendered indirectly, the address of the <see cref="GraphicsBuffer.IndirectDrawArgs"> draw arguments </see> used 
    /// to render the geometry. This is used to reference the draw arguments in <see cref="UtilityBuffers.ArgumentBuffer"/>.  </summary>
    public uint argsAddress = 0;
    /// <summary> Creates a geometry handle for triangle geometry(index buffer) that will be rendered indirectly. As such, 
    /// it needs to specify information on how to render it beyond just the location of the geometry itself. </summary>
    /// <param name="rp">The <see cref="rp">Render Parameters</see> describing how the geometry should be rendered </param>
    /// <param name="memory">The <see cref="memory">Memory Handle</see> of the buffer that holds the copied geometry.</param>
    /// <param name="addressIndex">The <see cref="addressIndex">><b>indirect</b> address</see> of the geometry within the memory handle.</param>
    /// <param name="argsAddress">The <see cref="argsAddress">location</see> of the rendering arguments to render the geometry. </param>
    /// <param name="matIndex">The material index of the material used to render the geometry, See <see cref="matIndex"/> for more info. </param>
    public GeometryHandle(RenderParams rp, GenerationPreset.MemoryHandle memory, uint addressIndex, uint argsAddress, int matIndex)
    {
        this.addressIndex = addressIndex;
        this.memory = memory;
        this.rp = rp;
        this.matIndex = matIndex;
        this.argsAddress = argsAddress;
        this.active = true;
    }

    /// <summary> Creates a geometry handle for storing geometry. The geometry will not be rendered indirectly
    /// and thus does not need to specify any rendering parameters. Useful for vertex buffers which 
    /// are only rendered through being referenced by triangles. </summary>
    public GeometryHandle()
    {
        this.active = true;
    }

    /// <summary> Destructor Releases GeoHandle to prevent memory leaks </summary>
    ~GeometryHandle(){
        Release();
    }

    /// <summary> Releases the geometry handle, freeing any memory blocks it holds in <see cref="memory"> the memory handle</see>. 
    /// This should be called when the geometry is no longer needed to ensure that the GPU memory is released. </summary>
    public void Release(){
        if(!this.active) return;
        this.active = false;

        //Release geometry memory
        if(this.addressIndex != 0)
            GenerationPreset.memoryHandle.ReleaseMemory(this.addressIndex);
        if(this.argsAddress != 0)
            UtilityBuffers.ReleaseArgs(this.argsAddress);
    }

    /// <summary> Updates the geometry handle to pass any indirect render commands it may have.
    /// If the geometry handle renders indirectly, the command to do so will be issued here.
    /// This done every frame through unity's update loop through <see cref="MainLoopUpdateTasks"/>.
    /// See <see cref="UpdateTask"/> for more information. </summary>
    /// <param name="mono">See <see cref="UpdateTask.Update(MonoBehaviour)"/> for more info. </param>
    public override void Update(MonoBehaviour mono = null)
    {
        if (!active)
            return;

        //Offset in bytes = address * 4 args per address * 4 bytes per arg
        Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, UtilityBuffers.ArgumentBuffer, 1, (int)argsAddress);
    }
}

/// <summary> Interface for formats of vertices that are read back from the GPU. If a vertex format, the 
/// layout of a vertex's data, is to be read back into a <see cref="ReadbackTask{T}.SharedMeshInfo"/>, 
/// the vertex being readback should implement this interface to ensure proper initialization of
/// the mesh destination. </summary>
public interface IVertFormat {
    /// <summary> Sets the vertex buffer parameters of a mesh to match the format of the vertex data being read back.</summary>
    /// <param name="mesh"> The mesh whose vertex format information is set. See <see cref="VertexAttributeDescriptor"/> for more info. </param>
    /// <param name="count"> The amount of vertices in the mesh expected to use the specified mesh format. </param>
    //Like to be static, but .NET 5 doesn't support
    public void SetVertexBufferParams(Mesh mesh, int count);
    
    /// <summary> The vertex format for terrain geometry created during the games 
    /// terrain generation. See <see cref="IVertFormat"/> for more information.  </summary>
    public struct TVert : IVertFormat
    {
        private Vector3 pos;
        private Vector3 norm;
        private int2 material;

        /// <exclude />
        public void SetVertexBufferParams(Mesh mesh, int count){
            mesh.SetVertexBufferParams(count, 
            new [] {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, stream: 0),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, stream: 0),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.SInt32, 2, stream: 0)
            });
        }
    }

    /// <summary> The vertex format for sprite geometry created in <see cref="SpriteExtruder"> sprite extrusion </see>
    /// extruding images to 3D models. See <see cref="IVertFormat"/> for more information.  </summary>
    public struct SVert : IVertFormat
    {
        private Vector3 pos;
        private int material;
        private int uv;

        /// <exclude />
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

/// <summary>
/// A readback task responsible for copying <b>all</b> of a mesh's data from the GPU to the CPU when both processers are
/// synchronized. If data is dispersed between multiple locations(e.g. vertex buffer, index buffer), it will only 
/// provide the combined mesh once all data is read back. This is to prevent the possibility of incomplete meshes 
/// replacing indirect rendering, possibly causing a gap to appear in the terrain.
/// </summary>
/// <typeparam name="Vert">The <see cref="IVertFormat">type of vertex</see>, of the mesh being read back. </typeparam>
public class ReadbackTask<Vert> where Vert : struct, IVertFormat{
    private int numRBTasks;
    private Action<SharedMeshInfo> RBMeshCallback;
    /// <summary> The mesh data that is read back from the GPU. This is the destination 
    /// of the readback task and will contain the mesh data once complete. </summary>
    public SharedMeshInfo RBMesh;
    /// <summary> Creates a readback task for reading back mesh data from the GPU. </summary>
    /// <param name="RBMeshCallback">The callback that will be answered when the full mesh has been readback. </param>
    /// <param name="numMeshes">The number of unique submeshes to be created in the final mesh. </param>
    public ReadbackTask(Action<SharedMeshInfo> RBMeshCallback, int numMeshes){
        this.numRBTasks = 0;
        this.RBMeshCallback = RBMeshCallback;
        this.RBMesh = new SharedMeshInfo(numMeshes);
    }

    /// <summary> Adds a task that needs to be completed before the mesh is considered fully read back.
    /// This effectively subscribes a handle capable of stalling the readback completion 
    /// until the subscribed task is complete. Used to ensure multiple independent
    /// tasks are all completed before the mesh is read back. <seealso cref="OnRBRecieved"/>. </summary>
    public void AddTask(){ numRBTasks++; }

    /// <summary> Called when a task is completed. If all tasks are completed, the mesh is considered fully read back and
    /// no further tasks will be considered. A <see cref="AddTask">subscribed task</see> may call this function
    /// to unsubscribe itself from stalling the readback process once it has injected its information. </summary>
    public void OnRBRecieved(){
        numRBTasks--;
        if(numRBTasks == 0) RBMeshCallback(RBMesh);
    }

    /// <summary> The shared mesh data that is read back from the GPU. This is the destination
    /// of the readback task and will contain the mesh data once complete. </summary>
    /// <remarks> As data is read from the buffer this way, reading to native array is significantly faster </remarks>
    public class SharedMeshInfo
    {
        /// <summary> The index buffer of all submeshes in the mesh. Each submesh is stored in a separate array. </summary>
        public NativeArray<uint>[] IndexBuffer;
        /// <summary> The vertex buffer of the mesh. This is shared between all submeshes in the mesh. </summary>
        public NativeArray<Vert> VertexBuffer;

        /// <summary> Creates a destination for mesh data with the specified amount
        /// of submeshes to be readback independently. </summary>
        /// <param name="numSubMeshes">The number of submeshes to support reading back. </param>
        public SharedMeshInfo(int numSubMeshes){ IndexBuffer = new NativeArray<uint>[numSubMeshes]; }

        /// <summary> Destructor to release all buffers, see <see cref="Release"/> for more information. </summary>
        ~SharedMeshInfo(){
            Release();
        }

        /// <summary> Releases all buffers used by the shared mesh info. This should be called 
        /// when the mesh is no longer needed to ensure any intermediate readback buffers are recovered. </summary>
        public void Release(){
            for(int i = 0; i < IndexBuffer.Length; i++)
                IndexBuffer[i].Dispose();
            VertexBuffer.Dispose();
        }

        /// <summary> Generates a <see cref="Mesh"/> using the readback 
        /// information stored in this object.  </summary>
        /// <param name="meshIndexFormat"> The <see cref="IndexFormat">index format</see> used in 
        /// the mesh's index buffer, either 16-bit or 32-bit indices. </param>
        /// <returns>The generated <see cref="Mesh"/> based off the <see cref="SharedMeshInfo"/>'s information. </returns>
        public Mesh GenerateMesh(UnityEngine.Rendering.IndexFormat meshIndexFormat)
        {
            if(VertexBuffer == null || VertexBuffer.Length == 0) return null;
            Mesh mesh = new();
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
            mesh.hideFlags = HideFlags.DontSave;
            return mesh;
        }

        /// <summary> Generates a submesh from the readback information stored in this object. This
        /// will obtain a mesh using only one of the submeshes listed in <see cref="IndexBuffer"/>. </summary>
        /// <remarks> This would still require reobtaining all vertex information from the entire <see cref="VertexBuffer"/>
        /// as the vertices belonging to only the submesh is not stored seperately. </remarks>
        /// <param name="submeshIndex">The index within the <see cref="IndexBuffer"/> of the triangles belonging to the desired submesh, </param>
        /// <param name="meshIndexFormat"> The <see cref="IndexFormat">index format</see> used in 
        /// the mesh's index buffer, either 16-bit or 32-bit indices. </param>
        /// <returns></returns>
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