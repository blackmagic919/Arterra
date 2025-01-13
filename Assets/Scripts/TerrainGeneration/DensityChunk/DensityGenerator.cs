using Unity.Mathematics;
using UnityEngine;
using static UtilityBuffers;
using TerrainGeneration;
using WorldConfig;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using WorldConfig.Generation.Biome;

public static class DensityGenerator
{
    [Header("Terrain Generation Shaders")]
    static ComputeShader baseGenCompute;//
    static ComputeShader biomeGenCompute;//
    static ComputeShader mapCompressor;//
    static ComputeShader dMeshGenerator;//
    static ComputeShader transVoxelGenerator;//
    static ComputeShader meshInfoCollector;
    
    /// <summary> The offsets within the <see cref="UtilityBuffers.GenerationBuffer"> working buffer </see> of different 
    /// logical regions used for different tasks during the terrain generation process. See <see cref="GeoGenOffsets"/>
    /// for more information. </summary>
    public static GeoGenOffsets bufferOffsets;
    

    static DensityGenerator(){ //That's a lot of Compute Shaders XD
        baseGenCompute = Resources.Load<ComputeShader>("Compute/TerrainGeneration/BaseGeneration/ChunkDataGen");
        biomeGenCompute = Resources.Load<ComputeShader>("Compute/TerrainGeneration/BaseGeneration/FullBiomeSampler");
        mapCompressor = Resources.Load<ComputeShader>("Compute/TerrainGeneration/BaseGeneration/MapCompressor");
        dMeshGenerator = Resources.Load<ComputeShader>("Compute/TerrainGeneration/BaseGeneration/CMarchingCubes");
        meshInfoCollector = Resources.Load<ComputeShader>("Compute/TerrainGeneration/BaseGeneration/BaseMapCollector");
        transVoxelGenerator = Resources.Load<ComputeShader>("Compute/TerrainGeneration/BaseGeneration/MarchTransitionCells");

        indirectCountToArgs = Resources.Load<ComputeShader>("Compute/Utility/CountToArgs");
    }

