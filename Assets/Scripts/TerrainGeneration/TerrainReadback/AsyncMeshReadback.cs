using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using static EditorMesh;
using static UtilityBuffers;

//Purpose: Render geometry directly from GPU until it is readback async.

/*Requirement: 
 * If readback called on same geometry while it is not readback
 * overide previous asyncReadbackTask with new readback task.
 */
public class AsyncMeshReadback : MonoBehaviour
{
    private ReadbackSettings settings;
    private Queue<ComputeBuffer> tempBuffers = new Queue<ComputeBuffer>();
    private GeometryHandle[] geoHandles = default;
    private Bounds shaderBounds = default;
    private MeshFilter[] meshFilters;

    const int MESH_VERTEX_STRIDE_4BYTE = (3 * 2 + (2 + 1));

    public void Initialize(ReadbackSettings settings, Bounds boundsOS, MeshFilter[] meshFilters)
    {
        this.settings = settings;
        this.geoHandles = new GeometryHandle[settings.indirectTerrainMats.Count];
        this.shaderBounds = TransformBounds(boundsOS);
        this.meshFilters = meshFilters;
    }

    public Bounds TransformBounds(Bounds boundsOS)
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

    private void OnDisable()
    {
        ReleaseAllGeometry();
        ReleaseTempBuffers();
    }

    private void ReleaseAllGeometry()
    {
        for(int i = 0; i < settings.indirectTerrainMats.Count; i++)
            ReleaseGeometry(geoHandles[i]);
    }

    private void ReleaseGeometry(GeometryHandle handle)
    {
        if (handle == null || !handle.isOnHeap) return;
        handle.isOnHeap = false;
        
        //Destroy Material
        if (Application.isPlaying) Destroy(handle.matInstance);
        else DestroyImmediate(handle.matInstance);

        //Release geometry memory
        this.settings.memoryBuffer.ReleaseMemory(handle.addressIndex);
        UtilityBuffers.ReleaseArgs(handle.argsAddress);
    }

    private void ReleaseTempBuffers()
    {
        while (tempBuffers.Count > 0)
            tempBuffers.Dequeue().Release();
    }
    
    void LateUpdate()
    {
        ComputeBuffer sharedArgs = UtilityBuffers.ArgumentBuffer;
        for (int i = 0; i < settings.indirectTerrainMats.Count; i++)
        {
            GeometryHandle handle = geoHandles[i];
            if (handle == null || !handle.isOnHeap) continue;
            //Offset in bytes = address * 4 args per address * 4 bytes per arg
            Graphics.DrawProceduralIndirect(handle.matInstance, shaderBounds, MeshTopology.Triangles, sharedArgs, (int)handle.argsAddress * 4 * 4, null, null, ShadowCastingMode.On, true, gameObject.layer);
        }
    }

    public void CreateReadbackMeshInfoTask(ComputeBuffer baseGeo, int matIndex, Action<MeshInfo> callback)
    {
        ReleaseGeometry(geoHandles[matIndex]);
        meshFilters[matIndex].mesh = null;

        //Transcribe data to memory heap for GPU-forward render
        ComputeBuffer baseCount = CopyCount(baseGeo, ref tempBuffers);

        ComputeBuffer memByte4Length = CalculateGeoSize(baseCount, ref tempBuffers);
        uint geoHeapMemoryAddress = this.settings.memoryBuffer.AllocateMemory(memByte4Length);

        uint drawArgsAddress = UtilityBuffers.AllocateArgs(); //Allocates 4 bytes
        CreateDispArg(UtilityBuffers.ArgumentBuffer, baseCount, (int)drawArgsAddress);

        TranscribeGeometry(this.settings.memoryBuffer.AccessStorage(), this.settings.memoryBuffer.AccessAddresses(),
                           baseGeo, baseCount, (int)geoHeapMemoryAddress, ref tempBuffers);

        //All buffers that are created by helper functions must enqueue to a buffer handle for consistency
        //Thus to indicate that they aren't being handled, initialize persistant buffers here
        Material matInstance = InstantiateMaterial(this.settings.memoryBuffer.AccessStorage(), this.settings.memoryBuffer.AccessAddresses(), (int)geoHeapMemoryAddress, matIndex);
        geoHandles[matIndex] = new GeometryHandle(matInstance, geoHeapMemoryAddress, drawArgsAddress, true);

        //Begin readback of data
        AsyncGPUReadback.Request(this.settings.memoryBuffer.AccessAddresses(), size: 4, offset: 4*(int)geoHeapMemoryAddress, ret => onMemAddressRecieved(ret, geoHandles[matIndex], callback));

        ReleaseTempBuffers();
    }

