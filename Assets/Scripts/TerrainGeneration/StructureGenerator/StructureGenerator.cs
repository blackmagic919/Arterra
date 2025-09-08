using UnityEngine;
using static UtilityBuffers;
using Unity.Mathematics;
using WorldConfig;
using TerrainGeneration;

namespace TerrainGeneration.Structure{
/// <summary>
/// A manager unique for every terrain chunk responsible for creating and holding onto
/// intermediate structure information required by the chunk during the terrain
/// generation process.
/// </summary>
public class Creator
{
    /// <summary>
    /// The address of the generated structure information for this chunk. The location within 
    /// <see cref="GenerationPreset.MemoryHandle.Address"/> of the address within <see cref="GenerationPreset.MemoryHandle.Storage"/> 
    /// of the beginning of the structure information for this chunk.
    /// </summary>
    public uint StructureDataIndex;
    const int STRUCTURE_STRIDE_WORD = 3 + 2 + 1;

    /// <summary>  Releases any intermediate structure information sheld by this instance. Call this to ensure
    /// that no memory is being held by a chunk being disposed. See <seealso cref="StructureDataIndex"/>. </summary>
    public void ReleaseStructure()
    {
        if(StructureDataIndex == 0) return;
        GenerationPreset.memoryHandle.ReleaseMemory(this.StructureDataIndex);
        StructureDataIndex = 0;
    }
    
    private int[] calculateLoDPoints(int maxLoD, int maxStructurePoints, float falloffFactor)
    {
        int[] points = new int[maxLoD + 2]; //suffix sum
        for(int LoD = maxLoD; LoD >= 0; LoD--)
        {
            points[LoD] = Mathf.CeilToInt(maxStructurePoints * Mathf.Pow(falloffFactor, -LoD)) + points[LoD+1];
        }
        return points;
    }
    private int calculateMaxStructurePoints(int maxLoD, int maxStructurePoints, float falloffFactor)
    {
        int totalPoints = 0;
        int processedChunks = 0;
        int maxDist = maxLoD + 2;
        int[] pointsPerLoD = calculateLoDPoints(maxLoD, maxStructurePoints, falloffFactor);

        for (int dist = 1; dist <= maxDist; dist++)
        {
            int numChunks = dist * dist * dist - processedChunks;
            int LoD = Mathf.Max(0, dist - 2);
            int maxPointsPerChunk = pointsPerLoD[LoD];

            totalPoints += maxPointsPerChunk * numChunks;
            processedChunks += numChunks;
        }
        return totalPoints;
    }

    /// <summary> Finds all structures that intersect with the current chunk's boundaries. Planned structures are represented
    /// through their <i>origin</i>, <i>structure index</i>, and <i>rotation</i> relative to the chunk's origin. 
    /// Planning structures encompasses the first two steps of structure generation, <see href="https://blackmagic919.github.io/AboutMe/2024/06/08/Structure%20Planning/">
    /// planning </see> and <see href="https://blackmagic919.github.io/AboutMe/2024/06/16/Structure-Pruning/">>pruning</see>, and is 
    /// necessary to ensure structures across chunk boundaries are recognized and generated correctly. </summary>
    /// <remarks>Structures are sampled in a deterministic manner meaning that the same structures 
    /// will be generated in the same location in the same world regardless of any other factors. </remarks>
    /// <param name="chunkCoord">The coordinate, in <see cref="TerrainChunk.CCoord"/>Chunk Space, of the chunk whose structures
    /// are planned. If the chunk spans multiple <paramref name="chunkCoord">ChunkCoords</paramref>, this is the coordinate of the origin of
    /// the region. </param>
    /// <param name="offset">The offset in grid space of the origin of the chunk. This should be 
    /// equvalent to (<paramref name="chunkCoord"/> * <paramref name="chunkSize"/>)</param>
    /// <param name="chunkSize">The size of the chunk a <see cref="TerrainChunk.RealChunk">real chunk</see> in grid space. The atomic
    /// unit for guaranteed determinstic sampling of chunk structures. </param>
    /// <param name="IsoLevel">The density of the surface of the terrain. See <see cref="WorldConfig.Quality.Terrain.IsoLevel"/> for more info. </param>
    /// <param name="depth"> The distance of the chunk from a leaf node within the <see cref="OctreeTerrain.Octree">chunk octree</see>. Identifies
    /// the size of the chunk relative to a <see cref="TerrainChunk.RealChunk"> real chunk </see>. See <see cref="TerrainChunk.depth"/> for more info. </param>
    public void PlanStructuresGPU(int3 chunkCoord, float3 offset, int chunkSize, float IsoLevel, int depth=0)
    {
        ReleaseStructure();
        UtilityBuffers.ClearRange(UtilityBuffers.GenerationBuffer, 4, 0);
        Generator.SampleStructureLoD(Config.CURRENT.Generation.Structures.value.maxLoD, chunkSize, depth, chunkCoord);
        Generator.IdentifyStructures(offset, IsoLevel);

        uint addressIndex = TerrainGeneration.GenerationPreset.memoryHandle.AllocateMemory(UtilityBuffers.GenerationBuffer,
            STRUCTURE_STRIDE_WORD, Generator.offsets.prunedCounter);
        this.StructureDataIndex = Generator.TranscribeStructures(GenerationPreset.memoryHandle.GetBlockBuffer(addressIndex),
            GenerationPreset.memoryHandle.Address, addressIndex);

        return;
    }

