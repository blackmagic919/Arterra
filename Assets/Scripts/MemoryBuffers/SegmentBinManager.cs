using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static OctreeTerrain;

public class SegmentBinManager
{
    public ComputeShader ChunkLLConstructor;
    public ComputeShader SectionConstructor;
    public ComputeShader AllocateChunkShader;
    public ComputeShader DeAllocateChunkShader;

    //Managed with chunk-bins because chunks
    private ComputeBuffer _GPUSectionedMemory;
    private ComputeBuffer _AddressRet;
    BinSection[] BufferSections;

    const int ChunkTagStride4Bytes = 3; //Doubly linked list + Section address reference
    const int SectionTagStride4Bytes = 1; //Address of first open space in LL
    const int UpdateContingencyFactor = 2;

    /*
     * Code is much more intuitive if chunks don't need to care about releasing data
     * rather data is released when another subscribing chunk releases the pre-existing data at the hash position
     * However, this means that data may be held hostage in bins, while the number of total allocated
     * chunks is constant, depending on the order of allocation, some sections may overflow. Thus we 
     * fit double the maxChunks per update, in the worst case where all chunks of a certain bin are released last
    */

    struct BinSection
    {
        public int chunkStride4Bytes;
        public int maxChunks;
        public int startingAddress;

        public BinSection(int chunkStride4Bytes, int maxChunks, int startingAddress)
        {
            this.chunkStride4Bytes = chunkStride4Bytes;
            this.maxChunks = maxChunks;
            this.startingAddress = startingAddress;
        }
    }

    public void Release()
    {
        _GPUSectionedMemory?.Release();
        _AddressRet?.Release();
    }

    public SegmentBinManager(int mapChunkSize, List<LODInfo> detailLevels, int PointStride4Bytes = 1)
    {
        ChunkLLConstructor = Resources.Load<ComputeShader>("Compute/MemoryStructures/SegmentBin/ChunkLLConstructor");
        SectionConstructor = Resources.Load<ComputeShader>("Compute/MemoryStructures/SegmentBin/SectionConstructor");
        AllocateChunkShader = Resources.Load<ComputeShader>("Compute/MemoryStructures/SegmentBin/AllocateChunk");
        DeAllocateChunkShader = Resources.Load<ComputeShader>("Compute/MemoryStructures/SegmentBin/DeAllocateChunk");
        _AddressRet = new ComputeBuffer(2, sizeof(uint), ComputeBufferType.Structured);

        int numChunks = 0;
        int num4Bytes = 0;

        int numLoDs = detailLevels.Count;
        BufferSections = new BinSection[numLoDs];
        for (int i = 0; i < numLoDs; i++)
        {
            LODInfo detailLevel = detailLevels[i];

            int meshSkipInc = 1 << i;
            int numPointsAxes = mapChunkSize / meshSkipInc;
            int chunkStride4Bytes = numPointsAxes * numPointsAxes * numPointsAxes * PointStride4Bytes;

            int sideLength = 2 * detailLevel.chunkDistThresh;
            int pSideLength = i == 0 ? 0 : 2 * detailLevels[i - 1].chunkDistThresh;
            int maxChunks = UpdateContingencyFactor * ((sideLength * sideLength * sideLength) - (pSideLength * pSideLength * pSideLength));

            BufferSections[i] = new BinSection(chunkStride4Bytes, maxChunks, num4Bytes);

            num4Bytes += maxChunks * (chunkStride4Bytes + ChunkTagStride4Bytes) + SectionTagStride4Bytes;
            numChunks += maxChunks;
        }

        _GPUSectionedMemory = new ComputeBuffer(num4Bytes, sizeof(uint), ComputeBufferType.Structured); //currently 2.5 Gb

        for (int i = 0; i < numLoDs; i++)
        {
            CreateChunkLL(BufferSections[i]);
            ConstructSection(BufferSections[i]);
        }
        /*
        uint[] data = new uint[num4Bytes];
        _GPUSectionedMemory.GetData(data);*/
    }

    void CreateChunkLL(BinSection binInfo)
    {
        this.ChunkLLConstructor.SetBuffer(0, "_SectionedMemory", _GPUSectionedMemory);
        this.ChunkLLConstructor.SetInt("chunkStride4Bytes", binInfo.chunkStride4Bytes);
        this.ChunkLLConstructor.SetInt("numChunks", binInfo.maxChunks);
        this.ChunkLLConstructor.SetInt("sectionAddress", binInfo.startingAddress);

        this.ChunkLLConstructor.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(binInfo.maxChunks / (float)threadGroupSize);
        this.ChunkLLConstructor.Dispatch(0, numThreadsPerAxis, 1, 1);
    }

    void ConstructSection(BinSection binInfo)
    {
        this.SectionConstructor.SetBuffer(0, "_SectionedMemory", _GPUSectionedMemory);
        this.SectionConstructor.SetInt("chunkStride4Bytes", binInfo.chunkStride4Bytes);
        this.SectionConstructor.SetInt("numChunks", binInfo.maxChunks);
        this.SectionConstructor.SetInt("sectionAddress", binInfo.startingAddress);

        this.SectionConstructor.Dispatch(0, 1, 1, 1);
    }

    public ComputeBuffer AllocateChunk(int LOD)
    {
        BinSection binInfo = BufferSections[LOD];

        AllocateChunkShader.SetBuffer(0, "_SectionedMemory", _GPUSectionedMemory);
        AllocateChunkShader.SetInt("sectionAddress", binInfo.startingAddress);
        AllocateChunkShader.SetBuffer(0, "memoryAddress", _AddressRet);

        AllocateChunkShader.Dispatch(0, 1, 1, 1);

        return _AddressRet;
    }

    public void ReleaseChunk(ComputeBuffer address){
        DeAllocateChunkShader.SetBuffer(0, "_SectionedMemory", _GPUSectionedMemory);
        DeAllocateChunkShader.SetBuffer(0, "memoryAddress", address);

        DeAllocateChunkShader.Dispatch(0, 1, 1, 1);
    }

    public ComputeBuffer AccessStorage(){
        return _GPUSectionedMemory;
    }
}