    void onMemAddressRecieved(AsyncGPUReadbackRequest request, GeometryHandle geoHandle, Action<MeshInfo> callback)
    {
        if (!geoHandle.isOnHeap) //Info was depreceated
            return;

        uint memAddress = request.GetData<uint>().ToArray()[0];

        if (memAddress == 0) { //No geometry to readback
            ReleaseGeometry(geoHandle);
            return; 
        }

        //AsyncGPUReadback.Request size and offset are in units of bytes... 
        AsyncGPUReadback.Request(this.settings.memoryBuffer.AccessStorage(), size: 4, offset: 4 * ((int)memAddress - 1), ret => onMemSizeRecieved(ret, geoHandle, memAddress, callback));
    }

    void onMemSizeRecieved(AsyncGPUReadbackRequest request, GeometryHandle geoHandle, uint address, Action<MeshInfo> callback)
    {
        if (!geoHandle.isOnHeap)  //Info was depreceated
            return;

        uint memSize = request.GetData<uint>().ToArray()[0];

        //AsyncGPUReadback.Request size and offset are in units of bytes... 
        AsyncGPUReadback.Request(this.settings.memoryBuffer.AccessStorage(), size: 4 * (int)memSize, offset: 4 * (int)address, ret => onMeshDataRecieved(ret, geoHandle, memSize, callback));
    }

    void onMeshDataRecieved(AsyncGPUReadbackRequest request, GeometryHandle geoHandle, uint size, Action<MeshInfo> callback)
    {
        if (!geoHandle.isOnHeap)  //Info was depreceated
            return;

        MeshInfo chunk = new MeshInfo();

        int numTris = ((int)size) / (MESH_VERTEX_STRIDE_4BYTE * 3);

        uint[] tris = request.GetData<uint>().ToArray();

        Dictionary<int2, int> vertDict = new Dictionary<int2, int>();
        int vertCount = 0;

        for (int i = 0; i < numTris; i++)
        {
            TriangleConst tri = new TriangleConst(i * (MESH_VERTEX_STRIDE_4BYTE * 3), ref tris);
            for (int j = 0; j < 3; j++)
            {
                if (vertDict.TryGetValue(tri[j].id, out int vertIndex))
                {
                    chunk.triangles.Add(vertIndex);
                }
                else
                {
                    vertDict.Add(tri[j].id, vertCount);
                    chunk.triangles.Add(vertCount);
                    chunk.vertices.Add(tri[j].pos);
                    chunk.normals.Add(tri[j].norm);
                    chunk.colorMap.Add(new Color(tri[j].material, 0, 0));
                    vertCount++;
                }
            }
        }

        ReleaseGeometry(geoHandle);
        callback(chunk);
    }

    void CreateDispArg(ComputeBuffer argumentBuffer, ComputeBuffer triCount, int address)
    {
        this.settings.meshDrawArgsCreator.SetBuffer(0, "triCount", triCount);
        this.settings.meshDrawArgsCreator.SetInt("argOffset", address);

        this.settings.meshDrawArgsCreator.SetBuffer(0, "_IndirectArgsBuffer", argumentBuffer);

        this.settings.meshDrawArgsCreator.Dispatch(0, 1, 1, 1);
    }

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

    Material InstantiateMaterial(ComputeBuffer storage, ComputeBuffer addresses, int addressIndex, int matIndex)
    {
        Material instance = Instantiate(this.settings.indirectTerrainMats[matIndex]);

        instance.EnableKeyword("INDIRECT");
        instance.SetBuffer("_StorageMemory", storage);
        instance.SetBuffer("_AddressDict", addresses);
        instance.SetInt("addressIndex", addressIndex);
        
        instance.SetInt("_Vertex4ByteStride", MESH_VERTEX_STRIDE_4BYTE);
        instance.SetMatrix("_LocalToWorld", this.transform.localToWorldMatrix);

        return instance;
    }

