using System.Collections.Generic;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Engine.Terrain;
using Arterra.Utils;

namespace Arterra.Core.Storage {
    public class ArgBuffers {
        public ComputeBuffer IndirectArgs;
        public ComputeBuffer AppendCount;
        public LogicalBlockBuffer DrawArgs;
        public static ComputeShader indirectCountToArgs;
        public static ComputeShader prefixCountToArgs;
        public static ComputeShader indirectCopy;
        private GraphicsContextId context;
        const int _MaxArgsCount = (int)5E4;
        const int ARGS_STRIDE_4BYTES = 4;
        private bool active;

        public static void Initialize() {
            indirectCopy = Resources.Load<ComputeShader>("Compute/Utility/Copy");
            indirectCountToArgs = Resources.Load<ComputeShader>("Compute/Utility/CountToArgs");
            prefixCountToArgs = Resources.Load<ComputeShader>("Compute/Utility/PrefixArgsCreator");
        }

        public ArgBuffers(GraphicsContextId context) {
            IndirectArgs = new ComputeBuffer(3, sizeof(int), ComputeBufferType.IndirectArguments);
            AppendCount = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
            DrawArgs = new LogicalBlockBuffer(GraphicsBuffer.Target.IndirectArguments, _MaxArgsCount + 1, sizeof(uint) * ARGS_STRIDE_4BYTES);
            this.context = context;
            active = true;
        }

        public void Release() {
            if (!active) return;
            active = false;

            IndirectArgs?.Release();
            AppendCount?.Release();
            DrawArgs?.Destroy();
        }

        public ComputeBuffer CopyCount(ComputeBuffer source, ComputeBuffer dest = null, int readOffset = 0, int writeOffset = 0) {
            ComputeBuffer count;

            if (dest != null) {
                count = dest;
            } else count = AppendCount;
            GraphicsResourceContext ctx = SharedResourceManager.Graphics(context);

            int kernel = indirectCopy.FindKernel("CopyCount");
            ctx.SetBuffer(indirectCopy, kernel, ShaderIDProps.Source, source);
            ctx.SetInt(indirectCopy, ShaderIDProps.ReadOffset, readOffset);
            ctx.SetBuffer(indirectCopy, kernel, ShaderIDProps.Destination, count);
            ctx.SetInt(indirectCopy, ShaderIDProps.WriteOffset, writeOffset);
            ctx.Dispatch(indirectCopy, kernel, 1, 1, 1);

            return count;
        }

        public ComputeBuffer CountToArgs(ComputeShader shader, ComputeBuffer count, int countOffset = 0, int kernel = 0) {
            shader.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
            return CountToArgs((int)threadGroupSize, count, countOffset);
        }

        public ComputeBuffer CountToArgs(int threadGroupSize, ComputeBuffer count, int countOffset = 0) {
            ComputeBuffer args = IndirectArgs;
            GraphicsResourceContext ctx = SharedResourceManager.Graphics(context);

            ctx.SetBuffer(indirectCountToArgs, 0, ShaderIDProps.Count, count);
            ctx.SetBuffer(indirectCountToArgs, 0, ShaderIDProps.Args, args);
            ctx.SetInt(indirectCountToArgs, ShaderIDProps.NumThreads, (int)threadGroupSize);
            ctx.SetInt(indirectCountToArgs, ShaderIDProps.CountOffset, countOffset);
            ctx.Dispatch(indirectCountToArgs, 0, 1, 1, 1);

            return args;
        }

        public ComputeBuffer PrefixCountToArgs(ComputeShader shader, ComputeBuffer count, int countOffset = 0, Queue<ComputeBuffer> bufferQueue = null) {
            shader.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
            return PrefixCountToArgs((int)threadGroupSize, count, countOffset, bufferQueue);
        }
        public ComputeBuffer PrefixCountToArgs(int threadGroupSize, ComputeBuffer count, int countOffset = 0, Queue<ComputeBuffer> bufferQueue = null) {
            ComputeBuffer args = IndirectArgs;
            if (bufferQueue != null) {
                args = new ComputeBuffer(3, sizeof(int), ComputeBufferType.IndirectArguments);
                bufferQueue.Enqueue(args);
            }

            GraphicsResourceContext ctx = SharedResourceManager.Graphics(context);
            ctx.SetBuffer(prefixCountToArgs, 0, ShaderIDProps.Count, count);
            ctx.SetBuffer(prefixCountToArgs, 0, ShaderIDProps.Args, args);
            ctx.SetInt(prefixCountToArgs, ShaderIDProps.NumThreads, (int)threadGroupSize);
            ctx.SetInt(prefixCountToArgs, ShaderIDProps.CountOffset, countOffset);
            ctx.Dispatch(prefixCountToArgs, 0, 1, 1, 1);

            return args;
        }
    }

