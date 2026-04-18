using Arterra.Configuration;
using Arterra.Data.Structure.Jigsaw;
using Arterra.Utils;
using Arterra.Editor;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using Newtonsoft.Json;
using FMOD.Studio;
using System;
using System.Text;
using Arterra.Data.Biome;

namespace Arterra.Data.Structure.Jigsaw {
    [Serializable]
    public class StructureSystem {
        //Warning don't make this too small or memory overflow
        public int CellSizeFactor = 4;
        public int MaxConnectionDist = 32;

        [RegistryReference("Noise")]
        public string coarseSSystemNoise;
        [RegistryReference("Noise")]
        public string fineSSystemNoise;
        [JsonIgnore]
        public int CoarseSSystemNoise => Config.CURRENT.Generation.Noise.RetrieveIndex(coarseSSystemNoise);
        [JsonIgnore]
        public int FineSSystemNoise => Config.CURRENT.Generation.Noise.RetrieveIndex(fineSSystemNoise);
        [JsonIgnore]
        public int CellSize => 1 << CellSizeFactor;
        public int MaxSystemLoD = 2;
        [FormerlySerializedAs("PathColoringBinSize")]
        public int StructureColoringBinSize = 8;
    }
}

namespace Arterra.Engine.Terrain.Structure.Jigsaw {
// The runtime flow in this file mirrors Assets/Scripts/TerrainGeneration/StructureGenerator/StructureSystems/Plan.txt.
// Step 1 and Steps 8/9 remain close to the previous implementation, while orchestration is split into explicit
// phases to match the new pipeline shape.

public static class Generator {
    private static int plannerPathfindDispatchDivisor = 1;
    private static int plannerBacktrackDispatchDivisor = 1;

    public static SSystemOffsets offsets;
    private static ComputeShader AnchorSampler;
    private static ComputeShader GraphConnector;
    private static ComputeShader SanitateBatches;
    private static ComputeShader PathBatchPlanner;
    private static ComputeShader StructurePathfinder;
    private static ComputeShader PathSetupRetriever;
    private static ComputeShader StructurePostProcess;

    private static StructureSystem jigsaw => Config.CURRENT.Generation.Structures.value.StructureSystemSettings;

    private static uint GetPlannerDispatchArgsOffsetBytes(int batchIndex, int stageIndex) {
        return (uint)((offsets.batchDispatchArgsStart + (batchIndex * SSystemOffsets.PLANNER_DISPATCH_ARGS_PER_BATCH + stageIndex) * SSystemOffsets.DISPATCH_ARGS_WORD) * sizeof(int));
    }

    static Generator() {
        AnchorSampler = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Structures/StructureSystem/SampleAnchors");
        GraphConnector = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Structures/StructureSystem/GraphConnector");
        SanitateBatches = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Structures/StructureSystem/SanitatePathBatches");
        PathBatchPlanner = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Structures/StructureSystem/PathBatchPlanner");
        StructurePathfinder = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Structures/StructureSystem/PopulatePaths");
        PathSetupRetriever = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Structures/StructureSystem/CreatePathStructures");
        StructurePostProcess = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Structures/StructureSystem/StructurePostProcess");
    }

