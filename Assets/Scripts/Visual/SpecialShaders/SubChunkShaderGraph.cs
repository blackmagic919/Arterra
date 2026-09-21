using System;
using System.Collections.Generic;
using System.Linq;
using Arterra.Core.Storage;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Configuration.Quality;
using Arterra.Engine.Terrain;
using Arterra.Engine.Terrain.Readback;
using Arterra.Utils;
using Arterra.Core;
using static Arterra.Core.Storage.SharedResourceManager;

namespace Arterra.Engine.Rendering
{
    public class SubChunkShaderGraph
    {
        public TerrainChunk parent;
        public FixedOctree tree;
        private static List<GeoShader> shaders => rSettings.Categories.Reg;
        private static GeoShaderSettings rSettings => Config.CURRENT.Quality.GeoShaders.value;
        private static ComputeShader geoSizeCounter;
        private static ComputeShader filterGeometry;
        private static ComputeShader sizePrefixSum;
        private static ComputeShader geoSizeCalculator;
        private static ComputeShader geoTranscriber;
        private static ComputeShader shaderDrawArgs;
        private static ComputeShader geoInfoLoader;
        private static ComputeShader subChunkInfo;
        private static GeoShaderOffsets offsets;
        private static LogicalBlockBuffer SortedSubChunks;
        private ArterraRuntime.IndirectUpdate executor;
        private BaseGeoHandle baseHandle;
        private static int SubChunkSizeOS;
        private static int SubChunksPerAxis;
        private static int NumSubChunks => SubChunksPerAxis * SubChunksPerAxis * SubChunksPerAxis;
        const int GEO_TRI_STRIDE = 3 * 3;
        const int GEN_TRI_STRIDE = 3 * 3 + 1;
        const int TRI_STRIDE_WORD = 3;
        public static void PresetData()
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            geoSizeCounter = Resources.Load<ComputeShader>("Compute/GeoShader/ShaderMatSizeCounter");
            filterGeometry = Resources.Load<ComputeShader>("Compute/GeoShader/FilterShaderGeometry");
            sizePrefixSum = Resources.Load<ComputeShader>("Compute/GeoShader/ShaderPrefixConstructor");
            geoSizeCalculator = Resources.Load<ComputeShader>("Compute/GeoShader/GeometryMemorySize");
            geoTranscriber = Resources.Load<ComputeShader>("Compute/GeoShader/TranscribeGeometry");
            shaderDrawArgs = Resources.Load<ComputeShader>("Compute/GeoShader/GeoDrawArgs");
            geoInfoLoader = Resources.Load<ComputeShader>("Compute/GeoShader/GeoInfoLoader");
            subChunkInfo = Resources.Load<ComputeShader>("Compute/GeoShader/SubChunkInfo");

            int maxChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
            int maxSubChunkDepth = FixedOctree.GetMaxDepth(rSettings.levels.value);
            offsets = new GeoShaderOffsets(maxChunkSize, rSettings.Categories.Count(),
                maxSubChunkDepth, 0);

            SubChunksPerAxis = 1 << maxSubChunkDepth;
            SubChunkSizeOS = maxChunkSize / SubChunksPerAxis;

            Arterra.Configuration.Quality.Terrain terrain = Config.CURRENT.Quality.Terrain;
            int numChunksAxis = OctreeTerrain.BalancedOctree.GetAxisChunksDepth(rSettings.MaxGeoShaderDepth, terrain.Balance, (uint)terrain.MinChunkRadius);
            int numChunks = numChunksAxis * numChunksAxis * numChunksAxis;
            SortedSubChunks = new LogicalBlockBuffer(GraphicsBuffer.Target.Structured, numChunks * 2, sizeof(uint) * (NumSubChunks + 1));

            int kernel = geoInfoLoader.FindKernel("GetBaseSize");
            gpuContext.SetBuffer(geoInfoLoader, kernel, "counter", gpuContext.Work.Scratch);
            gpuContext.SetInt(geoInfoLoader, "bCOUNT_tri", offsets.baseGeoCounter);
            gpuContext.SetInt(geoInfoLoader, "bCOUNT_offset", offsets.baseGeoOffset);
            gpuContext.SetInt(geoInfoLoader, "triStride", TRI_STRIDE_WORD);
            kernel = geoInfoLoader.FindKernel("GetSubChunkSize");
            gpuContext.SetBuffer(geoInfoLoader, kernel, "SubChunkPrefix", SortedSubChunks.Get());
            gpuContext.SetBuffer(geoInfoLoader, kernel, "counter", gpuContext.Work.Scratch);

