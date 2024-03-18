using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using static UtilityBuffers;

public static class DensityGenerator
{
    [Header("Terrain Generation Shaders")]
    static ComputeShader terrainNoiseCompute;//
    static ComputeShader rawNoiseSampler;//
    static ComputeShader materialGenCompute;//
    static ComputeShader meshGenerator;//
    static ComputeShader densitySimplification;//
    

    static DensityGenerator(){ //That's a lot of Compute Shaders XD
        terrainNoiseCompute = Resources.Load<ComputeShader>("TerrainGeneration/BaseGeneration/TerrainGenerator");
        materialGenCompute = Resources.Load<ComputeShader>("TerrainGeneration/BaseGeneration/MaterialGenerator");
        meshGenerator = Resources.Load<ComputeShader>("TerrainGeneration/BaseGeneration/MarchingCubes");
        densitySimplification = Resources.Load<ComputeShader>("TerrainGeneration/BaseGeneration/DensitySimplificator");
        rawNoiseSampler = Resources.Load<ComputeShader>("TerrainGeneration/SurfaceChunk/FullNoiseSampler");

        indirectThreads = Resources.Load<ComputeShader>("Utility/DivideByThreads");
        indirectCountToArgs = Resources.Load<ComputeShader>("Utility/CountToArgs");
    }
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
    }

    public static void SimplifyDensity(int chunkSize, int meshSkipInc, float[] density, ComputeBuffer pointBuffer, ref Queue<ComputeBuffer> bufferHandle)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int totalPointsAxes = chunkSize + 1;
        int totalPoints = totalPointsAxes * totalPointsAxes * totalPointsAxes;
        ComputeBuffer completeDensity = new ComputeBuffer(totalPoints, sizeof(float), ComputeBufferType.Structured, ComputeBufferMode.Immutable);
        completeDensity.SetData(density);
        bufferHandle.Enqueue(completeDensity);

        densitySimplification.DisableKeyword("USE_INT");
        densitySimplification.SetInt("meshSkipInc", meshSkipInc);
        densitySimplification.SetInt("totalPointsPerAxis", totalPointsAxes);
        densitySimplification.SetInt("pointsPerAxis", numPointsAxes);
        densitySimplification.SetBuffer(0, "points_full", completeDensity);
        densitySimplification.SetBuffer(0, "points", pointBuffer);

        densitySimplification.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

        densitySimplification.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

       
    public static ComputeBuffer GenerateMat( SurfaceChunk.SurfData surfaceData, ComputeBuffer densityBuffer, int coarseSampler, int fineSampler,
                                    float IsoLevel, int chunkSize, int meshSkipInc, Vector3 offset, ref Queue<ComputeBuffer> bufferHandle)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numPoints = numPointsAxes * numPointsAxes * numPointsAxes;

        ComputeBuffer materialBuffer = new ComputeBuffer(numPoints, sizeof(int));

        bufferHandle.Enqueue(materialBuffer);

        materialGenCompute.SetBuffer(0, "densityMap", densityBuffer);
        materialGenCompute.SetFloat("IsoLevel", IsoLevel);

        materialGenCompute.SetBuffer(0, "_SurfMemoryBuffer", surfaceData.Memory);
        materialGenCompute.SetBuffer(0, "_SurfAddressDict", surfaceData.Addresses);
        materialGenCompute.SetInt("surfAddress", (int)surfaceData.addressIndex);

        materialGenCompute.SetBuffer(0, "materialMap", materialBuffer);//Result
        materialGenCompute.SetInt("numPointsPerAxis", numPointsAxes);

        materialGenCompute.SetFloat("meshSkipInc", meshSkipInc);
        materialGenCompute.SetFloat("chunkSize", chunkSize);
        materialGenCompute.SetFloat("offsetY", offset.y);

        materialGenCompute.SetInt("fineSampler", fineSampler);
        materialGenCompute.SetInt("coarseSampler", coarseSampler);
        SetSampleData(materialGenCompute, offset, chunkSize, meshSkipInc);
        
        materialGenCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);
        materialGenCompute.Dispatch(0, numThreadsAxis, numThreadsAxis, numThreadsAxis);

        return materialBuffer;
    }

    public static void GenerateMesh(GPUDensityManager densityManager, Vector3 CCoord, int chunkSize, int meshSkipInc, float IsoLevel, ComputeBuffer triangleBuffer)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numCubesAxes = chunkSize / meshSkipInc;
        meshGenerator.SetBuffer(0, "_MemoryBuffer", densityManager.AccessStorage());
        meshGenerator.SetBuffer(0, "_AddressDict", densityManager.AccessAddresses());
        meshGenerator.SetInts("CCoord", new int[] { (int)CCoord.x, (int)CCoord.y, (int)CCoord.z });
        densityManager.SetCCoordHash(meshGenerator);

        meshGenerator.SetFloat("IsoLevel", IsoLevel);
        meshGenerator.SetInt("numPointsPerAxis", numPointsAxes);
        meshGenerator.SetInt("numCubesPerAxis", numCubesAxes);
        meshGenerator.SetFloat("ResizeFactor", meshSkipInc);
        meshGenerator.SetBuffer(0, "triangles", triangleBuffer);

        meshGenerator.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numCubesAxes / (float)threadGroupSize);

        meshGenerator.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }
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
    }

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