    public static void Initialize() {
        offsets = new SSystemOffsets();
        Configuration.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain.value;

        int originOffset = -(jigsaw.MaxConnectionDist * 2);
        int cellsPerChunk = rSettings.mapChunkSize / jigsaw.CellSize;
        int maxChunkSize = rSettings.mapChunkSize * (1 << jigsaw.MaxSystemLoD);
        offsets = new SSystemOffsets(maxChunkSize, jigsaw.MaxConnectionDist, jigsaw.CellSize, jigsaw.StructureColoringBinSize, 0);
        int capSectionCapacity = offsets.maxCapsPerBatch * offsets.maxBatchesPerChunk;

        int kernel = AnchorSampler.FindKernel("SamplePoints");
        AnchorSampler.SetBuffer(kernel, "anchors", UtilityBuffers.GenerationBuffer);
        AnchorSampler.SetInt("coarseSSystemNoise", jigsaw.CoarseSSystemNoise);
        AnchorSampler.SetInt("fineSSystemNoise", jigsaw.FineSSystemNoise);
        AnchorSampler.SetInt("cellSize", jigsaw.CellSize);
        AnchorSampler.SetInt("cellsPerChunk", cellsPerChunk);
        AnchorSampler.SetInt("bSTART_anchors", offsets.anchorsStart);
        AnchorSampler.SetInt("oCellOffset", originOffset);
        Structure.Generator.SetStructIDSettings(AnchorSampler);

        kernel = AnchorSampler.FindKernel("PoissonPrune");
        AnchorSampler.SetBuffer(kernel, "anchors", UtilityBuffers.GenerationBuffer);
        AnchorSampler.SetBuffer(kernel, "anchorDict", UtilityBuffers.GenerationBuffer);
        AnchorSampler.SetBuffer(kernel, "counters", UtilityBuffers.GenerationBuffer);
        AnchorSampler.SetInt("bCOUNT_dict", offsets.anchorDictCounter);
        AnchorSampler.SetInt("bSTART_dict", offsets.anchorDictStart);

        kernel = GraphConnector.FindKernel("ClearSockets");
        GraphConnector.SetBuffer(kernel, "counters", UtilityBuffers.GenerationBuffer);
        GraphConnector.SetBuffer(kernel, "anchorDict", UtilityBuffers.GenerationBuffer);
        GraphConnector.SetBuffer(kernel, "socketUsage", UtilityBuffers.GenerationBuffer);
        GraphConnector.SetInt("oCellOffset", originOffset);
        GraphConnector.SetInt("cellsPerChunk", cellsPerChunk);
        GraphConnector.SetInt("bCOUNT_dict", offsets.anchorDictCounter);
        GraphConnector.SetInt("bSTART_dict", offsets.anchorDictStart);
        GraphConnector.SetInt("bSTART_sockets", offsets.socketUsageStart);

        kernel = GraphConnector.FindKernel("SetSocketConnections");
        GraphConnector.SetBuffer(kernel, "counters", UtilityBuffers.GenerationBuffer);
        GraphConnector.SetBuffer(kernel, "anchorDict", UtilityBuffers.GenerationBuffer);
        GraphConnector.SetBuffer(kernel, "socketUsage", UtilityBuffers.GenerationBuffer);
        GraphConnector.SetBuffer(kernel, "anchors", UtilityBuffers.GenerationBuffer);
        GraphConnector.SetInt("cellSize", jigsaw.CellSize);
        GraphConnector.SetInt("connectRadius", jigsaw.MaxConnectionDist);
        GraphConnector.SetInt("bSTART_anchors", offsets.anchorsStart);

        kernel = GraphConnector.FindKernel("ConnectGraph");
        GraphConnector.SetBuffer(kernel, "counters", UtilityBuffers.GenerationBuffer);
        GraphConnector.SetBuffer(kernel, "anchorDict", UtilityBuffers.GenerationBuffer);
        GraphConnector.SetBuffer(kernel, "socketUsage", UtilityBuffers.GenerationBuffer);
        GraphConnector.SetBuffer(kernel, "anchors", UtilityBuffers.GenerationBuffer);
        GraphConnector.SetBuffer(kernel, "anchorPaths", UtilityBuffers.GenerationBuffer);
        GraphConnector.SetInt("cellSize", jigsaw.CellSize);
        GraphConnector.SetInt("connectRadius", jigsaw.MaxConnectionDist);
        GraphConnector.SetInt("bCOUNT_paths", offsets.anchorPathCounter);
        GraphConnector.SetInt("bSTART_anchors", offsets.anchorsStart);
        GraphConnector.SetInt("bSTART_paths", offsets.anchorPathStart);

        kernel = SanitateBatches.FindKernel("SelectAnchorPieces");
        SanitateBatches.SetBuffer(kernel, "anchors", UtilityBuffers.GenerationBuffer);
        SanitateBatches.SetBuffer(kernel, "anchorDict", UtilityBuffers.GenerationBuffer);
        SanitateBatches.SetBuffer(kernel, "socketUsage", UtilityBuffers.GenerationBuffer);
        SanitateBatches.SetBuffer(kernel, "counters", UtilityBuffers.GenerationBuffer);
        SanitateBatches.SetBuffer(kernel, "genStructures", UtilityBuffers.GenerationBuffer);
        SanitateBatches.SetInt("bCOUNT_dict", offsets.anchorDictCounter);
        SanitateBatches.SetInt("bSTART_dict", offsets.anchorDictStart);
        SanitateBatches.SetInt("bSTART_anchors", offsets.anchorsStart);
        SanitateBatches.SetInt("bSTART_sockets", offsets.socketUsageStart);
        SanitateBatches.SetInt("bCOUNT_paths", offsets.anchorPathCounter);
        SanitateBatches.SetInt("bSTART_paths", offsets.anchorPathStart);
        SanitateBatches.SetInt("bSTART_endpts", offsets.pathEndsStart);
        SanitateBatches.SetInt("bCOUNT_struct", offsets.intermediateStructCounter);
        SanitateBatches.SetInt("bSTART_struct", offsets.intermediateStructStart);
        SanitateBatches.SetInt("connectRadius", jigsaw.MaxConnectionDist);
        Structure.Generator.SetStructIDSettings(SanitateBatches);

        kernel = SanitateBatches.FindKernel("GetRealEndpoints");
        SanitateBatches.SetBuffer(kernel, "counters", UtilityBuffers.GenerationBuffer);
        SanitateBatches.SetBuffer(kernel, "anchors", UtilityBuffers.GenerationBuffer);
        SanitateBatches.SetBuffer(kernel, "anchorPaths", UtilityBuffers.GenerationBuffer);
        SanitateBatches.SetBuffer(kernel, "endPoints", UtilityBuffers.GenerationBuffer);

        kernel = SanitateBatches.FindKernel("FilterPathBatches");
        SanitateBatches.SetBuffer(kernel, "counters", UtilityBuffers.GenerationBuffer);
        SanitateBatches.SetBuffer(kernel, "anchors", UtilityBuffers.GenerationBuffer);
        SanitateBatches.SetBuffer(kernel, "anchorPaths", UtilityBuffers.GenerationBuffer);
        SanitateBatches.SetBuffer(kernel, "endPoints", UtilityBuffers.GenerationBuffer);
        SanitateBatches.SetBuffer(kernel, "SocketCaps", UtilityBuffers.GenerationBuffer);
        SanitateBatches.SetInt("oCellOffset", originOffset);
        SanitateBatches.SetInt("capCapacity", capSectionCapacity);
        SanitateBatches.SetInt("bSTART_capCounts", offsets.batchSocketCapCounter);
        SanitateBatches.SetInt("bSTART_caps", offsets.batchSocketCapStart);

        kernel = StructurePathfinder.FindKernel("BatchPathfind");
        StructurePathfinder.SetBuffer(kernel, "batchRanges", UtilityBuffers.GenerationBuffer);
        StructurePathfinder.SetBuffer(kernel, "batchPathList", UtilityBuffers.GenerationBuffer);
        StructurePathfinder.SetBuffer(kernel, "endPoints", UtilityBuffers.GenerationBuffer);
        StructurePathfinder.SetBuffer(kernel, "anchors", UtilityBuffers.GenerationBuffer);
        StructurePathfinder.SetBuffer(kernel, "anchorPaths", UtilityBuffers.GenerationBuffer);
        StructurePathfinder.SetBuffer(kernel, "batchVisit", UtilityBuffers.GenerationBuffer);
        StructurePathfinder.SetBuffer(kernel, "pathMeet", UtilityBuffers.GenerationBuffer);
        StructurePathfinder.SetBuffer(kernel, "frontierBuffer", UtilityBuffers.GenerationBuffer);
        StructurePathfinder.SetBuffer(kernel, "pathPrefix", UtilityBuffers.GenerationBuffer);
        StructurePathfinder.SetInt("bSTART_batchPathList", offsets.batchPathStart);
        StructurePathfinder.SetInt("bSTART_batchRanges", offsets.batchRangesStart);
        StructurePathfinder.SetInt("bSTART_endpts", offsets.pathEndsStart);
        StructurePathfinder.SetInt("bSTART_paths", offsets.anchorPathStart);
        StructurePathfinder.SetInt("bSTART_anchors", offsets.anchorsStart);
        StructurePathfinder.SetInt("bSTART_visited", offsets.batchVistStart);
        StructurePathfinder.SetInt("bSTART_pathMeet", offsets.pathMeetStart);
        StructurePathfinder.SetInt("bSTART_frontier", offsets.frontierStart);
        StructurePathfinder.SetInt("bSTART_pathPrefix", offsets.pathPrefixStart);
        Structure.Generator.SetStructIDSettings(StructurePathfinder);
        
        StructurePathfinder.SetBuffer(kernel, "genStructures", UtilityBuffers.GenerationBuffer);
        StructurePathfinder.SetInt("bCOUNT_struct", offsets.intermediateStructCounter);
        StructurePathfinder.SetInt("bSTART_struct", offsets.intermediateStructStart);

        int pathfindKernel = StructurePathfinder.FindKernel("BatchPathfind");
        StructurePathfinder.GetKernelThreadGroupSizes(pathfindKernel, out _, out _, out _);

        int backtrackKernel = PathSetupRetriever.FindKernel("BacktrackGridPath");
        PathSetupRetriever.GetKernelThreadGroupSizes(backtrackKernel, out uint backtrackThreadsX, out _, out _);

        // BatchPathfind indexes paths by SV_GroupID.x, so it is one path per workgroup by design.
        plannerPathfindDispatchDivisor = 1;
        plannerBacktrackDispatchDivisor = Mathf.Max(1, (int)backtrackThreadsX);

        kernel = PathBatchPlanner.FindKernel("CountPathSizes");
        PathBatchPlanner.SetBuffer(kernel, "counters", UtilityBuffers.GenerationBuffer);
        PathBatchPlanner.SetBuffer(kernel, "endPoints", UtilityBuffers.GenerationBuffer);
        PathBatchPlanner.SetBuffer(kernel, "pathPrefix", UtilityBuffers.GenerationBuffer);
        PathBatchPlanner.SetBuffer(kernel, "pathMeet", UtilityBuffers.GenerationBuffer);
        PathBatchPlanner.SetInt("bCOUNT_paths", offsets.anchorPathCounter);
        PathBatchPlanner.SetInt("bSTART_endpts", offsets.pathEndsStart);
        PathBatchPlanner.SetInt("bSTART_pathPrefix", offsets.pathPrefixStart);
        PathBatchPlanner.SetInt("bSTART_pathMeet", offsets.pathMeetStart);

        kernel = PathBatchPlanner.FindKernel("FinalizePathPlanner");
        PathBatchPlanner.SetBuffer(kernel, "counters", UtilityBuffers.GenerationBuffer);
        PathBatchPlanner.SetBuffer(kernel, "pathPrefix", UtilityBuffers.GenerationBuffer);
        PathBatchPlanner.SetBuffer(kernel, "batchPrefix", UtilityBuffers.GenerationBuffer);
        PathBatchPlanner.SetBuffer(kernel, "batchRanges", UtilityBuffers.GenerationBuffer);
        PathBatchPlanner.SetBuffer(kernel, "dispatchArgs", UtilityBuffers.GenerationBuffer);
        PathBatchPlanner.SetBuffer(kernel, "batchPathList", UtilityBuffers.GenerationBuffer);
        PathBatchPlanner.SetInt("bCOUNT_paths", offsets.anchorPathCounter);
        PathBatchPlanner.SetInt("bSTART_pathPrefix", offsets.pathPrefixStart);
        PathBatchPlanner.SetInt("bSTART_batchPrefix", offsets.batchPrefixStart);
        PathBatchPlanner.SetInt("bSTART_batchRanges", offsets.batchRangesStart);
        PathBatchPlanner.SetInt("bSTART_dispatchArgs", offsets.batchDispatchArgsStart);
        PathBatchPlanner.SetInt("bSTART_batchPathList", offsets.batchPathStart);
        PathBatchPlanner.SetInt("maxBatchCount", offsets.maxBatchesPerChunk);
        PathBatchPlanner.SetInt("pathfindDispatchDivisor", plannerPathfindDispatchDivisor);
        PathBatchPlanner.SetInt("backtrackDispatchDivisor", plannerBacktrackDispatchDivisor);

        kernel = PathBatchPlanner.FindKernel("ClearVisitedPathIds");
        PathBatchPlanner.SetBuffer(kernel, "batchVisit", UtilityBuffers.GenerationBuffer);
        PathBatchPlanner.SetInt("bSTART_visited", offsets.batchVistStart);

        kernel = PathSetupRetriever.FindKernel("BacktrackGridPath");
        PathSetupRetriever.SetBuffer(kernel, "batchRanges", UtilityBuffers.GenerationBuffer);
        PathSetupRetriever.SetBuffer(kernel, "batchPathList", UtilityBuffers.GenerationBuffer);
        PathSetupRetriever.SetBuffer(kernel, "pathPrefix", UtilityBuffers.GenerationBuffer);
        PathSetupRetriever.SetBuffer(kernel, "batchVisit", UtilityBuffers.GenerationBuffer);
        PathSetupRetriever.SetBuffer(kernel, "endPoints", UtilityBuffers.GenerationBuffer);
        PathSetupRetriever.SetBuffer(kernel, "SocketCaps", UtilityBuffers.GenerationBuffer);
        PathSetupRetriever.SetBuffer(kernel, "genStructures", UtilityBuffers.GenerationBuffer);
        PathSetupRetriever.SetBuffer(kernel, "counters", UtilityBuffers.GenerationBuffer);
        PathSetupRetriever.SetBuffer(kernel, "anchorPaths", UtilityBuffers.GenerationBuffer);
        PathSetupRetriever.SetBuffer(kernel, "anchors", UtilityBuffers.GenerationBuffer);
        PathSetupRetriever.SetBuffer(kernel, "pathMeet", UtilityBuffers.GenerationBuffer);
        PathSetupRetriever.SetInt("bSTART_batchPathList", offsets.batchPathStart);
        PathSetupRetriever.SetInt("bSTART_batchRanges", offsets.batchRangesStart);
        PathSetupRetriever.SetInt("bSTART_pathPrefix", offsets.pathPrefixStart);
        PathSetupRetriever.SetInt("bSTART_capCounts", offsets.batchSocketCapCounter);
        PathSetupRetriever.SetInt("bCOUNT_struct", offsets.intermediateStructCounter);
        PathSetupRetriever.SetInt("bSTART_struct", offsets.intermediateStructStart);
        PathSetupRetriever.SetInt("bSTART_caps", offsets.batchSocketCapStart);
        PathSetupRetriever.SetInt("bSTART_paths", offsets.anchorPathStart);
        PathSetupRetriever.SetInt("bSTART_anchors", offsets.anchorsStart);
        PathSetupRetriever.SetInt("capCapacity", capSectionCapacity);
        PathSetupRetriever.SetInt("bSTART_visited", offsets.batchVistStart);
        PathSetupRetriever.SetInt("bSTART_endpts", offsets.pathEndsStart);
        PathSetupRetriever.SetInt("bSTART_pathMeet", offsets.pathMeetStart);
        PathSetupRetriever.SetInt("oCellOffset", originOffset);

        kernel = PathSetupRetriever.FindKernel("CapDanglingSockets");
        PathSetupRetriever.SetBuffer(kernel, "counters", UtilityBuffers.GenerationBuffer);
        PathSetupRetriever.SetBuffer(kernel, "SocketCaps", UtilityBuffers.GenerationBuffer);
        PathSetupRetriever.SetBuffer(kernel, "genStructures", UtilityBuffers.GenerationBuffer);
        PathSetupRetriever.SetBuffer(kernel, "danglingDepths", UtilityBuffers.GenerationBuffer);
        PathSetupRetriever.SetInt("bSTART_danglingDepths", offsets.danglingDepthsStart);

        kernel = StructurePostProcess.FindKernel("InitStructurePruneState");
        StructurePostProcess.SetBuffer(kernel, "counters", UtilityBuffers.GenerationBuffer);
        StructurePostProcess.SetBuffer(kernel, "binHeads", UtilityBuffers.GenerationBuffer);
        StructurePostProcess.SetInt("bCOUNT_final", offsets.finalStructsCounter);
        StructurePostProcess.SetInt("bCOUNT_binList", offsets.binListCounter);
        StructurePostProcess.SetInt("bSTART_binHeads", offsets.binHeadsStart);
        StructurePostProcess.SetInt("chunkSize", rSettings.mapChunkSize);
        StructurePostProcess.SetInt("binSize", jigsaw.StructureColoringBinSize);

        kernel = StructurePostProcess.FindKernel("InitDepthTables");
        StructurePostProcess.SetBuffer(kernel, "counters", UtilityBuffers.GenerationBuffer);
        StructurePostProcess.SetBuffer(kernel, "maxDepths", UtilityBuffers.GenerationBuffer);
        StructurePostProcess.SetBuffer(kernel, "danglingDepths", UtilityBuffers.GenerationBuffer);
        StructurePostProcess.SetInt("bCOUNT_paths", offsets.anchorPathCounter);
        StructurePostProcess.SetInt("bSTART_capCounts", offsets.batchSocketCapCounter);
        StructurePostProcess.SetInt("capCapacity", capSectionCapacity);
        StructurePostProcess.SetInt("bSTART_maxDepths", offsets.maxDepthsStart);
        StructurePostProcess.SetInt("bSTART_danglingDepths", offsets.danglingDepthsStart);

        kernel = StructurePostProcess.FindKernel("BuildStructureBins");
        StructurePostProcess.SetBuffer(kernel, "counters", UtilityBuffers.GenerationBuffer);
        StructurePostProcess.SetBuffer(kernel, "intermediateStructures", UtilityBuffers.GenerationBuffer);
        StructurePostProcess.SetBuffer(kernel, "binHeads", UtilityBuffers.GenerationBuffer);
        StructurePostProcess.SetBuffer(kernel, "binList", UtilityBuffers.GenerationBuffer);
        StructurePostProcess.SetInt("bCOUNT_intermediate", offsets.intermediateStructCounter);
        StructurePostProcess.SetInt("bCOUNT_binList", offsets.binListCounter);
        StructurePostProcess.SetInt("bSTART_intermediate", offsets.intermediateStructStart);
        StructurePostProcess.SetInt("bSTART_binHeads", offsets.binHeadsStart);
        StructurePostProcess.SetInt("bSTART_binList", offsets.binListStart);
        StructurePostProcess.SetInt("oCellOffset", originOffset);

        kernel = StructurePostProcess.FindKernel("CalculateMinDepths");
        StructurePostProcess.SetBuffer(kernel, "counters", UtilityBuffers.GenerationBuffer);
        StructurePostProcess.SetBuffer(kernel, "intermediateStructures", UtilityBuffers.GenerationBuffer);
        StructurePostProcess.SetBuffer(kernel, "binHeads", UtilityBuffers.GenerationBuffer);
        StructurePostProcess.SetBuffer(kernel, "binList", UtilityBuffers.GenerationBuffer);
        StructurePostProcess.SetBuffer(kernel, "maxDepths", UtilityBuffers.GenerationBuffer);
        StructurePostProcess.SetBuffer(kernel, "danglingDepths", UtilityBuffers.GenerationBuffer);
        StructurePostProcess.SetInt("bCOUNT_paths", offsets.anchorPathCounter);
        StructurePostProcess.SetInt("bCOUNT_intermediate", offsets.intermediateStructCounter);
        StructurePostProcess.SetInt("bSTART_capCounts", offsets.batchSocketCapCounter);
        StructurePostProcess.SetInt("capCapacity", capSectionCapacity);
        StructurePostProcess.SetInt("bSTART_intermediate", offsets.intermediateStructStart);
        StructurePostProcess.SetInt("bSTART_binHeads", offsets.binHeadsStart);
        StructurePostProcess.SetInt("bSTART_binList", offsets.binListStart);
        StructurePostProcess.SetInt("bSTART_maxDepths", offsets.maxDepthsStart);
        StructurePostProcess.SetInt("bSTART_danglingDepths", offsets.danglingDepthsStart);

        kernel = StructurePostProcess.FindKernel("EmitFinalStructures");
        StructurePostProcess.SetBuffer(kernel, "counters", UtilityBuffers.GenerationBuffer);
        StructurePostProcess.SetBuffer(kernel, "intermediateStructures", UtilityBuffers.GenerationBuffer);
        StructurePostProcess.SetBuffer(kernel, "finalStructures", UtilityBuffers.GenerationBuffer);
        StructurePostProcess.SetBuffer(kernel, "maxDepths", UtilityBuffers.GenerationBuffer);
        StructurePostProcess.SetBuffer(kernel, "danglingDepths", UtilityBuffers.GenerationBuffer);
        StructurePostProcess.SetInt("bCOUNT_paths", offsets.anchorPathCounter);
        StructurePostProcess.SetInt("bCOUNT_intermediate", offsets.intermediateStructCounter);
        StructurePostProcess.SetInt("bCOUNT_final", offsets.finalStructsCounter);
        StructurePostProcess.SetInt("bSTART_capCounts", offsets.batchSocketCapCounter);
        StructurePostProcess.SetInt("capCapacity", capSectionCapacity);
        StructurePostProcess.SetInt("bSTART_intermediate", offsets.intermediateStructStart);
        StructurePostProcess.SetInt("bSTART_final", offsets.finalStructsStart);
        StructurePostProcess.SetInt("bSTART_maxDepths", offsets.maxDepthsStart);
        StructurePostProcess.SetInt("bSTART_danglingDepths", offsets.danglingDepthsStart);
        Structure.Generator.SetStructIDSettings(StructurePostProcess);
        Structure.Generator.SetStructIDSettings(PathSetupRetriever);
        Shader.SetGlobalInt("_StructTestDot", Config.CURRENT.Generation.Structures.value.StructureDictionary.RetrieveIndex("Dot"));
    }