            kernel = subChunkInfo.FindKernel("SetSubChunkDetail");
            gpuContext.SetBuffer(subChunkInfo, kernel, "SubChunkInfo", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(subChunkInfo, kernel, "SubChunkRegions", gpuContext.Work.Transfer);
            kernel = subChunkInfo.FindKernel("CollectSubChunkSizes");
            gpuContext.SetBuffer(subChunkInfo, kernel, "SubChunkInfo", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(subChunkInfo, kernel, "SubChunkRegions", gpuContext.Work.Transfer);
            kernel = subChunkInfo.FindKernel("SetSubChunkAddress");
            gpuContext.SetBuffer(subChunkInfo, kernel, "SubChunkInfo", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(subChunkInfo, kernel, "SubChunkRegions", gpuContext.Work.Transfer);
            gpuContext.SetInt(subChunkInfo, "bSTART_sChunkI", offsets.subChunkInfoStart);
            kernel = subChunkInfo.FindKernel("ConstructPrefixSizes");
            gpuContext.SetBuffer(subChunkInfo, kernel, "SubChunkPrefix", SortedSubChunks.Get());
            gpuContext.SetInt(subChunkInfo, "numSubChunks", NumSubChunks);
            kernel = subChunkInfo.FindKernel("SetGlobalDetail");
            gpuContext.SetBuffer(subChunkInfo, kernel, "SubChunkInfo", gpuContext.Work.Scratch);

            kernel = geoSizeCounter.FindKernel("CountShaderSizes");
            gpuContext.SetBuffer(geoSizeCounter, kernel, "counter", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(geoSizeCounter, kernel, "triangleIndexOffset", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(geoSizeCounter, kernel, "shaderIndexOffset", gpuContext.Work.Scratch);
            gpuContext.SetInt(geoSizeCounter, "bSTART_scount", offsets.matSizeCStart);
            gpuContext.SetInt(geoSizeCounter, "bSTART_tri", offsets.triIndDictStart);
            gpuContext.SetInt(geoSizeCounter, "bCOUNT_base", offsets.baseGeoCounter);
            gpuContext.SetInt(geoSizeCounter, "bCOUNT_offset", offsets.baseGeoOffset);
            kernel = geoSizeCounter.FindKernel("CountSubChunkSizes");
            gpuContext.SetBuffer(geoSizeCounter, kernel, "counter", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(geoSizeCounter, kernel, "triangleIndexOffset", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(geoSizeCounter, kernel, "SubChunkPrefix", SortedSubChunks.Get());
            gpuContext.SetInt(geoSizeCounter, "sChunkSize", SubChunkSizeOS);
            gpuContext.SetInt(geoSizeCounter, "sChunksPerAxis", SubChunksPerAxis);

            gpuContext.SetBuffer(sizePrefixSum, 0, "shaderCountOffset", gpuContext.Work.Scratch);
            gpuContext.SetInt(sizePrefixSum, "bSTART_scount", offsets.matSizeCStart);

            kernel = filterGeometry.FindKernel("FilterShader");
            gpuContext.SetBuffer(filterGeometry, kernel, "filteredIndicies", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(filterGeometry, kernel, "counter", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(filterGeometry, kernel, "triangleIndexOffset", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(filterGeometry, kernel, "shaderPrefix", gpuContext.Work.Scratch);
            gpuContext.SetInt(filterGeometry, "bSTART_scount", offsets.matSizeCStart);
            gpuContext.SetInt(filterGeometry, "bSTART_tri", offsets.triIndDictStart);
            gpuContext.SetInt(filterGeometry, "bCOUNT_base", offsets.baseGeoCounter);
            gpuContext.SetInt(filterGeometry, "bCOUNT_offset", offsets.baseGeoOffset);
            gpuContext.SetInt(filterGeometry, "bSTART_sort", offsets.fBaseGeoStart);

            kernel = filterGeometry.FindKernel("FilterSubChunks");
            gpuContext.SetBuffer(filterGeometry, kernel, "filteredGeometry", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(filterGeometry, kernel, "counter", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(filterGeometry, kernel, "triangleIndexOffset", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(filterGeometry, kernel, "SubChunkPrefix", SortedSubChunks.Get());
            gpuContext.SetInt(filterGeometry, "sChunkSize", SubChunkSizeOS);
            gpuContext.SetInt(filterGeometry, "sChunksPerAxis", SubChunksPerAxis);

            kernel = geoTranscriber.FindKernel("Transcribe");
            gpuContext.SetBuffer(geoTranscriber, kernel, "DrawTriangles", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(geoTranscriber, kernel, "ShaderPrefixes", gpuContext.Work.Scratch);
            gpuContext.SetInt(geoTranscriber, "bSTART_oGeo", offsets.shadGeoStart);

            kernel = geoTranscriber.FindKernel("BatchTranscribe");
            gpuContext.SetBuffer(geoTranscriber, kernel, "DrawTriangles", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(geoTranscriber, kernel, "ShaderPrefixes", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(geoTranscriber, kernel, "SubChunkInfo", gpuContext.Work.Scratch);
            gpuContext.SetInt(geoTranscriber, "bSTART_sChunkI", offsets.subChunkInfoStart);

            kernel = geoTranscriber.FindKernel("TranscribeSortedBase");
            gpuContext.SetBuffer(geoTranscriber, kernel, "counter", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(geoTranscriber, kernel, "SortedTriangles", gpuContext.Work.Scratch);
            gpuContext.SetInt(geoTranscriber, "bCOUNT_base", offsets.baseGeoCounter);
            gpuContext.SetInt(geoTranscriber, "bSTART_sort", offsets.fBaseGeoStart);

            kernel = geoSizeCalculator.FindKernel("GetPrefixSize");
            gpuContext.SetBuffer(geoSizeCalculator, kernel, "counter", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(geoSizeCalculator, kernel, "prefixSizes", gpuContext.Work.Scratch);
            gpuContext.SetInt(geoSizeCalculator, "bCOUNT_write", offsets.baseGeoCounter);
            kernel = geoSizeCalculator.FindKernel("CountSubChunkSizes");
            gpuContext.SetBuffer(geoSizeCalculator, kernel, "prefixSizes", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(geoSizeCalculator, kernel, "DrawTriangles", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(geoSizeCalculator, kernel, "SubChunkInfo", gpuContext.Work.Scratch);
            gpuContext.SetInt(geoSizeCalculator, "bSTART_oGeo", offsets.shadGeoStart);
            gpuContext.SetInt(geoSizeCalculator, "bSTART_sChunkI", offsets.subChunkInfoStart);

            kernel = shaderDrawArgs.FindKernel("FromPrefix");
            gpuContext.SetBuffer(shaderDrawArgs, kernel, "_IndirectArgsBuffer", gpuContext.Args.DrawArgs.Get());
            kernel = shaderDrawArgs.FindKernel("FromSubChunks");
            gpuContext.SetBuffer(shaderDrawArgs, kernel, "SubChunkRegions", gpuContext.Work.Transfer);
            gpuContext.SetBuffer(shaderDrawArgs, kernel, "_IndirectArgsBuffer", gpuContext.Args.DrawArgs.Get());

            for (int i = 0; i < rSettings.Categories.Reg.Count; i++)
            {
                GeoShader shader = rSettings.Categories.Retrieve(i);
                shader.PresetData(
                    offsets.fBaseGeoStart, offsets.matSizeCStart + i,
                    offsets.baseGeoCounter, offsets.shadGeoStart, i
                );
            }
        }

        public static void PresetSubChunkInfo(ComputeShader shader)
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            gpuContext.SetInt(shader, "sChunkSize", SubChunkSizeOS);
            gpuContext.SetInt(shader, "sChunksPerAxis", SubChunksPerAxis);
            gpuContext.SetInt(shader, "bSTART_sChunkI", offsets.subChunkInfoStart);
            gpuContext.SetBuffer(shader, 0, "SubChunkInfo", gpuContext.Work.Scratch);
        }


        public void Update(MonoBehaviour _)
        {
            tree?.ForEachActiveChunk(chunk => chunk.Update()); //Update
            tree?.VerifyChunks();
        }

        public static void Unset()
        {
            SortedSubChunks.Destroy();
            foreach (GeoShader shader in rSettings.Categories.Reg)
            {
                shader.Release();
            }
        }

        public SubChunkShaderGraph(TerrainChunk parent)
        {
            this.parent = parent;
            this.tree = new FixedOctree(this, rSettings.levels);
            this.tree.Initialize(parent.origin + parent.size / 2);
        }

        public void Release()
        {
            this.tree?.ForEachChunk(chunk => chunk.Destroy());
            if (executor != null) executor.Active = false;
            if (baseHandle.IsSorted) SortedSubChunks.Release((uint)baseHandle.SortedSubCInd);
        }
        public void ReleaseGeometry()
        {
            this.tree?.ForEachChunk(chunk => chunk.ReleaseGeometry());
            if (executor != null) executor.Active = false;
            if (baseHandle.IsSorted) SortedSubChunks.Release((uint)baseHandle.SortedSubCInd);
        }

        public void ComputeGeoShaderGeometry(GeometryHandle vertHandle, GeometryHandle triHandle)
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            ReleaseGeometry(); tree?.VerifyChunks();
            this.executor = new ArterraRuntime.IndirectUpdate(Update);
            ArterraRuntime.MainLoopUpdateTasks.Enqueue(executor);

            int triAddress = (int)triHandle.addressIndex;
            int vertAddress = (int)vertHandle.addressIndex;
            this.baseHandle = new BaseGeoHandle(vertHandle, triHandle);

            ShaderSubchunk[] subchunks = tree.GetAllActiveChunks();//
            gpuContext.Work.ClearRange(gpuContext.Work.Scratch, offsets.triIndDictStart, 0);
            LoadBaseGeoInfo(gpuContext.Memory, triHandle);
            FilterGeometry(gpuContext.Memory, triAddress, vertAddress);
            SetSubChunkDetailLevel(subchunks);
            ProcessGeoShaders(gpuContext.Memory, vertAddress, triAddress);

            uint2[][] allocs = AllocateForChunkGeometry(gpuContext.Memory, subchunks);
            for (int i = 0; i < subchunks.Length; i++) {
                subchunks[i].ApplyAllocToChunk(allocs[i]);
            }
        }

        public bool RecalculateSubChunkGeoShader(ShaderSubchunk chunk)
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            if (!baseHandle.IsValid) return false;
            if (!baseHandle.IsSorted) SortBaseGeometry(gpuContext.Memory);

            int2 SCInfo = chunk.GetInfoRegion();
            int triAddress = (int)baseHandle.triangles.addressIndex;
            int vertAddress = (int)baseHandle.vertex.addressIndex;
            gpuContext.Work.ClearRange(gpuContext.Work.Scratch, offsets.triIndDictStart, 0);
            LoadBaseSubChunkGeoInfo(SCInfo, baseHandle.SortedSubCInd);
            FilterGeometry(gpuContext.Memory, triAddress, vertAddress);
            SetGlobalDetailLevel(chunk.detailLevel);
            ProcessGeoShaders(gpuContext.Memory, vertAddress, triAddress);
            uint2[] allocs = AllocateForSubChunkGeometry(gpuContext.Memory);
            chunk.ApplyAllocToChunk(allocs);
            return true;
        }

        void FilterGeometry(MemoryBufferHandler memory, int triAddress, int vertAddress)
        {
            int numShaders = shaders.Count;
            ComputeBuffer triStorage = memory.GetBlockBuffer(triAddress);
            ComputeBuffer vertStorage = memory.GetBlockBuffer(vertAddress);
            GraphicsBuffer memAddresses = memory.Address;

            CountGeometrySizes(vertStorage, triStorage, memAddresses, vertAddress, triAddress);

            ConstructPrefixSum(numShaders);

            FilterShaderGeometry(vertStorage, triStorage, memAddresses, vertAddress, triAddress);
        }

        void ProcessGeoShaders(MemoryBufferHandler memory, int vertAddress, int triAddress)
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            gpuContext.Work.ClearRange(gpuContext.Work.Scratch, 1, 0); //clear base count
            for (int i = 0; i < shaders.Count; i++)
            {
                GeoShader geoShader = shaders[i];
                geoShader.ProcessGeoShader(memory, vertAddress, triAddress, offsets.matSizeCStart + i, parent.depth);
                gpuContext.Args.CopyCount(source: gpuContext.Work.Scratch, dest: gpuContext.Work.Scratch,
                    readOffset: offsets.baseGeoCounter, writeOffset: offsets.shadGeoCStart + i + 1);
            }
        }


        uint2[][] AllocateForChunkGeometry(MemoryBufferHandler memory, ShaderSubchunk[] subChunks)
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            uint2[][] allocs = new uint2[subChunks.Length][];
            int3[] scAddrInfo = new int3[subChunks.Length];
            for (int i = 0; i < subChunks.Length; i++)
            {
                allocs[i] = new uint2[shaders.Count];
                scAddrInfo[i].xy = subChunks[i].GetInfoRegion();
            }

            for (int i = 0; i < shaders.Count; i++)
            {
                CountSubChunkGeoSizes(offsets.shadGeoCStart + i, subChunks);
                for (int j = 0; j < subChunks.Length; j++)
                {
                    allocs[j][i].x = memory.AllocateMemory(
                        gpuContext.Work.Transfer,
                        GEO_TRI_STRIDE,
                        j * 3 + 2
                    );
                    scAddrInfo[j].z = (int)allocs[j][i].x;
                    allocs[j][i].y = gpuContext.Args.DrawArgs.Allocate();
                    SetSubChunkDrawArgs((int)allocs[j][i].y, j);
                }
                BatchTranscribe(memory.Storage, memory.Address,
                    scAddrInfo, offsets.shadGeoCStart + i);
            }
            return allocs;
        }

        public uint2[] AllocateForSubChunkGeometry(MemoryBufferHandler memory)
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            int numShaders = shaders.Count;

            uint2[] allocs = new uint2[numShaders];
            GraphicsBuffer addressesReference = memory.Address;

            for (int i = 0; i < numShaders; i++)
            {
                CopyGeoCount(offsets.shadGeoCStart + i);
                allocs[i].x = memory.AllocateMemory(gpuContext.Work.Scratch, GEO_TRI_STRIDE, offsets.baseGeoCounter);
                ComputeBuffer memoryReference = memory.GetBlockBuffer(allocs[i].x);
                TranscribeGeometry(memoryReference, addressesReference, (int)allocs[i].x, offsets.shadGeoCStart + i);
                allocs[i].y = gpuContext.Args.DrawArgs.Allocate();
                GetDrawArgs((int)allocs[i].y, offsets.shadGeoCStart + i);
            }
            return allocs;
        }


        private void LoadBaseGeoInfo(MemoryBufferHandler memory, GeometryHandle triHandle)
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            ComputeBuffer triStorage = memory.GetBlockBuffer(triHandle.addressIndex);
            GraphicsBuffer addresses = memory.Address;

            int kernel = geoInfoLoader.FindKernel("GetBaseSize");
            gpuContext.SetBuffer(geoInfoLoader, kernel, ShaderIDProps.MemoryBuffer, triStorage);
            gpuContext.SetBuffer(geoInfoLoader, kernel, ShaderIDProps.AddressDict, addresses);
            gpuContext.SetInt(geoInfoLoader, ShaderIDProps.TriAddress, (int)triHandle.addressIndex);
            gpuContext.Dispatch(geoInfoLoader, kernel, 1, 1, 1);
        }

        private void LoadBaseSubChunkGeoInfo(int2 SCInfo, int prefixStart)
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            int stride = NumSubChunks + 1;
            int kernel = geoInfoLoader.FindKernel("GetSubChunkSize");
            gpuContext.SetInt(geoInfoLoader, ShaderIDProps.StartSChunkP, prefixStart * stride);
            gpuContext.SetInt(geoInfoLoader, ShaderIDProps.SCStart, SCInfo.x);
            gpuContext.SetInt(geoInfoLoader, ShaderIDProps.SCEnd, SCInfo.y);
            gpuContext.Dispatch(geoInfoLoader, kernel, 1, 1, 1);
        }

        private static void CountGeometrySizes(ComputeBuffer vertMemory, ComputeBuffer triMemory, GraphicsBuffer addresses, int vertAddress, int triAddress)
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            int kernel = geoSizeCounter.FindKernel("CountShaderSizes");
            ComputeBuffer args = gpuContext.Args.CountToArgs(geoSizeCounter, gpuContext.Work.Scratch, offsets.baseGeoCounter, kernel);
            gpuContext.SetBuffer(geoSizeCounter, kernel, ShaderIDProps.Vertices, vertMemory);
            gpuContext.SetBuffer(geoSizeCounter, kernel, ShaderIDProps.Triangles, triMemory);
            gpuContext.SetBuffer(geoSizeCounter, kernel, ShaderIDProps.AddressDict, addresses);
            gpuContext.SetInt(geoSizeCounter, ShaderIDProps.VertAddress, vertAddress);
            gpuContext.SetInt(geoSizeCounter, ShaderIDProps.TriAddress, triAddress);

            gpuContext.DispatchIndirect(geoSizeCounter, kernel, args);
        }

        private static void ConstructPrefixSum(int numShaders)
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            gpuContext.SetInt(sizePrefixSum, "numShaders", numShaders);
            gpuContext.Dispatch(sizePrefixSum, 0, 1, 1, 1);
        }

        private static void FilterShaderGeometry(ComputeBuffer vertMemory, ComputeBuffer triMemory, GraphicsBuffer addresses, int vertAddress, int triAddress)
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            ComputeBuffer args = gpuContext.Args.CountToArgs(filterGeometry, gpuContext.Work.Scratch, offsets.baseGeoCounter);

            int kernel = filterGeometry.FindKernel("FilterShader");
            gpuContext.SetBuffer(filterGeometry, kernel, ShaderIDProps.Vertices, vertMemory);
            gpuContext.SetBuffer(filterGeometry, kernel, ShaderIDProps.Triangles, triMemory);
            gpuContext.SetBuffer(filterGeometry, kernel, ShaderIDProps.AddressDict, addresses);
            gpuContext.SetInt(filterGeometry, ShaderIDProps.VertAddress, vertAddress);
            gpuContext.SetInt(filterGeometry, ShaderIDProps.TriAddress, triAddress);

            gpuContext.DispatchIndirect(filterGeometry, kernel, args);
        }


        void CountSubChunkGeoSizes(int shadGeoCount, ShaderSubchunk[] subChunks)
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            gpuContext.Work.ClearRange(gpuContext.Work.Scratch, NumSubChunks, offsets.subChunkInfoStart);
            CopyGeoCount(shadGeoCount);

            int kernel = geoSizeCalculator.FindKernel("CountSubChunkSizes");
            ComputeBuffer args = gpuContext.Args.CountToArgs(geoSizeCalculator, gpuContext.Work.Scratch, offsets.baseGeoCounter, kernel);
            gpuContext.DispatchIndirect(geoSizeCalculator, kernel, args);

            kernel = subChunkInfo.FindKernel("CollectSubChunkSizes");
            gpuContext.SetInt(subChunkInfo, ShaderIDProps.NumSubChunkRegions, subChunks.Length);
            subChunkInfo.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
            int numThreadsAxis = (int)math.ceil((double)subChunks.Length / threadGroupSize);
            gpuContext.Dispatch(subChunkInfo, kernel, numThreadsAxis, 1, 1);
        }

        void CopyGeoCount(int shadGeoCount)
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            int kernel = geoSizeCalculator.FindKernel("GetPrefixSize");
            gpuContext.SetInt(geoSizeCalculator, ShaderIDProps.CountOGeo, shadGeoCount);
            gpuContext.Dispatch(geoSizeCalculator, kernel, 1, 1, 1);
        }

        void TranscribeGeometry(ComputeBuffer memory, GraphicsBuffer addresses, int addressIndex, int geoSizeCounter)
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            ComputeBuffer args = gpuContext.Args.CountToArgs(geoTranscriber, gpuContext.Work.Scratch, offsets.baseGeoCounter);

            int kernel = geoTranscriber.FindKernel("Transcribe");
            gpuContext.SetBuffer(geoTranscriber, kernel, ShaderIDProps.MemoryBuffer, memory);
            gpuContext.SetBuffer(geoTranscriber, kernel, ShaderIDProps.AddressDict, addresses);
            gpuContext.SetInt(geoTranscriber, ShaderIDProps.AddressIndex, addressIndex);
            gpuContext.SetInt(geoTranscriber, ShaderIDProps.CountOGeo, geoSizeCounter);
            gpuContext.DispatchIndirect(geoTranscriber, kernel, args);
        }

        void BatchTranscribe(ComputeBuffer memory, GraphicsBuffer addresses, int3[] SCAddressRegions, int shadGeoCount)
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            gpuContext.SetBufferData(gpuContext.Work.Transfer, SCAddressRegions);
            int kernel = subChunkInfo.FindKernel("SetSubChunkAddress");
            //This is safe ONLY if immediately using it after allocation
            gpuContext.SetBuffer(subChunkInfo, kernel, ShaderIDProps.AddressDict, addresses);
            gpuContext.SetInt(subChunkInfo, ShaderIDProps.NumSubChunkRegions, SCAddressRegions.Length);
            subChunkInfo.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
            int numThreadsAxis = (int)math.ceil((double)SCAddressRegions.Length / threadGroupSize);
            gpuContext.Dispatch(subChunkInfo, kernel, numThreadsAxis, 1, 1);

            kernel = geoTranscriber.FindKernel("BatchTranscribe");
            ComputeBuffer args = gpuContext.Args.CountToArgs(geoTranscriber, gpuContext.Work.Scratch, offsets.baseGeoCounter, kernel);
            gpuContext.SetBuffer(geoTranscriber, kernel, ShaderIDProps.MemoryBuffer, memory);
            gpuContext.SetInt(geoTranscriber, ShaderIDProps.CountOGeo, shadGeoCount);
            gpuContext.DispatchIndirect(geoTranscriber, kernel, args);
        }

        void SetSubChunkDetailLevel(ShaderSubchunk[] subchunks)
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            int3[] detailLevels = subchunks.Select(subchunk => new int3(
                subchunk.GetInfoRegion(), subchunk.detailLevel)).ToArray();
            gpuContext.SetBufferData(gpuContext.Work.Transfer, detailLevels);
            int kernel = subChunkInfo.FindKernel("SetSubChunkDetail");
            gpuContext.SetInt(subChunkInfo, ShaderIDProps.NumSubChunkRegions, subchunks.Length);
            subChunkInfo.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
            int numThreadsAxis = (int)math.ceil((double)subchunks.Length / threadGroupSize);
            gpuContext.Dispatch(subChunkInfo, kernel, numThreadsAxis, 1, 1);
        }

        void SetGlobalDetailLevel(int detailLevel)
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            int kernel = subChunkInfo.FindKernel("SetGlobalDetail");
            gpuContext.SetInt(subChunkInfo, ShaderIDProps.DetailLevel, detailLevel);
            subChunkInfo.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
            int numThreadsAxis = (int)math.ceil((double)NumSubChunks / threadGroupSize);
            gpuContext.Dispatch(subChunkInfo, kernel, numThreadsAxis, 1, 1);
        }

        void SetSubChunkDrawArgs(int address, int subChunkInd)
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            int kernel = shaderDrawArgs.FindKernel("FromSubChunks");
            gpuContext.SetInt(shaderDrawArgs, ShaderIDProps.ArgOffset, address);
            gpuContext.SetInt(shaderDrawArgs, ShaderIDProps.SubChunkInd, subChunkInd);
            gpuContext.Dispatch(shaderDrawArgs, kernel, 1, 1, 1);
        }

        void GetDrawArgs(int address, int geoSizeCounter)
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            int kernel = shaderDrawArgs.FindKernel("FromPrefix");
            gpuContext.SetBuffer(shaderDrawArgs, kernel, "prefixSizes", gpuContext.Work.Scratch);
            gpuContext.SetInt(shaderDrawArgs, ShaderIDProps.CountOGeo, geoSizeCounter);
            gpuContext.SetInt(shaderDrawArgs, ShaderIDProps.ArgOffset, address);
            gpuContext.Dispatch(shaderDrawArgs, kernel, 1, 1, 1);
        }

