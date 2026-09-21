using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Arterra.Configuration;
using Arterra.Utils;
using Arterra.Core.Storage;
using static Arterra.Core.Storage.SharedResourceManager;

namespace Arterra.Engine.Terrain.Surface{

/// <summary>
/// A manager unique for every terrain chunk responsible for creating and holding onto
/// intermediate surface information required by the chunk during the terrain
/// generation process.
/// </summary>
public class Creator
{
    public GraphicsContextId GraphicsContext;

    public Creator(GraphicsContextId graphicsContext = GraphicsContextId.Generation) {
        GraphicsContext = graphicsContext;
    }

    public GraphicsResourceContext GetGraphicsContext() => Graphics(GraphicsContext);

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
        GraphicsResourceContext gpuContext = GetGraphicsContext();
        SampleSurfaceData(offset, ChunkSize, SkipInc);
        int numPointsAxes = ChunkSize;
        int numOfPoints = numPointsAxes * numPointsAxes;

        uint mapAddressIndex = gpuContext.Memory.AllocateMemoryDirect(numOfPoints, (int)SURFDATA_STRIDE_4BYTE);
        TranscribeSurfaceMap(gpuContext.Memory.GetBlockBuffer(mapAddressIndex),
            gpuContext.Memory.Address, (int)mapAddressIndex, numOfPoints);

        ReleaseMap();
        SurfaceMapAddress = mapAddressIndex;
    }

    /// <summary>  Releases any intermediate surface maps held by this instance. Call this to ensure
    /// that no memory is being held by a chunk being disposed. See <seealso cref="SurfaceMapAddress"/>. </summary>
    public void ReleaseMap(){
        if(SurfaceMapAddress == 0) return;
        GetGraphicsContext().Memory.ReleaseMemory(SurfaceMapAddress);
        SurfaceMapAddress = 0;
    }

    static ComputeShader surfaceTranscriber;//
    static ComputeShader mapSimplifier; //
    static ComputeShader surfaceDataSampler;

    static Creator(){
        mapSimplifier = Resources.Load<ComputeShader>("Compute/TerrainGeneration/SurfaceChunk/SimplifyMap");
        surfaceTranscriber = Resources.Load<ComputeShader>("Compute/TerrainGeneration/SurfaceChunk/TranscribeSurfaceMap");
        surfaceDataSampler = Resources.Load<ComputeShader>("Compute/TerrainGeneration/SurfaceChunk/SurfaceMapSampler");
    }

    /// <summary>
    /// Presets all compute-shaders used in the surface generator by acquiring them and
    /// binding any constant values(information derived from the world's settings that
    /// won't change until the world is unloaded) to them. Referenced by
    /// <see cref="Terrain.SystemProtocol.Startup"/> </summary>
    public static void PresetData(GraphicsContextId graphicsContext = GraphicsContextId.Generation){
        GraphicsResourceContext gpuContext = Graphics(graphicsContext);
        Data.Generation.Map mesh = Config.CURRENT.Generation.Terrain.value;
        Data.Generation.Surface surface = Config.CURRENT.Generation.Surface.value;
        gpuContext.SetBuffer(surfaceDataSampler, 0, "surfMap", gpuContext.Work.Scratch);

        gpuContext.SetInt(surfaceDataSampler, "continentalSampler", surface.ContinentalIndex);
        gpuContext.SetInt(surfaceDataSampler, "majorWarpSampler", surface.MajorWarpIndex);
        gpuContext.SetInt(surfaceDataSampler, "minorWarpSampler", surface.MinorWarpIndex);
        gpuContext.SetInt(surfaceDataSampler, "erosionSampler", surface.ErosionIndex);
        gpuContext.SetInt(surfaceDataSampler, "squashSampler", surface.SquashIndex);
        gpuContext.SetInt(surfaceDataSampler, "InfHeightSampler", surface.InfHeightIndex);
        gpuContext.SetInt(surfaceDataSampler, "InfOffsetSampler", surface.InfOffsetIndex);
        gpuContext.SetInt(surfaceDataSampler, "atmosphereSampler", surface.AtmosphereIndex);

        gpuContext.SetFloat(surfaceDataSampler, "maxInfluenceHeight", surface.MaxInfluenceHeight);
        gpuContext.SetFloat(surfaceDataSampler, "maxTerrainHeight", surface.MaxTerrainHeight);
        gpuContext.SetFloat(surfaceDataSampler, "squashHeight", surface.MaxSquashHeight);
        gpuContext.SetFloat(surfaceDataSampler, "heightOffset", surface.terrainOffset);
        gpuContext.SetFloat(surfaceDataSampler, "waterHeight", mesh.waterHeight);
    }