    public static bool PlanStructureSystems(int chunkSize, int depth, int3 CCoord) {
        if (depth > jigsaw.MaxSystemLoD) return false;

        int counterStart = offsets.countersRange.x;
        int counterEnd = offsets.countersRange.y;
        UtilityBuffers.ClearRange(
            UtilityBuffers.GenerationBuffer,
            counterEnd - counterStart,
            counterStart
        );
        SampleSystemAnchors(chunkSize, depth, CCoord);
        ConnectGraphAnchors(chunkSize, depth, CCoord);
        SanitateComputeBatches(chunkSize, depth, CCoord);
        PreparePathPlannerBatches();
        PopulatePathsWithStructures(chunkSize, depth, CCoord);
        PruneIntersectionsAndEmitFinalStructures(chunkSize, depth, CCoord);

        UtilityBuffers.CopyBufferRegion(
            Generator.offsets.finalStructsCounter,
            Generator.offsets.finalStructsStart,
            Structure.Generator.offsets.structureCounter,
            Structure.Generator.offsets.structureStart,
            Creator.STRUCTURE_STRIDE_WORD
        );
        return true;
    }

    public static void SampleSystemAnchors(int chunkSize, int depth, int3 CCoord) {
        int worldChunkSize = chunkSize * (1 << depth);
        int paddedChunkSize = worldChunkSize + jigsaw.MaxConnectionDist * 4;

        int cellsPerChunk = chunkSize / jigsaw.CellSize;
        int numCellsPerAxis = Mathf.CeilToInt((float)paddedChunkSize / jigsaw.CellSize);
        AnchorSampler.SetInts("oCCoord", new int[] {CCoord.x, CCoord.y, CCoord.z});
        AnchorSampler.SetInt("cellsPerChunk", cellsPerChunk);
        AnchorSampler.SetInt("numPointsPerAxis", numCellsPerAxis);
        UtilityBuffers.SetSampleData(AnchorSampler, (float3)(CCoord * chunkSize), 1);

        int kernel = AnchorSampler.FindKernel("SamplePoints");
        AnchorSampler.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out uint _, out _);
        int numGroupsPerAxis = Mathf.CeilToInt(numCellsPerAxis / (float)threadGroupSize);