        void SortBaseGeometry(MemoryBufferHandler memory)
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            int stride = NumSubChunks + 1;
            baseHandle.SortedSubCInd = (int)SortedSubChunks.Allocate();
            int start = baseHandle.SortedSubCInd * stride;
            ComputeBuffer triStorage = memory.GetBlockBuffer(baseHandle.triangles.addressIndex);
            ComputeBuffer vertStorage = memory.GetBlockBuffer(baseHandle.vertex.addressIndex);
            GraphicsBuffer memAddresses = memory.Address;
            int triAddress = (int)baseHandle.triangles.addressIndex;
            int vertAddress = (int)baseHandle.vertex.addressIndex;

            LoadBaseGeoInfo(memory, baseHandle.triangles);
            gpuContext.Work.ClearRange(SortedSubChunks.Get(), stride, start);

            int kernel = geoSizeCounter.FindKernel("CountSubChunkSizes");
            ComputeBuffer args = gpuContext.Args.CountToArgs(geoSizeCounter, gpuContext.Work.Scratch, offsets.baseGeoCounter, kernel);
            gpuContext.SetBuffer(geoSizeCounter, kernel, ShaderIDProps.Vertices, vertStorage);
            gpuContext.SetBuffer(geoSizeCounter, kernel, ShaderIDProps.Triangles, triStorage);
            gpuContext.SetBuffer(geoSizeCounter, kernel, ShaderIDProps.AddressDict, memAddresses);
            gpuContext.SetInt(geoSizeCounter, ShaderIDProps.StartSChunkP, start);
            gpuContext.SetInt(geoSizeCounter, ShaderIDProps.VertAddress, vertAddress);
            gpuContext.SetInt(geoSizeCounter, ShaderIDProps.TriAddress, triAddress);
            gpuContext.DispatchIndirect(geoSizeCounter, kernel, args);

