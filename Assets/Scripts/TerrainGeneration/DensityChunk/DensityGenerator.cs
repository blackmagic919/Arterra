using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static UtilityBuffers;

public static class DensityGenerator
{
    [Header("Terrain Generation Shaders")]
    static ComputeShader baseGenCompute;//
    static ComputeShader meshGenerator;//

    public const int VERTEX_STRIDE_WORD = 3 * 2 + 2;
    public const int TRI_STRIDE_WORD = 3;

    public static GeoGenOffsets bufferOffsets;
    

    static DensityGenerator(){ //That's a lot of Compute Shaders XD
        baseGenCompute = Resources.Load<ComputeShader>("TerrainGeneration/BaseGeneration/ChunkDataGen");
        meshGenerator = Resources.Load<ComputeShader>("TerrainGeneration/BaseGeneration/MarchingCubes");

        indirectCountToArgs = Resources.Load<ComputeShader>("Utility/CountToArgs");
    }

    public static void PresetData(){
        MeshCreatorSettings mesh = WorldStorageHandler.WORLD_OPTIONS.Generation.value.Terrain.value;
        baseGenCompute.SetInt("coarseCaveSampler", mesh.CoarseTerrainNoise);
        baseGenCompute.SetInt("fineCaveSampler", mesh.FineTerrainNoise);
        baseGenCompute.SetInt("coarseMatSampler", mesh.CoarseMaterialNoise);
        baseGenCompute.SetInt("fineMatSampler", mesh.FineMaterialNoise);

        baseGenCompute.SetFloat("waterHeight", mesh.waterHeight);
        baseGenCompute.SetInt("waterMat", mesh.waterMat);

        baseGenCompute.SetBuffer(0, "BaseMap", UtilityBuffers.GenerationBuffer);

        //Set Marching Cubes Data
        int numPointsAxes = WorldStorageHandler.WORLD_OPTIONS.Quality.value.Rendering.value.mapChunkSize + 1;
        bufferOffsets = new GeoGenOffsets(new int3(numPointsAxes, numPointsAxes, numPointsAxes), 0, VERTEX_STRIDE_WORD, TRI_STRIDE_WORD);

        //They're all the same buffer lol
        meshGenerator.SetBuffer(0, "vertexes", UtilityBuffers.GenerationBuffer);
        meshGenerator.SetBuffer(0, "triangles", UtilityBuffers.GenerationBuffer);
        meshGenerator.SetBuffer(0, "triangleDict", UtilityBuffers.GenerationBuffer);
        meshGenerator.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        meshGenerator.SetInts("counterInd", new int[3]{bufferOffsets.vertexCounter, bufferOffsets.baseTriCounter, bufferOffsets.waterTriCounter});

        meshGenerator.SetInt("bSTART_dict", bufferOffsets.dictStart);
        meshGenerator.SetInt("bSTART_verts", bufferOffsets.vertStart);    
        meshGenerator.SetInt("bSTART_baseT", bufferOffsets.baseTriStart);
        meshGenerator.SetInt("bSTART_waterT", bufferOffsets.waterTriStart);
    }

    //TODO: Rewrite using BeginWrite() for parallel writing
    /*
    public static void SimplifyMap(int chunkSize, int meshSkipInc, ref NativeArray<TerrainChunk.MapData> chunkData)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numPoints = numPointsAxes * numPointsAxes * numPointsAxes;
        int totalPointsAxes = chunkSize + 1;
        int totalPoints = totalPointsAxes * totalPointsAxes * totalPointsAxes;
        
        NativeArray<TerrainChunk.MapData> writeDest = UtilityBuffers.GenerationBuffer.BeginWrite<TerrainChunk.MapData>(0, numPoints);

        WriteToGPU SimplifyJob = new WriteToGPU
        {
            source = chunkData,
            dest = writeDest,
            sourceMapSize = totalPointsAxes,
            destMapSize = numPointsAxes,
            meshSkipInc = meshSkipInc
        };
        
        SimplifyJob.Schedule(numPoints, 64).Complete();
        UtilityBuffers.GenerationBuffer.EndWrite<TerrainChunk.MapData>(numPoints);
    }*/
       