        AnchorSampler.Dispatch(kernel, numGroupsPerAxis, numGroupsPerAxis, numGroupsPerAxis);
        kernel = AnchorSampler.FindKernel("PoissonPrune");
        AnchorSampler.Dispatch(kernel, numGroupsPerAxis, numGroupsPerAxis, numGroupsPerAxis);
    }

    public static void ConnectGraphAnchors(int chunkSize, int depth, int3 CCoord) {
        int worldChunkSize = chunkSize * (1 << depth);
        int paddedChunkSize = worldChunkSize + jigsaw.MaxConnectionDist * 4;

        int cellsPerChunk = chunkSize / jigsaw.CellSize;
        int numCellsPerAxis = Mathf.CeilToInt((float)paddedChunkSize / jigsaw.CellSize);
        GraphConnector.SetInts("oCCoord", new int[] {CCoord.x, CCoord.y, CCoord.z});
        GraphConnector.SetInt("cellsPerChunk", cellsPerChunk);
        GraphConnector.SetInt("numPointsPerAxis", numCellsPerAxis);

        int kernel = GraphConnector.FindKernel("ClearSockets");
        ComputeBuffer args = UtilityBuffers.CountToArgs(GraphConnector, UtilityBuffers.GenerationBuffer, offsets.anchorDictCounter, kernel);
        GraphConnector.DispatchIndirect(kernel, args);

        kernel = GraphConnector.FindKernel("SetSocketConnections");
        args = UtilityBuffers.CountToArgs(GraphConnector, UtilityBuffers.GenerationBuffer, offsets.anchorDictCounter, kernel);
        GraphConnector.DispatchIndirect(kernel, args);
        
        kernel = GraphConnector.FindKernel("ConnectGraph");
        args = UtilityBuffers.CountToArgs(GraphConnector, UtilityBuffers.GenerationBuffer, offsets.anchorDictCounter, kernel);
        GraphConnector.DispatchIndirect(kernel, args);
    }