            kernel = subChunkInfo.FindKernel("ConstructPrefixSizes");
            gpuContext.SetInt(subChunkInfo, ShaderIDProps.StartSChunkP, start);
            gpuContext.Dispatch(subChunkInfo, kernel, 1, 1, 1);

            kernel = filterGeometry.FindKernel("FilterSubChunks");
            gpuContext.SetBuffer(filterGeometry, kernel, ShaderIDProps.Vertices, vertStorage);
            gpuContext.SetBuffer(filterGeometry, kernel, ShaderIDProps.Triangles, triStorage);
            gpuContext.SetBuffer(filterGeometry, kernel, ShaderIDProps.AddressDict, memAddresses);
            gpuContext.SetInt(filterGeometry, ShaderIDProps.VertAddress, vertAddress);
            gpuContext.SetInt(filterGeometry, ShaderIDProps.TriAddress, triAddress);
            gpuContext.SetInt(filterGeometry, ShaderIDProps.StartSChunkP, start);
            args = gpuContext.Args.CountToArgs(filterGeometry, gpuContext.Work.Scratch, offsets.baseGeoCounter, kernel);
            gpuContext.DispatchIndirect(filterGeometry, kernel, args);

            kernel = geoTranscriber.FindKernel("TranscribeSortedBase");
            gpuContext.SetBuffer(geoTranscriber, kernel, ShaderIDProps.MemoryBufferBase, triStorage);
            gpuContext.SetBuffer(geoTranscriber, kernel, ShaderIDProps.AddressDict, memAddresses);
            gpuContext.SetInt(geoTranscriber, ShaderIDProps.AddressIndex, triAddress);
            args = gpuContext.Args.CountToArgs(geoTranscriber, gpuContext.Work.Scratch, offsets.baseGeoCounter, kernel);
            gpuContext.DispatchIndirect(geoTranscriber, kernel, args);
        }

