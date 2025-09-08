using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using WorldConfig;

public static class UtilityBuffers 
{
    public static ComputeBuffer indirectArgs;
    public static ComputeBuffer appendCount;

    public static ComputeShader indirectCopy;
    public static ComputeShader indirectCountToArgs;
    public static ComputeShader clearRange;
    public static ComputeShader prefixCountToArgs;
    
    const int _MaxArgsCount = (int)5E4;
    public static GraphicsBuffer ArgumentBuffer;
    public static uint2[] addressLL;

    const int ARGS_STRIDE_4BYTES = 4;

    //First 16 words reserved for metadata, apply padding as per data member
    public static ComputeBuffer GenerationBuffer;
    public static ComputeBuffer TransferBuffer;
    const int GEN_BYTE_SIZE = 200000000; //200MB

    public static bool active = false;


    public static uint AllocateArgs(){
        
        uint addressIndex = addressLL[0].x;

        uint pAddress = addressLL[addressIndex].x;
        uint nAddress = addressLL[addressIndex].y == 0 ? addressLL[0].x+1 : addressLL[addressIndex].y;
        addressLL[0].x = nAddress;

        addressLL[pAddress].y = nAddress;
        addressLL[nAddress].x = pAddress;

        return addressIndex;
    }

    public static void ReleaseArgs(uint addressIndex){
        if(addressIndex == 0) return;

        uint nAddress = addressLL[0].x;
        uint pAddress = addressLL[nAddress].x;
        addressLL[pAddress].y = addressIndex;
        addressLL[nAddress].x = addressIndex;
        addressLL[addressIndex] = new uint2(pAddress, nAddress);

        addressLL[0].x = addressIndex;
    }
    
    public static void Initialize(){
        if(active) return;
        active = true;

        indirectArgs = new ComputeBuffer(3, sizeof(int), ComputeBufferType.IndirectArguments);
        appendCount = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
        int mapChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
        int maxPoints = (mapChunkSize+1) * (mapChunkSize+1) * (mapChunkSize+1);

        ArgumentBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, _MaxArgsCount+1, sizeof(uint) * ARGS_STRIDE_4BYTES);
        addressLL = new uint2[_MaxArgsCount+1];
        addressLL[0].x = 1;

        //This buffer will contain all temporary data during generation
        GenerationBuffer = new ComputeBuffer(GEN_BYTE_SIZE/4, 4, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
        //This buffer will be slower but will be written to a lot by CPU
        TransferBuffer = new ComputeBuffer(maxPoints, 4, ComputeBufferType.Structured, ComputeBufferMode.Dynamic);

        indirectCopy = Resources.Load<ComputeShader>("Compute/Utility/Copy");
        indirectCountToArgs = Resources.Load<ComputeShader>("Compute/Utility/CountToArgs");
        clearRange = Resources.Load<ComputeShader>("Compute/Utility/ClearCounters");
        prefixCountToArgs = Resources.Load<ComputeShader>("Compute/Utility/PrefixArgsCreator");
    }

    public static void Release(){
        indirectArgs?.Release();
        appendCount?.Release();
        ArgumentBuffer?.Release();
        GenerationBuffer?.Release();
        TransferBuffer?.Release();
        active = false;
    }

    public static void ClearRange(ComputeBuffer buffer, int length, int start){
        clearRange.SetBuffer(0, "counters", buffer);
        clearRange.SetInt("length", length);
        clearRange.SetInt("start", start);
        clearRange.Dispatch(0, 1, 1, 1);
    }

    public static ComputeBuffer CopyCount(ComputeBuffer source, ComputeBuffer dest = null, int readOffset = 0, int writeOffset = 0) {
        ComputeBuffer count;
        
        if(dest != null){
            count = dest;
        }else count = appendCount;
        
        int kernel = indirectCopy.FindKernel("CopyCount");
        indirectCopy.SetBuffer(kernel, "source", source);
        indirectCopy.SetInt("readOffset", readOffset);
        indirectCopy.SetBuffer(kernel, "destination", count);
        indirectCopy.SetInt("writeOffset", writeOffset);

        indirectCopy.Dispatch(kernel, 1, 1, 1);

        return count;
    }

    public static bool CopyBuffer(ComputeBuffer source, ComputeBuffer dest, int readOffset = 0, int writeOffset = 0, int count = 0) {
        if (count <= 0) return false;
        if (source == null || dest == null) return false;
        if (source.count < readOffset + count) return false;
        if (dest.count < writeOffset + count) return false;

        int kernel = indirectCopy.FindKernel("CopyBuffer");
        indirectCopy.SetBuffer(kernel, "source", source);
        indirectCopy.SetInt("readOffset", readOffset);
        indirectCopy.SetBuffer(kernel, "destination", dest);
        indirectCopy.SetInt("writeOffset", writeOffset);
        indirectCopy.SetInt("count", count);

        indirectCopy.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        int threadGroups = Mathf.CeilToInt(count / (float)threadGroupSize);
        indirectCopy.Dispatch(kernel, threadGroups, 1, 1);
        return true;
    }

    public static ComputeBuffer CountToArgs(ComputeShader shader, ComputeBuffer count, int countOffset = 0, int kernel = 0) {
        shader.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        return CountToArgs((int)threadGroupSize, count, countOffset);
    }

    public static ComputeBuffer CountToArgs(int threadGroupSize, ComputeBuffer count, int countOffset = 0) {
        ComputeBuffer args = indirectArgs;
        
        indirectCountToArgs.SetBuffer(0, "count", count);
        indirectCountToArgs.SetBuffer(0, "args", args);
        indirectCountToArgs.SetInt("numThreads", (int)threadGroupSize);
        indirectCountToArgs.SetInt("countOffset", countOffset);

        indirectCountToArgs.Dispatch(0, 1, 1, 1);

        return args;
    }

    public static ComputeBuffer PrefixCountToArgs(ComputeShader shader, ComputeBuffer count, int countOffset = 0, Queue<ComputeBuffer> bufferQueue = null) {
        shader.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        return PrefixCountToArgs((int)threadGroupSize, count, countOffset, bufferQueue);
    }
    public static ComputeBuffer PrefixCountToArgs(int threadGroupSize, ComputeBuffer count, int countOffset = 0, Queue<ComputeBuffer> bufferQueue = null) {
        ComputeBuffer args = indirectArgs;
        if(bufferQueue != null){
            args = new ComputeBuffer(3, sizeof(int), ComputeBufferType.IndirectArguments);
            bufferQueue.Enqueue(args);
        }
        
        prefixCountToArgs.SetBuffer(0, "count", count);
        prefixCountToArgs.SetBuffer(0, "args", args);
        prefixCountToArgs.SetInt("numThreads", (int)threadGroupSize);
        prefixCountToArgs.SetInt("countOffset", countOffset);

        prefixCountToArgs.Dispatch(0, 1, 1, 1);

        return args;
    }


    public static void SetSampleData(ComputeShader noiseGen, Vector3 offset, int meshSkipInc){
        noiseGen.SetFloats("sOffset", new float[]{offset.x, offset.y, offset.z});
        noiseGen.SetInt("skipInc", meshSkipInc);
    }
}

public interface BufferOffsets{
    public int bufferEnd{get;}
    public int bufferStart{get;}
}

