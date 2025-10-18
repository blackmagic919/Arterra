
using System.Collections.Generic;
using System.Linq;
using TerrainGeneration;
using Unity.Mathematics;
using UnityEditor.Playables;
using UnityEngine;
using UnityEngine.Rendering;
using Utils;
using WorldConfig;
using WorldConfig.Quality;

public class ShaderSubchunk : IOctreeChunk {
    /// <exclude />
    public bool Active => active;
    public SubChunkShaderGraph.FixedOctree tree;
    public uint index;
    public int3 origin;
    public int size;
    public bool active;
    public int detailLevel;
    private TerrainChunk.Status.State RefreshState;
    private ShaderUpdateTask[] activeRenders;
    private List<GeoShaderSettings.DetailLevel> Details => Config.CURRENT.Quality.GeoShaders.value.levels;
    public ShaderSubchunk(SubChunkShaderGraph.FixedOctree parent, int3 origin, int size, uint octreeIndex) {
        this.index = octreeIndex;
        this.tree = parent;
        this.origin = origin;
        this.size = size;
        this.active = true;
        this.detailLevel = CalculateDetailLevel();
        this.RefreshState = TerrainChunk.Status.State.InProgress;
    }
    public void VerifyChunk() {
        if (!active) return;
        //These two functions are self-verifying, so they will only execute if necessary
        tree.SubdivideChunk(index);
        tree.MergeSiblings(index);
        ReassessDetail();
    }
    public void Destroy() {
        active = false;
        ReleaseGeometry(); 
    }
    public void Kill() {
        if (!active) return;
        active = false;
        tree.ReapChunk(index);
    }

    //Because Shader Subchunks can't update spontaneously instead only
    //when the viewer has moved, Update doesn't need to be constantly called
    public void Update() {
        if (!active) return;
        if (RefreshState == TerrainChunk.Status.State.Pending) {
            RefreshState = TerrainChunk.Status.State.InProgress;
            OctreeTerrain.RequestQueue.Enqueue(new OctreeTerrain.GenTask {
                task = RegenerateChunk,
                id = (int)priorities.propogation,
                chunk = this
            });
        }
    }

    private bool IsParentUpdating() {
        TerrainChunk parent = tree.parent;
        if (parent.active == false)
            return true;
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
        TerrainChunk.Status.Initiate(RefreshState);
        detailLevel = level;
    }

    private int CalculateDetailLevel() {
        int viewerDist = GetMaxDist(tree.ViewerPosGS);
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
        int SCover = size / tree.MinChunkSize;
        int numPtsAxis = tree.parent.size / tree.MinChunkSize;
        int3 SOrigin = (origin - tree.parent.origin) / tree.MinChunkSize;
        int2 SCInfo;

        SCInfo.x = CustomUtility.indexFromCoord(SOrigin, numPtsAxis);
        SCInfo.y = SCover;
        return SCInfo;
    }

    public void ApplyAllocToChunk(List<GeoShader> geoShaders, uint2[] shadInfo) {
        tree.ReapChunk(index); ReleaseGeometry();
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
        ComputeBuffer sourceBuffer = memoryHandle.GetBlockBuffer(addressIndex);
        Bounds boundsOS = tree.parent.boundsOS;
        Transform pTransf = tree.parent.MeshTransform;
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

    private void RegenerateChunk() {

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
                UtilityBuffers.ArgumentBuffer,
                1, (int)dispArgs
            );
        }

        public void Release(ref MemoryOccupancyBalancer memory) {
            if (!active) return;
            active = false;

            memory.ReleaseMemory(address);
            UtilityBuffers.ReleaseArgs(dispArgs);
        }
    }
}