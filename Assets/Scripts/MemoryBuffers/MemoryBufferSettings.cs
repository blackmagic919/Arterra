using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

[CreateAssetMenu(menuName = "Memory Heap")]
public class MemoryBufferSettings : ScriptableObject
{
    public int _BufferSize4Bytes = (int)5E7; //200 mB
    public int _MaxHeapSize = (int)5E3; //5k references
    public int _MaxAddressSize = (int)5E4; //50k addresses

    [SerializeField]
    private ComputeShader HeapSetupShader;
    [SerializeField]
    private ComputeShader AllocateShader;
    [SerializeField]
    private ComputeShader DeallocateShader;
    [SerializeField]
    private ComputeShader StoreAddressShader;

    private ComputeBuffer _GPUMemorySource;
    private ComputeBuffer _EmptyBlockHeap;
    private ComputeBuffer _AddressBuffer;
    private uint2[] addressLL;
    private uint freeAddressIndex;

    public void OnEnable()
    {
        _GPUMemorySource = new ComputeBuffer(_BufferSize4Bytes, sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.Immutable);
        //2 channels, 1 for size, 2 for memory address
        _EmptyBlockHeap = new ComputeBuffer(_MaxHeapSize, sizeof(uint) * 2, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
        _AddressBuffer = new ComputeBuffer(_MaxAddressSize, sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.Immutable);

        addressLL = new uint2[_MaxAddressSize];
        freeAddressIndex = 0;

        PrepareMemory();
    }

    public void OnDisable()
    {
        _GPUMemorySource?.Release();
        _EmptyBlockHeap?.Release();
        _AddressBuffer?.Release();
    }

    void PrepareMemory()
    {
        HeapSetupShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
        HeapSetupShader.SetBuffer(0, "_Heap", _EmptyBlockHeap);
        HeapSetupShader.SetInt("_BufferSize4Bytes", _BufferSize4Bytes);

        HeapSetupShader.Dispatch(0, 1, 1, 1);
    }

    //Returns compute buffer with memory address of empty space
    //NOTE: It is caller's responsibility to release memory address
    public ComputeBuffer AllocateMemory(ComputeBuffer byte4Size)
    {
        ComputeBuffer memoryAddress = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);

        AllocateShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
        AllocateShader.SetBuffer(0, "_Heap", _EmptyBlockHeap);
        AllocateShader.SetBuffer(0, "allocSizeBuffer", byte4Size);
        AllocateShader.SetBuffer(0, "startIndex", memoryAddress);

        AllocateShader.Dispatch(0, 1, 1, 1);

        return memoryAddress;
    }

    public uint StoreAddress(ComputeBuffer address){
        uint addressIndex = freeAddressIndex;
        //Copy Address
        this.StoreAddressShader.SetBuffer(0, "_AddressBuffer", _AddressBuffer);
        this.StoreAddressShader.SetBuffer(0, "address", address);
        this.StoreAddressShader.SetInt("addressIndex", (int)addressIndex);
        this.StoreAddressShader.Dispatch(0, 1, 1, 1);

        //Increment LinkedList
        uint pAddress = addressLL[freeAddressIndex].x;
        uint nAddress = addressLL[freeAddressIndex].y == 0 ? freeAddressIndex+1 : addressLL[freeAddressIndex].y;
        addressLL[pAddress].y = nAddress;
        addressLL[nAddress].x = pAddress;

        return addressIndex;
    }
    
    public void ReleaseAddress(uint addressIndex){
        //Decrement LinkedList
        uint pAddress = addressLL[freeAddressIndex].x;
        uint nAddress = freeAddressIndex;
        addressLL[pAddress].y = addressIndex;
        addressLL[nAddress].x = addressIndex;
        addressLL[addressIndex] = new uint2(pAddress, nAddress);
    }

    //Releases heap memory of block at memory address
    public void ReleaseMemory(ComputeBuffer address)
    {
        DeallocateShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
        DeallocateShader.SetBuffer(0, "_Heap", _EmptyBlockHeap);
        DeallocateShader.SetBuffer(0, "targetIndex", address);

        DeallocateShader.Dispatch(0, 1, 1, 1);

        return;
    }

    public ComputeBuffer AccessStorage()
    {
        return _GPUMemorySource;
    }

}