    public static void SanitateComputeBatches(int chunkSize, int depth, int3 CCoord) {
        UtilityBuffers.SetSampleData(SanitateBatches, (float3)(CCoord * chunkSize), 1);
        chunkSize *= 1 << depth;
        chunkSize += jigsaw.MaxConnectionDist * 4;

        int kernel = SanitateBatches.FindKernel("SelectAnchorPieces");
        ComputeBuffer args = UtilityBuffers.CountToArgs(SanitateBatches, UtilityBuffers.GenerationBuffer, offsets.anchorDictCounter, kernel);
        SanitateBatches.DispatchIndirect(kernel, args);

        kernel = SanitateBatches.FindKernel("GetRealEndpoints");
        args = UtilityBuffers.CountToArgs(SanitateBatches, UtilityBuffers.GenerationBuffer, offsets.anchorPathCounter, kernel);
        SanitateBatches.DispatchIndirect(kernel, args);

        SanitateBatches.SetInt("numPointsPerAxis", 1);
        SanitateBatches.SetInt("numVoxelsPerChunk", chunkSize);

        kernel = SanitateBatches.FindKernel("FilterPathBatches");
        args = UtilityBuffers.CountToArgs(SanitateBatches, UtilityBuffers.GenerationBuffer, offsets.anchorPathCounter, kernel);
        SanitateBatches.DispatchIndirect(kernel, args);
    }

    private static void PreparePathPlannerBatches() {
        PathBatchPlanner.SetInt("maxVisitedNodesPerBatch", offsets.maxVisitedNodesPerBatch);
        PathBatchPlanner.SetInt("maxPathsPerBatch", offsets.maxPathsPerBatch);

        int kernel = PathBatchPlanner.FindKernel("CountPathSizes");
        ComputeBuffer args = UtilityBuffers.CountToArgs(PathBatchPlanner, UtilityBuffers.GenerationBuffer, offsets.anchorPathCounter, kernel);
        PathBatchPlanner.DispatchIndirect(kernel, args);

        kernel = PathBatchPlanner.FindKernel("FinalizePathPlanner");
        PathBatchPlanner.Dispatch(kernel, 1, 1, 1);
    }

    private static void LogAppendBufferRegionCounts(string phase) {
        const int pathMeetWord = 2;

        int counterStart = offsets.anchorDictCounter;
        int counterEnd = offsets.binListCounter;
        int counterLength = counterEnd - counterStart + 1;
        if (counterLength <= 0)
            return;

        int[] counters = new int[counterLength];
        UtilityBuffers.GenerationBuffer.GetData(counters, 0, counterStart, counterLength);

        int anchorDictCount = Mathf.Max(0, counters[offsets.anchorDictCounter - counterStart]);
        int anchorPathCount = Mathf.Max(0, counters[offsets.anchorPathCounter - counterStart]);
        int intermediateStructCount = Mathf.Max(0, counters[offsets.intermediateStructCounter - counterStart]);
        int finalStructCount = Mathf.Max(0, counters[offsets.finalStructsCounter - counterStart]);
        int socketCapCount = Mathf.Max(0, counters[offsets.batchSocketCapCounter - counterStart]);
        int binListCount = Mathf.Max(0, counters[offsets.binListCounter - counterStart]);
        int metPathCount = 0;

        if (anchorPathCount > 0) {
            uint[] pathMeetData = new uint[anchorPathCount * pathMeetWord];
            UtilityBuffers.GenerationBuffer.GetData(pathMeetData, 0, offsets.pathMeetStart * pathMeetWord, pathMeetData.Length);
            for (int pathIndex = 0; pathIndex < anchorPathCount; pathIndex++) {
                uint meetIndex = pathMeetData[pathIndex * pathMeetWord];
                if (meetIndex != uint.MaxValue)
                    metPathCount++;
            }
        }

        Debug.Log(//
            $"{phase} append buffer counts: " +
            $"anchorDict={anchorDictCount}, " +
            $"anchorPaths={anchorPathCount}, " +
            $"metPaths={metPathCount}/{anchorPathCount}, " +
            $"intermediateStructures={intermediateStructCount}, " +
            $"finalStructures={finalStructCount}, " +
            $"socketCaps={socketCapCount}, " +
            $"binList={binListCount}"
        );
    }

