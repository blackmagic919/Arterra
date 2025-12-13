
using System.Collections.Generic;
using System.Linq;
using TerrainGeneration;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
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
    private RenderParams[] rps;
    private List<GeoShaderSettings.DetailLevel> Details => Config.CURRENT.Quality.GeoShaders.value.levels;
    private static GeoShaderSettings rSettings => Config.CURRENT.Quality.GeoShaders.value;
    public ShaderSubchunk(SubChunkShaderGraph parent, List<GeoShader> shaders, int3 origin, int size, uint octreeIndex) {
        this.index = octreeIndex;
        this.graph = parent;
        this.origin = origin;
        this.size = size;
        this.active = true;
        this.detailLevel = CalculateDetailLevel();
        this.RefreshState = TerrainChunk.Status.State.Pending;
        Transform pTransf = graph.parent.MeshTransform;
        Bounds boundsOS = graph.parent.GetRelativeBoundsOS(origin, size);
        Bounds boundsWS = CustomUtility.TransformBounds(pTransf, boundsOS);
        rps = new RenderParams[shaders.Count];
        for (int i = 0; i < rps.Length; i++) {
            rps[i] = new RenderParams(shaders[i].GetMaterial()) {
                worldBounds = boundsWS,
                shadowCastingMode = ShadowCastingMode.Off,
                matProps = new MaterialPropertyBlock()
            };
            rps[i].matProps.SetMatrix(ShaderIDProps.LocalToWorld, pTransf.localToWorldMatrix);
        }
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

    public void ApplyAllocToChunk(uint2[] shadInfo) {
        graph.tree.ReapChunk(index); ReleaseGeometry();
        RefreshState = TerrainChunk.Status.State.Finished;
        activeRenders = new ShaderUpdateTask[shadInfo.Length];
        for (int i = 0; i < shadInfo.Length; i++) {
            uint2 info = shadInfo[i];
            RenderParams rp = SetupShaderMaterials(i, GenerationPreset.memoryHandle, info.x);
            ShaderUpdateTask shader = new ShaderUpdateTask(info.x, info.y, rp);
            OctreeTerrain.MainLateUpdateTasks.Enqueue(shader);
            activeRenders[i] = shader;

            GenerationPreset.memoryHandle.TestAllocIsEmpty((int)shader.address, address => {
                //Captured shader is in this scope so ok
                shader.Release(ref GenerationPreset.memoryHandle);
            });
        }
    }

    public RenderParams SetupShaderMaterials(
        int shadInd, MemoryBufferHandler memoryHandle, uint addressIndex
    ) {
        RenderParams rp = rps[shadInd]; 
        ComputeBuffer sourceBuffer = memoryHandle.GetBlockBuffer(addressIndex);
        rp.matProps.SetBuffer(ShaderIDProps.StorageMemory, sourceBuffer);
        rp.matProps.SetBuffer(ShaderIDProps.AddressDict, memoryHandle.Address);
        rp.matProps.SetInt(ShaderIDProps.AddressIndex, (int)addressIndex);
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