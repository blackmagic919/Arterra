using System.Collections.Generic;
using MapStorage;
using TerrainGeneration;
using Unity.Mathematics;
using WorldConfig;
using WorldConfig.Quality;

public static class SubchunkOctree {
    private static GeoShaderSettings s;
    private static Dictionary<TerrainChunk, FixedOctree> ChunkOctrees;
    /// <summary> The last tracked position of the viewer in 
    /// chunk space. This value is only updated when the viewer's
    /// position exceeds the viewDistUpdate threshold. </summary>
    public static int3 ViewerPosGS;
    public static void Initialize() {
        s = Config.CURRENT.Quality.GeoShaders.value;
        ChunkOctrees = new Dictionary<TerrainChunk, FixedOctree>();
        UpdateViewerPos();
    }
    private static void VerifyChunks() {
        int3 ViewerPosition = (int3)math.round(CPUMapManager.WSToGS(OctreeTerrain.viewer.position));
        if (math.distance(ViewerPosGS, ViewerPosition) < s.SubchunkUpdateThresh) return;
        UpdateViewerPos();

        //This is buffered because Verifying Leaf Chunks may change octree structure
        Queue<ShaderSubchunk> frameChunks = new Queue<ShaderSubchunk>();
        foreach(FixedOctree tree in ChunkOctrees.Values) {
            tree.ForEachChunk(chunk => frameChunks.Enqueue(chunk));
        }
        while (frameChunks.Count > 0) {
            ShaderSubchunk chunk = frameChunks.Dequeue();
            if (!chunk.active) continue;
            chunk.VerifyChunk();
        }
    }

    private static void UpdateViewerPos() {
        int3 ViewerPosition = (int3)math.round(CPUMapManager.WSToGS(OctreeTerrain.viewer.position));
        ViewerPosGS = ViewerPosition;
    }
    

    public class FixedOctree : Octree<ShaderSubchunk> {
        public TerrainChunk parent;
        private GeoShaderSettings.DetailLevel[] Details;
        private static int GetMaxDepth(GeoShaderSettings.DetailLevel[] detailLevels) {
            int divisions = 0;
            foreach (var level in detailLevels)
                if (level.IncreaseSize) divisions++;
            return divisions;
        }

        private static int GetMaxNumChunks(int depth) {
            int length = 1 << depth;
            return length * length * length;
        }
        private static int GetMinChunkSize(int depth, int rootChunkSize) {
            return rootChunkSize / (1 << depth);
        }
        public FixedOctree(TerrainChunk parent, GeoShaderSettings.DetailLevel[] detailLevels) :
            base(GetMaxDepth(detailLevels),
                GetMinChunkSize(GetMaxDepth(detailLevels), parent.size),
                GetMaxNumChunks(GetMaxDepth(detailLevels))) {
            this.Details = detailLevels;
            this.parent = parent;
        }

        public override bool IsBalanced(ref Node node) {
            int accDist = 0; int chunkSize = MinChunkSize;
            int viewerDist = node.GetMaxDist(ViewerPosGS);
            for (int i = 0; i < Details.Length; i++) {
                accDist += Details[i].Distance;
                if (viewerDist < accDist)
                    return node.size <= chunkSize;
                if (Details[i].IncreaseSize)
                    chunkSize *= 2;
            }
            return false;
        }
        
        protected override bool RemapRoot(uint node) => false;

        protected override void AddTerrainChunk(uint octreeIndex) {

        }
        
    }
}
