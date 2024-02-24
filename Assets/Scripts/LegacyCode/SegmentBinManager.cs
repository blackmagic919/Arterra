using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static EditorMesh;

public class SegmentBinManager : UpdatableData
{
    public ComputeShader ChunkLLConstructor;
    public ComputeShader SectionConstructor;
    public ComputeShader AllocateChunkShader;
    public ComputeShader DeAllocateChunkShader;

    //Managed with chunk-bins because chunks
    private ComputeBuffer _ChunkAddressDict;
    private ComputeBuffer _GPUSectionedMemory;
    BinSection[] BufferSections;

    const int ChunkTagStride4Bytes = 2; //Doubly linked list
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

    public void OnDisable()
    {
        _ChunkAddressDict?.Release();
        _GPUSectionedMemory?.Release();
    }

    public void InitGPUManagement(int mapChunkSize, LODInfo[] detailLevels, int PointStride4Bytes = 1)
    {
        int numChunks = 0;
        int num4Bytes = 0;

        int numLoDs = detailLevels.Length;
        BufferSections = new BinSection[numLoDs];
        for (int i = 0; i < numLoDs; i++)
        {
            LODInfo detailLevel = detailLevels[i];

            int meshSkipInc = meshSkipTable[detailLevel.LOD];
            int numPointsAxes = mapChunkSize / meshSkipInc + 1;
            int chunkStride4Bytes = numPointsAxes * numPointsAxes * numPointsAxes * PointStride4Bytes;

            int sideLength = 2 * Mathf.CeilToInt(detailLevel.distanceThresh / mapChunkSize);
            int pSideLength = i == 0 ? 0 : 2 * Mathf.CeilToInt(detailLevels[i - 1].distanceThresh / mapChunkSize);
            int maxChunks = UpdateContingencyFactor * ((sideLength * sideLength * sideLength) - (pSideLength * pSideLength * pSideLength));

            BufferSections[i] = new BinSection(chunkStride4Bytes, maxChunks, num4Bytes);

            num4Bytes += maxChunks * (chunkStride4Bytes + ChunkTagStride4Bytes) + SectionTagStride4Bytes;
            numChunks += maxChunks;
        }

        _GPUSectionedMemory = new ComputeBuffer(num4Bytes, sizeof(uint), ComputeBufferType.Structured);
        _ChunkAddressDict = new ComputeBuffer(numChunks, sizeof(uint), ComputeBufferType.Structured);
        _ChunkAddressDict.SetData(Enumerable.Repeat(0, numChunks).ToArray());

        for (int i = 0; i < numLoDs; i++)
        {
            CreateChunkLL(BufferSections[i]);
            ConstructSection(BufferSections[i]);
        }
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
}