        public class FixedOctree : Octree<ShaderSubchunk>
        {
            /// <summary> The last tracked position of the viewer in
            /// chunk space. This value is only updated when the viewer's
            /// position exceeds the viewDistUpdate threshold. </summary>
            public int3 ViewerPosGS;
            private List<GeoShaderSettings.DetailLevel> Details;
            private WeakReference<SubChunkShaderGraph> Graph;
            public static int GetMaxDepth(List<GeoShaderSettings.DetailLevel> detailLevels)
            {
                int divisions = 0;
                foreach (var level in detailLevels)
                    if (level.IncreaseSize) divisions++;
                return divisions;
            }

            private static int GetMaxNumChunks(int depth)
            {
                int length = 1 << depth;
                return length * length * length;
            }
            private static int GetMinChunkSize(int depth, int rootChunkSize)
            {
                return rootChunkSize / (1 << depth);
            }
            public FixedOctree(SubChunkShaderGraph graph, List<GeoShaderSettings.DetailLevel> detailLevels) :
                base(GetMaxDepth(detailLevels),
                    GetMinChunkSize(GetMaxDepth(detailLevels), graph.parent.size),
                    GetMaxNumChunks(GetMaxDepth(detailLevels)))
            {
                this.Graph = new WeakReference<SubChunkShaderGraph>(graph);
                this.Details = detailLevels;
            }
            public void Initialize(int3 center)
            {
                ViewerPosGS = (int3)math.round(CPUMapManager.WSToGS(OctreeTerrain.viewer.position));
                base.Initialize(1, center);
            }

