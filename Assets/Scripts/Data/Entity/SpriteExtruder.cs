using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Arterra.Engine.Terrain;
using Arterra.Engine.Terrain.Readback;
using static Arterra.Engine.Terrain.Readback.IVertFormat;
using Arterra.Configuration;
using Arterra.Utils;
using Arterra.Core.Storage;
using Arterra.Data.Entity.Behavior;
using static Arterra.Core.Storage.SharedResourceManager;

public static class SpriteExtruder{
    public static ComputeShader ImageExtruder;
    public static ComputeShader triangleTranscriber;
    public static ComputeShader vertexTranscriber;
    private static ExtruderOffsets offsets;

    private const int VERTEX_STRIDE_WORD = 3 + 2;
    private const int TRI_STRIDE_WORD = 3;


    public static void PresetData(){
        GraphicsResourceContext gpuContext = GraphicsGeneration;
        ImageExtruder = Resources.Load<ComputeShader>("Compute/CGeometry/Extruder/SpriteExtruder");
        triangleTranscriber = Resources.Load<ComputeShader>("Compute/CGeometry/Extruder/TranscribeTriangles");
        vertexTranscriber = Resources.Load<ComputeShader>("Compute/CGeometry/Extruder/TranscribeVertices");

        int2 maxSampleSize = new (0);
        if(((BehaviorEntity.AnimalSetting)Config.CURRENT.Generation.Entities.Retrieve("EntityItem").Setting).Is(out EntityItemDisplaySettings settings))
            maxSampleSize = new (settings.SpriteSampleSize);

        offsets = new ExtruderOffsets(maxSampleSize, 0, VERTEX_STRIDE_WORD, TRI_STRIDE_WORD);

        int kernel = ImageExtruder.FindKernel("March");
        gpuContext.SetBuffer(ImageExtruder, kernel, "counter", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(ImageExtruder, kernel, "triangles", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(ImageExtruder, kernel, "vertexes", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(ImageExtruder, kernel, "triangleDict", gpuContext.Work.Scratch);
        gpuContext.SetInts(ImageExtruder, "counterInd", new int[2]{offsets.vertexCounter, offsets.triangleCounter});
        gpuContext.SetInt(ImageExtruder, "bSTART_dict", offsets.dictStart);
        gpuContext.SetInt(ImageExtruder, "bSTART_verts", offsets.vertexStart);
        gpuContext.SetInt(ImageExtruder, "bSTART_tris", offsets.triangleStart);

        kernel = triangleTranscriber.FindKernel("Transcribe");
        gpuContext.SetBuffer(triangleTranscriber, kernel, "triDict", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(triangleTranscriber, kernel, "BaseTriangles", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(triangleTranscriber, kernel, "counter", gpuContext.Work.Scratch);
        gpuContext.SetInt(triangleTranscriber, "bCOUNT_Tri", offsets.triangleCounter);
        gpuContext.SetInt(triangleTranscriber, "bSTART_Tri", offsets.triangleStart);
        gpuContext.SetInt(triangleTranscriber, "bSTART_Dict", offsets.dictStart);
        gpuContext.SetBuffer(triangleTranscriber, kernel, "_AddressDict", gpuContext.Memory.Address);

        kernel = vertexTranscriber.FindKernel("Transcribe");
        gpuContext.SetBuffer(vertexTranscriber, kernel, "baseVertices", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(vertexTranscriber, kernel, "counter", gpuContext.Work.Scratch);
        gpuContext.SetInt(vertexTranscriber, "bCOUNTER", offsets.vertexCounter);
        gpuContext.SetInt(vertexTranscriber, "bSTART", offsets.vertexStart);
        gpuContext.SetBuffer(vertexTranscriber, kernel, "_AddressDict", gpuContext.Memory.Address);
    }

    public static void Extrude(ExtrudeSettings settings, Action<ReadbackTask<SVert>.SharedMeshInfo> OnMeshRecieved){
        GraphicsResourceContext gpuContext = GraphicsGeneration;
        GenerateMesh(settings);

        uint vertAddress = gpuContext.Memory.AllocateMemory(gpuContext.Work.Scratch, VERTEX_STRIDE_WORD, offsets.vertexCounter);
        uint triAddress = gpuContext.Memory.AllocateMemory(gpuContext.Work.Scratch, TRI_STRIDE_WORD, offsets.triangleCounter);
        TranscribeVertices((int)vertAddress, offsets.vertexCounter);
        TranscribeTriangles((int)triAddress, offsets.triangleCounter);
        BeginMeshReadback(vertAddress, triAddress, OnMeshRecieved);
    }

    public static void BeginMeshReadback(uint vertAddress, uint triAddress, Action<ReadbackTask<SVert>.SharedMeshInfo> OnMeshRecieved){
        GraphicsResourceContext gpuContext = GraphicsGeneration;
        void ReleaseMemory(){
            GraphicsResourceContext gpuContext = GraphicsGeneration;
            gpuContext.Memory.ReleaseMemory(vertAddress);
            gpuContext.Memory.ReleaseMemory(triAddress);
        }

        ReadbackTask<SVert> RBTask = new ReadbackTask<SVert>((ReadbackTask<SVert>.SharedMeshInfo ret) => {
            ReleaseMemory();
            OnMeshRecieved(ret);
        }, 1);
        RBTask.AddTask(); RBTask.AddTask();
        //await until we obtain a fixed long term storage location
        gpuContext.Memory.RegisterRebind(
            vertAddress,
            _ => ReadbackVertices(vertAddress, RBTask)
        );
        gpuContext.Memory.RegisterRebind(
            triAddress,
            _ => ReadbackTriangles(triAddress, RBTask)
        );
    }

    static void ReadbackVertices(uint address, ReadbackTask<SVert> RBTask){
        if (!GraphicsGeneration.Memory.GetDirectAllocation(address, VERTEX_STRIDE_WORD,
            out ComputeBuffer source, out _, out _, out int start, out int count))
            return;
        RBTask.RBMesh.VertexBuffer = new NativeArray<SVert>(count, Allocator.Persistent);
        GraphicsGeneration.RequestAsyncReadbackIntoNativeArray(ref RBTask.RBMesh.VertexBuffer,
            source, size: 4 * count * VERTEX_STRIDE_WORD,
            offset: 4 * start * VERTEX_STRIDE_WORD,
            _ => RBTask.OnRBRecieved());
    }

    static void ReadbackTriangles(uint address, ReadbackTask<SVert> RBTask){
        if (!GraphicsGeneration.Memory.GetDirectAllocation(address, TRI_STRIDE_WORD,
            out ComputeBuffer source, out _, out _, out int start, out int count))
            return;
        RBTask.RBMesh.IndexBuffer[0] = new NativeArray<uint>(
            count * TRI_STRIDE_WORD, Allocator.Persistent);
        GraphicsGeneration.RequestAsyncReadbackIntoNativeArray(ref RBTask.RBMesh.IndexBuffer[0],
            source, size: 4 * count * TRI_STRIDE_WORD,
            offset: 4 * start * TRI_STRIDE_WORD,
            _ => RBTask.OnRBRecieved());
    }

    public static void GenerateMesh(ExtrudeSettings settings){
        GraphicsResourceContext gpuContext = GraphicsGeneration;
        gpuContext.Work.ClearRange(gpuContext.Work.Scratch, 2, 0);
        gpuContext.SetInts(ImageExtruder, "SampleSize", new int[]{settings.SampleSize.x, settings.SampleSize.y});
        gpuContext.SetFloat(ImageExtruder, "AlphaClip", settings.AlphaClip);
        gpuContext.SetFloat(ImageExtruder, "ExtrudeHeight", settings.ExtrudeHeight);

        gpuContext.SetInt(ImageExtruder, "textureInd", settings.ImageIndex);
        uint2 threadGroupSize;
        int kernel = ImageExtruder.FindKernel("March");
        ImageExtruder.GetKernelThreadGroupSizes(kernel, out threadGroupSize.x, out threadGroupSize.y, out uint _);
        threadGroupSize.x = (uint)Mathf.CeilToInt(settings.SampleSize.x / (float)threadGroupSize.x);
        threadGroupSize.y = (uint)Mathf.CeilToInt(settings.SampleSize.y / (float)threadGroupSize.y);
        gpuContext.Dispatch(ImageExtruder, kernel, (int)threadGroupSize.x, (int)threadGroupSize.y, 1);
    }

    public static void TranscribeVertices(int address, int vertCounter){
        GraphicsResourceContext gpuContext = GraphicsGeneration;
        if (!gpuContext.Memory.GetBlockBufferSafe(address, out ComputeBuffer vertexBuffer))
            return;
        ComputeBuffer args = gpuContext.Args.CountToArgs(vertexTranscriber, gpuContext.Work.Scratch, countOffset: vertCounter);
        int kernel = vertexTranscriber.FindKernel("Transcribe");
        gpuContext.SetInt(vertexTranscriber, "addressIndex", address);
        gpuContext.SetBuffer(vertexTranscriber, kernel, "_MemoryBuffer", vertexBuffer);

        gpuContext.DispatchIndirect(vertexTranscriber, kernel, args);
    }

    public static void TranscribeTriangles(int address, int triCounter){
        GraphicsResourceContext gpuContext = GraphicsGeneration;
        if (!gpuContext.Memory.GetBlockBufferSafe(address, out ComputeBuffer triBuffer))
            return;
        ComputeBuffer args = gpuContext.Args.CountToArgs(triangleTranscriber, gpuContext.Work.Scratch, countOffset: triCounter);

        int kernel = triangleTranscriber.FindKernel("Transcribe");
        gpuContext.SetBuffer(triangleTranscriber, kernel, "_MemoryBuffer", triBuffer);
        gpuContext.SetInt(triangleTranscriber, "triAddress", address);

        gpuContext.DispatchIndirect(triangleTranscriber, kernel, args);
    }

    public struct ExtrudeSettings{
        public int ImageIndex;
        public int2 SampleSize;
        public float AlphaClip;
        public float ExtrudeHeight;
    }

    public struct ExtruderOffsets : BufferOffsets {
        public int vertexCounter;
        public int triangleCounter;
        public int vertexStart;
        public int dictStart;
        public int triangleStart;
        private int offsetStart; private int offsetEnd;
        public int bufferStart{get{return offsetStart;}} public int bufferEnd{get{return offsetEnd;}}
        public ExtruderOffsets(int2 MaxSampleSize, int bufferStart, int VertexStride, int TriangleStride){
            this.offsetStart = bufferStart;
            int numPoints = MaxSampleSize.x * MaxSampleSize.y;

            this.vertexCounter = bufferStart;
            this.triangleCounter = bufferStart + 1;

            this.dictStart = bufferStart + 2;
            int dictEnd_W = dictStart + numPoints * 6;

            this.vertexStart = Mathf.CeilToInt((float)dictEnd_W / VertexStride);
            //each grid square spawns at most 2 vertices, * 2 for bottom and top
            int vertexEnd_W = (vertexStart + numPoints * 4) * VertexStride;

            this.triangleStart = Mathf.CeilToInt((float)vertexEnd_W / TriangleStride);
            //each grid square has at most 4 trianlges, + 4 for bottom, +4 for sides
            int triangleEnd_W = (triangleStart + numPoints * 12) * TriangleStride;

            this.offsetEnd = triangleEnd_W;
        }

    }
}
