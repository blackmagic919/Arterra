using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class UtilityBuffers : MonoBehaviour
{
    public static ComputeBuffer indirectArgs;
    public static ComputeBuffer appendCount;

    public static ComputeShader indirectThreads;
    public static ComputeShader indirectCountToArgs;
    
    const int _MaxArgsCount = (int)5E4;
    public static ComputeBuffer ArgumentBuffer;
    public static uint2[] addressLL;

    const int ARGS_STRIDE_4BYTES = 4;


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
    public void OnEnable()
    {
        indirectArgs = new ComputeBuffer(3, sizeof(int), ComputeBufferType.IndirectArguments);
        appendCount = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);

        ArgumentBuffer = new ComputeBuffer(_MaxArgsCount+1, sizeof(uint) * ARGS_STRIDE_4BYTES, ComputeBufferType.Structured);
        addressLL = new uint2[_MaxArgsCount+1];
        addressLL[0].x = 1;

        indirectThreads = (ComputeShader)Resources.Load("DivideByThreads");
        indirectCountToArgs = (ComputeShader)Resources.Load("CountToArgs");
    }

    public void OnDisable()
    {
        indirectArgs?.Release();
        appendCount?.Release();
        ArgumentBuffer?.Release();
    }

    public static ComputeBuffer CopyCount(ComputeBuffer data, Queue<ComputeBuffer> bufferQueue = null)
    {
        ComputeBuffer count = appendCount;
        if(bufferQueue != null){
            count = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
            bufferQueue.Enqueue(count);
        }

        count.SetData(new uint[] { 0u });
        ComputeBuffer.CopyCount(data, count, 0);

        return count;
    }

    public static ComputeBuffer CountToArgs(ComputeShader shader, ComputeBuffer count, Queue<ComputeBuffer> bufferQueue = null) {
        ComputeBuffer args = indirectArgs;
        if(bufferQueue != null){
            args = new ComputeBuffer(3, sizeof(int), ComputeBufferType.IndirectArguments);
            bufferQueue.Enqueue(args);
        }

        args.SetData(new int[] { 1, 1, 1 });
        shader.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        indirectCountToArgs.SetBuffer(0, "count", count);
        indirectCountToArgs.SetBuffer(0, "args", args);
        indirectCountToArgs.SetInt("numThreads", (int)threadGroupSize);

        indirectCountToArgs.Dispatch(0, 1, 1, 1);

        return args;
    }
    
    //Copying large buffers is inefficient if done alot, try to use CountToArgs 
    public static ComputeBuffer SetArgs(ComputeShader shader, ComputeBuffer data, Queue<ComputeBuffer> bufferQueue = null)
    {
        ComputeBuffer args = indirectArgs;
        if(bufferQueue != null){
            args = new ComputeBuffer(3, sizeof(int), ComputeBufferType.IndirectArguments);
            bufferQueue.Enqueue(args);
        }

        args.SetData(new int[] { 1, 1, 1 });
        ComputeBuffer.CopyCount(data, args, 0);
        shader.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        indirectThreads.SetBuffer(0, "args", args);
        indirectThreads.SetInt("numThreads", (int)threadGroupSize);

        indirectThreads.Dispatch(0, 1, 1, 1);

        return args;
    }
}
