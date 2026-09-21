using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Arterra.Utils;
using Arterra.Core.Storage;
using static Arterra.Core.Storage.SharedResourceManager;

namespace Arterra.Engine.Terrain.Readback {
    public class AsyncGenInfoReadback {
        private int Allocation = -1;
        private static ComputeShader GenPointRealloc;
        public GraphicsContextId GraphicsContext;
        private readonly Structure.Creator StructureCreator;

        public static void PresetData() {
            GenPointRealloc = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Readback/GenPointRealloc");
            int kernel = GenPointRealloc.FindKernel("CombineCount");
            GraphicsGeneration.SetBuffer(GenPointRealloc, kernel, "_AddressDict", GraphicsGeneration.Memory.Address);

            kernel = GenPointRealloc.FindKernel("CopyToNewAlloc");
            GraphicsGeneration.SetBuffer(GenPointRealloc, kernel, "_AddressDict", GraphicsGeneration.Memory.Address);
        }

        public AsyncGenInfoReadback(
            Structure.Creator structureCreator,
            GraphicsContextId graphicsContext = GraphicsContextId.Generation
        ) {
            GraphicsContext = graphicsContext;
            StructureCreator = structureCreator;
            Allocation = -1;
        }

        public GraphicsResourceContext GetGraphicsContext() => Graphics(GraphicsContext);

        public void Release() {
            if (Allocation <= 0) return;
            GetGraphicsContext().Memory.ReleaseMemory((uint)Allocation);
            Allocation = -1;
        }

        public int AddGenPoints(ComputeBuffer countBuffer, int countOffset, int tempCounter) {
            GraphicsResourceContext gpuContext = GetGraphicsContext();
            if (Allocation <= 0) {
                Allocation = (int)gpuContext.Memory.AllocateMemory(countBuffer, GenPoint.size, countOffset);
                return Allocation;
            }

            int kernel = GenPointRealloc.FindKernel("CombineCount");
            ComputeBuffer bufferOld = gpuContext.Memory.GetBlockBuffer(Allocation);
            gpuContext.SetBuffer(GenPointRealloc, kernel, ShaderIDProps.MemoryBuffer, bufferOld);
            gpuContext.SetBuffer(GenPointRealloc, kernel, ShaderIDProps.Counters, countBuffer);
            gpuContext.SetInt(GenPointRealloc, ShaderIDProps.BufferCounter, countOffset);
            gpuContext.SetInt(GenPointRealloc, ShaderIDProps.TempCounter, tempCounter);
            gpuContext.SetInt(GenPointRealloc, ShaderIDProps.AddressIndex, Allocation);
            gpuContext.Dispatch(GenPointRealloc, kernel, 1, 1, 1);

            int nAllocation = (int)gpuContext.Memory.AllocateMemory(countBuffer, GenPoint.size, tempCounter);
            ComputeBuffer bufferNew = gpuContext.Memory.GetBlockBuffer(nAllocation);

            kernel = GenPointRealloc.FindKernel("CopyToNewAlloc");
            gpuContext.SetBuffer(GenPointRealloc, kernel, ShaderIDProps.Counters, countBuffer);
            gpuContext.SetBuffer(GenPointRealloc, kernel, ShaderIDProps.SourceMemory, bufferOld);
            gpuContext.SetBuffer(GenPointRealloc, kernel, ShaderIDProps.DestMemory, bufferNew);
            gpuContext.SetInt(GenPointRealloc, ShaderIDProps.NewAddressIndex, nAllocation);
            ComputeBuffer args = gpuContext.Args.CountToArgs(GenPointRealloc, countBuffer, countOffset: tempCounter, kernel: kernel);
            gpuContext.DispatchIndirect(GenPointRealloc, kernel, args);

            Release(); //Release previous allocation
            Allocation = nAllocation;

            return Allocation;
        }

        public void BeginGenInfoReadback(int3 CCoord, byte cxt){
            GraphicsResourceContext gpuContext = GetGraphicsContext();
            if (Allocation <= 0) return; //No alloc exists
            uint allocation = (uint)Allocation;
            //This call ensures that readback is only called with a stable permanent allocation
            gpuContext.Memory.RegisterRebind(
                allocation,
                _1 => {
                    if (!gpuContext.Memory.GetDirectAllocation(
                        allocation, GenPoint.size, out ComputeBuffer block,
                        out _, out _, out int start, out int count)) return;
                    gpuContext.RequestAsyncReadback(block, size: count * GenPoint.size * 4,
                        offset: start * GenPoint.size * 4,
                        req => ProcessGenPoints(req.GetData<GenPoint>(), CCoord, cxt));
                }
            );
        }

        /// <summary> Flag to create entities from structure meta </summary>
        public const int CREATE_ENTITIES = 0x1;
        /// <summary> Flag to create entities from structure meta </summary>
        public const int CREATE_META = 0x2;

        private void ProcessGenPoints(NativeArray<GenPoint> points, int3 CCoord, byte cxt) {
            Release();
            foreach(GenPoint point in points) {
                switch (point.type) {
                    case GenPoint.GenType.Entity:
                        EntityManager.InitializeChunkEntity(point, CCoord, cxt);
                        break;
                    case GenPoint.GenType.StructureMeta:
                        StructureCreator.InitializeStructureMeta(point, CCoord, cxt);
                        break;
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GenPoint{
        public int3 position;
        public uint config;
        public uint index;

        public GenType type => (GenType)(config & 0xFF);
        public static int sizeRaw => sizeof(int)*3 + sizeof(uint)*2;
        public static int size => 3 + 2;

        public uint rotY => (config >> 8) & 0xFF;
        public uint rotX => (config >> 16) & 0xFF;
        public uint rotZ => (config >> 24) & 0xFF;

        public enum GenType : byte{
            Entity = 0,
            StructureMeta = 1,
        }
        public GenPoint(int3 position, uint config, uint index){
            this.position = position;
            this.config = config;
            this.index = index;
        }
    }
}
