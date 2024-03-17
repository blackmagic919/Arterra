using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using static EndlessTerrain;

public class ShaderGenerator : UpdateTask
{
    private GeneratorSettings settings;

    private Transform transform;

    //                          Color                  Position & Normal        UV
    const int GEO_VERTEX_STRIDE = ((sizeof(float) * 4) + (sizeof(float) * 3 * 2) + sizeof(float) * 2);
    const int MESH_TRIANGLES_STRIDE = (sizeof(float) * 3 * 2 + sizeof(int) * (2 + 1)) * 3;

    private Bounds shaderBounds;

    private uint[] geoShaderMemAdds = null;
    private uint[] geoShaderDispArgs = null;
    private Material[] geoShaderMats = null;

    Queue<ComputeBuffer> tempBuffers = new Queue<ComputeBuffer>();

    public ShaderGenerator(GeneratorSettings settings, Transform transform, Bounds boundsOS)
    {
        this.settings = settings;
        this.transform = transform;
        this.shaderBounds = TransformBounds(transform, boundsOS);
    }

    public Bounds TransformBounds(Transform transform, Bounds boundsOS)
    {
        var center = transform.TransformPoint(boundsOS.center);

        var size = boundsOS.size;
        var axisX = transform.TransformVector(size.x, 0, 0);
        var axisY = transform.TransformVector(0, size.y, 0);
        var axisZ = transform.TransformVector(0, 0, size.z);

        size.x = Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x);
        size.y = Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y);
        size.z = Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z);

        return new Bounds(center, size);
    }

    public override void Update()
    {
        ReleaseTempBuffers();
        
        ComputeBuffer sharedArgs = UtilityBuffers.ArgumentBuffer;
        for(uint i = 0; i < this.settings.shaderDictionary.Count; i++) {
            Graphics.DrawProceduralIndirect(geoShaderMats[i], shaderBounds, MeshTopology.Triangles, sharedArgs, (int)geoShaderDispArgs[i] * (4*4), null, null, ShadowCastingMode.Off, true, 0);
        }
    }

    public void ReleaseGeometry()
    {
        if (!initialized)
            return;
        initialized = false;

        foreach (Material geoMat in geoShaderMats)
            if (Application.isPlaying) UnityEngine.Object.Destroy(geoMat); else  UnityEngine.Object.DestroyImmediate(geoMat);

        foreach (uint address in geoShaderMemAdds) {
            this.settings.memoryBuffer.ReleaseMemory(address);
        }
        foreach (uint address in geoShaderDispArgs) {
            UtilityBuffers.ReleaseArgs(address);
        }
    }

    private void ReleaseTempBuffers()
    {
        while(tempBuffers.Count > 0)
        {
            tempBuffers.Dequeue().Release();
        }
    }

    public void ComputeGeoShaderGeometry(ComputeBuffer geometry, int chunkSize, int LOD)
    { 
        ReleaseGeometry();

        ComputeBuffer filteredGeometry; ComputeBuffer shaderBaseIndexes;
        (filteredGeometry, shaderBaseIndexes) = FilterGeometry(geometry, chunkSize, LOD);

        ComputeBuffer geoShaderGeometry; ComputeBuffer shaderStartIndexes;
        (geoShaderGeometry, shaderStartIndexes) = ProcessGeoShaders(filteredGeometry, shaderBaseIndexes, chunkSize, LOD);

        this.geoShaderMemAdds = TranscribeGeometries(geoShaderGeometry, shaderStartIndexes);
        this.geoShaderDispArgs = GetShaderDrawArgs(shaderStartIndexes);
        this.geoShaderMats = SetupShaderMaterials(this.settings.memoryBuffer.AccessStorage(), this.settings.memoryBuffer.AccessAddresses(), this.geoShaderMemAdds);

        if(!enqueued) MainLoopUpdateTasks.Enqueue(this);
        this.enqueued = true;
        this.initialized = true;

        ReleaseTempBuffers();
    }


    public (ComputeBuffer, ComputeBuffer) FilterGeometry(ComputeBuffer geometry, int chunkSize, int LOD)
    {
        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfTris = (numPointsAxes - 1) * (numPointsAxes - 1) * (numPointsAxes - 1) * 5;

        int numShaders = this.settings.shaderDictionary.Count;

        ComputeBuffer baseCount = CopyCount(geometry, ref tempBuffers);

        ComputeBuffer shaderBaseIndexes = new ComputeBuffer(numShaders + 1, sizeof(uint), ComputeBufferType.Structured);
        shaderBaseIndexes.SetData(Enumerable.Repeat(0u, numShaders + 1).ToArray());
        tempBuffers.Enqueue(shaderBaseIndexes);

        ComputeBuffer geometryOffset = CountGeometrySizes(geometry, baseCount, numOfTris, ref shaderBaseIndexes, ref tempBuffers);
        tempBuffers.Enqueue(geometryOffset);

        ConstructPrefixSum(numShaders, ref shaderBaseIndexes);

        ComputeBuffer filteredGeometry = new ComputeBuffer(numOfTris, MESH_TRIANGLES_STRIDE, ComputeBufferType.Structured);
        tempBuffers.Enqueue(filteredGeometry);

        FilterShaderGeometry(geometry, shaderBaseIndexes, geometryOffset, baseCount, ref filteredGeometry, ref tempBuffers);

        return (filteredGeometry, shaderBaseIndexes);
    }

    public (ComputeBuffer, ComputeBuffer) ProcessGeoShaders(ComputeBuffer filteredGeometry, ComputeBuffer shaderBaseIndexes, int chunkSize, int LOD)
    {
        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfTris = (numPointsAxes - 1) * (numPointsAxes - 1) * (numPointsAxes - 1) * 5;

        ComputeBuffer geoShaderGeometry = new ComputeBuffer(numOfTris * this.settings.maxVertexPerBase, GEO_VERTEX_STRIDE, ComputeBufferType.Append);
        geoShaderGeometry.SetCounterValue(0);
        tempBuffers.Enqueue(geoShaderGeometry);

        ComputeBuffer shaderStartIndexes = new ComputeBuffer(this.settings.shaderDictionary.Count + 1, sizeof(int), ComputeBufferType.Structured);
        ComputeBuffer.CopyCount(geoShaderGeometry, shaderStartIndexes, 0);
        tempBuffers.Enqueue(shaderStartIndexes);

        for (int i = 0; i < this.settings.shaderDictionary.Count; i++){
            SpecialShader geoShader = this.settings.shaderDictionary[i];

            geoShader.ProcessGeoShader(transform, filteredGeometry, shaderBaseIndexes, geoShaderGeometry, i);
            ComputeBuffer.CopyCount(geoShaderGeometry, shaderStartIndexes, (i+1) * sizeof(int));
            geoShader.ReleaseTempBuffers();
        }

        return (geoShaderGeometry, shaderStartIndexes);
    }

    public uint[] TranscribeGeometries(ComputeBuffer geoShaderGeometry, ComputeBuffer shaderStartIndexes)
    {
        int numShaders = this.settings.shaderDictionary.Count;

        uint[] geoShaderAddresses = new uint[numShaders];
        ComputeBuffer memoryReference = settings.memoryBuffer.AccessStorage();
        ComputeBuffer addressesReference = settings.memoryBuffer.AccessAddresses();

        for (int i = 0; i < numShaders; i++)
        {
            ComputeBuffer memByte4Length = CalculateGeoSize(shaderStartIndexes, (GEO_VERTEX_STRIDE * 3) / 4, i);
            geoShaderAddresses[i] = settings.memoryBuffer.AllocateMemory(memByte4Length);

            ComputeBuffer geoShaderArgs = CalculateArgsFromPrefix(shaderStartIndexes, i);

            TranscribeGeometry(memoryReference, addressesReference, (int)geoShaderAddresses[i], geoShaderGeometry, shaderStartIndexes, geoShaderArgs, i);
        }

        return geoShaderAddresses;
    }

    public Material[] SetupShaderMaterials(ComputeBuffer storageBuffer, ComputeBuffer addressBuffer, uint[] address)
    {
        Material[] shaderMaterials = settings.GetMaterialInstances();
        for(int i = 0; i < this.settings.shaderDictionary.Count; i++)
        {
            Material shaderMaterial = shaderMaterials[i];
            shaderMaterial.SetBuffer("_StorageMemory", storageBuffer);
            shaderMaterial.SetBuffer("_AddressDict", addressBuffer);
            shaderMaterial.SetInt("addressIndex", (int)address[i]);
            shaderMaterial.SetInt("_Vertex4ByteStride", GEO_VERTEX_STRIDE / 4);
        }
        return shaderMaterials;
    }

    public uint[] GetShaderDrawArgs(ComputeBuffer shaderStartIndexes)
    {
        int numShaders = this.settings.shaderDictionary.Count;
        uint[] shaderDrawArgs = new uint[numShaders];

        for (int i = 0; i < numShaders; i++)
        {
            shaderDrawArgs[i] = UtilityBuffers.AllocateArgs();
            GetDrawArgs(UtilityBuffers.ArgumentBuffer, (int)shaderDrawArgs[i], shaderStartIndexes, i);
        }
        return shaderDrawArgs;
    }

    void GetDrawArgs(ComputeBuffer indirectArgs, int address, ComputeBuffer shaderPrefix, int shaderIndex)
    {
        this.settings.shaderDrawArgs.SetBuffer(0, "prefixSizes", shaderPrefix);
        this.settings.shaderDrawArgs.SetInt("shaderIndex", shaderIndex);

        this.settings.shaderDrawArgs.SetBuffer(0, "_IndirectArgsBuffer", indirectArgs);
        this.settings.shaderDrawArgs.SetInt("argOffset", address);

        this.settings.shaderDrawArgs.Dispatch(0, 1, 1, 1);
    }

    ComputeBuffer CalculateGeoSize(ComputeBuffer shaderStartIndexes, int stride4Bytes, int shaderIndex)
    {
        ComputeBuffer shaderByteLength = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
        tempBuffers.Enqueue(shaderByteLength);

        this.settings.geoSizeCalculator.SetBuffer(0, "prefixSizes", shaderStartIndexes);
        this.settings.geoSizeCalculator.SetInt("geoIndex", shaderIndex);
        this.settings.geoSizeCalculator.SetInt("stride4Bytes", stride4Bytes);

        this.settings.geoSizeCalculator.SetBuffer(0, "byteLength", shaderByteLength);

        this.settings.geoSizeCalculator.Dispatch(0, 1, 1, 1);
        return shaderByteLength;
    }

    void ConstructPrefixSum(int numShaders, ref ComputeBuffer sizes)
    {
        this.settings.sizePrefixSum.SetBuffer(0, "sizes", sizes);
        this.settings.sizePrefixSum.SetInt("numShaders", numShaders);

        this.settings.sizePrefixSum.Dispatch(0, 1, 1, 1);
    }

    void FilterShaderGeometry(ComputeBuffer geometry, ComputeBuffer shaderPrefix, ComputeBuffer geoOffsets, ComputeBuffer count, ref ComputeBuffer filteredGeometry, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer args = SetArgs(this.settings.filterGeometry, geometry, ref bufferHandle);

        this.settings.filterGeometry.SetBuffer(0, "baseGeometry", geometry);
        this.settings.filterGeometry.SetBuffer(0, "triangleIndexOffset", geoOffsets);
        this.settings.filterGeometry.SetBuffer(0, "shaderPrefix", shaderPrefix);
        this.settings.filterGeometry.SetBuffer(0, "numTriangles", count);

        this.settings.filterGeometry.SetBuffer(0, "filteredGeometry", filteredGeometry);
        this.settings.filterGeometry.DispatchIndirect(0, args);
    }

    ComputeBuffer CalculateArgsFromPrefix(ComputeBuffer shaderStartIndexes, int shaderIndex)
    {
        ComputeBuffer geoShaderArgs = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.Structured);
        geoShaderArgs.SetData(new uint[] { 1, 1, 1 });
        tempBuffers.Enqueue(geoShaderArgs);

        this.settings.geoTranscriber.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);

        this.settings.prefixShaderArgs.SetBuffer(0, "_PrefixStart", shaderStartIndexes);
        this.settings.prefixShaderArgs.SetInt("shaderIndex", shaderIndex);
        this.settings.prefixShaderArgs.SetInt("threadGroupSize", (int)threadGroupSize);

        this.settings.prefixShaderArgs.SetBuffer(0, "indirectArgs", geoShaderArgs);

        this.settings.prefixShaderArgs.Dispatch(0, 1, 1, 1);
        return geoShaderArgs;
    }

    void TranscribeGeometry(ComputeBuffer memory, ComputeBuffer addresses, int addressIndex, ComputeBuffer geoShaderGeometry, ComputeBuffer shaderStartIndexes, ComputeBuffer args, int shaderIndex)
    {
        this.settings.geoTranscriber.SetBuffer(0, "_DrawTriangles", geoShaderGeometry);
        this.settings.geoTranscriber.SetBuffer(0, "_ShaderPrefixes", shaderStartIndexes);
        this.settings.geoTranscriber.SetBuffer(0, "_MemoryBuffer", memory);
        this.settings.geoTranscriber.SetBuffer(0, "_AddressDict", addresses);
        this.settings.geoTranscriber.SetInt("addressIndex", addressIndex);
        this.settings.geoTranscriber.SetInt("shaderIndex", shaderIndex);

        this.settings.geoTranscriber.DispatchIndirect(0, args);
    }

    ComputeBuffer CountGeometrySizes(ComputeBuffer geometry, ComputeBuffer count, int numTris, ref ComputeBuffer geometrySize, ref Queue<ComputeBuffer> bufferHandle)
    {

        ComputeBuffer geometryOffset = new ComputeBuffer(numTris, sizeof(uint), ComputeBufferType.Structured);
        bufferHandle.Enqueue(geometryOffset);

        ComputeBuffer args = SetArgs(this.settings.matSizeCounter, geometry, ref bufferHandle);

        this.settings.matSizeCounter.SetBuffer(0, "baseGeometry", geometry);
        this.settings.matSizeCounter.SetBuffer(0, "sizes", geometrySize);
        this.settings.matSizeCounter.SetBuffer(0, "triangleIndexOffset", geometryOffset);
        this.settings.matSizeCounter.SetBuffer(0, "numTriangles", count);

        this.settings.matSizeCounter.DispatchIndirect(0, args);

        return geometryOffset;
    }

    ComputeBuffer CopyCount(ComputeBuffer data, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer count = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
        bufferHandle.Enqueue(count);

        count.SetData(new uint[] { 0 });
        ComputeBuffer.CopyCount(data, count, 0);

        return count;
    }

    ComputeBuffer SetArgs(ComputeShader shader, ComputeBuffer data, ref Queue<ComputeBuffer> bufferHandle)
    {
        uint threadGroupSize; shader.GetKernelThreadGroupSizes(0, out threadGroupSize, out _, out _);
        return SetArgs(data, (int)threadGroupSize, ref bufferHandle);
    }

    ComputeBuffer SetArgs(ComputeBuffer data, int threadGroupSize, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer args = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.IndirectArguments);
        bufferHandle.Enqueue(args);

        args.SetData(new int[] { 1, 1, 1 });
        ComputeBuffer.CopyCount(data, args, 0);

        this.settings.indirectThreads.SetBuffer(0, "args", args);
        this.settings.indirectThreads.SetInt("numThreads", threadGroupSize);

        this.settings.indirectThreads.Dispatch(0, 1, 1, 1);

        return args;
    }

    
    struct Triangle
    {
        #pragma warning disable 649
        public Point a;
        public Point b;
        public Point c;

        public struct Point
        {
            public Vector3 pos;
            public Vector3 norm;
            public Vector2 uv;
            public Vector4 color;
        }

        public Point this[int i] //courtesy of sebastian laugue, this is pretty smart
        {
            get
            {
                switch (i)
                {
                    case 0:
                        return a;
                    case 1:
                        return b;
                    default:
                        return c;
                }
            }
        }
    }

    /*
     
    public MeshInfo ReadBackMesh()
    {
        MeshInfo chunk = new MeshInfo();

        int numShaders = this.settings.shaderDictionary.Count;

        int[] geoLengths = new int[numShaders + 1];
        shaderStartIndexes.GetData(geoLengths);

        if (geoLengths[numShaders - 1] == 0)
            return chunk;

        int fullLength = geoLengths[numShaders];
        Triangle[] geometry = new Triangle[fullLength];
        geoShaderGeometry.GetData(geometry);
        for (int s = 0; s < numShaders; s++)
        {
            for (int i = geoLengths[s]; i < geoLengths[s+1]; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    chunk.triangles.Add(3*i + j);
                    chunk.vertices.Add(geometry[i][j].pos);
                    chunk.UVs.Add(geometry[i][j].uv);
                    chunk.normals.Add(geometry[i][j].norm);
                    chunk.colorMap.Add(new Color(geometry[i][j].color.x, geometry[i][j].color.y, geometry[i][j].color.z, geometry[i][j].color.w));
                }
            }

            chunk.subMeshes.Add(new UnityEngine.Rendering.SubMeshDescriptor(geoLengths[s] * 3, (geoLengths[s + 1] - geoLengths[s]) * 3, MeshTopology.Triangles));
        }

        return chunk;
    }
     */


}
