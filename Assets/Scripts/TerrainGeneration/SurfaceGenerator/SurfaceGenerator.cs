using System.Collections.Generic;
using UnityEngine;
using static UtilityBuffers;
using WorldConfig;
using WorldConfig.Generation;
using Unity.Mathematics;

namespace TerrainGeneration.Surface{

/// <summary>
/// A manager unique for every terrain chunk responsible for creating and holding onto
/// intermediate surface information required by the chunk during the terrain
/// generation process.
/// </summary>
public class Creator
{
    /// <summary>
    /// The address of the generated surface map for this chunk. The location within 
    /// <see cref="GenerationPreset.MemoryHandle.Address"/> of the address within <see cref="GenerationPreset.MemoryHandle.Storage"/> 
    /// of the beginning of the surface map information cached for this chunk. 
    /// </summary>
    public uint SurfaceMapAddress;
    //Biome, 
    const uint SURFDATA_STRIDE_4BYTE = 6;   
    /// <summary> Samples the surface information based off the position and size of the chunk
    /// and saves it in long-term GPU memory, referenced through <see cref="SurfaceMapAddress"/>. </summary>
    /// <param name="offset">The offset in grid space of the origin(bottom left corner) of the chunk. </param>
    /// <param name="ChunkSize">The resolution of the chunk; how many samples are conducted per axis of the chunk.</param>
    /// <param name="SkipInc">The distance between consecutive samples in the chunk; the side length of a surface pixel</param>
    public void SampleSurfaceMaps(float2 offset, int ChunkSize, int SkipInc){
        Generator.SampleSurfaceData(offset, ChunkSize, SkipInc);

        int numPointsAxes = ChunkSize;
        int numOfPoints = numPointsAxes * numPointsAxes;
        
        uint mapAddressIndex = GenerationPreset.memoryHandle.AllocateMemoryDirect(numOfPoints, (int)SURFDATA_STRIDE_4BYTE);
        Generator.TranscribeSurfaceMap(GenerationPreset.memoryHandle.GetBlockBuffer(mapAddressIndex),
            GenerationPreset.memoryHandle.Address, (int)mapAddressIndex, numOfPoints);
        SurfaceMapAddress = mapAddressIndex;
    }

    /// <summary>  Releases any intermediate surface maps held by this instance. Call this to ensure
    /// that no memory is being held by a chunk being disposed. See <seealso cref="SurfaceMapAddress"/>. </summary>
    public void ReleaseMap(){
        if(SurfaceMapAddress == 0) return;
        GenerationPreset.memoryHandle.ReleaseMemory(SurfaceMapAddress);
        SurfaceMapAddress = 0;
    }
}

/// <summary>
/// A static manager responsible for managing loading and access
/// of all compute-shaders used within the surface generation process
/// of terrain generation. All instructions related to surface
/// generation done by the GPU is streamlined from this module. 
/// </summary>
public static class Generator
{
    static ComputeShader surfaceTranscriber;//
    static ComputeShader mapSimplifier; //
    static ComputeShader surfaceDataSampler;

    static Generator(){
        mapSimplifier = Resources.Load<ComputeShader>("Compute/TerrainGeneration/SurfaceChunk/SimplifyMap");
        surfaceTranscriber = Resources.Load<ComputeShader>("Compute/TerrainGeneration/SurfaceChunk/TranscribeSurfaceMap");
        surfaceDataSampler = Resources.Load<ComputeShader>("Compute/TerrainGeneration/SurfaceChunk/SurfaceMapSampler");
    }