    public static void PresetData(){
        WorldConfig.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain;
        WorldConfig.Generation.Map mesh = Config.CURRENT.Generation.Terrain.value;

        //Set Marching Cubes Data
        int numPointsAxes = rSettings.mapChunkSize;
        bufferOffsets = new GeoGenOffsets(new int3(numPointsAxes, numPointsAxes, numPointsAxes), rSettings.Balance, 0);
        
        baseGenCompute.SetBuffer(0, "_SurfMemoryBuffer", GenerationPreset.memoryHandle.Storage);
        baseGenCompute.SetBuffer(0, "_SurfAddressDict", GenerationPreset.memoryHandle.Address);
        baseGenCompute.SetInt("caveFreqSampler", mesh.CaveFrequencyIndex);
        baseGenCompute.SetInt("caveSizeSampler", mesh.CaveSizeIndex);
        baseGenCompute.SetInt("caveShapeSampler", mesh.CaveShapeIndex);
        baseGenCompute.SetInt("coarseCaveSampler", mesh.CoarseTerrainIndex);
        baseGenCompute.SetInt("fineCaveSampler", mesh.FineTerrainIndex);
        baseGenCompute.SetInt("coarseMatSampler", mesh.CoarseMaterialIndex);
        baseGenCompute.SetInt("fineMatSampler", mesh.FineMaterialIndex);

        baseGenCompute.SetFloat("heightSFalloff", mesh.heightFalloff);
        baseGenCompute.SetFloat("atmoStrength", mesh.atmosphereFalloff);
        baseGenCompute.SetFloat("waterHeight", mesh.waterHeight);
        baseGenCompute.SetInt("waterMat", mesh.WaterIndex);

        baseGenCompute.SetBuffer(0, "BiomeMap", UtilityBuffers.GenerationBuffer);
        baseGenCompute.SetBuffer(0, "BaseMap", UtilityBuffers.GenerationBuffer);
        baseGenCompute.SetInt("bSTART_map", bufferOffsets.rawMapStart);
        baseGenCompute.SetInt("bSTART_biome", bufferOffsets.biomeMapStart);

        biomeGenCompute.SetBuffer(0, "_SurfMemoryBuffer", GenerationPreset.memoryHandle.Storage);
        biomeGenCompute.SetBuffer(0, "_SurfAddressDict", GenerationPreset.memoryHandle.Address);
        biomeGenCompute.SetInt("caveSizeSampler", mesh.CaveSizeIndex);
        biomeGenCompute.SetInt("caveShapeSampler", mesh.CaveShapeIndex);
        biomeGenCompute.SetInt("caveFreqSampler", mesh.CaveFrequencyIndex);

        biomeGenCompute.SetBuffer(0, "BiomeMap", UtilityBuffers.GenerationBuffer);
        biomeGenCompute.SetInt("bSTART_biome", bufferOffsets.biomeMapStart);

        mapCompressor.SetBuffer(0, "rawData", UtilityBuffers.GenerationBuffer);
        mapCompressor.SetBuffer(0, "chunkData", UtilityBuffers.GenerationBuffer);
        mapCompressor.SetInt("bSTART_raw", bufferOffsets.rawMapStart);
        mapCompressor.SetInt("bSTART_chunk", bufferOffsets.mapStart);
        
        //They're all the same buffer lol
        dMeshGenerator.SetBuffer(0, "MapData", UtilityBuffers.GenerationBuffer);
        dMeshGenerator.SetBuffer(0, "vertexes", UtilityBuffers.GenerationBuffer);
        dMeshGenerator.SetBuffer(0, "triangles", UtilityBuffers.GenerationBuffer);
        dMeshGenerator.SetBuffer(0, "triangleDict", UtilityBuffers.GenerationBuffer);
        dMeshGenerator.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        dMeshGenerator.SetInts("counterInd", new int[3]{bufferOffsets.vertexCounter, bufferOffsets.baseTriCounter, bufferOffsets.waterTriCounter});
        dMeshGenerator.SetInt("meshSkipInc", 1); //we are only dealing with same size chunks in this model

        dMeshGenerator.SetInt("bSTART_map", bufferOffsets.mapStart);
        dMeshGenerator.SetInt("bSTART_dict", bufferOffsets.dictStart);
        dMeshGenerator.SetInt("bSTART_verts", bufferOffsets.vertStart);
        dMeshGenerator.SetInt("bSTART_baseT", bufferOffsets.baseTriStart);
        dMeshGenerator.SetInt("bSTART_waterT", bufferOffsets.waterTriStart);

        transVoxelGenerator.SetBuffer(0, "MapData", UtilityBuffers.GenerationBuffer);
        transVoxelGenerator.SetBuffer(0, "vertexes", UtilityBuffers.GenerationBuffer);
        transVoxelGenerator.SetBuffer(0, "triangles", UtilityBuffers.GenerationBuffer);
        transVoxelGenerator.SetBuffer(0, "triangleDict", UtilityBuffers.GenerationBuffer);
        transVoxelGenerator.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        transVoxelGenerator.SetBuffer(0, "FaceProperty", UtilityBuffers.TransferBuffer);
        transVoxelGenerator.SetInts("counterInd", new int[3]{bufferOffsets.vertexCounter, bufferOffsets.baseTriCounter, bufferOffsets.waterTriCounter});

        transVoxelGenerator.SetInt("bSTART_map", bufferOffsets.mapStart);
        transVoxelGenerator.SetInt("bSTART_dict", bufferOffsets.dictStart);
        transVoxelGenerator.SetInt("bSTART_verts", bufferOffsets.vertStart);
        transVoxelGenerator.SetInt("bSTART_baseT", bufferOffsets.baseTriStart);
        transVoxelGenerator.SetInt("bSTART_waterT", bufferOffsets.waterTriStart);


        int kernel = meshInfoCollector.FindKernel("CollectReal");
        meshInfoCollector.SetBuffer(kernel, "MapData", UtilityBuffers.GenerationBuffer);
        meshInfoCollector.SetBuffer(kernel, "_MemoryBuffer", GPUDensityManager.Storage);
        meshInfoCollector.SetBuffer(kernel, "_AddressDict", GPUDensityManager.Address);
        kernel = meshInfoCollector.FindKernel("CollectVisual");
        meshInfoCollector.SetBuffer(kernel, "MapData", UtilityBuffers.GenerationBuffer);
        meshInfoCollector.SetBuffer(kernel, "_MemoryBuffer", GPUDensityManager.Storage);
        meshInfoCollector.SetBuffer(kernel, "_AddressDict", GPUDensityManager.Address);
        meshInfoCollector.SetBuffer(kernel, "_DirectAddress", GPUDensityManager.DirectAddress);
        meshInfoCollector.SetInt("bSTART_map", bufferOffsets.mapStart);

        //TransitionVoxel.Initialize();//TEMPORARY
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
       
    public static void GenerateBaseData( Vector3 offset, uint surfaceData, int numPointsPerAxis, int mapSkip, float IsoLevel)
    {
        baseGenCompute.SetFloat("IsoLevel", IsoLevel);
        baseGenCompute.SetInt("surfAddress", (int)surfaceData);
        baseGenCompute.SetInt("numPointsPerAxis", numPointsPerAxis);
        
        SetSampleData(baseGenCompute, offset, mapSkip);

        baseGenCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(numPointsPerAxis / (float)threadGroupSize);
        baseGenCompute.Dispatch(0, numThreadsAxis, numThreadsAxis, numThreadsAxis);
    }

    public static void GenerateBiomeData(Vector3 offset, uint surfaceData, int numPointsPerAxis, int mapSkip){
        biomeGenCompute.SetInt("numPointsPerAxis", numPointsPerAxis);
        biomeGenCompute.SetInt("surfAddress", (int)surfaceData);
        SetSampleData(biomeGenCompute, offset, mapSkip);

        biomeGenCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(numPointsPerAxis / (float)threadGroupSize);
        biomeGenCompute.Dispatch(0, numThreadsAxis, numThreadsAxis, numThreadsAxis);
    }

    public static void CompressMapData(int chunkSize){
        int numPointsAxes = chunkSize;
        int numPoints = numPointsAxes * numPointsAxes * numPointsAxes;

        mapCompressor.SetInt("numPoints", numPoints);
        mapCompressor.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(numPoints / (float)threadGroupSize);
        mapCompressor.Dispatch(0, numThreadsAxis, 1, 1);
    }

    public static void CollectRealMap(int3 CCoord, int chunkSize){
        int fChunkSize = chunkSize + 3;
        meshInfoCollector.SetInts("CCoord", new int[]{CCoord.x, CCoord.y, CCoord.z});
        meshInfoCollector.SetInt("numPointsPerAxis", fChunkSize);
        meshInfoCollector.SetInt("mapChunkSize", chunkSize);
        GPUDensityManager.SetCCoordHash(meshInfoCollector);

        int kernel = meshInfoCollector.FindKernel("CollectReal");
        meshInfoCollector.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(fChunkSize / (float)threadGroupSize);
        meshInfoCollector.Dispatch(kernel, numThreadsAxis, numThreadsAxis, numThreadsAxis);
    }

    public static void CollectVisualMap(int3 CCoord, int defaultAddress, int chunkSize, int depth){
        int fChunkSize = chunkSize + 3; int skipInc = 1 << depth;
        meshInfoCollector.SetInts("CCoord", new int[]{CCoord.x, CCoord.y, CCoord.z});
        meshInfoCollector.SetInt("numPointsPerAxis", fChunkSize);
        meshInfoCollector.SetInt("mapChunkSize", chunkSize);
        meshInfoCollector.SetInt("defAddress", defaultAddress);
        meshInfoCollector.SetInt("chunkSkip", skipInc);
        GPUDensityManager.SetCCoordHash(meshInfoCollector);

        int kernel = meshInfoCollector.FindKernel("CollectVisual");
        meshInfoCollector.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(fChunkSize / (float)threadGroupSize);
        meshInfoCollector.Dispatch(kernel, numThreadsAxis, numThreadsAxis, numThreadsAxis);
    }

    public static void GenerateMesh(int chunkSize, float IsoLevel)
    {
        int numCubesAxes = chunkSize;
        int numPointsAxes = numCubesAxes + 1;
        UtilityBuffers.ClearRange(UtilityBuffers.GenerationBuffer, 3, 0);

        dMeshGenerator.SetFloat("IsoLevel", IsoLevel);
        dMeshGenerator.SetInt("numCubesPerAxis", numCubesAxes);
        dMeshGenerator.SetInt("numPointsPerAxis", numPointsAxes);

        dMeshGenerator.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

        dMeshGenerator.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

    public static void GenerateTransition(uint neighborDepths, int chunkSize, float IsoLevel){
        int numCubesAxis = chunkSize;
        int numPointsAxis = numCubesAxis + 1;
        TransFaceInfo[] transFaces = GetNeighborFaces(neighborDepths, numPointsAxis);
        int numTransFaces = transFaces.Length; if(numTransFaces == 0) return;
        //TransitionVoxel.Execute(transFaces);
        UtilityBuffers.TransferBuffer.SetData(transFaces, 0, 0, numTransFaces);

        int kernel = transVoxelGenerator.FindKernel("MarchTransition");
        transVoxelGenerator.SetFloat("IsoLevel", IsoLevel);
        transVoxelGenerator.SetInt("numCubesPerAxis", numCubesAxis);
        transVoxelGenerator.SetInt("numPointsPerAxis", numPointsAxis);
        transVoxelGenerator.SetInt("numTransFaces", numTransFaces);
        transVoxelGenerator.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        //Only half the threads are used because each grid covers 2^2 faces
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxis / ((float)threadGroupSize * 2));
        transVoxelGenerator.Dispatch(kernel, numThreadsPerAxis, numThreadsPerAxis, numTransFaces);
    }

    private static TransFaceInfo[] GetNeighborFaces(uint neighborDepths, int numPointsAxes){
        int dictSizeBase = numPointsAxes * numPointsAxes * numPointsAxes * 3;
        int dictSizeFace = numPointsAxes * numPointsAxes * 2;

        float transWidth = Config.CURRENT.Quality.Terrain.value.transitionWidth;
        List<TransFaceInfo> transFaces = new List<TransFaceInfo>();
        for(int n = 0; n < 3; n++){
            uint nDepth = (neighborDepths >> (8 * n)) & 0x7F;
            bool isUpper = ((neighborDepths >> (8 * n)) & 0x80) != 0;
            for(int i = 0; i < nDepth; i++){
                TransFaceInfo faceInfo = new ();
                faceInfo.transWidth = transWidth / nDepth;
                faceInfo.transStart = (nDepth-i) * faceInfo.transWidth;
                faceInfo.dictStart = (uint)((transFaces.Count + n) * dictSizeFace + dictSizeBase);

                faceInfo.Align((uint)((isUpper ? 3 : 0) + n));
                faceInfo.SkipInc((uint)(1 << i));
                faceInfo.MergeFace(i == 0);
                faceInfo.IsEnd(i == nDepth - 1);
                transFaces.Add(faceInfo);
            }
        } return transFaces.ToArray();
    }


    public struct GeoGenOffsets : BufferOffsets{
        public int vertexCounter;
        public int baseTriCounter;
        public int waterTriCounter;
        public int mapStart;
        public int rawMapStart;
        public int biomeMapStart;
        public int dictStart;
        public int vertStart;
        public int baseTriStart;
        public int waterTriStart;
        private int offsetStart; private int offsetEnd;
        public int bufferStart{get{return offsetStart;}} public int bufferEnd{get{return offsetEnd;}}

        public const int VERTEX_STRIDE_WORD = 3 * 2 + 2;
        public const int TRI_STRIDE_WORD = 3;
        public const int RAW_MAP_WORD = 3;

        public GeoGenOffsets(int3 GridSize, int chunkBalance, int bufferStart, int VertexStride = VERTEX_STRIDE_WORD){
            this.offsetStart = bufferStart;
            vertexCounter = bufferStart; baseTriCounter = bufferStart + 1; waterTriCounter = bufferStart + 2;
            int numOfPoints = GridSize.x * GridSize.y * GridSize.z;
            int numOfPointsDict = (GridSize.x + 1) * (GridSize.y + 1) * (GridSize.z + 1);
            int numOfPointsOOB = (GridSize.x + 3) * (GridSize.y + 3) * (GridSize.z + 3);
            int numOfTris = (GridSize.x - 1) * (GridSize.y - 1) * (GridSize.z - 1) * 5;
            //Transition voxel dictionary
            numOfPointsDict += (GridSize.x + 1) * (GridSize.y + 1) * 3 * (chunkBalance + 1);
            
            //This is cached map, only used for visual chunks, real chunks
            //have their maps stored in the GPUDensityManager
            mapStart = bufferStart + 3;
            int mapEnd_W = mapStart + numOfPointsOOB;
            rawMapStart = Mathf.CeilToInt((float)mapEnd_W / RAW_MAP_WORD); 
            biomeMapStart = (rawMapStart + numOfPointsOOB) * RAW_MAP_WORD;

            dictStart = mapEnd_W;
            int dictEnd_W = dictStart + numOfPointsDict * TRI_STRIDE_WORD;

            vertStart = Mathf.CeilToInt((float)dictEnd_W / VertexStride);
            int vertexEnd_W = vertStart * VertexStride + (numOfPoints * 3) * VertexStride;

            baseTriStart = Mathf.CeilToInt((float)vertexEnd_W / TRI_STRIDE_WORD);
            int baseTriEnd_W = baseTriStart * TRI_STRIDE_WORD + numOfTris * TRI_STRIDE_WORD;

            waterTriStart = Mathf.CeilToInt((float)baseTriEnd_W / TRI_STRIDE_WORD);
            int waterTriEnd_W = waterTriStart * TRI_STRIDE_WORD + numOfTris * TRI_STRIDE_WORD;

            this.offsetEnd = waterTriEnd_W;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TransFaceInfo{
        public float transWidth;
        public float transStart;
        public uint dictStart;
        public uint data;

        public void Align(uint value){
            data = (data & 0xFFFFFF00) | (value & 0xFF);
        }
        public void SkipInc(uint value){
            data = (data & 0xFFFF00FF) | ((value & 0xFF) << 8);
        }
        public void IsEnd(bool value) {
            data = (data & 0x7FFFFFFF) | (value ? 0x80000000 : 0);
        }
        public void MergeFace(bool value){
            data = (data & 0xBFFFFFFF) | (value ? 0x40000000u : 0);
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