    public static void PopulatePathsWithStructures(int chunkSize, int depth, int3 CCoord) {
        int worldChunkSize = chunkSize * (1 << depth);
        worldChunkSize += jigsaw.MaxConnectionDist * 4;

        int plannerBatchCount = offsets.maxBatchesPerChunk;
        UtilityBuffers.SetSampleData(PathSetupRetriever, (float3)(CCoord * chunkSize), 1);
        UtilityBuffers.SetSampleData(StructurePathfinder, (float3)(CCoord * chunkSize), 1);
        PathSetupRetriever.SetInt("numPointsPerAxis", worldChunkSize);
        StructurePathfinder.SetInt("numPointsPerAxis", worldChunkSize);
        PathSetupRetriever.SetInt("numVoxelsPerChunk", worldChunkSize);
        StructurePathfinder.SetInt("numVoxelsPerChunk", worldChunkSize);

        int kernel = PathBatchPlanner.FindKernel("ClearVisitedPathIds");
        PathBatchPlanner.SetInt("clearNodeCount", offsets.maxVisitedNodesPerBatch);
        PathBatchPlanner.GetKernelThreadGroupSizes(kernel, out uint clearThreads, out uint _, out _);
        int clearGroups = Mathf.CeilToInt(offsets.maxVisitedNodesPerBatch / (float)clearThreads);
        PathBatchPlanner.Dispatch(kernel, clearGroups, 1, 1);
        
        
        for (int index = 0; index < plannerBatchCount; index++) {
            kernel = StructurePathfinder.FindKernel("BatchPathfind");
            StructurePathfinder.SetInt("batchIndex", index);
            uint pathArgsOffsetBytes = GetPlannerDispatchArgsOffsetBytes(index, 0);
            StructurePathfinder.DispatchIndirect(kernel, UtilityBuffers.GenerationBuffer, pathArgsOffsetBytes);
            
            kernel = PathSetupRetriever.FindKernel("BacktrackGridPath");
            PathSetupRetriever.SetInt("batchIndex", index);
            uint backtrackArgsOffsetBytes = GetPlannerDispatchArgsOffsetBytes(index, 1);
            PathSetupRetriever.DispatchIndirect(kernel, UtilityBuffers.GenerationBuffer, backtrackArgsOffsetBytes);
        }

        kernel = PathSetupRetriever.FindKernel("CapDanglingSockets");
        ComputeBuffer capArgs = UtilityBuffers.CountToArgs(PathSetupRetriever, UtilityBuffers.GenerationBuffer, offsets.batchSocketCapCounter, kernel);
        PathSetupRetriever.DispatchIndirect(kernel, capArgs);
        //
        //LogAppendBufferRegionCounts("PopulatePathsWithStructures");
    }

    private static void PruneIntersectionsAndEmitFinalStructures(int chunkSize, int depth, int3 CCoord) {
        int worldChunkSize = chunkSize * (1 << depth);
        worldChunkSize += jigsaw.MaxConnectionDist * 4;

        int binSize = jigsaw.StructureColoringBinSize;
        int numBinsPerAxis = Mathf.CeilToInt(worldChunkSize / (float)binSize);
        int maxCellsPerChunkAxis = Mathf.CeilToInt((float)worldChunkSize / jigsaw.CellSize);
        int maxCellsPerChunk = maxCellsPerChunkAxis * maxCellsPerChunkAxis * maxCellsPerChunkAxis;
        int maxPathsPerChunk = maxCellsPerChunk * 6;
        int binListCapacity = maxPathsPerChunk * 3;

        StructurePostProcess.SetInt("numVoxelsPerChunk", worldChunkSize);
        StructurePostProcess.SetInt("numBinsPerAxis", numBinsPerAxis);
        StructurePostProcess.SetInt("binListCapacity", binListCapacity);
        StructurePostProcess.SetInts("oCCoord", new int[] { CCoord.x, CCoord.y, CCoord.z });

        int kernel = StructurePostProcess.FindKernel("InitStructurePruneState");
        StructurePostProcess.GetKernelThreadGroupSizes(kernel, out uint binThreads, out uint _, out _);
        int binGroupsPerAxis = Mathf.CeilToInt(numBinsPerAxis / (float)binThreads);
        StructurePostProcess.Dispatch(kernel, binGroupsPerAxis, binGroupsPerAxis, binGroupsPerAxis);

        kernel = StructurePostProcess.FindKernel("InitDepthTables");
        ComputeBuffer args = UtilityBuffers.CountToArgs(StructurePostProcess, UtilityBuffers.GenerationBuffer, offsets.anchorPathCounter, kernel);
        StructurePostProcess.DispatchIndirect(kernel, args);

        kernel = StructurePostProcess.FindKernel("BuildStructureBins");
        args = UtilityBuffers.CountToArgs(StructurePostProcess, UtilityBuffers.GenerationBuffer, offsets.intermediateStructCounter, kernel);
        StructurePostProcess.DispatchIndirect(kernel, args);

        kernel = StructurePostProcess.FindKernel("CalculateMinDepths");
        args = UtilityBuffers.CountToArgs(StructurePostProcess, UtilityBuffers.GenerationBuffer, offsets.intermediateStructCounter, kernel);
        StructurePostProcess.DispatchIndirect(kernel, args);

        kernel = StructurePostProcess.FindKernel("EmitFinalStructures");
        args = UtilityBuffers.CountToArgs(StructurePostProcess, UtilityBuffers.GenerationBuffer, offsets.intermediateStructCounter, kernel);
        StructurePostProcess.DispatchIndirect(kernel, args);
    }

    public struct SSystemOffsets : BufferOffsets {
        public int anchorDictCounter;
        public int anchorPathCounter;
        public int intermediateStructCounter;
        public int finalStructsCounter;
        public int batchSocketCapCounter;
        public int binListCounter;
        public int2 countersRange;
        public int maxBatchesPerChunk;
        public int maxVisitedNodesPerBatch;
        public int maxCapsPerBatch;
        public int maxPathsPerBatch;

        public int anchorsStart;
        public int anchorDictStart;
        public int socketUsageStart;
        public int anchorPathStart;
        public int pathEndsStart;
        public int pathMeetStart;
        public int batchPathStart;
        public int batchSocketCapStart;
        public int batchVistStart;
        public int frontierStart;
        public int pathPrefixStart;
        public int batchPrefixStart;
        public int batchDispatchArgsStart;
        public int batchRangesStart;
        public int binHeadsStart;
        public int binListStart;
        public int intermediateStructStart;
        public int maxDepthsStart;
        public int danglingDepthsStart;
        public int finalStructsStart;