    public class WorkBuffers {
        public static ComputeShader indirectCopy;
        public static ComputeShader clearRange;

        //First 16 words reserved for metadata, apply padding as per data member
        public ComputeBuffer Scratch;
        public ComputeBuffer Transfer;
        const int GEN_BYTE_SIZE = 200000000; //200MB
        public GraphicsContextId context;
        private bool active;


        public static void Initialize() {
            indirectCopy = Resources.Load<ComputeShader>("Compute/Utility/Copy");
            clearRange = Resources.Load<ComputeShader>("Compute/Utility/ClearCounters");
        }

        public WorkBuffers(GraphicsContextId context) {
            int mapChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
            int maxPoints = (mapChunkSize + 2) * (mapChunkSize + 2) * (mapChunkSize + 2);
            //This buffer will contain all temporary data during generation
            Scratch = new ComputeBuffer(GEN_BYTE_SIZE / 4, 4, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
            //This buffer will be slower but will be written to a lot by CPU
            Transfer = new ComputeBuffer(maxPoints * 2, 4, ComputeBufferType.Structured, ComputeBufferMode.Dynamic);
            this.context = context;
            active = true;
        }

        public void Release() {
            if (!active) return;
            active = false;

            Scratch?.Release();
            Transfer?.Release();
        }

        public void ClearRange(ComputeBuffer buffer, int length, int start) {
            GraphicsResourceContext ctx = SharedResourceManager.Graphics(context);
            ctx.SetBuffer(clearRange, 0, ShaderIDProps.Counters, buffer);
            ctx.SetInt(clearRange, ShaderIDProps.Length, length);
            ctx.SetInt(clearRange, ShaderIDProps.Start, start);
            clearRange.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
            int threadGroups = Mathf.CeilToInt(length / (float)threadGroupSize);
            ctx.Dispatch(clearRange, 0, threadGroups, 1, 1);
        }

        public void ClearRange(GraphicsBuffer buffer, int length, int start) {
            GraphicsResourceContext ctx = SharedResourceManager.Graphics(context);
            ctx.SetBuffer(clearRange, 0, ShaderIDProps.Counters, buffer);
            ctx.SetInt(clearRange, ShaderIDProps.Length, length);
            ctx.SetInt(clearRange, ShaderIDProps.Start, start);
            clearRange.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
            int threadGroups = Mathf.CeilToInt(length / (float)threadGroupSize);
            ctx.Dispatch(clearRange, 0, threadGroups, 1, 1);
        }

        /// <summary> Copies a region, delineated by a counter and buffer start from one part of the
        /// Generation buffer to another. </summary>
        /// <param name="sourceCounter">The offset in 4byte words of the source counter</param>
        /// <param name="sourceStart">The offset in stride units of the start of the source</param>
        /// <param name="destCounter">The offset in 4byte words of the destination counter</param>
        /// <param name="destStart">The offset in stride units of the start of the destination</param>
        /// <param name="stride">The size of one counting unit in 4byte units</param>
        /// <param name="overlaps">Whether or not the destination and source can overlap and are in the same buffer</param>
        public void CopyBufferRegion(int sourceCounter, int sourceStart, int destCounter, int destStart, int stride = 1, bool overlaps = true) {
            sourceStart *= stride; destStart *= stride;
            GraphicsResourceContext ctx = SharedResourceManager.Graphics(context);
            if (overlaps) {
                uint tempAddr = ctx.Memory.AllocateMemory(Scratch, stride, sourceCounter);
                ComputeBuffer tempStorage = ctx.Memory.GetBlockBuffer(tempAddr);
                GraphicsBuffer addressDict = ctx.Memory.Address;

                CopyIndirectToStorage(tempStorage, addressDict, tempAddr, sourceStart, sourceCounter, stride);
                CopyIndirectFromStorage(tempStorage, addressDict, tempAddr, destStart, sourceCounter, stride);
                ctx.Args.CopyCount(Scratch, Scratch, sourceCounter, destCounter);
                ctx.Memory.ReleaseMemory(tempAddr);
            } else {
                CopyBufferIndirect(Scratch, Scratch, sourceStart, destStart, sourceCounter, stride: stride);
                ctx.Args.CopyCount(Scratch, Scratch, sourceCounter, destCounter);
            }
        }

        private void CopyIndirectToStorage(ComputeBuffer destStorage, GraphicsBuffer addressDict, uint addrIndex, int readOffset, int countOffset, int stride) {
            int kernel = indirectCopy.FindKernel("CopyToStorage");
            GraphicsResourceContext ctx = SharedResourceManager.Graphics(context);
            ctx.SetBuffer(indirectCopy, kernel, ShaderIDProps.Source, Scratch);
            ctx.SetInt(indirectCopy, ShaderIDProps.ReadOffset, readOffset);
            ctx.SetInt(indirectCopy, ShaderIDProps.Count, countOffset);
            ctx.SetInt(indirectCopy, ShaderIDProps.Stride, stride);
            ctx.SetBuffer(indirectCopy, kernel, ShaderIDProps.DestMemory, destStorage);
            ctx.SetBuffer(indirectCopy, kernel, ShaderIDProps.AddressDict, addressDict);
            ctx.SetInt(indirectCopy, ShaderIDProps.AddressIndex, (int)addrIndex);

            ComputeBuffer args = ctx.Args.CountToArgs(
                indirectCopy, Scratch, countOffset, kernel: kernel);
            ctx.DispatchIndirect(indirectCopy, kernel, args);
        }

        public void CopyToStorage(uint addressIndex, int readOffset, int count, int stride = 1) {
            if (count <= 0) return;

            GraphicsResourceContext ctx = SharedResourceManager.Graphics(context);
            int kernel = indirectCopy.FindKernel("CopyDirectToStorage");
            ctx.SetBuffer(indirectCopy, kernel, ShaderIDProps.Source, Scratch);
            ctx.SetInt(indirectCopy, ShaderIDProps.ReadOffset, readOffset * stride);
            ctx.SetInt(indirectCopy, ShaderIDProps.Count, count);
            ctx.SetInt(indirectCopy, ShaderIDProps.Stride, stride);
            ctx.SetBuffer(indirectCopy, kernel, ShaderIDProps.DestMemory,
                ctx.Memory.GetBlockBuffer(addressIndex));
            ctx.SetBuffer(indirectCopy, kernel, ShaderIDProps.AddressDict,
                ctx.Memory.Address);
            ctx.SetInt(indirectCopy, ShaderIDProps.AddressIndex, (int)addressIndex);

            indirectCopy.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize,
                out _, out _);
            int threadGroups = Mathf.CeilToInt((count * stride) /
                (float)threadGroupSize);
            ctx.Dispatch(indirectCopy, kernel, threadGroups, 1, 1);
        }