    /// <summary> Generates the planned structures for the current chunk. This involves actually transcribing the <see cref="WorldConfig.Generation.Structure.StructureData.map">
    /// map information </see> of each structure onto the chunk's map in <see cref="GenerationBuffer">working memory</see> that will be used to create the visual and
    /// interactable features of the chunk. This must be called after the chunk's base map has been populated through
    /// <see cref="Map.Generator.GenerateBaseData(Vector3, uint, int, int, float)"/>. </summary>
    /// <remarks>The time complexity of this operation is O(n) with respect to the size of the largest structure within the chunk.</remarks>
    /// <param name="chunkSize">The size of the chunk a <see cref="TerrainChunk.RealChunk">real chunk</see> in grid space.</param>
    /// <param name="skipInc">The distance in grid space between two adjacent samples in the chunk's terrain map. Used to convert
    /// a structure's coordinate from grid space to map space(the location within the chunk's terrain map). </param>
    /// <param name="mapStart">The start of the chunk's terrain map within the <see cref="UtilityBuffers.GenerationBuffer"/>. See <see cref="Map.Generator.GeoGenOffsets.rawMapStart"/>
    /// for more info. </param>
    /// <param name="IsoLevel">The density of the surface of the terrain. See <see cref="WorldConfig.Quality.Terrain.IsoLevel"/> for more info.</param>
    /// <param name="wChunkSize">The axis size of the chunk's terrain map as it currently is in <see cref="GenerationBuffer"> working memory </see>.</param>
    /// <param name="wOffset">The offset within the chunk's map generated structures will be transcribed to. If the chunk's map
    /// extends beyond what it exclusively contains, this should be used to indicate the axis offest relative to the chunk map's first
    /// entry of the first entry exclusively contained by the chunk. </param>
    public void GenerateStrucutresGPU(int chunkSize, int skipInc, int mapStart, float IsoLevel, int wChunkSize = -1, int wOffset = 0)
    {
        if(wChunkSize == -1) wChunkSize = chunkSize;
        ComputeBuffer blockSource = GenerationPreset.memoryHandle.GetBlockBuffer(StructureDataIndex);
        ComputeBuffer structCount = Generator.GetStructCount(blockSource, GenerationPreset.memoryHandle.Address, (int)StructureDataIndex, STRUCTURE_STRIDE_WORD);
        Generator.ApplyStructures(blockSource, GenerationPreset.memoryHandle.Address, structCount, 
                        (int)StructureDataIndex, mapStart, chunkSize, skipInc, wOffset, wChunkSize, IsoLevel);

        return;
    }

    /*
    public ComputeBuffer AnalyzeCaveTerrain(ComputeBuffer points, ComputeBuffer count, Vector3 offset, int chunkSize, int maxPoints){
        ComputeBuffer coarseDetail = AnalyzeNoiseMapGPU(points, count, meshSettings.CoarseTerrainNoise, offset, 1, chunkSize, maxPoints, false, true, false, tempBuffers);
        ComputeBuffer fineDetail = AnalyzeNoiseMapGPU(points, count, meshSettings.CoarseTerrainNoise, offset, 1, chunkSize, maxPoints, false, true, false, tempBuffers);

        return null;        
    }*/

}

/// <summary> A static manager responsible for managing loading and access
/// of all compute-shaders used within the structure generation process
/// of terrain generation. All instructions related to structure
/// generation done by the GPU is streamlined from this module.  </summary>
public static class Generator 
{
    static ComputeShader StructureLoDSampler;//
    static ComputeShader StructureIdentifier;//
    static ComputeShader structureChunkGenerator;//
    static ComputeShader structureDataTranscriber;//
    static ComputeShader structureSizeCounter;//

    const int STRUCTURE_STRIDE_WORD = 3 + 2 + 1;
    const int SAMPLE_STRIDE_WORD = 3 + 1;
    const int CHECK_STRIDE_WORD = 2;

    /// <summary> The offsets within the <see cref="UtilityBuffers.GenerationBuffer"> working buffer </see> of different 
    /// logical regions used for different tasks during the terrain generation process. See <see cref="StructureOffsets"/>
    /// for more information. </summary>
    public static StructureOffsets offsets;
    
    static Generator(){
        StructureLoDSampler = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Structures/StructureLODSampler");
        StructureIdentifier = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Structures/StructureIdentifier");
        structureChunkGenerator = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Structures/StructureChunkGenerator");
        structureDataTranscriber = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Structures/TranscribeStructPoints");
        structureSizeCounter = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Structures/StructureSizeCounter");
    }

    static int[] calculateLoDPoints(int maxLoD, int maxStructurePoints, float falloffFactor)
    {
        int[] points = new int[maxLoD + 2]; //suffix sum
        for(int LoD = maxLoD; LoD >= 0; LoD--)
        {
            points[LoD] = Mathf.CeilToInt(maxStructurePoints * Mathf.Pow(falloffFactor, -LoD)) + points[LoD+1];
        }
        return points;
    }

    static int calculateMaxStructurePoints(int maxLoD, int maxDepthL, int maxStructurePoints, float falloffFactor)
    {
        int totalPoints = 0;
        int processedChunks = 0;
        int baseDist = (1<<maxDepthL) + 1;
        int maxDist = maxLoD + baseDist;
        int[] pointsPerLoD = calculateLoDPoints(maxLoD, maxStructurePoints, falloffFactor);

        for (int dist = baseDist; dist <= maxDist; dist++)
        {
            int numChunks = dist * dist * dist - processedChunks;
            int LoD = Mathf.Max(0, dist - baseDist);
            int maxPointsPerChunk = pointsPerLoD[LoD];

            totalPoints += maxPointsPerChunk * numChunks;
            processedChunks += numChunks;
        }
        return totalPoints;
    }