    //The wonder shader that does everything (This way more parallelization is achieved)
    /// <summary> Samples surface terrain information for a chunk based off the position and size of the chunk.
    /// The resultant sampled map is stored in a <see cref="GraphicsGeneration.Work.Scratch"> working
    /// memory buffer </see> and will be lost unless transcribed to long term storage through <see cref="TranscribeSurfaceMap"/>. </summary>
    /// <remarks>
    /// The surface map is a 2D map describing 6 values for every pixel. The <see cref="Generation.Biome.SurfaceBiome.biome"> surface biome index </see>,
    /// the <see cref="Generation.Surface.MaxTerrainHeight">height of the surface</see>, the <see cref="Generation.Surface.SquashNoise"> squash height </see>, the
    /// <see cref="Generation.Surface.AtmosphereNoise"> falloff intensity of the atmosphere</see>, and the <see cref="Generation.Biome.SurfaceBiome.InfluenceStart"> start </see> and
    /// <see cref="Configuration.Generation.Biome.SurfaceBiome.InfluenceEnd"> end </see> of its vertical influence,
    /// </remarks>
    /// <param name="offset">The offset in grid space of the origin to begin sampling. </param>
    /// <param name="chunkSize">The resolution to sample with; how many samples are conducted per axis. </param>
    /// <param name="mapSkipInc">The distance between adjacent samples; the side length of a surface pixel</param>
    public void SampleSurfaceData(Vector2 offset, int chunkSize, int mapSkipInc){
        GraphicsResourceContext gpuContext = GetGraphicsContext();
        int numPointsAxes = chunkSize;
        Vector3 offset3D = new Vector3(offset.x, 0, offset.y);
        gpuContext.SetInt(surfaceDataSampler, "numPointsPerAxis", numPointsAxes);
        gpuContext.Work.SetSampleData(surfaceDataSampler, offset3D, mapSkipInc);

        surfaceDataSampler.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);
        gpuContext.Dispatch(surfaceDataSampler, 0, numThreadsPerAxis, numThreadsPerAxis, 1);
    }

    /// <summary>  Transcribes the surface information. Copies the generated surface map created through <see cref="SampleSurfaceData"/> from
    /// <see cref="GraphicsGeneration.Work.Scratch"> working memory </see> to a location preallocated for it in
    /// <see cref="TerrainGeneration.GenerationPreset.MemoryHandle.Storage"> long term storage </see> where it won't be
    /// overwritten. </summary>
    /// <param name="memory">The destination buffer that the surface map will be copied to</param>
    /// <param name="addresses">The buffer containing the direct address to the location within
    /// <paramref name="memory"/> where the surface map will be copied to. </param>
    /// <param name="addressIndex">The indirect index within <paramref name="addresses"/> of the address
    /// within <paramref name="memory"/> where the surface map will be copied to. </param>
    /// <param name="numPoints">The <b>total</b> amount of points copied from working memory to the specified location.</param>
    public void TranscribeSurfaceMap(ComputeBuffer memory, GraphicsBuffer addresses, int addressIndex, int numPoints){
        GraphicsResourceContext gpuContext = GetGraphicsContext();
        gpuContext.SetBuffer(surfaceTranscriber, 0, "SurfaceMap", gpuContext.Work.Scratch);
        gpuContext.SetInt(surfaceTranscriber, "numSurfacePoints", numPoints);

        gpuContext.SetBuffer(surfaceTranscriber, 0, "_MemoryBuffer", memory);
        gpuContext.SetBuffer(surfaceTranscriber, 0, "_AddressDict", addresses);
        gpuContext.SetInt(surfaceTranscriber, "addressIndex", addressIndex);

        surfaceTranscriber.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPoints / (float)threadGroupSize);
        gpuContext.Dispatch(surfaceTranscriber, 0, numThreadsPerAxis, 1, 1);
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
    public ComputeBuffer SimplifyMap(ComputeBuffer memory, ComputeBuffer addresses, int addressIndex, int chunkSize, int sourceSkipInc, int destSkipInc, Queue<ComputeBuffer> bufferHandle = null)
    {
        GraphicsResourceContext gpuContext = GetGraphicsContext();
        int sourcePointsAxes = chunkSize / sourceSkipInc + 1;
        int destPointsAxes = chunkSize / destSkipInc + 1;
        int destNumOfPoints = destPointsAxes * destPointsAxes;

        ComputeBuffer dest = new ComputeBuffer(destNumOfPoints, sizeof(uint));
        bufferHandle?.Enqueue(dest);

        gpuContext.SetInt(mapSimplifier, "destPointsPerAxis", destPointsAxes);
        gpuContext.SetInt(mapSimplifier, "destSkipInc", destSkipInc);

        gpuContext.SetInt(mapSimplifier, "sourcePointsPerAxis", sourcePointsAxes);
        gpuContext.SetInt(mapSimplifier, "sourceSkipInc", sourceSkipInc);

        gpuContext.SetBuffer(mapSimplifier, 0, "_MemoryBuffer", memory);
        gpuContext.SetBuffer(mapSimplifier, 0, "_AddressDict", addresses);
        gpuContext.SetInt(mapSimplifier, "addressIndex", addressIndex);

        gpuContext.SetBuffer(mapSimplifier, 0, "destination", dest);

        mapSimplifier.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(destPointsAxes / (float)threadGroupSize);
        gpuContext.Dispatch(mapSimplifier, 0, numThreadsPerAxis, numThreadsPerAxis, 1);

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
    GraphicsGeneration.SetBuffer(noiseMapGenerator, 0, "rawPoints", rawPoints);
    GraphicsGeneration.SetBuffer(noiseMapGenerator, 0, "points", results);
    GraphicsGeneration.SetFloat(noiseMapGenerator, "influenceHeight", maxInfluenceHeight);
    GraphicsGeneration.SetInt(noiseMapGenerator, "numPointsPerAxis", numPointsAxes);

    SetNoiseData(noiseMapGenerator, chunkSize, meshSkipInc, noiseData, offset3D);
    noiseMapGenerator.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
    int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);
    GraphicsGeneration.Dispatch(noiseMapGenerator, 0, numThreadsPerAxis, numThreadsPerAxis, 1);

    return rawPoints;
}