    /// <summary>
    /// Presets all compute-shaders used in the surface generator by acquiring them and
    /// binding any constant values(information derived from the world's settings that 
    /// won't change until the world is unloaded) to them. Referenced by
    /// <see cref="TerrainGeneration.SystemProtocol.Startup"/> </summary>
    public static void PresetData(){
        WorldConfig.Generation.Surface surface = Config.CURRENT.Generation.Surface.value;
        surfaceDataSampler.SetBuffer(0, "surfMap", UtilityBuffers.GenerationBuffer);

        surfaceDataSampler.SetInt("continentalSampler", surface.ContinentalIndex);
        surfaceDataSampler.SetInt("majorWarpSampler", surface.MajorWarpIndex);
        surfaceDataSampler.SetInt("minorWarpSampler", surface.MinorWarpIndex);
        surfaceDataSampler.SetInt("erosionSampler", surface.ErosionIndex);
        surfaceDataSampler.SetInt("squashSampler", surface.SquashIndex);
        surfaceDataSampler.SetInt("InfHeightSampler", surface.InfHeightIndex);
        surfaceDataSampler.SetInt("InfOffsetSampler", surface.InfOffsetIndex);
        surfaceDataSampler.SetInt("atmosphereSampler", surface.AtmosphereIndex);

        surfaceDataSampler.SetFloat("maxInfluenceHeight", surface.MaxInfluenceHeight);
        surfaceDataSampler.SetFloat("maxTerrainHeight", surface.MaxTerrainHeight);
        surfaceDataSampler.SetFloat("squashHeight", surface.MaxSquashHeight);
        surfaceDataSampler.SetFloat("heightOffset", surface.terrainOffset);
    }