    /// <summary>
    /// Presets all compute-shaders used in the structure generator by acquiring them and
    /// binding any constant values(information derived from the world's settings that 
    /// won't change until the world is unloaded) to them. Referenced by
    /// <see cref="TerrainGeneration.SystemProtocol.Startup"/> </summary>
    public static void PresetData()
    {
        WorldConfig.Generation.Map mesh = Config.CURRENT.Generation.Terrain.value;
        WorldConfig.Generation.Surface surface = Config.CURRENT.Generation.Surface.value;
        WorldConfig.Generation.Structure.Generation structures = Config.CURRENT.Generation.Structures.value;
        WorldConfig.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain.value;
        int maxStructurePoints = calculateMaxStructurePoints(structures.maxLoD, rSettings.MaxStructureDepth, structures.StructureChecksPerChunk, structures.LoDFalloff);
        offsets = new StructureOffsets(maxStructurePoints, 0);

        StructureLoDSampler.SetInt("maxLOD", structures.maxLoD);
        StructureLoDSampler.SetInt("numPoints0", structures.StructureChecksPerChunk);
        StructureLoDSampler.SetFloat("LoDFalloff", structures.LoDFalloff);
        StructureLoDSampler.SetBuffer(0, "structures", UtilityBuffers.GenerationBuffer);
        StructureLoDSampler.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        StructureLoDSampler.SetInt("bSTART", offsets.sampleStart);
        StructureLoDSampler.SetInt("bCOUNTER", offsets.sampleCounter);

        StructureIdentifier.SetInt("caveFreqSampler", mesh.CaveFrequencyIndex);
        StructureIdentifier.SetInt("caveSizeSampler", mesh.CaveSizeIndex);
        StructureIdentifier.SetInt("caveShapeSampler", mesh.CaveShapeIndex);
        StructureIdentifier.SetInt("caveCoarseSampler", mesh.CoarseTerrainIndex);
        StructureIdentifier.SetInt("caveFineSampler", mesh.FineTerrainIndex);

        StructureIdentifier.SetInt("continentalSampler", surface.ContinentalIndex);
        StructureIdentifier.SetInt("erosionSampler", surface.ErosionIndex);
        StructureIdentifier.SetInt("PVSampler", surface.PVIndex);
        StructureIdentifier.SetInt("squashSampler", surface.SquashIndex);
        StructureIdentifier.SetInt("InfHeightSampler", surface.InfHeightIndex);
        StructureIdentifier.SetInt("InfOffsetSampler", surface.InfOffsetIndex);
        StructureIdentifier.SetInt("atmosphereSampler", surface.AtmosphereIndex);

        StructureIdentifier.SetFloat("maxInfluenceHeight", surface.MaxInfluenceHeight);
        StructureIdentifier.SetFloat("maxTerrainHeight", surface.MaxTerrainHeight);
        StructureIdentifier.SetFloat("squashHeight", surface.MaxSquashHeight);
        StructureIdentifier.SetFloat("heightOffset", surface.terrainOffset);
        StructureIdentifier.SetFloat("heightSFalloff", mesh.heightFalloff);
        StructureIdentifier.SetFloat("waterHeight", mesh.waterHeight);

        StructureIdentifier.SetBuffer(0, "structurePlan", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetBuffer(0, "genStructures", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetBuffer(0, "structureChecks", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetBuffer(1, "structureChecks", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetBuffer(1, "structurePlan", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetBuffer(1, "genStructures", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetBuffer(2, "genStructures", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetInt("bSTART_plan", offsets.sampleStart);
        StructureIdentifier.SetInt("bSTART_check", offsets.checkStart);
        StructureIdentifier.SetInt("bSTART_struct", offsets.structureStart);
        StructureIdentifier.SetInt("bSTART_prune", offsets.prunedStart);

        StructureIdentifier.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetBuffer(1, "counter", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetBuffer(2, "counter", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetInt("bCOUNTER_plan", offsets.sampleCounter);
        StructureIdentifier.SetInt("bCOUNTER_check", offsets.checkCounter);
        StructureIdentifier.SetInt("bCOUNTER_struct", offsets.structureCounter);
        StructureIdentifier.SetInt("bCOUNTER_prune", offsets.prunedCounter);

        structureDataTranscriber.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        structureDataTranscriber.SetBuffer(0, "structPoints", UtilityBuffers.GenerationBuffer);
        structureDataTranscriber.SetInt("bSTART_struct", offsets.prunedStart);
        structureDataTranscriber.SetInt("bCOUNTER_struct", offsets.prunedCounter);
    }
    
    /// <summary> Samples the origins of all structures that intersect with the current chunk's boundaries. This is the <see href="https://blackmagic919.github.io/AboutMe/2024/06/08/Structure%20Planning/">
    /// first step </see> of structure generation and is necessary to ensure that the chunk is aware of all structures that overlap
    /// with its boundaries. </summary>
    /// <param name="maxLoD">The maximum LoD of structures generated. The LoD dictates the maximum side length of a structure
    /// that is guaranteed to be generated properly. See <see cref="WorldConfig.Generation.Structure.Generation.maxLoD"/> for more info.</param>
    /// <param name="chunkSize">The size of a <see cref="TerrainChunk.RealChunk"/> in grid space. </param>
    /// <param name="depth">The distance of the chunk from a leaf node within the <see cref="OctreeTerrain.Octree">chunk octree</see>. Identifies
    /// the size of the chunk relative to a <see cref="TerrainChunk.RealChunk"> real chunk </see>. See <see cref="TerrainChunk.depth"/> for more info. </param>
    /// <param name="chunkCoord">The coordinate, in <see cref="TerrainChunk.CCoord">Chunk Space</see>, of the chunk whose structures
    /// are planned. If the chunk spans multiple <paramref name="chunkCoord">ChunkCoords</paramref>, this is the coordinate of the origin of
    /// the region. </param>
    public static void SampleStructureLoD(int maxLoD, int chunkSize, int depth, int3 chunkCoord)
    {   
        //base depthL is the base chunk axis size to sample maximum detail
        int numChunksPerAxis = maxLoD + (1<<depth) + 1;
        int numChunksMax = numChunksPerAxis * numChunksPerAxis * numChunksPerAxis;

        StructureLoDSampler.SetInts("originChunkCoord", new int[] { chunkCoord.x, chunkCoord.y, chunkCoord.z });
        StructureLoDSampler.SetInt("chunkSize", chunkSize);
        StructureLoDSampler.SetInt("BaseDepthL", depth); 

        StructureLoDSampler.GetKernelThreadGroupSizes(0, out uint threadChunkSize, out uint threadLoDSize, out _);
        int numThreadsChunk = Mathf.CeilToInt(numChunksMax / (float)threadChunkSize);
        int numThreadsLoD = Mathf.CeilToInt(maxLoD / (float)threadLoDSize);
        StructureLoDSampler.Dispatch(0, numThreadsChunk, numThreadsLoD, 1);
    }

    /// <summary> Assigns structures to each position given by <see cref="SampleStructureLoD(int, int, int, int3)"/> based on 
    /// the biome and removes any invalid or trivial structures. This is the <see href="https://blackmagic919.github.io/AboutMe/2024/06/16/Structure-Pruning/">
    /// second step </see> of structure generation and allows for varied and localized structure generation. </summary>
    /// <param name="offset">The offset in grid space of the origin of the chunk</param>
    /// <param name="IsoLevel">The density of the surface of the terrain. See <see cref="WorldConfig.Quality.Terrain.IsoLevel"/> for more info.</param>
    public static void IdentifyStructures(Vector3 offset, float IsoLevel)
    {
        ComputeBuffer args = UtilityBuffers.CountToArgs(StructureIdentifier, UtilityBuffers.GenerationBuffer, offsets.sampleCounter);

        StructureIdentifier.SetFloat("IsoLevel", IsoLevel);
        SetSampleData(StructureIdentifier, offset, 1);

        int kernel = StructureIdentifier.FindKernel("Identify");
        StructureIdentifier.DispatchIndirect(kernel, args);//byte offset

        args = UtilityBuffers.CountToArgs(StructureIdentifier, UtilityBuffers.GenerationBuffer, offsets.checkCounter);
        kernel = StructureIdentifier.FindKernel("Check");
        StructureIdentifier.DispatchIndirect(kernel, args);//byte offset

        args = UtilityBuffers.CountToArgs(StructureIdentifier, UtilityBuffers.GenerationBuffer, offsets.structureCounter);
        kernel = StructureIdentifier.FindKernel("Prune");
        StructureIdentifier.DispatchIndirect(kernel, args);
    }

    /// <summary>  Transcribes the generation information of structures instersecting with the chunk from <see cref="GenerationBuffer"> working memory</see> to
    /// <paramref name="memory">long term storage</paramref>. This is the instance information of structures that <b>will</b> be generated
    /// in the chunk innevitably, following all pruning steps. </summary>
    /// <param name="memory">The destination buffer that the structure generation information will be copied to </param>
    /// <param name="addresses">The buffer containing the direct address within <paramref name="memory"/> where the information will be stored. </param>
    /// <returns>The index within <paramref name="addresses"/> of the location that contains the direct address to the 
    /// region within <paramref name="memory"/> where the information will be stored. </returns>
    public static uint TranscribeStructures(ComputeBuffer memory, ComputeBuffer addresses, uint addressIndex)
    {
        ComputeBuffer args = UtilityBuffers.CountToArgs(structureDataTranscriber, UtilityBuffers.GenerationBuffer, offsets.structureCounter);

        structureDataTranscriber.SetBuffer(0, "_MemoryBuffer", memory);
        structureDataTranscriber.SetBuffer(0, "_AddressDict", addresses);
        structureDataTranscriber.SetInt("addressIndex", (int)addressIndex);

        structureDataTranscriber.DispatchIndirect(0, args);
        return addressIndex;
    }

    /// <summary> Gets the number of structures saved in the memory block pointed to by a chunk's <paramref name="addressIndex">address handle</paramref> for its structures.
    /// This information is not explicitly stored but can be reconstructed by dividing the size of the allocated memory block by the size of a structure, factoring
    /// in meta data and padding. If the memory block is not represented in a known way or does not contain structure generation information, the result is undefined. </summary>
    /// <param name="memory">The GPU buffer containing the structure generation information that is to be counted.</param>
    /// <param name="address">The buffer containing the direct address within <paramref name="memory"/>where the information is stored. </param>
    /// <param name="addressIndex">The index within <paramref name="address"/> of the location that contains the direct address to the 
    /// region within <paramref name="memory"/> where the information is stored.</param>
    /// <param name="STRUCTURE_STRIDE_4BYTE">The size of the generation information of a single structure in units of 4-bytes. The amount of 
    /// unique structures can be obtained by dividing the total size of the chunk's structure generation information by this size.</param>
    /// <returns>A buffer containing the amount of structures in its first entry.</returns>
    public static ComputeBuffer GetStructCount(ComputeBuffer memory, ComputeBuffer address, int addressIndex, int STRUCTURE_STRIDE_4BYTE)
    {
        ComputeBuffer structCount = UtilityBuffers.appendCount;

        structureSizeCounter.SetBuffer(0, "_MemoryBuffer", memory);
        structureSizeCounter.SetBuffer(0, "_AddressDict", address);
        structureSizeCounter.SetInt("addressIndex", addressIndex);
        structureSizeCounter.SetInt("STRUCTURE_STRIDE_4BYTE", STRUCTURE_STRIDE_4BYTE);

        structureSizeCounter.SetBuffer(0, "structCount", structCount);
        structureSizeCounter.Dispatch(0, 1, 1, 1);

        return structCount;
    }
    
    /// <summary> Applies the generation information of structures to the chunk's terrain map. This is the <see href="https://blackmagic919.github.io/AboutMe/2024/07/03/Structure-Placement/">
    /// final step</see> of structure generation and involves transcribing the <see cref="WorldConfig.Generation.Structure.StructureData.map"/> information of each structure 
    /// over the chunk's terrain map. </summary>
    /// <remarks> The time complexity of this operation is O(n) with respect to the size of the largest structure within the chunk. </remarks>
    /// <param name="memory">The buffer containing the generation information for all structures generated by the chunk.</param>
    /// <param name="addresses">he buffer containing the direct address within <paramref name="memory"/>where the generation information is stored. </param>
    /// <param name="count">A single entry buffer dictating the amount of structures to place. </param>
    /// <param name="addressIndex">The index within <paramref name="addresses"/> of the location that contains the direct address to the 
    /// region within <paramref name="memory"/> where the generation information is stored.</param>
    /// <param name="mapStart">The location within <see cref="GenerationBuffer">working memory</see> of the start of the
    /// chunk's terrain map. See <see cref="Map.Generator.GeoGenOffsets.rawMapStart"/> for more info. </param>
    /// <param name="chunkSize">The size of a <see cref="TerrainChunk.RealChunk"/> in grid space. </param>
    /// <param name="meshSkipInc">The distance in grid space between two adjacent samples in the chunk's terrain map. Used to convert
    /// a structure's coordinate from grid space to map space(the location within the chunk's terrain map).</param>
    /// <param name="wOffset">The offset within the chunk's map generated structures will be transcribed to. If the chunk's map
    /// extends beyond what it exclusively contains, this should be used to indicate the axis offest relative to the chunk map's first
    /// entry of the first entry exclusively contained by the chunk.</param>
    /// <param name="wChunkSize">The axis size of the chunk's terrain map as it currently is in <see cref="GenerationBuffer"> working memory </see>.</param>
    /// <param name="IsoLevel">The density of the surface of the terrain. See <see cref="WorldConfig.Quality.Terrain.IsoLevel"/> for more info.</param>
    public static void ApplyStructures(ComputeBuffer memory, ComputeBuffer addresses, ComputeBuffer count, int addressIndex, int mapStart, int chunkSize, int meshSkipInc, int wOffset, int wChunkSize, float IsoLevel)
    {
        ComputeBuffer args = UtilityBuffers.CountToArgs(structureChunkGenerator, count);

        structureChunkGenerator.SetBuffer(0, "_MemoryBuffer", memory);
        structureChunkGenerator.SetBuffer(0, "_AddressDict", addresses);
        structureChunkGenerator.SetInt("addressIndex", addressIndex);

        structureChunkGenerator.SetBuffer(0, "numPoints", count);

        structureChunkGenerator.SetBuffer(0, "chunkData", UtilityBuffers.GenerationBuffer);
        structureChunkGenerator.SetInt("bSTART_map", mapStart);
        structureChunkGenerator.SetInt("chunkSize", chunkSize);
        structureChunkGenerator.SetInt("meshSkipInc", meshSkipInc);
        structureChunkGenerator.SetFloat("IsoLevel", IsoLevel);

        structureChunkGenerator.SetInt("wOffset", wOffset);
        structureChunkGenerator.SetInt("numPointsPerAxis", wChunkSize);

        structureChunkGenerator.DispatchIndirect(0, args);
    }

    /// <summary> Responsible for segmenting a fixed sized <see cref="GenerationBuffer"> working memory </see> buffer
    /// into regions for different purposes within the structure generation process. Only regions that can
    /// exist simultaneously need to occupy exclusive regions, otherwise the memory may be reused. See
    /// <see cref="BufferOffsets"/> for more info. </summary>
    /// <remarks>The locations specified within this structure is relative to the size of the objects that will be occupying
    /// them and not based off a universal atomic unit. </remarks>
    public struct StructureOffsets : BufferOffsets{
        /// <summary> The location storing the amount of raw structure origins generated by <see cref="SampleStructureLoD(int, int, int, int3)"/>. </summary>
        public int sampleCounter;
        /// <summary> The location storing the amount of concrete structures after pruning non-intersecting structures, provided by <see cref="IdentifyStructures(Vector3, float)"/>. </summary>
        public int structureCounter;
        /// <summary> The location storing the total amount of checks of all structures within the chunk, used in <see cref="IdentifyStructures(Vector3, float)"/>.
        /// See <see cref="WorldConfig.Generation.Structure.StructureData.CheckPoint"/> for more info. </summary>
        public int checkCounter;
        /// <summary> The location storing the amount of structures after pruning all invalid checks, provided by <see cref="IdentifyStructures(Vector3, float)"/>. </summary>
        public int prunedCounter;
        /// <summary> The location of the pruned structures, the structures that will actually be generated, provided by <see cref="IdentifyStructures(Vector3, float)"/>.</summary>
        public int prunedStart;
        /// <summary> The location of the raw structure origins provided by <see cref="SampleStructureLoD(int, int, int, int3)"/>. </summary>
        public int sampleStart;
        /// <summary> The location of the structure generation information provided by <see cref="IdentifyStructures(Vector3, float)"/>. </summary>
        public int structureStart;
        /// <summary> The location of the checks of all structures within the chunk, used in <see cref="IdentifyStructures(Vector3, float)"/>. </summary>
        public int checkStart;
        private int offsetStart; private int offsetEnd;
        /// <summary> The start of the buffer region that is used by the structure generator. 
        /// See <see cref="BufferOffsets.bufferStart"/> for more info. </summary>
        public int bufferStart{get{return offsetStart;}} 
        /// <summary> The end of the buffer region that is used by the structure generator. 
        /// See <see cref="BufferOffsets.bufferEnd"/> for more info. </summary>
        public int bufferEnd{get{return offsetEnd;}}

        /// <summary> Creates a new division scheme of working memory based on the maximum amount of structure points
        /// a chunk may need to reference simultaneously. An increased number of points will require more working
        /// memory allocated for the structure generator. The caller should make sure this does not 
        /// exceed the capacity of the buffer. </summary>
        /// <param name="maxStructurePoints">The maximum amount of structures that a single chunk may reference at the same time.
        /// This is the maximum possible structures that can be provided by <see cref="SampleStructureLoD(int, int, int, int3)"/> for
        /// the <see cref="WorldConfig.Quality.Terrain.MaxStructureDepth">largest possible chunk requiring structures</see> under the given 
        /// world's configuration. A larger chunk will require more structure points as it encompasses a larger region. </param>
        /// <param name="bufferStart">The start of the region within working memory the structure generator may utilize. See 
        /// <see cref="BufferOffsets.bufferStart"/> for more info. </param>
        public StructureOffsets(int maxStructurePoints, int bufferStart){
            this.offsetStart = bufferStart;
            sampleCounter = bufferStart; structureCounter = bufferStart + 1;
            checkCounter = bufferStart + 2; prunedCounter = bufferStart + 3;

            structureStart = Mathf.CeilToInt((float)(bufferStart + 4)/ STRUCTURE_STRIDE_WORD);
            int StructureEndInd_W = structureStart * STRUCTURE_STRIDE_WORD + maxStructurePoints * STRUCTURE_STRIDE_WORD;

            prunedStart = Mathf.CeilToInt((float)StructureEndInd_W / STRUCTURE_STRIDE_WORD);
            int PrunedEndInd_W = prunedStart * STRUCTURE_STRIDE_WORD + maxStructurePoints * STRUCTURE_STRIDE_WORD;

            sampleStart = Mathf.CeilToInt((float)PrunedEndInd_W / SAMPLE_STRIDE_WORD); //U for unit, W for word
            int SampleEndInd_W = sampleStart * SAMPLE_STRIDE_WORD + maxStructurePoints * SAMPLE_STRIDE_WORD;

            //These two regions can be reused
            checkStart = Mathf.CeilToInt((float)PrunedEndInd_W / CHECK_STRIDE_WORD);
            int CheckEndInd_W = checkStart * CHECK_STRIDE_WORD + maxStructurePoints * CHECK_STRIDE_WORD;
            this.offsetEnd = math.max(CheckEndInd_W, SampleEndInd_W);
        }
    }
}}
/*
public static ComputeBuffer CalculateStructureSize(ComputeBuffer structureCount, int structureStride, ref Queue<ComputeBuffer> bufferHandle)
{
    ComputeBuffer result = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
    bufferHandle.Enqueue(result);

    structureMemorySize.SetBuffer(0, "structureCount", structureCount);
    structureMemorySize.SetInt("structStride4Byte", structureStride);
    structureMemorySize.SetBuffer(0, "byteLength", result);

    structureMemorySize.Dispatch(0, 1, 1, 1);

    return result;
}

public static void AnalyzeTerrain(ComputeBuffer checks, ComputeBuffer structs, ComputeBuffer args, ComputeBuffer count, int[] samplers, float[] heights, Vector3 offset, int chunkSize, float IsoLevel)
{
    terrainAnalyzerGPU.SetBuffer(0, "numPoints", count);
    terrainAnalyzerGPU.SetBuffer(0, "checks", checks);
    terrainAnalyzerGPU.SetBuffer(0, "structs", structs);//output
    terrainAnalyzerGPU.SetFloat("IsoLevel", IsoLevel);

    terrainAnalyzerGPU.SetInt("caveCoarseSampler", samplers[0]);
    terrainAnalyzerGPU.SetInt("caveFineSampler", samplers[1]);
    terrainAnalyzerGPU.SetInt("continentalSampler", samplers[2]);
    terrainAnalyzerGPU.SetInt("erosionSampler", samplers[3]);
    terrainAnalyzerGPU.SetInt("PVSampler", samplers[4]);
    terrainAnalyzerGPU.SetInt("squashSampler", samplers[5]);

    terrainAnalyzerGPU.SetFloat("continentalHeight", heights[0]);
    terrainAnalyzerGPU.SetFloat("PVHeight", heights[1]);
    terrainAnalyzerGPU.SetFloat("squashHeight", heights[2]);
    terrainAnalyzerGPU.SetFloat("heightOffset", heights[3]);
    SetSampleData(terrainAnalyzerGPU, offset, chunkSize, 1);

    terrainAnalyzerGPU.DispatchIndirect(0, args);
}

public static ComputeBuffer CreateChecks(ComputeBuffer structures, ComputeBuffer args, ComputeBuffer count, int maxPoints, ref Queue<ComputeBuffer> bufferHandle)
{
    ComputeBuffer results = new ComputeBuffer(maxPoints, sizeof(uint) * 2 + sizeof(float) * 3, ComputeBufferType.Append);
    bufferHandle.Enqueue(results);

    StructureChecks.SetBuffer(0, "structures", structures);
    StructureChecks.SetBuffer(0, "numPoints", count);
    StructureChecks.SetBuffer(0, "checks", results);

    StructureChecks.DispatchIndirect(0, args);

    return results;
}
public static ComputeBuffer FilterStructures(ComputeBuffer structures, ComputeBuffer args, ComputeBuffer count, int maxPoints, ref Queue<ComputeBuffer> bufferHandle)
{
    ComputeBuffer result = new ComputeBuffer(maxPoints, sizeof(float) * 3 + sizeof(uint) * 3, ComputeBufferType.Append);
    result.SetCounterValue(0);
    bufferHandle.Enqueue(result);

    structureCheckFilter.SetBuffer(0, "numPoints", count);
    structureCheckFilter.SetBuffer(0, "structureInfos", structures);
    structureCheckFilter.SetBuffer(0, "validStructures", result);

    structureCheckFilter.DispatchIndirect(0, args);

    return result;
}

public static void PresetSampleShader(ComputeShader sampler, NoiseData noiseData, float maxInfluenceHeight, bool sample2D, bool interp, bool centerNoise){
    sampler.SetFloat("influenceHeight", maxInfluenceHeight);

    if(sample2D)
        sampler.EnableKeyword("SAMPLE_2D");
    else
        sampler.DisableKeyword("SAMPLE_2D");

    if(interp)
        sampler.EnableKeyword("INTERP");
    else
        sampler.DisableKeyword("INTERP");
    
    if (centerNoise)
        sampler.EnableKeyword("CENTER_NOISE");
    else
        sampler.DisableKeyword("CENTER_NOISE");
    
    PresetNoiseData(sampler, noiseData);
}

public static ComputeBuffer AnalyzeBiome(ComputeBuffer structs, ComputeBuffer args, ComputeBuffer count, int[] samplers, Vector3 offset, int chunkSize, int maxPoints, Queue<ComputeBuffer> bufferHandle)
{
    ComputeBuffer result = new ComputeBuffer(maxPoints, sizeof(int), ComputeBufferType.Structured);
    bufferHandle.Enqueue(result);

    biomeMapGenerator.SetBuffer(0, "structOrigins", structs);
    biomeMapGenerator.SetBuffer(0, "numPoints", count);
    biomeMapGenerator.SetBuffer(0, "biomeMap", result);
    biomeMapGenerator.SetInt("continentalSampler", samplers[0]);
    biomeMapGenerator.SetInt("erosionSampler", samplers[1]);
    biomeMapGenerator.SetInt("PVSampler", samplers[2]);
    biomeMapGenerator.SetInt("squashSampler", samplers[3]);
    biomeMapGenerator.SetInt("atmosphereSampler", samplers[4]);
    biomeMapGenerator.SetInt("humiditySampler", samplers[5]);
    SetSampleData(biomeMapGenerator, offset, chunkSize, 1);

    biomeMapGenerator.DispatchIndirect(0, args);

    return result;
}

public static ComputeBuffer AnalyzeNoiseMapGPU(ComputeBuffer checks, ComputeBuffer count, NoiseData noiseData, Vector3 offset, float maxInfluenceHeight, int chunkSize, int maxPoints, bool sample2D, bool interp, bool centerNoise, Queue<ComputeBuffer> bufferHandle){
    ComputeBuffer args = UtilityBuffers.CountToArgs(checkNoiseSampler, count);
    return AnalyzeNoiseMapGPU(checks, args, count, noiseData, offset, maxInfluenceHeight, chunkSize, maxPoints, sample2D, interp, centerNoise, bufferHandle);
}
public static ComputeBuffer AnalyzeNoiseMapGPU(ComputeBuffer checks, ComputeBuffer args, ComputeBuffer count, NoiseData noiseData, Vector3 offset, float maxInfluenceHeight, int chunkSize, int maxPoints, bool sample2D, bool interp, bool centerNoise, Queue<ComputeBuffer> bufferHandle)
{
    ComputeBuffer result = new ComputeBuffer(maxPoints, sizeof(float), ComputeBufferType.Append);
    bufferHandle.Enqueue(result);

    checkNoiseSampler.SetBuffer(0, "CheckPoints", checks);
    checkNoiseSampler.SetBuffer(0, "Results", result);
    checkNoiseSampler.SetBuffer(0, "numPoints", count);
    checkNoiseSampler.SetFloat("influenceHeight", maxInfluenceHeight);

    if(sample2D)
        checkNoiseSampler.EnableKeyword("SAMPLE_2D");
    else
        checkNoiseSampler.DisableKeyword("SAMPLE_2D");

    if(interp)
        checkNoiseSampler.EnableKeyword("INTERP");
    else
        checkNoiseSampler.DisableKeyword("INTERP");
    
    if (centerNoise)
        checkNoiseSampler.EnableKeyword("CENTER_NOISE");
    else
        checkNoiseSampler.DisableKeyword("CENTER_NOISE");


    SetNoiseData(checkNoiseSampler, chunkSize, 1, noiseData, offset);

    checkNoiseSampler.DispatchIndirect(0, args);

    return result;
}

public static void AnalyzeChecks(ComputeBuffer checks, ComputeBuffer args, ComputeBuffer count, ComputeBuffer density, float IsoValue, ref ComputeBuffer valid, ref Queue<ComputeBuffer> bufferHandle)
{
    checkVerification.SetBuffer(0, "numPoints", count);
    checkVerification.SetBuffer(0, "checks", checks);
    checkVerification.SetBuffer(0, "density", density);
    checkVerification.SetFloat("IsoValue", IsoValue);

    checkVerification.SetBuffer(0, "validity", valid);

    checkVerification.DispatchIndirect(0, args);
}

public static ComputeBuffer AnalyzeNoiseMapGPU(ComputeShader sampler, ComputeBuffer checks, ComputeBuffer count, Vector3 offset, int chunkSize, int maxPoints, Queue<ComputeBuffer> bufferHandle){
    ComputeBuffer args = UtilityBuffers.CountToArgs(sampler, count);
    return AnalyzeNoiseMapGPU(sampler, checks, args, count, offset, chunkSize, maxPoints, bufferHandle);
}
public static ComputeBuffer AnalyzeNoiseMapGPU(ComputeShader sampler, ComputeBuffer checks, ComputeBuffer args, ComputeBuffer count, Vector3 offset, int chunkSize, int maxPoints, Queue<ComputeBuffer> bufferHandle){
    ComputeBuffer result = new ComputeBuffer(maxPoints, sizeof(float), ComputeBufferType.Append);
    bufferHandle.Enqueue(result);

    sampler.SetBuffer(0, "CheckPoints", checks);
    sampler.SetBuffer(0, "Results", result);
    sampler.SetBuffer(0, "numPoints", count);

    SetSampleData(sampler, offset, chunkSize, 1);
    sampler.DispatchIndirect(0, args);
    return result;
}

public static ComputeBuffer CombineTerrainMapsGPU(ComputeBuffer args, ComputeBuffer count, ComputeBuffer contBuffer, ComputeBuffer erosionBuffer, ComputeBuffer PVBuffer, int maxPoints, float terrainOffset, Queue<ComputeBuffer> bufferHandle)
{
    ComputeBuffer results = new ComputeBuffer(maxPoints, sizeof(float), ComputeBufferType.Structured);
    bufferHandle.Enqueue(results);

    terrainCombinerGPU.SetBuffer(0, "continental", contBuffer);
    terrainCombinerGPU.SetBuffer(0, "erosion", erosionBuffer);
    terrainCombinerGPU.SetBuffer(0, "peaksValleys", PVBuffer);
    terrainCombinerGPU.SetBuffer(0, "Result", results);

    terrainCombinerGPU.SetBuffer(0, "numOfPoints", count);
    terrainCombinerGPU.SetFloat("heightOffset", terrainOffset);

    terrainCombinerGPU.DispatchIndirect(0, args);

    return results;
}


public static ComputeBuffer InitializeIndirect<T>(ComputeBuffer args, ComputeBuffer count, T val, int maxPoints, ref Queue<ComputeBuffer> bufferHandle)
{
    ComputeBuffer map;
    indirectMapInitialize.DisableKeyword("USE_BOOL");
    indirectMapInitialize.DisableKeyword("USE_INT");

    //Size of int and float are technically the same, but it's more unreadable
    if (val.GetType() == typeof(int))
    {
        indirectMapInitialize.EnableKeyword("USE_INT");
        indirectMapInitialize.SetInt("value", (int)(object)val);
        map = new ComputeBuffer(maxPoints, sizeof(int), ComputeBufferType.Structured);
    }
    else if (val.GetType() == typeof(bool))
    {
        indirectMapInitialize.EnableKeyword("USE_BOOL");
        indirectMapInitialize.SetBool("value", (bool)(object)val);
        map = new ComputeBuffer(maxPoints, sizeof(bool), ComputeBufferType.Structured);
    }
    else { 
        indirectMapInitialize.SetFloat("value", (float)(object)val);
        map = new ComputeBuffer(maxPoints, sizeof(float), ComputeBufferType.Structured);
    }

    bufferHandle.Enqueue(map);
    indirectMapInitialize.SetBuffer(0, "numPoints", count);
    indirectMapInitialize.SetBuffer(0, "map", map);

    indirectMapInitialize.DispatchIndirect(0, args);

    return map;
}
*/