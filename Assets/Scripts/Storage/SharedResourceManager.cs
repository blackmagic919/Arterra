using Arterra.Configuration;
using Arterra.Configuration.Quality;
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arterra.Core.Storage {
    public static class SharedResourceManager {
        public static GraphicsResourceContext[] GraphicsContexts;
        public static GraphicsResourceContext GraphicsGeneration;
        public static GraphicsResourceContext GraphicsRendering;

        public static void Initialize(bool minimal = false) {
            WorkBuffers.Initialize();
            ArgBuffers.Initialize();

            GraphicsRendering = new GraphicsResourceContext(GraphicsContextId.Rendering, autoFlush: minimal);
            if (SystemInfo.supportsAsyncCompute && !minimal) {
                GraphicsGeneration = new GraphicsResourceContext(GraphicsContextId.Generation, asyncCompute: true);
            } else GraphicsGeneration = GraphicsRendering;
            GraphicsContexts = new GraphicsResourceContext[2] {
                GraphicsGeneration,
                GraphicsRendering,
            };
        }

        public static void Release() {
            foreach(var context in GraphicsContexts)
                context.Release();
        }

        public static GraphicsResourceContext Graphics(GraphicsContextId id) => GraphicsContexts[(int)id];
    }

    public enum GraphicsContextId {
        None = -1,
        Generation = 0,
        Rendering = 1,
    }

    public class GraphicsResourceContext {
        public readonly MemoryOccupancyBalancer Memory;
        public readonly WorkBuffers Work;
        public readonly ArgBuffers Args;
        public readonly CommandBuffer Commands;
        public readonly GraphicsContextId id;
        private readonly bool asyncCompute;
        private readonly bool autoFlush;
        private LinkedList<FencePoll> Polls;
        private readonly HashSet<ComputeBuffer> readbackSnapshots = new();
        private bool active;

        public GraphicsResourceContext(
            MemoryOccupancyBalancer graphicsMemory,
            WorkBuffers graphicsWork,
            ArgBuffers graphicsArgs,
            CommandBuffer commands,
            GraphicsContextId id
        ) {
            Memory = graphicsMemory;
            Work = graphicsWork;
            Args = graphicsArgs;
            Commands = commands;
            Polls = new LinkedList<FencePoll>();
            this.id = id;
            active = true;
        }

        public GraphicsResourceContext(GraphicsContextId id, bool asyncCompute = false, bool autoFlush = false) {
            Commands = CommandBufferPool.Get();
            this.asyncCompute = asyncCompute;
            this.autoFlush = autoFlush;
            ClearCommands();
            this.id = id;
            active = true;
            Polls = new LinkedList<FencePoll>();
            Work = new WorkBuffers(id);
            Args = new ArgBuffers(id);
            Memory = new MemoryOccupancyBalancer(Config.CURRENT.Quality.Memory.value, this);
        }

        // Clear() resets execution flags too, so re-apply after every clear.
        public void ClearCommands() {
            Commands.Clear();
            if (asyncCompute) Commands.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);
        }

        // Immediately submits any recorded commands when no runtime loop is around to pick them up.
        private void FlushIfAuto() {
            if (!autoFlush || Commands.sizeInBytes == 0) return;
            Graphics.ExecuteCommandBuffer(Commands);
            ClearCommands();
        }


        public void Release() {
            if (!active) return;
            active = false;

            foreach (ComputeBuffer snapshot in readbackSnapshots) snapshot.Release();
            readbackSnapshots.Clear();
            Memory.Release();
            Work.Release();
            Args.Release();
            Polls = null; //GC reasons
            CommandBufferPool.Release(Commands);
        }

        public void AnswerPolls() {
            if (!active) return;
            if (!active || Polls == null || Polls.Count == 0)
                return;

            List<FencePoll> completed = null;
            LinkedListNode<FencePoll> current = Polls.First;
            while (current != null) {
                LinkedListNode<FencePoll> next = current.Next;
                FencePoll poll = current.Value;

                if (poll.Passed) {
                    completed ??= new List<FencePoll>();
                    completed.Add(poll);
                    Polls.Remove(current);
                }

                current = next;
            }

            if (completed == null)
                return;

            foreach (FencePoll poll in completed) {
                if (!active)
                    return;
                poll.OnPassed.Invoke(poll.context);
            }
        }

        public void PollGraphicsContext(GraphicsResourceContext thread, Action<GraphicsResourceContext> callback) {
            if (thread.id == id) {
                callback.Invoke(thread);
                return;
            }

            GraphicsFence fence = thread.Commands.CreateGraphicsFence(
                GraphicsFenceType.AsyncQueueSynchronisation,
                SynchronisationStageFlags.AllGPUOperations
            ); 
            FencePoll poll = new FencePoll(fence, thread, callback);
            Polls.AddLast(poll);
        }

        public void AwaitOnGraphicsContext(GraphicsResourceContext thread) {
            if (thread.id == id) return; //Don't await on yourself
            GraphicsFence fence = thread.Commands.CreateGraphicsFence(
                GraphicsFenceType.AsyncQueueSynchronisation,
                SynchronisationStageFlags.AllGPUOperations
            ); 
            Commands.WaitOnAsyncGraphicsFence(fence);
        }

        public void Dispatch(ComputeShader shader, int kernelIndex, int threadGroupsX, int threadGroupsY, int threadGroupsZ) {
            if (autoFlush) shader.Dispatch(kernelIndex, threadGroupsX, threadGroupsY, threadGroupsZ);
            else Commands.DispatchCompute(shader, kernelIndex, threadGroupsX, threadGroupsY, threadGroupsZ);
        }

        public void DispatchIndirect(ComputeShader shader, int kernelIndex, ComputeBuffer argsBuffer, uint argsOffset = 0u) {
            if (autoFlush) shader.DispatchIndirect(kernelIndex, argsBuffer, argsOffset);
            else Commands.DispatchCompute(shader, kernelIndex, argsBuffer, argsOffset);
        }

        public void DispatchIndirect(ComputeShader shader, int kernelIndex, GraphicsBuffer argsBuffer, uint argsOffset = 0u) {
            if (autoFlush) shader.DispatchIndirect(kernelIndex, argsBuffer, argsOffset);
            else Commands.DispatchCompute(shader, kernelIndex, argsBuffer, argsOffset);
        }

        public void SetBool(ComputeShader shader, string name, bool value) => SetInt(shader, name, value ? 1 : 0);
        public void SetBool(ComputeShader shader, int nameID, bool value) => SetInt(shader, nameID, value ? 1 : 0);

        public void SetInt(ComputeShader shader, string name, int value) {
            if (autoFlush) shader.SetInt(name, value);
            else Commands.SetComputeIntParam(shader, name, value);
        }

        public void SetInt(ComputeShader shader, int nameID, int value) {
            if (autoFlush) shader.SetInt(nameID, value);
            else Commands.SetComputeIntParam(shader, nameID, value);
        }

        public void SetInts(ComputeShader shader, string name, params int[] values) {
            if (autoFlush) shader.SetInts(name, values);
            else Commands.SetComputeIntParams(shader, name, values);
        }

        public void SetInts(ComputeShader shader, int nameID, params int[] values) {
            if (autoFlush) shader.SetInts(nameID, values);
            else Commands.SetComputeIntParams(shader, nameID, values);
        }

        public void SetFloat(ComputeShader shader, string name, float value) {
            if (autoFlush) shader.SetFloat(name, value);
            else Commands.SetComputeFloatParam(shader, name, value);
        }

        public void SetFloat(ComputeShader shader, int nameID, float value) {
            if (autoFlush) shader.SetFloat(nameID, value);
            else Commands.SetComputeFloatParam(shader, nameID, value);
        }

        public void SetFloats(ComputeShader shader, string name, params float[] values) {
            if (autoFlush) shader.SetFloats(name, values);
            else Commands.SetComputeFloatParams(shader, name, values);
        }

        public void SetFloats(ComputeShader shader, int nameID, params float[] values) {
            if (autoFlush) shader.SetFloats(nameID, values);
            else Commands.SetComputeFloatParams(shader, nameID, values);
        }

        public void SetVector(ComputeShader shader, string name, Vector4 value) {
            if (autoFlush) shader.SetVector(name, value);
            else Commands.SetComputeVectorParam(shader, name, value);
        }

        public void SetVector(ComputeShader shader, int nameID, Vector4 value) {
            if (autoFlush) shader.SetVector(nameID, value);
            else Commands.SetComputeVectorParam(shader, nameID, value);
        }

        public void SetVectorArray(ComputeShader shader, string name, Vector4[] values) {
            if (autoFlush) shader.SetVectorArray(name, values);
            else Commands.SetComputeVectorArrayParam(shader, name, values);
        }

        public void SetVectorArray(ComputeShader shader, int nameID, Vector4[] values) {
            if (autoFlush) shader.SetVectorArray(nameID, values);
            else Commands.SetComputeVectorArrayParam(shader, nameID, values);
        }

        public void SetMatrix(ComputeShader shader, string name, Matrix4x4 value) {
            if (autoFlush) shader.SetMatrix(name, value);
            else Commands.SetComputeMatrixParam(shader, name, value);
        }

        public void SetMatrix(ComputeShader shader, int nameID, Matrix4x4 value) {
            if (autoFlush) shader.SetMatrix(nameID, value);
            else Commands.SetComputeMatrixParam(shader, nameID, value);
        }

        public void SetMatrixArray(ComputeShader shader, string name, Matrix4x4[] values) {
            if (autoFlush) shader.SetMatrixArray(name, values);
            else Commands.SetComputeMatrixArrayParam(shader, name, values);
        }

        public void SetMatrixArray(ComputeShader shader, int nameID, Matrix4x4[] values) {
            if (autoFlush) shader.SetMatrixArray(nameID, values);
            else Commands.SetComputeMatrixArrayParam(shader, nameID, values);
        }

        public void SetBuffer(ComputeShader shader, int kernelIndex, string name, ComputeBuffer buffer) {
            if (autoFlush) shader.SetBuffer(kernelIndex, name, buffer);
            else Commands.SetComputeBufferParam(shader, kernelIndex, name, buffer);
        }

        public void SetBuffer(ComputeShader shader, int kernelIndex, int nameID, ComputeBuffer buffer) {
            if (autoFlush) shader.SetBuffer(kernelIndex, nameID, buffer);
            else Commands.SetComputeBufferParam(shader, kernelIndex, nameID, buffer);
        }

        public void SetBuffer(ComputeShader shader, int kernelIndex, string name, GraphicsBuffer buffer) {
            if (autoFlush) shader.SetBuffer(kernelIndex, name, buffer);
            else Commands.SetComputeBufferParam(shader, kernelIndex, name, buffer);
        }

        public void SetBuffer(ComputeShader shader, int kernelIndex, int nameID, GraphicsBuffer buffer) {
            if (autoFlush) shader.SetBuffer(kernelIndex, nameID, buffer);
            else Commands.SetComputeBufferParam(shader, kernelIndex, nameID, buffer);
        }

        // ComputeShader.SetTexture only accepts Texture, not RenderTargetIdentifier, so flush right after recording.
        public void SetTexture(ComputeShader shader, int kernelIndex, string name, RenderTargetIdentifier texture, int mipLevel = 0) {
            Commands.SetComputeTextureParam(shader, kernelIndex, name, texture, mipLevel);
            FlushIfAuto();
        }

        public void SetTexture(ComputeShader shader, int kernelIndex, int nameID, RenderTargetIdentifier texture, int mipLevel = 0) {
            Commands.SetComputeTextureParam(shader, kernelIndex, nameID, texture, mipLevel);
            FlushIfAuto();
        }

        public void SetTexture(
            ComputeShader shader,
            int kernelIndex,
            string name,
            RenderTargetIdentifier texture,
            int mipLevel,
            RenderTextureSubElement element
        ) {
            Commands.SetComputeTextureParam(shader, kernelIndex, name, texture, mipLevel, element);
            FlushIfAuto();
        }

        public void SetTexture(
            ComputeShader shader,
            int kernelIndex,
            int nameID,
            RenderTargetIdentifier texture,
            int mipLevel,
            RenderTextureSubElement element
        ) {
            Commands.SetComputeTextureParam(shader, kernelIndex, nameID, texture, mipLevel, element);
            FlushIfAuto();
        }

        public void SetConstantBuffer(
            ComputeShader shader,
            string name,
            ComputeBuffer buffer,
            int offset,
            int size
        ) {
            if (autoFlush) shader.SetConstantBuffer(name, buffer, offset, size);
            else Commands.SetComputeConstantBufferParam(shader, name, buffer, offset, size);
        }

        public void SetConstantBuffer(
            ComputeShader shader,
            int nameID,
            ComputeBuffer buffer,
            int offset,
            int size
        ) {
            if (autoFlush) shader.SetConstantBuffer(nameID, buffer, offset, size);
            else Commands.SetComputeConstantBufferParam(shader, nameID, buffer, offset, size);
        }

        // No immediate equivalent exists for these, so flush right after recording.
        public void SetParamsFromMaterial(ComputeShader shader, int kernelIndex, Material material) {
            Commands.SetComputeParamsFromMaterial(shader, kernelIndex, material);
            FlushIfAuto();
        }

        public void SetBufferCounterValue(ComputeBuffer buffer, uint value) {
            Commands.SetBufferCounterValue(buffer, value);
            FlushIfAuto();
        }

        public void SetBufferCounterValue(GraphicsBuffer buffer, uint value) {
            Commands.SetBufferCounterValue(buffer, value);
            FlushIfAuto();
        }

        public void SetConstantBuffer(
            ComputeShader shader,
            string name,
            GraphicsBuffer buffer,
            int offset,
            int size
        ) {
            if (autoFlush) shader.SetConstantBuffer(name, buffer, offset, size);
            else Commands.SetComputeConstantBufferParam(shader, name, buffer, offset, size);
        }

        public void SetConstantBuffer(
            ComputeShader shader,
            int nameID,
            GraphicsBuffer buffer,
            int offset,
            int size
        ) {
            if (autoFlush) shader.SetConstantBuffer(nameID, buffer, offset, size);
            else Commands.SetComputeConstantBufferParam(shader, nameID, buffer, offset, size);
        }

        // Submit recorded work before GetData blocks for the buffer's GPU writes.
        private void FlushForReadback() {
            if (Commands.sizeInBytes == 0) return;
            Graphics.ExecuteCommandBuffer(Commands);
            ClearCommands();
        }

        public void GetData(ComputeBuffer buffer, Array data) {
            FlushForReadback();
            buffer.GetData(data);
        }

        public void GetData(ComputeBuffer buffer, Array data, int managedBufferStartIndex, int computeBufferStartIndex, int count) {
            FlushForReadback();
            buffer.GetData(data, managedBufferStartIndex, computeBufferStartIndex, count);
        }

        public void GetData(GraphicsBuffer buffer, Array data) {
            FlushForReadback();
            buffer.GetData(data);
        }

        public void GetData(GraphicsBuffer buffer, Array data, int managedBufferStartIndex, int computeBufferStartIndex, int count) {
            FlushForReadback();
            buffer.GetData(data, managedBufferStartIndex, computeBufferStartIndex, count);
        }

        public void SetBufferData(ComputeBuffer buffer, Array data) {
            if (autoFlush) buffer.SetData(data);
            else Commands.SetBufferData(buffer, data);
        }
        public void SetBufferData<T>(ComputeBuffer buffer, List<T> data) where T : struct {
            if (autoFlush) buffer.SetData(data);
            else Commands.SetBufferData(buffer, data);
        }
        public void SetBufferData<T>(ComputeBuffer buffer, NativeArray<T> data) where T : struct {
            if (autoFlush) buffer.SetData(data);
            else Commands.SetBufferData(buffer, data);
        }
        public void SetBufferData(GraphicsBuffer buffer, Array data) {
            if (autoFlush) buffer.SetData(data);
            else Commands.SetBufferData(buffer, data);
        }
        public void SetBufferData<T>(GraphicsBuffer buffer, List<T> data) where T : struct {
            if (autoFlush) buffer.SetData(data);
            else Commands.SetBufferData(buffer, data);
        }
        public void SetBufferData<T>(GraphicsBuffer buffer, NativeArray<T> data) where T : struct {
            if (autoFlush) buffer.SetData(data);
            else Commands.SetBufferData(buffer, data);
        }

        public void SetBufferData(
            ComputeBuffer buffer,
            Array data,
            int sourceStartIndex,
            int destinationStartIndex,
            int count
        ) {
            if (autoFlush) buffer.SetData(data, sourceStartIndex, destinationStartIndex, count);
            else Commands.SetBufferData(buffer, data, sourceStartIndex, destinationStartIndex, count);
        }

        public void SetBufferData<T>(
            ComputeBuffer buffer,
            List<T> data,
            int sourceStartIndex,
            int destinationStartIndex,
            int count
        ) where T : struct {
            if (autoFlush) buffer.SetData(data, sourceStartIndex, destinationStartIndex, count);
            else Commands.SetBufferData(buffer, data, sourceStartIndex, destinationStartIndex, count);
        }

        public void SetBufferData<T>(
            ComputeBuffer buffer,
            NativeArray<T> data,
            int sourceStartIndex,
            int destinationStartIndex,
            int count
        ) where T : struct {
            if (autoFlush) buffer.SetData(data, sourceStartIndex, destinationStartIndex, count);
            else Commands.SetBufferData(buffer, data, sourceStartIndex, destinationStartIndex, count);
        }

        public void SetBufferData(
            GraphicsBuffer buffer,
            Array data,
            int sourceStartIndex,
            int destinationStartIndex,
            int count
        ) {
            if (autoFlush) buffer.SetData(data, sourceStartIndex, destinationStartIndex, count);
            else Commands.SetBufferData(buffer, data, sourceStartIndex, destinationStartIndex, count);
        }

        public void SetBufferData<T>(
            GraphicsBuffer buffer,
            List<T> data,
            int sourceStartIndex,
            int destinationStartIndex,
            int count
        ) where T : struct {
            if (autoFlush) buffer.SetData(data, sourceStartIndex, destinationStartIndex, count);
            else Commands.SetBufferData(buffer, data, sourceStartIndex, destinationStartIndex, count);
        }

        public void SetBufferData<T>(
            GraphicsBuffer buffer,
            NativeArray<T> data,
            int sourceStartIndex,
            int destinationStartIndex,
            int count
        ) where T : struct {
            if (autoFlush) buffer.SetData(data, sourceStartIndex, destinationStartIndex, count);
            else Commands.SetBufferData(buffer, data, sourceStartIndex, destinationStartIndex, count);
        }

        // Readback commands belong on the graphics queue. Snapshot generation data
        // in its own queue so later producers may reuse the original immediately.
        private ComputeBuffer SnapshotReadback(ComputeBuffer source, int size, int offset) =>
            SnapshotReadback(source, null, source.stride, source.count, size, offset);

        private ComputeBuffer SnapshotReadback(ComputeBuffer source, GraphicsBuffer graphicsSource,
            int stride, int count, int size, int offset) {
            if (size <= 0 || offset < 0 || size % 4 != 0 || offset % 4 != 0 ||
                (long)offset + size > (long)count * stride)
                throw new ArgumentOutOfRangeException(nameof(size), "Readback must be aligned to 4-byte words.");
            ComputeBuffer snapshot = new ComputeBuffer(size / 4, 4);
            readbackSnapshots.Add(snapshot);
            if (source != null) Work.CopyBuffer(source, snapshot, offset / 4, 0, size / 4, graphicsContext: this);
            else Work.CopyBuffer(graphicsSource, snapshot, offset / 4, 0, size / 4, graphicsContext: this);
            return snapshot;
        }

        private void QueueSnapshotReadback(ComputeBuffer snapshot, Action<CommandBuffer, Action<AsyncGPUReadbackRequest>> record,
            Action<AsyncGPUReadbackRequest> callback) {
            GraphicsFence fence = Commands.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation,
                SynchronisationStageFlags.AllGPUOperations);
            // Keep this poll on the producer: it also works during context construction,
            // before SharedResourceManager has assigned GraphicsRendering.
            Polls.AddLast(new FencePoll(fence, this, _ =>
                record(SharedResourceManager.GraphicsRendering.Commands, request => {
                    try { if (active) callback?.Invoke(request); }
                    finally { if (readbackSnapshots.Remove(snapshot)) snapshot.Release(); }
                })));
        }

        public void RequestAsyncReadback(ComputeBuffer source, int size, int offset, Action<AsyncGPUReadbackRequest> callback) {
            if (autoFlush) {
                AsyncGPUReadback.Request(source, size, offset, callback);
                return;
            }
            if (id == GraphicsContextId.Rendering) {
                Commands.RequestAsyncReadback(source, size, offset, callback);
                return;
            }
            ComputeBuffer snapshot = SnapshotReadback(source, size, offset);
            QueueSnapshotReadback(snapshot, (command, complete) => command.RequestAsyncReadback(snapshot, size, 0, complete), callback);
        }

        public void RequestAsyncReadback(GraphicsBuffer source, int size, int offset, Action<AsyncGPUReadbackRequest> callback) {
            if (autoFlush) {
                AsyncGPUReadback.Request(source, size, offset, callback);
                return;
            }
            if (id == GraphicsContextId.Rendering) {
                Commands.RequestAsyncReadback(source, size, offset, callback);
                return;
            }
            ComputeBuffer snapshot = SnapshotReadback(null, source, source.stride, source.count, size, offset);
            QueueSnapshotReadback(snapshot, (command, complete) => command.RequestAsyncReadback(snapshot, size, 0, complete), callback);
        }

        public void RequestAsyncReadbackIntoNativeArray<T>(ref NativeArray<T> destination, ComputeBuffer source,
            int size, int offset, Action<AsyncGPUReadbackRequest> callback) where T : struct {
            if (autoFlush) {
                AsyncGPUReadback.RequestIntoNativeArray(ref destination, source, size, offset, callback);
                return;
            }
            if (id == GraphicsContextId.Rendering) {
                Commands.RequestAsyncReadbackIntoNativeArray(ref destination, source, size, offset, callback);
                return;
            }
            ComputeBuffer snapshot = SnapshotReadback(source, size, offset);
            NativeArray<T> output = destination;
            QueueSnapshotReadback(snapshot, (command, complete) =>
                command.RequestAsyncReadbackIntoNativeArray(ref output, snapshot, size, 0, complete), callback);
        }

        private struct FencePoll {
            public GraphicsFence fence;
            public GraphicsResourceContext context;
            public Action<GraphicsResourceContext> OnPassed;
            public FencePoll(GraphicsFence fence, GraphicsResourceContext ctx, Action<GraphicsResourceContext> passed) {
                this.fence = fence;
                this.context = ctx;
                this.OnPassed = passed;
            }

            public bool Passed => fence.passed;
        }
    }
}
