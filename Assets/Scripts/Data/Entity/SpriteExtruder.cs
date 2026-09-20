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
using Arterra.Data.Entity.Behavior;

public static class SpriteExtruder{
    public static ComputeShader ImageExtruder;
    public static ComputeShader triangleTranscriber;
    public static ComputeShader vertexTranscriber;
    private static ExtruderOffsets offsets;

    private const int VERTEX_STRIDE_WORD = 3 + 2;
    private const int TRI_STRIDE_WORD = 3;


    public static void PresetData(){
        ImageExtruder = Resources.Load<ComputeShader>("Compute/CGeometry/Extruder/SpriteExtruder");
        triangleTranscriber = Resources.Load<ComputeShader>("Compute/CGeometry/Extruder/TranscribeTriangles");
        vertexTranscriber = Resources.Load<ComputeShader>("Compute/CGeometry/Extruder/TranscribeVertices");

        int2 maxSampleSize = new (0);
        if(((BehaviorEntity.AnimalSetting)Config.CURRENT.Generation.Entities.Retrieve("EntityItem").Setting).Is(out EntityItemDisplaySettings settings))
            maxSampleSize = new (settings.SpriteSampleSize);

        offsets = new ExtruderOffsets(maxSampleSize, 0, VERTEX_STRIDE_WORD, TRI_STRIDE_WORD);

        int kernel = ImageExtruder.FindKernel("March");
        ImageExtruder.SetBuffer(kernel, "counter", UtilityBuffers.GenerationBuffer);
        ImageExtruder.SetBuffer(kernel, "triangles", UtilityBuffers.GenerationBuffer);
        ImageExtruder.SetBuffer(kernel, "vertexes", UtilityBuffers.GenerationBuffer);
        ImageExtruder.SetBuffer(kernel, "triangleDict", UtilityBuffers.GenerationBuffer);
        ImageExtruder.SetInts("counterInd", new int[2]{offsets.vertexCounter, offsets.triangleCounter});
        ImageExtruder.SetInt("bSTART_dict", offsets.dictStart);
        ImageExtruder.SetInt("bSTART_verts", offsets.vertexStart);
        ImageExtruder.SetInt("bSTART_tris", offsets.triangleStart);

        kernel = triangleTranscriber.FindKernel("Transcribe");
        triangleTranscriber.SetBuffer(kernel, "triDict", UtilityBuffers.GenerationBuffer);
        triangleTranscriber.SetBuffer(kernel, "BaseTriangles", UtilityBuffers.GenerationBuffer);
        triangleTranscriber.SetBuffer(kernel, "counter", UtilityBuffers.GenerationBuffer);
        triangleTranscriber.SetInt("bCOUNT_Tri", offsets.triangleCounter);
        triangleTranscriber.SetInt("bSTART_Tri", offsets.triangleStart);
        triangleTranscriber.SetInt("bSTART_Dict", offsets.dictStart);
        triangleTranscriber.SetBuffer(kernel, "_AddressDict", GenerationPreset.memoryHandle.Address);
        
        kernel = vertexTranscriber.FindKernel("Transcribe");
        vertexTranscriber.SetBuffer(kernel, "baseVertices", UtilityBuffers.GenerationBuffer);
        vertexTranscriber.SetBuffer(kernel, "counter", UtilityBuffers.GenerationBuffer);
        vertexTranscriber.SetInt("bCOUNTER", offsets.vertexCounter);
        vertexTranscriber.SetInt("bSTART", offsets.vertexStart);
        vertexTranscriber.SetBuffer(kernel, "_AddressDict", GenerationPreset.memoryHandle.Address);
    }

    public static void Extrude(ExtrudeSettings settings, Action<ReadbackTask<SVert>.SharedMeshInfo> OnMeshRecieved){
        GenerateMesh(settings);

        uint vertAddress = GenerationPreset.memoryHandle.AllocateMemory(UtilityBuffers.GenerationBuffer, VERTEX_STRIDE_WORD, offsets.vertexCounter);
        uint triAddress = GenerationPreset.memoryHandle.AllocateMemory(UtilityBuffers.GenerationBuffer, TRI_STRIDE_WORD, offsets.triangleCounter);
        TranscribeVertices((int)vertAddress, offsets.vertexCounter);
        TranscribeTriangles((int)triAddress, offsets.triangleCounter);
        BeginMeshReadback(vertAddress, triAddress, OnMeshRecieved);
    }