        private void CopyIndirectFromStorage(ComputeBuffer sourceStorage, GraphicsBuffer addressDict, uint addrIndex, int writeOffset, int countOffset, int stride) {
            int kernel = indirectCopy.FindKernel("CopyFromStorage");
            GraphicsResourceContext ctx = SharedResourceManager.Graphics(context);
            ctx.SetBuffer(indirectCopy, kernel, ShaderIDProps.Source, Scratch);
            ctx.SetInt(indirectCopy, ShaderIDProps.Count, countOffset);
            ctx.SetInt(indirectCopy, ShaderIDProps.Stride, stride);
            ctx.SetBuffer(indirectCopy, kernel, ShaderIDProps.SourceMemory, sourceStorage);
            ctx.SetBuffer(indirectCopy, kernel, ShaderIDProps.AddressDict, addressDict);
            ctx.SetInt(indirectCopy, ShaderIDProps.AddressIndex, (int)addrIndex);
            ctx.SetBuffer(indirectCopy, kernel, ShaderIDProps.Destination, Scratch);
            ctx.SetInt(indirectCopy, ShaderIDProps.WriteOffset, writeOffset);

            ComputeBuffer args = ctx.Args.CountToArgs(
                indirectCopy, Scratch, countOffset, kernel: kernel);
            ctx.DispatchIndirect(indirectCopy, kernel, args);
        }

