using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

[CreateAssetMenu(menuName = "BufferMemory/Memory Heap")]
public class MemoryBufferSettings : ScriptableObject
{
    public int _BufferSize4Bytes = (int)5E7; //200 mB
    public int _MaxHeapSize = (int)5E3; //5k references
    public int _MaxAddressSize = (int)5E4; //50k addresses

    private ComputeShader HeapSetupShader;
    private ComputeShader AllocateShader;
    private ComputeShader DeallocateShader;

    private ComputeBuffer _GPUMemorySource;
    private ComputeBuffer _EmptyBlockHeap;
    private ComputeBuffer _AddressBuffer;
    private uint2[] addressLL;
    private bool initialized = false;

    public void OnEnable()
    {
        HeapSetupShader = Resources.Load<ComputeShader>("MemoryStructures/Heap/PrepareHeap");
        AllocateShader = Resources.Load<ComputeShader>("MemoryStructures/Heap/AllocateData");
        DeallocateShader = Resources.Load<ComputeShader>("MemoryStructures/Heap/DeallocateData");

        _GPUMemorySource = new ComputeBuffer(_BufferSize4Bytes, sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.Immutable);
        //2 channels, 1 for size, 2 for memory address
        _EmptyBlockHeap = new ComputeBuffer(_MaxHeapSize, sizeof(uint) * 2, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
        _AddressBuffer = new ComputeBuffer(_MaxAddressSize+1, sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.Immutable);

        addressLL = new uint2[_MaxAddressSize+1];
        addressLL[0].y = 1;

        initialized = true;
        PrepareMemory();
    }

    public void OnDisable()
    {
        _GPUMemorySource?.Release();
        _EmptyBlockHeap?.Release();
        _AddressBuffer?.Release();
        initialized = false;
    }

    void PrepareMemory()
    {
        HeapSetupShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
        HeapSetupShader.SetBuffer(0, "_Heap", _EmptyBlockHeap);
        HeapSetupShader.SetInt("_BufferSize4Bytes", _BufferSize4Bytes);

        HeapSetupShader.Dispatch(0, 1, 1, 1);
    }

    public ComputeBuffer AllocateMemoryDirect(ComputeBuffer byte4Size)
    {
        if(!initialized) return null;   
        ComputeBuffer address = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
        //Allocate Memory
        AllocateShader.EnableKeyword("DIRECT_ALLOCATE");
        AllocateShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
        AllocateShader.SetBuffer(0, "_Heap", _EmptyBlockHeap);
        AllocateShader.SetBuffer(0, "allocSizeBuffer", byte4Size);
        AllocateShader.SetBuffer(0, "_Address", address);

        AllocateShader.Dispatch(0, 1, 1, 1);

        return address;
    }

    public void ReleaseMemoryDirect(ComputeBuffer address)
    {   
        if(!initialized) return;
        //Allocate Memory
        DeallocateShader.EnableKeyword("DIRECT_DEALLOCATE");
        DeallocateShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
        DeallocateShader.SetBuffer(0, "_Heap", _EmptyBlockHeap);
        DeallocateShader.SetBuffer(0, "_Address", address);

        DeallocateShader.Dispatch(0, 1, 1, 1);
    }

    //Returns compute buffer with memory address of empty space
    //NOTE: It is caller's responsibility to release memory address
    public uint AllocateMemory(ComputeBuffer byte4Size)
    {
        if(!initialized) return 0;

        uint addressIndex = addressLL[0].y;
        
        //Allocate Memory
        AllocateShader.DisableKeyword("DIRECT_ALLOCATE");
        AllocateShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
        AllocateShader.SetBuffer(0, "_Heap", _EmptyBlockHeap);
        AllocateShader.SetBuffer(0, "allocSizeBuffer", byte4Size);
        AllocateShader.SetBuffer(0, "_AddressDict", _AddressBuffer);
        AllocateShader.SetInt("addressIndex", (int)addressIndex);

        AllocateShader.Dispatch(0, 1, 1, 1);

        //Head node always points to first free node, remove node from LL to mark allocated
        uint pAddress = addressLL[addressIndex].x;//should always be 0
        uint nAddress = addressLL[addressIndex].y == 0 ? addressIndex+1 : addressLL[addressIndex].y;

        addressLL[pAddress].y = nAddress;
        addressLL[nAddress].x = pAddress;

        return addressIndex;
    }

    //Releases heap memory of block at memory address
    public void ReleaseMemory(uint addressIndex)
    {//
        if(!initialized || addressIndex == 0) return;
        //Release Memory
        DeallocateShader.DisableKeyword("DIRECT_DEALLOCATE");
        DeallocateShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
        DeallocateShader.SetBuffer(0, "_Heap", _EmptyBlockHeap);
        DeallocateShader.SetBuffer(0, "_AddressDict", _AddressBuffer);
        DeallocateShader.SetInt("addressIndex", (int)addressIndex);

        DeallocateShader.Dispatch(0, 1, 1, 1);
        //Add node back to head of LL
        uint nAddress = addressLL[0].y;
        uint pAddress = addressLL[nAddress].x; //Is equivalent to 0(head node), but this is more clear
        addressLL[pAddress].y = addressIndex;
        addressLL[nAddress].x = addressIndex;
        addressLL[addressIndex] = new uint2(pAddress, nAddress);

        /*
        if(_BufferSize4Bytes == 500000000){
            uint[] heap = new uint[4];
            _EmptyBlockHeap.GetData(heap);
            Debug.Log(heap[3]/250000 + "MB");
        }*/
    }

    public ComputeBuffer AccessStorage()
    {
        return _GPUMemorySource;
    }

    public ComputeBuffer AccessAddresses()
    {
        return _AddressBuffer;
    }
}