        private int offsetStart; private int offsetEnd;
        /// <summary> The start of the buffer region that is used by the ssystem generator. 
        /// See <see cref="offsetss.bufferStart"/> for more info. </summary>
        public int bufferStart{get{return offsetStart;}} 
        /// <summary> The end of the buffer region that is used by the ssystem generator. 
        /// See <see cref="offsetss.bufferEnd"/> for more info. </summary>
        public int bufferEnd{get{return offsetEnd;}}

        const int ANCHOR_STRIDE_WORD = 3 + 1 + 1;
        const int ANCHOR_DICT_WORD = 1;
        const int SOCKET_USAGE_WORD = 6;
        const int ANCHOR_PATH_WORD = 3;
        const int PATH_ENDS_WORD = 6;
        const int PATH_MEET_WORD = 2;
        const int PATH_INDEX_WORD = 1;
        const int STRUCT_SOCKET_WORD = 3;
        const int VISITED_NODE_WORD = 2;
        const int FRONTIER_NODE_WORD = 1;
        const int PATH_PREFIX_WORD = 3;
        const int BATCH_PREFIX_WORD = 2;
        public const int DISPATCH_ARGS_WORD = 3;
        public const int BATCH_RANGE_WORD = 2;
        public const int PLANNER_DISPATCH_ARGS_PER_BATCH = 2;
        const int BIN_HEAD_WORD = 1;
        const int BIN_LIST_NODE_WORD = 2;

        const int GEN_STRUCT_WORD = 4;
        const int GEN_STRUCT_INFO_WORD = 2;
        const int INTERMEDIATE_STRUCT_WORD = GEN_STRUCT_WORD + GEN_STRUCT_INFO_WORD;
        const int DEPTH_TABLE_WORD = 1;
        const int DANGLING_DEPTH_TABLE_WORD = 2;
        const int AVG_STRUCTS_PER_PATH = 3;
        const int MAX_VISITED_NODES_PER_BATCH = 224 * 224 * 224;

        private static void LogLargestBufferRegions(string[] regionNames, int[] regionWordCounts, int totalWords, int topCount)
        {
            int[] topIndices = new int[topCount];
            for (int i = 0; i < topCount; i++)
                topIndices[i] = -1;

            for (int regionIndex = 0; regionIndex < regionNames.Length; regionIndex++) {
                int candidateBytes = regionWordCounts[regionIndex] * sizeof(int);
                for (int slot = 0; slot < topCount; slot++) {
                    int existingIndex = topIndices[slot];
                    int existingBytes = existingIndex >= 0 ? regionWordCounts[existingIndex] * sizeof(int) : -1;
                    if (candidateBytes > existingBytes) {
                        for (int shift = topCount - 1; shift > slot; shift--)
                            topIndices[shift] = topIndices[shift - 1];
                        topIndices[slot] = regionIndex;
                        break;
                    }
                }
            }

            string log = $"SSystemOffsets largest {topCount} buffer regions (total {(totalWords * sizeof(int)):N0} bytes):\n";
            for (int rank = 0; rank < topCount; rank++) {
                int regionIndex = topIndices[rank];
                if (regionIndex < 0)
                    continue;

                int words = regionWordCounts[regionIndex];
                int bytes = words * sizeof(int);
                float mb = bytes / (1024f * 1024f);
                log += $"{rank + 1}. {regionNames[regionIndex]}: {words:N0} words ({bytes:N0} bytes, {mb:F2} MB)\n";
            }

            Debug.Log(log.TrimEnd());
        }

