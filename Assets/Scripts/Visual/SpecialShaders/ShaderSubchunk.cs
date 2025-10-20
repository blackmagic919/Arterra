
using System.Collections.Generic;
using TerrainGeneration;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Utils;
using WorldConfig;
using WorldConfig.Quality;

public class ShaderSubchunk : IOctreeChunk {
    /// <exclude />
    public bool Active => active;
    public SubChunkShaderGraph graph;
    public uint index;
    public int3 origin;
    public int size;
    public bool active;
    public int detailLevel;
    private TerrainChunk.Status.State RefreshState;
    private ShaderUpdateTask[] activeRenders;
    private List<GeoShaderSettings.DetailLevel> Details => Config.CURRENT.Quality.GeoShaders.value.levels;
    public ShaderSubchunk(SubChunkShaderGraph parent, int3 origin, int size, uint octreeIndex) {
        this.index = octreeIndex;
        this.graph = parent;
        this.origin = origin;
        this.size = size;
        this.active = true;
        this.detailLevel = CalculateDetailLevel();
        this.RefreshState = TerrainChunk.Status.State.Pending;
    }
    public void VerifyChunk() {
        if (!active) return;
        //These two functions are self-verifying, so they will only execute if necessary
        graph.tree.SubdivideChunk(index);
        graph.tree.MergeSiblings(index);
        ReassessDetail();
    }
    public void Destroy() {
        active = false;
        ReleaseGeometry(); 
    }
    public void Kill() {
        if (!active) return;
        active = false;
        graph.tree.ReapChunk(index);
    }

    //Because Shader Subchunks can't update spontaneously instead only
    //when the viewer has moved, Update doesn't need to be constantly called
    public void Update() {
        if (RefreshState == TerrainChunk.Status.State.Pending) {
            RefreshState = TerrainChunk.Status.State.InProgress;
            OctreeTerrain.RequestQueue.Enqueue(new OctreeTerrain.GenTask {
                task = RegenerateChunk,
                id = (int)priorities.geoshader,
                chunk = this
            });
        }
    }

    private void RegenerateChunk() {
        RefreshState = TerrainChunk.Status.State.Finished;
        if (IsParentUpdating()) return;
        if (!graph.RecalculateSubChunkGeoShader(this)) {
            this.graph.tree.ReapChunk(index);
        }
    }

    private bool IsParentUpdating() {
        TerrainChunk parent = graph.parent;
        if (parent.status.UpdateMesh != TerrainChunk.Status.State.Finished)
            return true;
        if (parent.status.CanUpdateMesh != TerrainChunk.Status.State.Finished)
            return true;
        return false;
    }

    public void ReleaseGeometry() {
        if (activeRenders == null) return;
        foreach (ShaderUpdateTask task in activeRenders){
            if (task == null || !task.Active) continue;
            task.Release(ref GenerationPreset.memoryHandle);
        } 
    }

    private void ReassessDetail() {
        int level = CalculateDetailLevel();
        if (detailLevel == level) return;
        RefreshState = TerrainChunk.Status.Initiate(RefreshState);
        detailLevel = level;
    }

    private int CalculateDetailLevel() {
        int viewerDist = GetMaxDist(graph.tree.ViewerPosGS);
        int accDist = 0;

        for (int i = 0; i < Details.Count; i++) {
            accDist += Details[i].Distance;
            if (viewerDist < accDist)
                return Details[i].Level;
        }
        return Details[^1].Level;
    }

    private int GetMaxDist(int3 GCoord) {
        int3 origin = this.origin;
        int3 end = this.origin + (int)size;
        int3 dist = math.abs(math.clamp(GCoord, origin, end) - GCoord);
        return math.cmax(dist);
    }

    public int2 GetInfoRegion() {
        int SCover = size / graph.tree.MinChunkSize;
        int3 SOrigin = (origin - graph.parent.origin) / graph.tree.MinChunkSize;
        int2 SCInfo;

        SCInfo.x = (int)CustomUtility.EncodeMorton3((uint3)SOrigin);
        SCInfo.y = SCInfo.x + SCover * SCover * SCover;
        return SCInfo;
    }

    public void ApplyAllocToChunk(List<GeoShader> geoShaders, uint2[] shadInfo) {
        graph.tree.ReapChunk(index); ReleaseGeometry();
        RefreshState = TerrainChunk.Status.State.Finished;
        activeRenders = new ShaderUpdateTask[shadInfo.Length];
        for (int i = 0; i < shadInfo.Length; i++) {
            uint2 info = shadInfo[i];
            RenderParams rp = SetupShaderMaterials(geoShaders[i], GenerationPreset.memoryHandle, info.x);
            activeRenders[i] = new ShaderUpdateTask(info.x, info.y, rp);
            OctreeTerrain.MainLateUpdateTasks.Enqueue(activeRenders[i]);
            ReleaseEmptyShaders(activeRenders[i]);
        }
    }
    
    private void ReleaseEmptyShaders(ShaderUpdateTask shader){
        void OnAddressRecieved(AsyncGPUReadbackRequest request){
            if (!shader.Active) return;

            uint2 memAddress = request.GetData<uint2>().ToArray()[0];
            if (memAddress.x != 0) return; //No geometry to readback
            shader.Release(ref GenerationPreset.memoryHandle);
            return;
        }
        AsyncGPUReadback.Request(GenerationPreset.memoryHandle.Address, size: 8, offset: 8*(int)shader.address, OnAddressRecieved);
    }

    public RenderParams SetupShaderMaterials(
        GeoShader shader, MemoryBufferHandler memoryHandle, uint addressIndex
    ) {
        Transform pTransf = graph.parent.MeshTransform;
        ComputeBuffer sourceBuffer = memoryHandle.GetBlockBuffer(addressIndex);
        Bounds boundsOS = graph.parent.GetRelativeBoundsOS(origin, size);
        Bounds boundsWS = CustomUtility.TransformBounds(pTransf, boundsOS);

        RenderParams rp = new RenderParams(shader.GetMaterial()) {
            worldBounds = boundsWS,
            shadowCastingMode = ShadowCastingMode.Off,
            matProps = new MaterialPropertyBlock()
        };

        rp.matProps.SetMatrix("_LocalToWorld", pTransf.localToWorldMatrix);
        rp.matProps.SetBuffer("_StorageMemory", sourceBuffer);
        rp.matProps.SetBuffer("_AddressDict", memoryHandle.Address);
        rp.matProps.SetInt("addressIndex", (int)addressIndex);
        return rp;
    }

    
    
    public class ShaderUpdateTask : IUpdateSubscriber {
        private bool active = false;
        public bool Active {
            get => active;
            set => active = value;
        }
        public uint address;
        public uint dispArgs;
        RenderParams rp;
        public ShaderUpdateTask(uint address, uint dispArgs, RenderParams rp) {
            this.address = address;
            this.dispArgs = dispArgs;
            this.active = true;
            this.rp = rp;
        }

        public void Update(MonoBehaviour mono) {
            Graphics.RenderPrimitivesIndirect(
                rp, MeshTopology.Triangles,
                UtilityBuffers.DrawArgs.Get(),
                1, (int)dispArgs
            );
        }

        public void Release(ref MemoryOccupancyBalancer memory) {
            if (!active) return;
            active = false;

            memory.ReleaseMemory(address);
            UtilityBuffers.DrawArgs.Release(dispArgs);
        }
    }
}