            public void VerifyChunks()
            {
                int3 ViewerPosition = (int3)math.round(CPUMapManager.WSToGS(OctreeTerrain.viewer.position));
                if (math.distance(ViewerPosGS, ViewerPosition) < rSettings.SubchunkUpdateThresh) return;
                ViewerPosGS = ViewerPosition;

                Queue<ShaderSubchunk> frameChunks = new Queue<ShaderSubchunk>();
                ForEachChunk(chunk => frameChunks.Enqueue(chunk));
                while (frameChunks.Count > 0)
                {
                    ShaderSubchunk chunk = frameChunks.Dequeue();
                    if (!chunk.active) continue;
                    chunk.VerifyChunk();
                }
            }

            public override bool IsBalanced(ref Node node)
            {
                int accDist = 0; int chunkSize = MinChunkSize;
                int viewerDist = node.GetMaxDist(ViewerPosGS);
                for (int i = 0; i < Details.Count; i++)
                {
                    accDist += Details[i].Distance;
                    if (viewerDist < accDist)
                        return node.size <= chunkSize;
                    if (Details[i].IncreaseSize)
                        chunkSize *= 2;
                }
                return true;
            }

            protected override bool RemapRoot(uint node) => true;
            protected override void AddTerrainChunk(uint octreeIndex)
            {
                if (!Graph.TryGetTarget(out SubChunkShaderGraph g)) return;
                ref Node node = ref nodes[octreeIndex];
                ShaderSubchunk nChunk = new ShaderSubchunk(g, shaders, node.origin, (int)node.size, octreeIndex);
                node.Chunk = chunks.Enqueue(nChunk);
                node.IsComplete = false;
            }
        }