        // Offsets are words; count * stride is the number of words copied.
        public bool CopyBuffer(ComputeBuffer source, ComputeBuffer dest, int readOffset = 0, int writeOffset = 0, int count = 0, int stride = 1, GraphicsResourceContext graphicsContext = null) {
            if (count <= 0 || stride <= 0 || readOffset < 0 || writeOffset < 0) return false;
            if (source == null || dest == null) return false;
            long words = (long)count * stride;
            if ((long)source.count * source.stride / 4 < readOffset + words) return false;
            if ((long)dest.count * dest.stride / 4 < writeOffset + words) return false;

            int kernel = indirectCopy.FindKernel("CopyBuffer");
            GraphicsResourceContext ctx = graphicsContext ?? SharedResourceManager.Graphics(context);
            ctx.SetBuffer(indirectCopy, kernel, ShaderIDProps.Source, source);
            ctx.SetInt(indirectCopy, ShaderIDProps.ReadOffset, readOffset);
            ctx.SetBuffer(indirectCopy, kernel, ShaderIDProps.Destination, dest);
            ctx.SetInt(indirectCopy, ShaderIDProps.WriteOffset, writeOffset);
            ctx.SetInt(indirectCopy, ShaderIDProps.Count, count);
            ctx.SetInt(indirectCopy, ShaderIDProps.Stride, stride);

            indirectCopy.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
            int threadGroups = Mathf.CeilToInt(words / (float)threadGroupSize);
            ctx.Dispatch(indirectCopy, kernel, threadGroups, 1, 1);
            return true;
        }

        public bool CopyBuffer(GraphicsBuffer source, ComputeBuffer dest, int readOffset = 0, int writeOffset = 0, int count = 0, int stride = 1, GraphicsResourceContext graphicsContext = null) {
            if (count <= 0 || stride <= 0 || readOffset < 0 || writeOffset < 0) return false;
            if (source == null || dest == null) return false;
            long words = (long)count * stride;
            if ((long)source.count * source.stride / 4 < readOffset + words) return false;
            if ((long)dest.count * dest.stride / 4 < writeOffset + words) return false;

            int kernel = indirectCopy.FindKernel("CopyBuffer");
            GraphicsResourceContext ctx = graphicsContext ?? SharedResourceManager.Graphics(context);
            ctx.SetBuffer(indirectCopy, kernel, ShaderIDProps.Source, source);
            ctx.SetInt(indirectCopy, ShaderIDProps.ReadOffset, readOffset);
            ctx.SetBuffer(indirectCopy, kernel, ShaderIDProps.Destination, dest);
            ctx.SetInt(indirectCopy, ShaderIDProps.WriteOffset, writeOffset);
            ctx.SetInt(indirectCopy, ShaderIDProps.Count, count);
            ctx.SetInt(indirectCopy, ShaderIDProps.Stride, stride);

            indirectCopy.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
            int threadGroups = Mathf.CeilToInt(words / (float)threadGroupSize);
            ctx.Dispatch(indirectCopy, kernel, threadGroups, 1, 1);
            return true;
        }

        public bool CopyBufferIndirect(ComputeBuffer source, ComputeBuffer dest, int readOffset = 0, int writeOffset = 0, int countOffset = 0, int stride = 1) {
            if (countOffset < 0) return false;
            if (source == null || dest == null) return false;

            int kernel = indirectCopy.FindKernel("CopyIndirect");
            GraphicsResourceContext ctx = SharedResourceManager.Graphics(context);
            ctx.SetBuffer(indirectCopy, kernel, ShaderIDProps.Source, source);
            ctx.SetInt(indirectCopy, ShaderIDProps.ReadOffset, readOffset);
            ctx.SetBuffer(indirectCopy, kernel, ShaderIDProps.Destination, dest);
            ctx.SetInt(indirectCopy, ShaderIDProps.WriteOffset, writeOffset);
            ctx.SetInt(indirectCopy, ShaderIDProps.Count, countOffset);
            ctx.SetInt(indirectCopy, ShaderIDProps.Stride, stride);

            ComputeBuffer args = ctx.Args.CountToArgs(
                indirectCopy, source, countOffset, kernel: kernel);
            ctx.DispatchIndirect(indirectCopy, kernel, args);
            return true;
        }



        public void SetSampleData(ComputeShader noiseGen, Vector3 offset, int meshSkipInc) {
            GraphicsResourceContext ctx = SharedResourceManager.Graphics(context);
            ctx.SetFloats(noiseGen, ShaderIDProps.SampleOffset, new float[] { offset.x, offset.y, offset.z });
            ctx.SetInt(noiseGen, ShaderIDProps.SkipInc, meshSkipInc);
        }
    }

    public interface BufferOffsets {
        public int bufferEnd { get; }
        public int bufferStart { get; }
    }
}