        //... this buffer is sectioned way too much lol
        public SSystemOffsets(int maxChunkAxis, int maxPathLength, int cellSize, int binSize, int bufferStart) {
            maxChunkAxis += maxPathLength * 4;
            int maxCellsPerChunkAxis = Mathf.CeilToInt((float)maxChunkAxis / cellSize);
            int maxBinsPerAxis = Mathf.CeilToInt((float)maxChunkAxis / binSize);

            maxVisitedNodesPerBatch = MAX_VISITED_NODES_PER_BATCH;
            long estimatedPathVisitedNodes = Math.Max(1L, (long)maxPathLength * maxPathLength * maxPathLength);

            int maxCellsPerChunk = maxCellsPerChunkAxis * maxCellsPerChunkAxis * maxCellsPerChunkAxis;
            int maxPathsPerChunk = maxCellsPerChunk * 6;
            this.maxPathsPerBatch = Mathf.Max(1, (int)Math.Min((long)maxPathsPerChunk, MAX_VISITED_NODES_PER_BATCH / estimatedPathVisitedNodes));
            this.maxBatchesPerChunk = Mathf.Max(1, Mathf.CeilToInt(maxPathsPerChunk / (float)Mathf.Max(maxPathsPerBatch, 1)));
            maxCapsPerBatch = maxPathsPerBatch;
            int maxCapsPerChunk = maxBatchesPerChunk * maxCapsPerBatch;
            int maxBinsPerChunk = maxBinsPerAxis * maxBinsPerAxis * maxBinsPerAxis;
            int maxIntermediateStructs = maxPathsPerChunk * AVG_STRUCTS_PER_PATH;
            int maxFinalStructs = maxPathsPerChunk * AVG_STRUCTS_PER_PATH;
            int maxBinListNodes = maxFinalStructs;

            this.offsetStart = bufferStart;
            anchorDictCounter = 0; anchorPathCounter = 1;
            intermediateStructCounter = 2; finalStructsCounter = 3;
            batchSocketCapCounter = finalStructsCounter + 1;
            binListCounter = batchSocketCapCounter + 1;
            int counterEnd = binListCounter + 1;
            
            batchPrefixStart = Mathf.CeilToInt((float)counterEnd / BATCH_PREFIX_WORD);
            int BatchPrefixEndInd_W = (batchPrefixStart + maxBatchesPerChunk) * BATCH_PREFIX_WORD;

            batchDispatchArgsStart = BatchPrefixEndInd_W;
            int BatchDispatchArgsEndInd_W = batchDispatchArgsStart + maxBatchesPerChunk * PLANNER_DISPATCH_ARGS_PER_BATCH * DISPATCH_ARGS_WORD;

            batchRangesStart = Mathf.CeilToInt((float)BatchDispatchArgsEndInd_W / BATCH_RANGE_WORD);
            int BatchRangesEndInd_W = (batchRangesStart + maxBatchesPerChunk) * BATCH_RANGE_WORD;

            int prefixEndInd_W = BatchRangesEndInd_W;
            countersRange = new (anchorDictCounter, prefixEndInd_W);

            anchorsStart = Mathf.CeilToInt((float)prefixEndInd_W / ANCHOR_STRIDE_WORD);
            int AnchorEndInd_W = (anchorsStart + maxCellsPerChunk) * ANCHOR_STRIDE_WORD;

            anchorDictStart = Mathf.CeilToInt((float)AnchorEndInd_W / ANCHOR_DICT_WORD);
            int AnchorDictEndInd_W = (anchorDictStart + maxCellsPerChunk) * ANCHOR_DICT_WORD;

            socketUsageStart = AnchorDictEndInd_W;
            int SocketUsageEndInd_W = socketUsageStart + maxCellsPerChunk * SOCKET_USAGE_WORD;

            anchorPathStart = Mathf.CeilToInt((float)SocketUsageEndInd_W / ANCHOR_PATH_WORD);
            int AnchorPathEndInd_W = (anchorPathStart + maxPathsPerChunk) * ANCHOR_PATH_WORD;

            pathEndsStart = Mathf.CeilToInt((float)AnchorPathEndInd_W / PATH_ENDS_WORD);
            int PathEndsEndInd_W = (pathEndsStart + maxPathsPerChunk) * PATH_ENDS_WORD;

            pathMeetStart = Mathf.CeilToInt((float)PathEndsEndInd_W / PATH_MEET_WORD);
            int PathMeetEndInd_W = (pathMeetStart + maxPathsPerChunk) * PATH_MEET_WORD;
            
            batchPathStart = Mathf.CeilToInt((float)PathMeetEndInd_W / PATH_INDEX_WORD);
            int BatchPathsEndInd_W = (batchPathStart + maxPathsPerChunk) * PATH_INDEX_WORD;

            //This one is not mathematically accurate upper bound but an estimate
            batchSocketCapStart = Mathf.CeilToInt((float)BatchPathsEndInd_W / STRUCT_SOCKET_WORD); 
            int BatchSocketEndInd_W = (batchSocketCapStart + maxBatchesPerChunk * maxPathsPerBatch) * STRUCT_SOCKET_WORD;

            batchVistStart = Mathf.CeilToInt((float)BatchSocketEndInd_W / VISITED_NODE_WORD); 
            int BatchVisitedEndInd_W = (batchVistStart + maxVisitedNodesPerBatch) * VISITED_NODE_WORD;

            frontierStart = Mathf.CeilToInt((float)BatchVisitedEndInd_W / FRONTIER_NODE_WORD);
            int FrontierEndInd_W = (frontierStart + maxVisitedNodesPerBatch) * FRONTIER_NODE_WORD;

            pathPrefixStart = Mathf.CeilToInt((float)FrontierEndInd_W / PATH_PREFIX_WORD);
            int PathPrefixEndInd_W = (pathPrefixStart + maxPathsPerChunk) * PATH_PREFIX_WORD;

            // Postprocess bin buffers are only used after pathfinding/planner buffers are no longer needed,
            // so they can reuse that workspace when capacity allows. Start at batchVisit to reuse
            // both visited + frontier capacity instead of leaving that region idle.
            int postProcessBinWorkspaceStart_W = batchVistStart;
            int candidateBinHeadsStart = Mathf.CeilToInt((float)postProcessBinWorkspaceStart_W / BIN_HEAD_WORD);
            int candidateBinHeadsEndInd_W = (candidateBinHeadsStart + maxBinsPerChunk) * BIN_HEAD_WORD;
            int candidateBinListStart = Mathf.CeilToInt((float)candidateBinHeadsEndInd_W / BIN_LIST_NODE_WORD);
            int candidateBinListEndInd_W = (candidateBinListStart + maxBinListNodes) * BIN_LIST_NODE_WORD;

            binHeadsStart = candidateBinHeadsStart;
            binListStart = candidateBinListStart;

            // Intermediate structures are produced before pathfinding and consumed again during postprocess,
            // so they must not overlap any transient pathfinding workspace (visited/frontier/pathPrefix) or bins.
            int persistentRegionStart_W = Math.Max(Math.Max(BatchRangesEndInd_W, PathPrefixEndInd_W), candidateBinListEndInd_W);

            intermediateStructStart = Mathf.CeilToInt((float)persistentRegionStart_W / INTERMEDIATE_STRUCT_WORD);
            int IntermediateStructEndInd_W = (intermediateStructStart + maxIntermediateStructs) * INTERMEDIATE_STRUCT_WORD;

            maxDepthsStart = Mathf.CeilToInt((float)IntermediateStructEndInd_W / DEPTH_TABLE_WORD);
            int MaxDepthsEndInd_W = (maxDepthsStart + maxPathsPerChunk) * DEPTH_TABLE_WORD;

            danglingDepthsStart = Mathf.CeilToInt((float)MaxDepthsEndInd_W / DANGLING_DEPTH_TABLE_WORD);
            int DanglingDepthsEndInd_W = (danglingDepthsStart + maxCapsPerChunk) * DANGLING_DEPTH_TABLE_WORD;

            finalStructsStart = Mathf.CeilToInt((float)DanglingDepthsEndInd_W / GEN_STRUCT_WORD);
            offsetEnd = (finalStructsStart + maxFinalStructs) * GEN_STRUCT_WORD;

            string[] regionNames = new string[] {
                "Counters",
                "Anchors",
                "AnchorDict",
                "SocketUsage",
                "AnchorPaths",
                "PathEndpoints",
                "PathMeet",
                "BatchPathList",
                "SocketCaps",
                "BatchVisited",
                "Frontier",
                "PathPrefix",
                "BatchPrefix",
                "BatchDispatchArgs",
                "BatchRanges",
                "BinHeads",
                "BinList",
                "IntermediateStructures",
                "MaxDepths",
                "DanglingDepths",
                "FinalStructures"
            };

            int[] regionWordCounts = new int[] {
                counterEnd - anchorDictCounter,
                maxCellsPerChunk * ANCHOR_STRIDE_WORD,
                maxCellsPerChunk * ANCHOR_DICT_WORD,
                maxCellsPerChunk * SOCKET_USAGE_WORD,
                maxPathsPerChunk * ANCHOR_PATH_WORD,
                maxPathsPerChunk * PATH_ENDS_WORD,
                maxPathsPerChunk * PATH_MEET_WORD,
                maxPathsPerChunk * PATH_INDEX_WORD,
                maxBatchesPerChunk * maxPathsPerBatch * STRUCT_SOCKET_WORD,
                maxVisitedNodesPerBatch * VISITED_NODE_WORD,
                maxVisitedNodesPerBatch * FRONTIER_NODE_WORD,
                maxPathsPerChunk * PATH_PREFIX_WORD,
                maxBatchesPerChunk * BATCH_PREFIX_WORD,
                maxBatchesPerChunk * PLANNER_DISPATCH_ARGS_PER_BATCH * DISPATCH_ARGS_WORD,
                maxBatchesPerChunk * BATCH_RANGE_WORD,
                maxBinsPerChunk * BIN_HEAD_WORD,
                maxBinListNodes * BIN_LIST_NODE_WORD,
                maxIntermediateStructs * INTERMEDIATE_STRUCT_WORD,
                maxPathsPerChunk * DEPTH_TABLE_WORD,
                maxCapsPerChunk * DANGLING_DEPTH_TABLE_WORD,
                maxFinalStructs * GEN_STRUCT_WORD
            };

            int topCount = Mathf.Min(6, regionNames.Length);
            LogLargestBufferRegions(regionNames, regionWordCounts, offsetEnd, topCount);
        }
    }
}
}