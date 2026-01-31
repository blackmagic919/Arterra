using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Arterra.Utils;

namespace Arterra.Engine.Terrain.Readback {
    public class AsyncGenInfoReadback {
        private int Allocation = -1;
        private static ComputeShader GenPointRealloc;

        public static void PresetData() {
            GenPointRealloc = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Readback/GenPointRealloc");

            int kernel = GenPointRealloc.FindKernel("CombineCount");
            GenPointRealloc.SetBuffer(kernel, "_AddressDict", GenerationPreset.memoryHandle.Address);
            
            kernel = GenPointRealloc.FindKernel("CopyToNewAlloc");
            GenPointRealloc.SetBuffer(kernel, "_AddressDict", GenerationPreset.memoryHandle.Address);
        }

        public AsyncGenInfoReadback() {
            Allocation = -1;
        }

        public void Release() {
            if (Allocation <= 0) return;
            GenerationPreset.memoryHandle.ReleaseMemory((uint)Allocation);
            Allocation = -1;
        }

        public int AddGenPoints(ComputeBuffer countBuffer, int countOffset, int tempCounter) {
            if (Allocation <= 0) {
                Allocation = (int)GenerationPreset.memoryHandle.AllocateMemory(countBuffer, GenPoint.size, countOffset);
                return Allocation;
            } 

            int kernel = GenPointRealloc.FindKernel("CombineCount"); 
            ComputeBuffer bufferOld = GenerationPreset.memoryHandle.GetBlockBuffer(Allocation);
            GenPointRealloc.SetBuffer(kernel, ShaderIDProps.MemoryBuffer, bufferOld);
            GenPointRealloc.SetBuffer(kernel, ShaderIDProps.Counters, countBuffer);
            GenPointRealloc.SetInt(ShaderIDProps.BufferCounter, countOffset);
            GenPointRealloc.SetInt(ShaderIDProps.TempCounter, tempCounter);
            GenPointRealloc.SetInt(ShaderIDProps.AddressIndex, Allocation);
            GenPointRealloc.Dispatch(kernel, 1, 1, 1);

            int nAllocation = (int)GenerationPreset.memoryHandle.AllocateMemory(countBuffer, GenPoint.size, tempCounter);
            ComputeBuffer bufferNew = GenerationPreset.memoryHandle.GetBlockBuffer(nAllocation);

            kernel = GenPointRealloc.FindKernel("CopyToNewAlloc");
            GenPointRealloc.SetBuffer(kernel, ShaderIDProps.Counters, countBuffer);
            GenPointRealloc.SetBuffer(kernel, ShaderIDProps.SourceMemory, bufferOld);
            GenPointRealloc.SetBuffer(kernel, ShaderIDProps.DestMemory, bufferNew);
            GenPointRealloc.SetInt(ShaderIDProps.NewAddressIndex, nAllocation);
            ComputeBuffer args = UtilityBuffers.CountToArgs(GenPointRealloc, countBuffer, countOffset: tempCounter, kernel: kernel);
            GenPointRealloc.DispatchIndirect(kernel, args);

            Release(); //Release previous allocation
            Allocation = nAllocation;
            
            return Allocation;
        }

        public void BeginGenInfoReadback(int3 CCoord, byte cxt){
            void OnRegionSizeRecieved(AsyncGPUReadbackRequest request, uint2 memHandle){
                if (!GenerationPreset.memoryHandle.GetBlockBufferSafe(Allocation, out ComputeBuffer block))
                    return;
                int memSize = (int)(request.GetData<uint>()[0] - GenPoint.size);
                int entityStartWord = GenPoint.size * (int)memHandle.y;
                AsyncGPUReadback.Request(block, size: memSize * 4, offset: 4 * entityStartWord, (req) => ProcessGenPoints(req.GetData<GenPoint>(), CCoord, cxt));
            }
            void OnRegionAddressRecieved(AsyncGPUReadbackRequest request){
                if (!GenerationPreset.memoryHandle.GetBlockBufferSafe(Allocation, out ComputeBuffer block))
                    return;
                uint2 memHandle = request.GetData<uint2>()[0];
                if(memHandle.x == 0) {
                    return; // No entities
                }

                AsyncGPUReadback.Request(block, size: 4, offset: 4 * ((int)memHandle.x - 1), (req) => OnRegionSizeRecieved(req, memHandle));
            }

            if (Allocation <= 0) return; //No alloc exists
            AsyncGPUReadback.Request(GenerationPreset.memoryHandle.Address, size: 8, offset: (int)(8 * Allocation), (req) => OnRegionAddressRecieved(req));
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
                        Structure.Generator.InitializeStructureMeta(point, CCoord, cxt);
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