    //The wonder shader that does everything (This way more parallelization is achieved)
    /// <summary> Samples surface terrain information for a chunk based off the position and size of the chunk. 
    /// The resultant sampled map is stored in a <see cref="UtilityBuffers.GenerationBuffer"> working
    /// memory buffer </see> and will be lost unless transcribed to long term storage through <see cref="TranscribeSurfaceMap"/>. </summary>
    /// <remarks> 
    /// The surface map is a 2D map describing 6 values for every pixel. The <see cref="WorldConfig.Generation.Biome.SurfaceBiome.biome"> surface biome index </see>,
    /// the <see cref="WorldConfig.Generation.Surface.MaxTerrainHeight">height of the surface</see>, the <see cref="WorldConfig.Generation.Surface.SquashNoise"> squash height </see>, the 
    /// <see cref="WorldConfig.Generation.Surface.AtmosphereNoise"> falloff intensity of the atmosphere</see>, and the <see cref="WorldConfig.Generation.Biome.SurfaceBiome.InfluenceStart"> start </see> and 
    /// <see cref="WorldConfig.Generation.Biome.SurfaceBiome.InfluenceEnd"> end </see> of its vertical influence, 
    /// </remarks>
    /// <param name="offset">The offset in grid space of the origin to begin sampling. </param>
    /// <param name="chunkSize">The resolution to sample with; how many samples are conducted per axis. </param>
    /// <param name="mapSkipInc">The distance between adjacent samples; the side length of a surface pixel</param>
    public static void SampleSurfaceData(Vector2 offset, int chunkSize, int mapSkipInc){
        int numPointsAxes = chunkSize;
        Vector3 offset3D = new Vector3(offset.x, 0, offset.y);
        surfaceDataSampler.SetInt("numPointsPerAxis", numPointsAxes);
        SetSampleData(surfaceDataSampler, offset3D, mapSkipInc);

        surfaceDataSampler.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);
        surfaceDataSampler.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, 1);
    }

    /// <summary>  Transcribes the surface information. Copies the generated surface map created through <see cref="SampleSurfaceData"/> from
    /// <see cref="UtilityBuffers.GenerationBuffer"> working memory </see> to a location preallocated for it in
    /// <see cref="TerrainGeneration.GenerationPreset.MemoryHandle.Storage"> long term storage </see> where it won't be 
    /// overwritten. </summary>
    /// <param name="memory">The destination buffer that the surface map will be copied to</param>
    /// <param name="addresses">The buffer containing the direct address to the location within 
    /// <paramref name="memory"/> where the surface map will be copied to. </param>
    /// <param name="addressIndex">The indirect index within <paramref name="addresses"/> of the address
    /// within <paramref name="memory"/> where the surface map will be copied to. </param>
    /// <param name="numPoints">The <b>total</b> amount of points copied from working memory to the specified location.</param>
    public static void TranscribeSurfaceMap(ComputeBuffer memory, GraphicsBuffer addresses, int addressIndex, int numPoints){
        surfaceTranscriber.SetBuffer(0, "SurfaceMap", UtilityBuffers.GenerationBuffer);
        surfaceTranscriber.SetInt("numSurfacePoints", numPoints);

        surfaceTranscriber.SetBuffer(0, "_MemoryBuffer", memory);
        surfaceTranscriber.SetBuffer(0, "_AddressDict", addresses);
        surfaceTranscriber.SetInt("addressIndex", addressIndex);

        surfaceTranscriber.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPoints / (float)threadGroupSize);
        surfaceTranscriber.Dispatch(0, numThreadsPerAxis, 1, 1);
    }

    /// <summary> Converts a 2D surface map sampled at a <paramref name="sourceSkipInc"> higher resolution </paramref> to one of a 
    /// <paramref name="destSkipInc"> lower resolution </paramref>. Reducing the resolution reduces the size of the surface map
    /// by taking every (<paramref name="destSkipInc"/> / <paramref name="sourceSkipInc"/>)th element on every axis of the map. 
    /// <paramref name="destSkipInc"/> must be an integer multiple of <paramref name="sourceSkipInc"/>. </summary>
    /// <remarks> This function is deprecated and should no longer be used. </remarks>
    /// <param name="memory">The source buffer that the surface map will be referenced from</param>
    /// <param name="addresses">The buffer containing the direct address to the location within 
    /// <paramref name="memory"/> of the surface map that is to be simplified. </param>
    /// <param name="addressIndex">The indirect index within <paramref name="addresses"/> of the address
    /// within <paramref name="memory"/>  of the surface map that is to be simplified. </param>
    /// <param name="chunkSize">The side length in grid space of the surface map in grid space. </param>
    /// <param name="sourceSkipInc">The distance between adjacent samples in the saved surface map 
    /// currently in <paramref name="addresses">long-term storage</paramref>. </param>
    /// <param name="destSkipInc">The distance between adjacent samples in the resultant simplified surface
    /// map that will be written to in the returned buffer.</param>
    /// <param name="bufferHandle">The optional buffer handle that will be given the output buffer to facilitate
    /// its management and release. </param>
    /// <returns>A <see cref="ComputeBuffer"/> containing the simplified surface map.</returns>
    public static ComputeBuffer SimplifyMap(ComputeBuffer memory, ComputeBuffer addresses, int addressIndex, int chunkSize, int sourceSkipInc, int destSkipInc, Queue<ComputeBuffer> bufferHandle = null)
    {
        int sourcePointsAxes = chunkSize / sourceSkipInc + 1;
        int destPointsAxes = chunkSize / destSkipInc + 1;
        int destNumOfPoints = destPointsAxes * destPointsAxes;

        ComputeBuffer dest = new ComputeBuffer(destNumOfPoints, sizeof(uint));
        bufferHandle?.Enqueue(dest);

        mapSimplifier.SetInt("destPointsPerAxis", destPointsAxes);
        mapSimplifier.SetInt("destSkipInc", destSkipInc);

        mapSimplifier.SetInt("sourcePointsPerAxis", sourcePointsAxes);
        mapSimplifier.SetInt("sourceSkipInc", sourceSkipInc);

        mapSimplifier.SetBuffer(0, "_MemoryBuffer", memory);
        mapSimplifier.SetBuffer(0, "_AddressDict", addresses);
        mapSimplifier.SetInt("addressIndex", addressIndex);
        
        mapSimplifier.SetBuffer(0, "destination", dest);

        mapSimplifier.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(destPointsAxes / (float)threadGroupSize);
        mapSimplifier.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, 1);

        return dest;
    }
}}
/*
//Returns raw noise data
public static ComputeBuffer GetNoiseMap(NoiseData noiseData, Vector2 offset, float maxInfluenceHeight, int chunkSize, int meshSkipInc, Queue<ComputeBuffer> bufferHandle, out ComputeBuffer results)
{
    int numPointsAxes = chunkSize / meshSkipInc + 1;
    int numOfPoints = numPointsAxes * numPointsAxes;
    ComputeBuffer rawPoints = new ComputeBuffer(numOfPoints, sizeof(float));
    results = new ComputeBuffer(numOfPoints, sizeof(float));

    bufferHandle.Enqueue(rawPoints);

    Vector3 offset3D = new Vector3(offset.x, 0, offset.y);
    noiseMapGenerator.SetBuffer(0, "rawPoints", rawPoints);
    noiseMapGenerator.SetBuffer(0, "points", results);
    noiseMapGenerator.SetFloat("influenceHeight", maxInfluenceHeight);
    noiseMapGenerator.SetInt("numPointsPerAxis", numPointsAxes);

    SetNoiseData(noiseMapGenerator, chunkSize, meshSkipInc, noiseData, offset3D);
    noiseMapGenerator.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
    int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);
    noiseMapGenerator.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, 1);

    return rawPoints;
}


public static ComputeBuffer CombineTerrainMaps(ComputeBuffer contBuffer, ComputeBuffer erosionBuffer, ComputeBuffer PVBuffer, int numOfPoints, float terrainOffset, Queue<ComputeBuffer> bufferHandle = null)
{
    ComputeBuffer results = new ComputeBuffer(numOfPoints, sizeof(float));

    bufferHandle?.Enqueue(results);

    terrainCombiner.SetBuffer(0, "continental", contBuffer);
    terrainCombiner.SetBuffer(0, "erosion", erosionBuffer);
    terrainCombiner.SetBuffer(0, "peaksValleys", PVBuffer);
    terrainCombiner.SetBuffer(0, "Result", results);

    terrainCombiner.SetInt("numOfPoints", numOfPoints);
    terrainCombiner.SetFloat("heightOffset", terrainOffset);

    terrainCombiner.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
    int numThreadsPerAxis = Mathf.CeilToInt(numOfPoints / (float)threadGroupSize);
    terrainCombiner.Dispatch(0, numThreadsPerAxis, 1, 1);

    return results;
}

public static ComputeBuffer GetBiomeMap(int chunkSize, int meshSkipInc, SurfaceChunk.NoiseMaps noiseData, Queue<ComputeBuffer> bufferHandle = null)
{
    int numPointsAxes = chunkSize / meshSkipInc + 1;
    int numOfPoints = numPointsAxes * numPointsAxes;

    ComputeBuffer biomes = new ComputeBuffer(numOfPoints, sizeof(int), ComputeBufferType.Structured);

    bufferHandle?.Enqueue(biomes);

    biomeMapGenerator.DisableKeyword("INDIRECT");
    biomeMapGenerator.SetInt("numOfPoints", numOfPoints);
    biomeMapGenerator.SetBuffer(0, "continental", noiseData.continental);
    biomeMapGenerator.SetBuffer(0, "erosion", noiseData.erosion);
    biomeMapGenerator.SetBuffer(0, "peaksValleys", noiseData.pvNoise);
    biomeMapGenerator.SetBuffer(0, "squash", noiseData.squash);
    biomeMapGenerator.SetBuffer(0, "atmosphere", noiseData.atmosphere);
    biomeMapGenerator.SetBuffer(0, "humidity", noiseData.humidity);
    biomeMapGenerator.SetBuffer(0, "biomeMap", biomes);

    biomeMapGenerator.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
    int numThreadsPerAxis = Mathf.CeilToInt(numOfPoints / (float)threadGroupSize);

    biomeMapGenerator.Dispatch(0, numThreadsPerAxis, 1, 1);

    return biomes;
}

public static ComputeBuffer CombineTerrainMapsGPU(ComputeBuffer count, ComputeBuffer contBuffer, ComputeBuffer erosionBuffer, ComputeBuffer PVBuffer, int maxPoints, float terrainOffset, Queue<ComputeBuffer> bufferHandle)
{
    ComputeBuffer results = new ComputeBuffer(maxPoints, sizeof(float), ComputeBufferType.Structured);
    ComputeBuffer args = UtilityBuffers.CountToArgs(terrainCombinerGPU, count);
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
*/