    public static void BeginMeshReadback(uint vertAddress, uint triAddress, Action<ReadbackTask<SVert>.SharedMeshInfo> OnMeshRecieved){
        void ReleaseMemory(){
            GenerationPreset.memoryHandle.ReleaseMemory(vertAddress);
            GenerationPreset.memoryHandle.ReleaseMemory(triAddress);
        }

        ReadbackTask<SVert> RBTask = new ReadbackTask<SVert>((ReadbackTask<SVert>.SharedMeshInfo ret) => {
            ReleaseMemory();
            OnMeshRecieved(ret);
        }, 1);
        RBTask.AddTask(); RBTask.AddTask();
        //await until we obtain a fixed long term storage location
        GenerationPreset.memoryHandle.RegisterRebind(
            vertAddress,
            _ => ReadbackVertices(vertAddress, RBTask)
        );
        GenerationPreset.memoryHandle.RegisterRebind(
            triAddress,
            _ => ReadbackTriangles(triAddress, RBTask)
        );
    }

    static void ReadbackVertices(uint address, ReadbackTask<SVert> RBTask){
        if (!GenerationPreset.memoryHandle.GetDirectAllocation(address, VERTEX_STRIDE_WORD,
            out ComputeBuffer source, out _, out _, out int start, out int count))
            return;
        RBTask.RBMesh.VertexBuffer = new NativeArray<SVert>(count, Allocator.Persistent);
        AsyncGPUReadback.RequestIntoNativeArray(ref RBTask.RBMesh.VertexBuffer,
            source, size: 4 * count * VERTEX_STRIDE_WORD,
            offset: 4 * start * VERTEX_STRIDE_WORD,
            _ => RBTask.OnRBRecieved());
    }

    static void ReadbackTriangles(uint address, ReadbackTask<SVert> RBTask){
        if (!GenerationPreset.memoryHandle.GetDirectAllocation(address, TRI_STRIDE_WORD,
            out ComputeBuffer source, out _, out _, out int start, out int count))
            return;
        RBTask.RBMesh.IndexBuffer[0] = new NativeArray<uint>(
            count * TRI_STRIDE_WORD, Allocator.Persistent);
        AsyncGPUReadback.RequestIntoNativeArray(ref RBTask.RBMesh.IndexBuffer[0],
            source, size: 4 * count * TRI_STRIDE_WORD,
            offset: 4 * start * TRI_STRIDE_WORD,
            _ => RBTask.OnRBRecieved());
    }

    public static void GenerateMesh(ExtrudeSettings settings){
        UtilityBuffers.ClearRange(UtilityBuffers.GenerationBuffer, 2, 0);
        ImageExtruder.SetInts("SampleSize", new int[]{settings.SampleSize.x, settings.SampleSize.y});
        ImageExtruder.SetFloat("AlphaClip", settings.AlphaClip);
        ImageExtruder.SetFloat("ExtrudeHeight", settings.ExtrudeHeight);

        ImageExtruder.SetInt("textureInd", settings.ImageIndex);
        uint2 threadGroupSize;
        int kernel = ImageExtruder.FindKernel("March");
        ImageExtruder.GetKernelThreadGroupSizes(kernel, out threadGroupSize.x, out threadGroupSize.y, out uint _);
        threadGroupSize.x = (uint)Mathf.CeilToInt(settings.SampleSize.x / (float)threadGroupSize.x);
        threadGroupSize.y = (uint)Mathf.CeilToInt(settings.SampleSize.y / (float)threadGroupSize.y);
        ImageExtruder.Dispatch(kernel, (int)threadGroupSize.x, (int)threadGroupSize.y, 1);
    }

    public static void TranscribeVertices(int address, int vertCounter){
        if (!GenerationPreset.memoryHandle.GetBlockBufferSafe(address, out ComputeBuffer vertexBuffer))
            return;
        ComputeBuffer args = UtilityBuffers.CountToArgs(vertexTranscriber, UtilityBuffers.GenerationBuffer, countOffset: vertCounter);
        int kernel = vertexTranscriber.FindKernel("Transcribe");
        vertexTranscriber.SetInt("addressIndex", address);
        vertexTranscriber.SetBuffer(kernel, "_MemoryBuffer", vertexBuffer);

        vertexTranscriber.DispatchIndirect(kernel, args);
    }

    public static void TranscribeTriangles(int address, int triCounter){
        if (!GenerationPreset.memoryHandle.GetBlockBufferSafe(address, out ComputeBuffer triBuffer))
            return;
        ComputeBuffer args = UtilityBuffers.CountToArgs(triangleTranscriber, UtilityBuffers.GenerationBuffer, countOffset: triCounter);

        int kernel = triangleTranscriber.FindKernel("Transcribe");
        triangleTranscriber.SetBuffer(kernel, "_MemoryBuffer", triBuffer);
        triangleTranscriber.SetInt("triAddress", address);
        
        triangleTranscriber.DispatchIndirect(kernel, args);
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
