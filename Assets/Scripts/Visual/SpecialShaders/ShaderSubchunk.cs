
using System.Collections.Generic;
using TerrainGeneration;
using Unity.Mathematics;
using UnityEditor.Playables;
using Utils;
using WorldConfig;
using WorldConfig.Quality;

public class ShaderSubchunk : IOctreeChunk {
    /// <exclude />
    public bool Active => active;
    public SubchunkOctree.FixedOctree tree;
    public uint index;
    public int3 origin;
    public int size;
    public bool active;
    private int detailLevel;
    private TerrainChunk.Status.State RefreshState;
    private List<GeoShaderSettings.DetailLevel> Details => Config.CURRENT.Quality.GeoShaders.value.levels;
    public ShaderSubchunk(SubchunkOctree.FixedOctree parent, int3 origin, int size, uint octreeIndex) {
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
        if (IsParentUpdating()) return;
        //These two functions are self-verifying, so they will only execute if necessary
        tree.SubdivideChunk(index);
        tree.MergeSiblings(index);
        ReassessDetail();
        Update();
    }
    public void Destroy() {
        active = false;

    }
    public void Kill() {
        if (!active) return;
        active = false;
        tree.ReapChunk(index);
    }

    //Because Shader Subchunks can't update spontaneously instead only
    //when the viewer has moved, Update doesn't need to be constantly called
    private void Update() {
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

    private void ReassessDetail() {
        int level = CalculateDetailLevel();
        if (detailLevel == level) return;
        TerrainChunk.Status.Initiate(RefreshState);
        detailLevel = level;
    }

    private int CalculateDetailLevel() {
        int viewerDist = GetMaxDist(SubchunkOctree.ViewerPosGS);
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

    private void RegenerateChunk() {

    }

    public int3 GetDetailLevelLookup() {
        int SCover = size / tree.MinChunkSize;
        int numPtsAxis = tree.parent.size / tree.MinChunkSize;
        int3 SOrigin = (origin - tree.parent.origin) / tree.MinChunkSize;
        int3 SEnd = SOrigin + SCover; int3 SCInfo;
        SCInfo.x = CustomUtility.indexFromCoord(SOrigin, numPtsAxis);
        SCInfo.y = CustomUtility.indexFromCoord(SEnd, numPtsAxis);
        SCInfo.z = detailLevel;
        return SCInfo;
    }
    
    public int3 AllocateSubchunkRegion() {
        return int3.zero;
    }
}