        /* Gen Buffer Organization
        [ baseGeoCounter(4b) | matSizeCounters(20b) |  shadGeoCounters(20b) | subchunkLUT(256b) | triIndDict(5.25 mb) | filteredBaseGeo(5.25mb) | GeoShaderGeometry(rest of buffer)]
        */
        public struct GeoShaderOffsets : BufferOffsets
        {
            public int baseGeoCounter;
            public int baseGeoOffset;
            public int matSizeCStart;
            public int shadGeoCStart;
            public int triIndDictStart;
            public int subChunkInfoStart;
            public int fBaseGeoStart;
            public int shadGeoStart;
            private int offsetStart; private int offsetEnd;
            /// <summary> The start of the buffer region that is used by the GeoShader generator.
            /// See <see cref="BufferOffsets.bufferStart"/> for more info. </summary>
            public int bufferStart { get { return offsetStart; } }
            /// <summary> The end of the buffer region that is used by the GeoShader generator.
            /// See <see cref="BufferOffsets.bufferEnd"/> for more info. </summary>
            public int bufferEnd { get { return offsetEnd; } }
            public GeoShaderOffsets(int maxChunkSize, int maxShaderCount, int maxSubChunkDepth, int bufferStart)
            {
                int numPointsPerAxis = maxChunkSize + 1;
                int numOfTris = (numPointsPerAxis - 1) * (numPointsPerAxis - 1) * (numPointsPerAxis - 1) * 5;
                int subChunksPerAxis = (1 << maxSubChunkDepth);
                int numSubChunks = subChunksPerAxis * subChunksPerAxis * subChunksPerAxis;

                this.offsetStart = bufferStart;
                baseGeoCounter = bufferStart;
                baseGeoOffset = 1 + baseGeoCounter;
                matSizeCStart = 1 + baseGeoOffset;
                shadGeoCStart = (maxShaderCount + 1) + matSizeCStart;
                subChunkInfoStart = (maxShaderCount + 1) + shadGeoCStart; ;
                triIndDictStart = numSubChunks + subChunkInfoStart;
                fBaseGeoStart = numOfTris + triIndDictStart;
                shadGeoStart = Mathf.CeilToInt(((float)numOfTris + fBaseGeoStart) / GEN_TRI_STRIDE);
                this.offsetEnd = (shadGeoStart + numOfTris) * GEN_TRI_STRIDE;
            }
        }

        private struct BaseGeoHandle
        {
            public GeometryHandle vertex;
            public GeometryHandle triangles;
            public int SortedSubCInd;
            public bool IsSorted => SortedSubCInd != -1;
            public bool IsValid => vertex.Active && triangles.Active;

            public BaseGeoHandle(GeometryHandle v, GeometryHandle t)
            {
                this.vertex = v;
                this.triangles = t;
                SortedSubCInd = -1;
            }
        }
    }
}
