using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using static EditorMesh;

[CreateAssetMenu(menuName = "Settings/DensityManager")]
public class GPUDensityManager : UpdatableData
{
    public MemoryBufferSettings memorySpace;
    public ComputeShader dictReplaceKey;
    public ComputeShader transcribeMapInfo;

    private ComputeBuffer _ChunkAddressDict;
    private int numChunksAxis;
    private int mapChunkSize;
    private float lerpScale;
    private const int pointStride4Byte = 2; //Only density for now

    public Queue<ComputeBuffer> tempBuffers = new Queue<ComputeBuffer>();
    [HideInInspector]
    public bool initialized = false;

    public void InitializeManage(int chunksInViewableDistance, int mapChunkSize, float lerpScale)
    {
        OnDisable();

        this.lerpScale = lerpScale;

        this.mapChunkSize = mapChunkSize;
        this.numChunksAxis = 2 * (chunksInViewableDistance + 1);
        int numChunks = numChunksAxis * numChunksAxis * numChunksAxis;

        this._ChunkAddressDict = new ComputeBuffer(numChunks, sizeof(uint) * 2, ComputeBufferType.Structured);
        this._ChunkAddressDict.SetData(Enumerable.Repeat(0u, numChunks * 2).ToArray());
        initialized = true;
    }

    private void OnDisable()
    {
        _ChunkAddressDict?.Release();
        initialized = false;
    }

    public void SubscribeChunk(ComputeBuffer densityMap, ComputeBuffer materialMap, Vector3 CCoord, int LOD)
    {
        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxis = mapChunkSize / meshSkipInc + 1;
        int numPoints = numPointsAxis * numPointsAxis * numPointsAxis;
        uint chunkBytes = (uint)(pointStride4Byte * numPoints);

        ComputeBuffer chunkSize = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
        chunkSize.SetData(new uint[1] { chunkBytes });

        ComputeBuffer address = this.memorySpace.AllocateMemory(chunkSize);
        ComputeBuffer oAddress = ReplaceAddress(address, CCoord, meshSkipInc);
        this.memorySpace.ReleaseMemory(oAddress);

        TranscribeData(this.memorySpace.AccessStorage(), densityMap, materialMap, address, numPoints);

        chunkSize.Release();
        address.Release();
        oAddress.Release();

        uint2[] data = new uint2[numChunksAxis* numChunksAxis * numChunksAxis];
        _ChunkAddressDict.GetData(data);
        }

    void TranscribeData(ComputeBuffer memory, ComputeBuffer density, ComputeBuffer material, ComputeBuffer address, int numPoints)
    {
        this.transcribeMapInfo.SetBuffer(0, "_MemoryBuffer", memory);
        this.transcribeMapInfo.SetBuffer(0, "densityMap", density);
        this.transcribeMapInfo.SetBuffer(0, "materialMap", material);
        this.transcribeMapInfo.SetBuffer(0, "startAddress", address); 
        this.transcribeMapInfo.SetInt("numPoints", numPoints);

        this.transcribeMapInfo.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(numPoints / (float)threadGroupSize);
        this.transcribeMapInfo.Dispatch(0, numThreadsAxis, 1, 1);
    }

    ComputeBuffer ReplaceAddress(ComputeBuffer memoryAddress, Vector3 CCoord, int meshSkipInc)
    {
        ComputeBuffer oldAddress = new ComputeBuffer(1, sizeof(uint)*2, ComputeBufferType.Structured);

        this.dictReplaceKey.SetBuffer(0, "chunkAddressDict", _ChunkAddressDict);
        this.dictReplaceKey.SetBuffer(0, "oAddress", oldAddress);
        this.dictReplaceKey.SetBuffer(0, "nAddress", memoryAddress);
        this.dictReplaceKey.SetInts("CCoord", new int[3] { (int)CCoord.x, (int)CCoord.y, (int)CCoord.z });
        this.dictReplaceKey.SetInt("meshSkipInc", meshSkipInc);

        SetCCoordHash(this.dictReplaceKey);

        this.dictReplaceKey.Dispatch(0, 1, 1, 1);

        return oldAddress;
    }


    public void SetDensitySampleData(ComputeShader shader) {
        this.SetCCoordHash(shader);
        this.SetWSCCoordHelper(shader);

        shader.SetBuffer(0, "_ChunkAddressDict", this._ChunkAddressDict);
        shader.SetBuffer(0, "_ChunkInfoBuffer", this.memorySpace.AccessStorage());
    }

    public void SetDensitySampleData(Material material){
        this.SetCCoordHash(material);
        this.SetWSCCoordHelper(material);

        material.SetBuffer("_ChunkAddressDict", this._ChunkAddressDict);
        material.SetBuffer("_ChunkInfoBuffer", this.memorySpace.AccessStorage());
    }

    void SetWSCCoordHelper(ComputeShader shader) { 
        shader.SetFloat("lerpScale", lerpScale);
        shader.SetInt("mapChunkSize", mapChunkSize);
    }

    void SetWSCCoordHelper(Material material) { 
        material.SetFloat("lerpScale", lerpScale);
        material.SetInt("mapChunkSize", mapChunkSize);
    }
    void SetCCoordHash(ComputeShader shader) { shader.SetInt("numChunksAxis", numChunksAxis); }
    void SetCCoordHash(Material material) { material.SetInt("numChunksAxis", numChunksAxis); }
    


}