    public static void GenerateBaseData( uint surfaceData, float IsoLevel, int chunkSize, int meshSkipInc, Vector3 offset)
    {
        int numPointsAxes = chunkSize / meshSkipInc;

        baseGenCompute.SetFloat("IsoLevel", IsoLevel);
        baseGenCompute.SetBuffer(0, "_SurfMemoryBuffer", GenerationPreset.memoryHandle.Storage);
        baseGenCompute.SetBuffer(0, "_SurfAddressDict", GenerationPreset.memoryHandle.Address);
        baseGenCompute.SetInt("surfAddress", (int)surfaceData);
        baseGenCompute.SetInt("numPointsPerAxis", numPointsAxes);
        baseGenCompute.SetInt("meshSkipInc", meshSkipInc);

        baseGenCompute.SetFloat("chunkSize", chunkSize);
        baseGenCompute.SetFloat("offsetY", offset.y);
        
        SetSampleData(baseGenCompute, offset, chunkSize, meshSkipInc);

        baseGenCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);
        baseGenCompute.Dispatch(0, numThreadsAxis, numThreadsAxis, numThreadsAxis);
    }

    public static void GenerateMesh(int3 CCoord, int chunkSize, int meshSkipInc, float IsoLevel)
    {
        int numCubesAxes = chunkSize / meshSkipInc;
        int numPointsAxes = numCubesAxes + 1;
        UtilityBuffers.ClearRange(UtilityBuffers.GenerationBuffer, 3, 0);

        meshGenerator.SetBuffer(0, "_MemoryBuffer", GPUDensityManager.Storage);
        meshGenerator.SetBuffer(0, "_AddressDict", GPUDensityManager.Address);
        meshGenerator.SetInts("CCoord", new int[] { CCoord.x, CCoord.y, CCoord.z });
        GPUDensityManager.SetCCoordHash(meshGenerator);

        meshGenerator.SetFloat("IsoLevel", IsoLevel);
        meshGenerator.SetInt("numCubesPerAxis", numCubesAxes);
        meshGenerator.SetInt("numPointsPerAxis", numPointsAxes);
        meshGenerator.SetInt("meshSkipInc", meshSkipInc);

        meshGenerator.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

        meshGenerator.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

    public struct GeoGenOffsets : BufferOffsets{
        public int vertexCounter;
        public int baseTriCounter;
        public int waterTriCounter;
        public int dictStart;
        public int vertStart;
        public int baseTriStart;
        public int waterTriStart;
        private int offsetStart; private int offsetEnd;
        public int bufferStart{get{return offsetStart;}} public int bufferEnd{get{return offsetEnd;}}

        public GeoGenOffsets(int3 GridSize, int bufferStart, int VertexStride, int TriStride){
            this.offsetStart = bufferStart;
            vertexCounter = bufferStart; baseTriCounter = bufferStart + 1; waterTriCounter = bufferStart + 2;
            dictStart = bufferStart + 3;

            int numOfPoints = GridSize.x * GridSize.y * GridSize.z;
            int numOfTris = (GridSize.x - 1) * (GridSize.y - 1) * (GridSize.z - 1) * 5;

            int dictEnd_W = dictStart + numOfPoints * TriStride;

            vertStart = Mathf.CeilToInt((float)dictEnd_W / VertexStride);
            int vertexEnd_W = vertStart * VertexStride + (numOfPoints * 3) * VertexStride;

            baseTriStart = Mathf.CeilToInt((float)vertexEnd_W / TriStride);
            int baseTriEnd_W = baseTriStart * TriStride + numOfTris * TriStride;

            waterTriStart = Mathf.CeilToInt((float)baseTriEnd_W / TriStride);
            int waterTriEnd_W = waterTriStart * TriStride + numOfTris * TriStride;

            this.offsetEnd = waterTriEnd_W;
        }
    }

    /*
    public static void SimplifyMaterials(int chunkSize, int meshSkipInc, int[] materials, ComputeBuffer pointBuffer, ref Queue<ComputeBuffer> bufferHandle)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int totalPointsAxes = chunkSize + 1;
        int totalPoints = totalPointsAxes * totalPointsAxes * totalPointsAxes;
        ComputeBuffer completeMaterial = new ComputeBuffer(totalPoints, sizeof(int), ComputeBufferType.Structured, ComputeBufferMode.Immutable);
        completeMaterial.SetData(materials);
        bufferHandle.Enqueue(completeMaterial);

        densitySimplification.EnableKeyword("USE_INT");
        densitySimplification.SetInt("meshSkipInc", meshSkipInc);
        densitySimplification.SetInt("totalPointsPerAxis", totalPointsAxes);
        densitySimplification.SetInt("pointsPerAxis", numPointsAxes);
        densitySimplification.SetBuffer(0, "points_full", completeMaterial);
        densitySimplification.SetBuffer(0, "points", pointBuffer);

        densitySimplification.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

        densitySimplification.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }*/

    /*
    public static ComputeBuffer GenerateTerrain(int chunkSize, int meshSkipInc, SurfaceChunk.SurfData surfaceData, int coarseCave, int fineCave, Vector3 offset, float IsoValue, ref Queue<ComputeBuffer> bufferHandle)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numPoints = numPointsAxes * numPointsAxes * numPointsAxes;
        
        ComputeBuffer densityMap = new ComputeBuffer(numPoints, sizeof(float), ComputeBufferType.Structured);
        bufferHandle.Enqueue(densityMap);

        terrainNoiseCompute.SetBuffer(0, "points", densityMap);
        terrainNoiseCompute.SetBuffer(0, "_SurfMemoryBuffer", surfaceData.Memory);
        terrainNoiseCompute.SetBuffer(0, "_SurfAddressDict", surfaceData.Addresses);
        terrainNoiseCompute.SetInt("surfAddress", (int)surfaceData.addressIndex);

        terrainNoiseCompute.SetInt("coarseSampler", coarseCave);
        terrainNoiseCompute.SetInt("fineSampler", fineCave);

        terrainNoiseCompute.SetInt("numPointsPerAxis", numPointsAxes);
        terrainNoiseCompute.SetFloat("meshSkipInc", meshSkipInc);
        terrainNoiseCompute.SetFloat("chunkSize", chunkSize);
        terrainNoiseCompute.SetFloat("offsetY", offset.y);
        terrainNoiseCompute.SetFloat("IsoLevel", IsoValue);
        SetSampleData(terrainNoiseCompute, offset, chunkSize, meshSkipInc);

        terrainNoiseCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

        terrainNoiseCompute.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);

        return densityMap;
    }

    public static ComputeBuffer GenerateNoiseMap(ComputeShader shader, Vector3 offset, int chunkSize, int meshSkipInc, ref Queue<ComputeBuffer> bufferHandle){
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numPoints = numPointsAxes * numPointsAxes * numPointsAxes;

        ComputeBuffer density = new ComputeBuffer(numPoints, sizeof(float), ComputeBufferType.Structured);
        bufferHandle.Enqueue(density);

        shader.SetBuffer(0, "points", density);
        shader.SetInt("numPointsPerAxis", numPointsAxes);
        SetSampleData(shader, offset, chunkSize, meshSkipInc);

        shader.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);
        shader.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
        return density;
    }

    public static ComputeBuffer GenerateNoiseMap(int chunkSize, int meshSkipInc, NoiseData noiseData, Vector3 offset, ref Queue<ComputeBuffer> bufferHandle)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numPoints = numPointsAxes * numPointsAxes * numPointsAxes;

        ComputeBuffer density = new ComputeBuffer(numPoints, sizeof(float), ComputeBufferType.Structured);
        bufferHandle.Enqueue(density);

        rawNoiseSampler.SetBuffer(0, "points", density);
        rawNoiseSampler.SetInt("numPointsPerAxis", numPointsAxes);
        SetNoiseData(rawNoiseSampler, chunkSize, meshSkipInc, noiseData, offset);

        rawNoiseSampler.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

        rawNoiseSampler.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
        return density;
    }*/

    /*
    public static ComputeBuffer GenerateCaveNoise(SurfaceChunk.SurfData surfaceData, Vector3 offset, int coarseSampler, int fineSampler, int chunkSize, int meshSkipInc, ref Queue<ComputeBuffer> bufferHandle){
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numPoints = numPointsAxes * numPointsAxes * numPointsAxes;

        ComputeBuffer caveDensity = new ComputeBuffer(numPoints, sizeof(float), ComputeBufferType.Structured);
        bufferHandle.Enqueue(caveDensity);
        
        baseCaveGenerator.SetBuffer(0, "_SurfMemoryBuffer", surfaceData.Memory);
        baseCaveGenerator.SetBuffer(0, "_SurfAddressDict", surfaceData.Addresses);
        baseCaveGenerator.SetInt("surfAddress", (int)surfaceData.addressIndex);

        baseCaveGenerator.SetInt("coarseSampler", coarseSampler);
        baseCaveGenerator.SetInt("fineSampler", fineSampler);
        baseCaveGenerator.SetInt("numPointsPerAxis", numPointsAxes);
        SetSampleData(baseCaveGenerator, offset, chunkSize, meshSkipInc);

        baseCaveGenerator.SetBuffer(0, "densityMap", caveDensity);

        baseCaveGenerator.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);
        baseCaveGenerator.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
        
        return caveDensity;
    }*/

    /*
    public ComputeBuffer GetAdjacentDensity(GPUDensityManager densityManager, Vector3 CCoord, int chunkSize, int meshSkipInc, ref Queue<ComputeBuffer> bufferHandle)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        ComputeBuffer neighborDensity = new ComputeBuffer(numPointsAxes * numPointsAxes * 6, sizeof(float), ComputeBufferType.Structured);
        bufferHandle.Enqueue(neighborDensity);

        neighborDensitySampler.SetBuffer(0, "_MemoryBuffer", densityManager.AccessStorage());
        neighborDensitySampler.SetBuffer(0, "_AddressDict", densityManager.AccessAddresses());
        densityManager.SetCCoordHash(neighborDensitySampler);

        neighborDensitySampler.SetInts("CCoord", new int[] { (int)CCoord.x, (int)CCoord.y, (int)CCoord.z });
        neighborDensitySampler.SetInt("numPointsPerAxis", numPointsAxes);
        neighborDensitySampler.SetInt("meshSkipInc", meshSkipInc);
        neighborDensitySampler.SetBuffer(0, "nDensity", neighborDensity);

        neighborDensitySampler.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

        neighborDensitySampler.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, 1);
        return neighborDensity;
    }*/
}