public static ComputeBuffer CombineTerrainMaps(ComputeBuffer contBuffer, ComputeBuffer erosionBuffer, ComputeBuffer PVBuffer, int numOfPoints, float terrainOffset, Queue<ComputeBuffer> bufferHandle = null)
{
    ComputeBuffer results = new ComputeBuffer(numOfPoints, sizeof(float));

    bufferHandle?.Enqueue(results);

    GraphicsGeneration.SetBuffer(terrainCombiner, 0, "continental", contBuffer);
    GraphicsGeneration.SetBuffer(terrainCombiner, 0, "erosion", erosionBuffer);
    GraphicsGeneration.SetBuffer(terrainCombiner, 0, "peaksValleys", PVBuffer);
    GraphicsGeneration.SetBuffer(terrainCombiner, 0, "Result", results);

    GraphicsGeneration.SetInt(terrainCombiner, "numOfPoints", numOfPoints);
    GraphicsGeneration.SetFloat(terrainCombiner, "heightOffset", terrainOffset);

    terrainCombiner.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
    int numThreadsPerAxis = Mathf.CeilToInt(numOfPoints / (float)threadGroupSize);
    GraphicsGeneration.Dispatch(terrainCombiner, 0, numThreadsPerAxis, 1, 1);

    return results;
}

public static ComputeBuffer GetBiomeMap(int chunkSize, int meshSkipInc, SurfaceChunk.NoiseMaps noiseData, Queue<ComputeBuffer> bufferHandle = null)
{
    int numPointsAxes = chunkSize / meshSkipInc + 1;
    int numOfPoints = numPointsAxes * numPointsAxes;

    ComputeBuffer biomes = new ComputeBuffer(numOfPoints, sizeof(int), ComputeBufferType.Structured);

    bufferHandle?.Enqueue(biomes);

    biomeMapGenerator.DisableKeyword("INDIRECT");
    GraphicsGeneration.SetInt(biomeMapGenerator, "numOfPoints", numOfPoints);
    GraphicsGeneration.SetBuffer(biomeMapGenerator, 0, "continental", noiseData.continental);
    GraphicsGeneration.SetBuffer(biomeMapGenerator, 0, "erosion", noiseData.erosion);
    GraphicsGeneration.SetBuffer(biomeMapGenerator, 0, "peaksValleys", noiseData.pvNoise);
    GraphicsGeneration.SetBuffer(biomeMapGenerator, 0, "squash", noiseData.squash);
    GraphicsGeneration.SetBuffer(biomeMapGenerator, 0, "atmosphere", noiseData.atmosphere);
    GraphicsGeneration.SetBuffer(biomeMapGenerator, 0, "humidity", noiseData.humidity);
    GraphicsGeneration.SetBuffer(biomeMapGenerator, 0, "biomeMap", biomes);

    biomeMapGenerator.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
    int numThreadsPerAxis = Mathf.CeilToInt(numOfPoints / (float)threadGroupSize);

    GraphicsGeneration.Dispatch(biomeMapGenerator, 0, numThreadsPerAxis, 1, 1);

    return biomes;
}

public static ComputeBuffer CombineTerrainMapsGPU(ComputeBuffer count, ComputeBuffer contBuffer, ComputeBuffer erosionBuffer, ComputeBuffer PVBuffer, int maxPoints, float terrainOffset, Queue<ComputeBuffer> bufferHandle)
{
    ComputeBuffer results = new ComputeBuffer(maxPoints, sizeof(float), ComputeBufferType.Structured);
    ComputeBuffer args = GraphicsGeneration.Args.CountToArgs(terrainCombinerGPU, count);
    bufferHandle.Enqueue(results);

    GraphicsGeneration.SetBuffer(terrainCombinerGPU, 0, "continental", contBuffer);
    GraphicsGeneration.SetBuffer(terrainCombinerGPU, 0, "erosion", erosionBuffer);
    GraphicsGeneration.SetBuffer(terrainCombinerGPU, 0, "peaksValleys", PVBuffer);
    GraphicsGeneration.SetBuffer(terrainCombinerGPU, 0, "Result", results);

    GraphicsGeneration.SetBuffer(terrainCombinerGPU, 0, "numOfPoints", count);
    GraphicsGeneration.SetFloat(terrainCombinerGPU, "heightOffset", terrainOffset);

    GraphicsGeneration.DispatchIndirect(terrainCombinerGPU, 0, args);

    return results;
}
*/