    void TranscribeGeometry(ComputeBuffer memoryBuffer, ComputeBuffer addressBufer, ComputeBuffer source, ComputeBuffer count, int addressIndex, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer args = SetArgs(this.settings.baseGeoTranscriber, source, ref bufferHandle);

        this.settings.baseGeoTranscriber.SetBuffer(0, "_BaseTriangles", source);
        this.settings.baseGeoTranscriber.SetBuffer(0, "triLength", count);

        this.settings.baseGeoTranscriber.SetBuffer(0, "_MemoryBuffer", memoryBuffer);
        this.settings.baseGeoTranscriber.SetBuffer(0, "_AddressDict", addressBufer);
        this.settings.baseGeoTranscriber.SetInt("addressIndex", addressIndex);

        this.settings.baseGeoTranscriber.DispatchIndirect(0, args);
    }

    ComputeBuffer CopyCount(ComputeBuffer data, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer count = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
        bufferHandle.Enqueue(count);

        count.SetData(new uint[] { 0 });
        ComputeBuffer.CopyCount(data, count, 0);

        return count;
    }

    ComputeBuffer SetArgs(ComputeShader shader, ComputeBuffer data, ref Queue<ComputeBuffer> bufferHandle)
    {
        shader.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        return SetArgs(data, (int)threadGroupSize, ref bufferHandle);
    }

    ComputeBuffer SetArgs(ComputeBuffer data, int threadGroupSize, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer args = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.IndirectArguments);
        bufferHandle.Enqueue(args);

        args.SetData(new int[] { 1, 1, 1 });
        ComputeBuffer.CopyCount(data, args, 0);

        this.settings.indirectThreads.SetBuffer(0, "args", args);
        this.settings.indirectThreads.SetInt("numThreads", threadGroupSize);

        this.settings.indirectThreads.Dispatch(0, 1, 1, 1);

        return args;
    }

    public struct TriangleConst
    {
#pragma warning disable 649
        public Point a;
        public Point b;
        public Point c;

        public TriangleConst(int triAddress, ref uint[] data)
        {
            this.a = new Point(triAddress + 0 * MESH_VERTEX_STRIDE_4BYTE, ref data);
            this.b = new Point(triAddress + 1 * MESH_VERTEX_STRIDE_4BYTE, ref data);
            this.c = new Point(triAddress + 2 * MESH_VERTEX_STRIDE_4BYTE, ref data);
        }

        public struct Point
        {
            public Vector3 pos;
            public Vector3 norm;
            public int2 id;
            public int material;

            public Point(int address, ref uint[] data)
            {
                this.pos.x = BitConverter.ToSingle(BitConverter.GetBytes(data[address]), 0);
                this.pos.y = BitConverter.ToSingle(BitConverter.GetBytes(data[address + 1]), 0);
                this.pos.z = BitConverter.ToSingle(BitConverter.GetBytes(data[address + 2]), 0);

                this.norm.x = BitConverter.ToSingle(BitConverter.GetBytes(data[address + 3]), 0);
                this.norm.y = BitConverter.ToSingle(BitConverter.GetBytes(data[address + 4]), 0);
                this.norm.z = BitConverter.ToSingle(BitConverter.GetBytes(data[address + 5]), 0);

                this.id.x = BitConverter.ToInt32(BitConverter.GetBytes(data[address + 6]), 0);
                this.id.y = BitConverter.ToInt32(BitConverter.GetBytes(data[address + 7]), 0);

                this.material = BitConverter.ToInt32(BitConverter.GetBytes(data[address + 8]), 0);
            }
        }

        public Point this[int i] //courtesy of sebastian laugue, this is pretty smart
        {
            get
            {
                switch (i)
                {
                    case 0:
                        return a;
                    case 1:
                        return b;
                    default:
                        return c;
                }
            }
        }
    }

    class GeometryHandle
    {
        public Material matInstance;
        public uint addressIndex = 0;
        public uint argsAddress = 0;
        public bool isOnHeap = false; 

        public GeometryHandle(Material matInstance, uint addressIndex, uint argsAddress, bool isOnHeap)
        {
            this.addressIndex = addressIndex;
            this.matInstance = matInstance;
            this.argsAddress = argsAddress;
            this.isOnHeap = isOnHeap;
        }
    }
}
