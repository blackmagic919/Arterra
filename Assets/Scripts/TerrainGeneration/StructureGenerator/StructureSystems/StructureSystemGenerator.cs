using Arterra.Configuration;
using Arterra.Data.Structure.Jigsaw;
using Arterra.Utils;
using Arterra.Core.Storage;
using Arterra.Editor;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using Newtonsoft.Json;
using FMOD.Studio;
using System;
using System.Text;
using Arterra.Data.Biome;
using static Arterra.Core.Storage.SharedResourceManager;

namespace Arterra.Data.Structure.Jigsaw {
    [Serializable]
    public class StructureSystem {
        //Warning don't make this too small or memory overflow
        public int CellSizeFactor = 4;
        public int MaxConnectionDist = 64;

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
        public int MaxBatchExecute = 16;
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
        Structure.Creator structureCreator = new();
        GraphicsResourceContext gpuContext = structureCreator.GetGraphicsContext();
        offsets = new SSystemOffsets();
        Configuration.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain.value;

        int originOffset = -(jigsaw.MaxConnectionDist * 2);
        int cellsPerChunk = rSettings.mapChunkSize / jigsaw.CellSize;
        int maxChunkSize = rSettings.mapChunkSize * (1 << jigsaw.MaxSystemLoD);
        offsets = new SSystemOffsets(maxChunkSize, jigsaw.MaxConnectionDist, jigsaw.CellSize, jigsaw.StructureColoringBinSize, 0);
        int capSectionCapacity = offsets.maxCapsPerBatch * offsets.maxBatchesPerChunk;

        int kernel = AnchorSampler.FindKernel("SamplePoints");
        gpuContext.SetBuffer(AnchorSampler, kernel, "anchors", gpuContext.Work.Scratch);
        gpuContext.SetInt(AnchorSampler, "coarseSSystemNoise", jigsaw.CoarseSSystemNoise);
        gpuContext.SetInt(AnchorSampler, "fineSSystemNoise", jigsaw.FineSSystemNoise);
        gpuContext.SetInt(AnchorSampler, "cellSize", jigsaw.CellSize);
        gpuContext.SetInt(AnchorSampler, "cellsPerChunk", cellsPerChunk);
        gpuContext.SetInt(AnchorSampler, "bSTART_anchors", offsets.anchorsStart);
        gpuContext.SetInt(AnchorSampler, "oCellOffset", originOffset);
        structureCreator.SetStructIDSettings(AnchorSampler);

        kernel = AnchorSampler.FindKernel("PoissonPrune");
        gpuContext.SetBuffer(AnchorSampler, kernel, "anchors", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(AnchorSampler, kernel, "anchorDict", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(AnchorSampler, kernel, "counters", gpuContext.Work.Scratch);
        gpuContext.SetInt(AnchorSampler, "bCOUNT_dict", offsets.anchorDictCounter);
        gpuContext.SetInt(AnchorSampler, "bSTART_dict", offsets.anchorDictStart);

        kernel = GraphConnector.FindKernel("ClearSockets");
        gpuContext.SetBuffer(GraphConnector, kernel, "counters", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(GraphConnector, kernel, "anchorDict", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(GraphConnector, kernel, "socketUsage", gpuContext.Work.Scratch);
        gpuContext.SetInt(GraphConnector, "oCellOffset", originOffset);
        gpuContext.SetInt(GraphConnector, "cellsPerChunk", cellsPerChunk);
        gpuContext.SetInt(GraphConnector, "bCOUNT_dict", offsets.anchorDictCounter);
        gpuContext.SetInt(GraphConnector, "bSTART_dict", offsets.anchorDictStart);
        gpuContext.SetInt(GraphConnector, "bSTART_sockets", offsets.socketUsageStart);

        kernel = GraphConnector.FindKernel("SetSocketConnections");
        gpuContext.SetBuffer(GraphConnector, kernel, "counters", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(GraphConnector, kernel, "anchorDict", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(GraphConnector, kernel, "socketUsage", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(GraphConnector, kernel, "anchors", gpuContext.Work.Scratch);
        gpuContext.SetInt(GraphConnector, "cellSize", jigsaw.CellSize);
        gpuContext.SetInt(GraphConnector, "connectRadius", jigsaw.MaxConnectionDist);
        gpuContext.SetInt(GraphConnector, "bSTART_anchors", offsets.anchorsStart);

        kernel = GraphConnector.FindKernel("ConnectGraph");
        gpuContext.SetBuffer(GraphConnector, kernel, "counters", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(GraphConnector, kernel, "anchorDict", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(GraphConnector, kernel, "socketUsage", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(GraphConnector, kernel, "anchors", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(GraphConnector, kernel, "anchorPaths", gpuContext.Work.Scratch);
        gpuContext.SetInt(GraphConnector, "cellSize", jigsaw.CellSize);
        gpuContext.SetInt(GraphConnector, "connectRadius", jigsaw.MaxConnectionDist);
        gpuContext.SetInt(GraphConnector, "bCOUNT_paths", offsets.anchorPathCounter);
        gpuContext.SetInt(GraphConnector, "bSTART_anchors", offsets.anchorsStart);
        gpuContext.SetInt(GraphConnector, "bSTART_paths", offsets.anchorPathStart);

        kernel = SanitateBatches.FindKernel("SelectAnchorPieces");
        gpuContext.SetBuffer(SanitateBatches, kernel, "anchors", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(SanitateBatches, kernel, "anchorDict", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(SanitateBatches, kernel, "socketUsage", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(SanitateBatches, kernel, "counters", gpuContext.Work.Scratch);
        gpuContext.SetInt(SanitateBatches, "bCOUNT_dict", offsets.anchorDictCounter);
        gpuContext.SetInt(SanitateBatches, "bSTART_dict", offsets.anchorDictStart);
        gpuContext.SetInt(SanitateBatches, "bSTART_anchors", offsets.anchorsStart);
        gpuContext.SetInt(SanitateBatches, "bSTART_sockets", offsets.socketUsageStart);
        gpuContext.SetInt(SanitateBatches, "bCOUNT_paths", offsets.anchorPathCounter);
        gpuContext.SetInt(SanitateBatches, "bSTART_paths", offsets.anchorPathStart);
        gpuContext.SetInt(SanitateBatches, "bSTART_endpts", offsets.pathEndsStart);
        gpuContext.SetInt(SanitateBatches, "bSTART_pathMeet", offsets.pathMeetStart);
        gpuContext.SetInt(SanitateBatches, "bSTART_anchorConnections", offsets.anchorConnectionStart);
        gpuContext.SetInt(SanitateBatches, "bCOUNT_struct", offsets.intermediateStructCounter);
        gpuContext.SetInt(SanitateBatches, "bSTART_struct", offsets.intermediateStructStart);
        structureCreator.SetStructIDSettings(SanitateBatches);

        kernel = SanitateBatches.FindKernel("GetRealEndpoints");
        gpuContext.SetBuffer(SanitateBatches, kernel, "counters", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(SanitateBatches, kernel, "anchors", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(SanitateBatches, kernel, "anchorPaths", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(SanitateBatches, kernel, "endPoints", gpuContext.Work.Scratch);

        kernel = SanitateBatches.FindKernel("CountAnchorConnections");
        gpuContext.SetBuffer(SanitateBatches, kernel, "counters", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(SanitateBatches, kernel, "anchorPaths", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(SanitateBatches, kernel, "pathMeet", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(SanitateBatches, kernel, "anchorConnections", gpuContext.Work.Scratch);

        kernel = SanitateBatches.FindKernel("FilterPathAnchors");
        gpuContext.SetBuffer(SanitateBatches, kernel, "counters", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(SanitateBatches, kernel, "anchorDict", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(SanitateBatches, kernel, "anchors", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(SanitateBatches, kernel, "SocketCaps", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(SanitateBatches, kernel, "anchorConnections", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(SanitateBatches, kernel, "genStructures", gpuContext.Work.Scratch);
        gpuContext.SetInt(SanitateBatches, "oCellOffset", originOffset);
        gpuContext.SetInt(SanitateBatches, "capCapacity", capSectionCapacity);
        gpuContext.SetInt(SanitateBatches, "bSTART_capCounts", offsets.batchSocketCapCounter);
        gpuContext.SetInt(SanitateBatches, "bSTART_caps", offsets.batchSocketCapStart);

        kernel = StructurePathfinder.FindKernel("BatchPathfind");
        gpuContext.SetBuffer(StructurePathfinder, kernel, "batchRanges", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePathfinder, kernel, "batchPathList", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePathfinder, kernel, "endPoints", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePathfinder, kernel, "anchors", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePathfinder, kernel, "anchorPaths", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePathfinder, kernel, "batchVisit", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePathfinder, kernel, "pathMeet", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePathfinder, kernel, "frontierBuffer", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePathfinder, kernel, "pathPrefix", gpuContext.Work.Scratch);
        gpuContext.SetInt(StructurePathfinder, "bSTART_batchPathList", offsets.batchPathStart);
        gpuContext.SetInt(StructurePathfinder, "bSTART_batchRanges", offsets.batchRangesStart);
        gpuContext.SetInt(StructurePathfinder, "bSTART_endpts", offsets.pathEndsStart);
        gpuContext.SetInt(StructurePathfinder, "bSTART_paths", offsets.anchorPathStart);
        gpuContext.SetInt(StructurePathfinder, "bSTART_anchors", offsets.anchorsStart);
        gpuContext.SetInt(StructurePathfinder, "bSTART_visited", offsets.batchVistStart);
        gpuContext.SetInt(StructurePathfinder, "bSTART_pathMeet", offsets.pathMeetStart);
        gpuContext.SetInt(StructurePathfinder, "bSTART_frontier", offsets.frontierStart);
        gpuContext.SetInt(StructurePathfinder, "bSTART_pathPrefix", offsets.pathPrefixStart);

        gpuContext.SetBuffer(StructurePathfinder, kernel, "genStructures", gpuContext.Work.Scratch);
        gpuContext.SetInt(StructurePathfinder, "bCOUNT_struct", offsets.intermediateStructCounter);
        gpuContext.SetInt(StructurePathfinder, "bSTART_struct", offsets.intermediateStructStart);

        int pathfindKernel = StructurePathfinder.FindKernel("BatchPathfind");
        StructurePathfinder.GetKernelThreadGroupSizes(pathfindKernel, out _, out _, out _);

        int backtrackKernel = PathSetupRetriever.FindKernel("BacktrackGridPath");
        PathSetupRetriever.GetKernelThreadGroupSizes(backtrackKernel, out uint backtrackThreadsX, out _, out _);

        // BatchPathfind indexes paths by SV_GroupID.x, so it is one path per workgroup by design.
        plannerPathfindDispatchDivisor = 1;
        plannerBacktrackDispatchDivisor = Mathf.Max(1, (int)backtrackThreadsX);

        kernel = PathBatchPlanner.FindKernel("CountPathSizes");
        gpuContext.SetBuffer(PathBatchPlanner, kernel, "counters", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(PathBatchPlanner, kernel, "endPoints", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(PathBatchPlanner, kernel, "pathPrefix", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(PathBatchPlanner, kernel, "pathMeet", gpuContext.Work.Scratch);
        gpuContext.SetInt(PathBatchPlanner, "bCOUNT_paths", offsets.anchorPathCounter);
        gpuContext.SetInt(PathBatchPlanner, "bSTART_endpts", offsets.pathEndsStart);
        gpuContext.SetInt(PathBatchPlanner, "bSTART_pathPrefix", offsets.pathPrefixStart);
        gpuContext.SetInt(PathBatchPlanner, "bSTART_pathMeet", offsets.pathMeetStart);
        gpuContext.SetInt(PathBatchPlanner, "connectRadius", jigsaw.MaxConnectionDist);

        kernel = PathBatchPlanner.FindKernel("FinalizePathPlanner");
        gpuContext.SetBuffer(PathBatchPlanner, kernel, "counters", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(PathBatchPlanner, kernel, "pathPrefix", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(PathBatchPlanner, kernel, "batchPrefix", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(PathBatchPlanner, kernel, "batchRanges", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(PathBatchPlanner, kernel, "dispatchArgs", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(PathBatchPlanner, kernel, "batchPathList", gpuContext.Work.Scratch);
        gpuContext.SetInt(PathBatchPlanner, "bCOUNT_paths", offsets.anchorPathCounter);
        gpuContext.SetInt(PathBatchPlanner, "bSTART_pathPrefix", offsets.pathPrefixStart);
        gpuContext.SetInt(PathBatchPlanner, "bSTART_batchPrefix", offsets.batchPrefixStart);
        gpuContext.SetInt(PathBatchPlanner, "bSTART_batchRanges", offsets.batchRangesStart);
        gpuContext.SetInt(PathBatchPlanner, "bSTART_dispatchArgs", offsets.batchDispatchArgsStart);
        gpuContext.SetInt(PathBatchPlanner, "bSTART_batchPathList", offsets.batchPathStart);
        gpuContext.SetInt(PathBatchPlanner, "maxBatchCount", offsets.maxBatchesPerChunk);
        gpuContext.SetInt(PathBatchPlanner, "pathfindDispatchDivisor", plannerPathfindDispatchDivisor);
        gpuContext.SetInt(PathBatchPlanner, "backtrackDispatchDivisor", plannerBacktrackDispatchDivisor);

        kernel = PathBatchPlanner.FindKernel("ClearVisitedPathIds");
        gpuContext.SetBuffer(PathBatchPlanner, kernel, "batchVisit", gpuContext.Work.Scratch);
        gpuContext.SetInt(PathBatchPlanner, "bSTART_visited", offsets.batchVistStart);

        kernel = PathSetupRetriever.FindKernel("BacktrackGridPath");
        gpuContext.SetBuffer(PathSetupRetriever, kernel, "batchRanges", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(PathSetupRetriever, kernel, "batchPathList", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(PathSetupRetriever, kernel, "pathPrefix", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(PathSetupRetriever, kernel, "batchVisit", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(PathSetupRetriever, kernel, "endPoints", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(PathSetupRetriever, kernel, "SocketCaps", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(PathSetupRetriever, kernel, "genStructures", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(PathSetupRetriever, kernel, "counters", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(PathSetupRetriever, kernel, "anchorPaths", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(PathSetupRetriever, kernel, "anchors", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(PathSetupRetriever, kernel, "pathMeet", gpuContext.Work.Scratch);
        gpuContext.SetInt(PathSetupRetriever, "bSTART_batchPathList", offsets.batchPathStart);
        gpuContext.SetInt(PathSetupRetriever, "bSTART_batchRanges", offsets.batchRangesStart);
        gpuContext.SetInt(PathSetupRetriever, "bSTART_pathPrefix", offsets.pathPrefixStart);
        gpuContext.SetInt(PathSetupRetriever, "bSTART_capCounts", offsets.batchSocketCapCounter);
        gpuContext.SetInt(PathSetupRetriever, "bCOUNT_struct", offsets.intermediateStructCounter);
        gpuContext.SetInt(PathSetupRetriever, "bSTART_struct", offsets.intermediateStructStart);
        gpuContext.SetInt(PathSetupRetriever, "bSTART_caps", offsets.batchSocketCapStart);
        gpuContext.SetInt(PathSetupRetriever, "bSTART_paths", offsets.anchorPathStart);
        gpuContext.SetInt(PathSetupRetriever, "bSTART_anchors", offsets.anchorsStart);
        gpuContext.SetInt(PathSetupRetriever, "capCapacity", capSectionCapacity);
        gpuContext.SetInt(PathSetupRetriever, "bSTART_visited", offsets.batchVistStart);
        gpuContext.SetInt(PathSetupRetriever, "bSTART_endpts", offsets.pathEndsStart);
        gpuContext.SetInt(PathSetupRetriever, "bSTART_pathMeet", offsets.pathMeetStart);
        gpuContext.SetInt(PathSetupRetriever, "oCellOffset", originOffset);

        kernel = PathSetupRetriever.FindKernel("CapDanglingSockets");
        gpuContext.SetBuffer(PathSetupRetriever, kernel, "counters", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(PathSetupRetriever, kernel, "SocketCaps", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(PathSetupRetriever, kernel, "genStructures", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(PathSetupRetriever, kernel, "danglingDepths", gpuContext.Work.Scratch);
        gpuContext.SetInt(PathSetupRetriever, "bSTART_danglingDepths", offsets.danglingDepthsStart);

        kernel = StructurePostProcess.FindKernel("InitStructurePruneState");
        gpuContext.SetBuffer(StructurePostProcess, kernel, "counters", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePostProcess, kernel, "binHeads", gpuContext.Work.Scratch);
        gpuContext.SetInt(StructurePostProcess, "bCOUNT_final", offsets.finalStructsCounter);
        gpuContext.SetInt(StructurePostProcess, "bCOUNT_binList", offsets.binListCounter);
        gpuContext.SetInt(StructurePostProcess, "bSTART_binHeads", offsets.binHeadsStart);
        gpuContext.SetInt(StructurePostProcess, "chunkSize", rSettings.mapChunkSize);
        gpuContext.SetInt(StructurePostProcess, "binSize", jigsaw.StructureColoringBinSize);

        kernel = StructurePostProcess.FindKernel("InitDepthTables");
        gpuContext.SetBuffer(StructurePostProcess, kernel, "counters", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePostProcess, kernel, "maxDepths", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePostProcess, kernel, "danglingDepths", gpuContext.Work.Scratch);
        gpuContext.SetInt(StructurePostProcess, "bCOUNT_paths", offsets.anchorPathCounter);
        gpuContext.SetInt(StructurePostProcess, "bSTART_capCounts", offsets.batchSocketCapCounter);
        gpuContext.SetInt(StructurePostProcess, "capCapacity", capSectionCapacity);
        gpuContext.SetInt(StructurePostProcess, "bSTART_maxDepths", offsets.maxDepthsStart);
        gpuContext.SetInt(StructurePostProcess, "bSTART_danglingDepths", offsets.danglingDepthsStart);

        kernel = StructurePostProcess.FindKernel("BuildStructureBins");
        gpuContext.SetBuffer(StructurePostProcess, kernel, "counters", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePostProcess, kernel, "intermediateStructures", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePostProcess, kernel, "binHeads", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePostProcess, kernel, "binList", gpuContext.Work.Scratch);
        gpuContext.SetInt(StructurePostProcess, "bCOUNT_intermediate", offsets.intermediateStructCounter);
        gpuContext.SetInt(StructurePostProcess, "bCOUNT_binList", offsets.binListCounter);
        gpuContext.SetInt(StructurePostProcess, "bSTART_intermediate", offsets.intermediateStructStart);
        gpuContext.SetInt(StructurePostProcess, "bSTART_binHeads", offsets.binHeadsStart);
        gpuContext.SetInt(StructurePostProcess, "bSTART_binList", offsets.binListStart);
        gpuContext.SetInt(StructurePostProcess, "oCellOffset", originOffset);

        kernel = StructurePostProcess.FindKernel("CalculateMinDepths");
        gpuContext.SetBuffer(StructurePostProcess, kernel, "counters", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePostProcess, kernel, "intermediateStructures", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePostProcess, kernel, "binHeads", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePostProcess, kernel, "binList", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePostProcess, kernel, "maxDepths", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePostProcess, kernel, "danglingDepths", gpuContext.Work.Scratch);
        gpuContext.SetInt(StructurePostProcess, "bCOUNT_paths", offsets.anchorPathCounter);
        gpuContext.SetInt(StructurePostProcess, "bCOUNT_intermediate", offsets.intermediateStructCounter);
        gpuContext.SetInt(StructurePostProcess, "bSTART_capCounts", offsets.batchSocketCapCounter);
        gpuContext.SetInt(StructurePostProcess, "capCapacity", capSectionCapacity);
        gpuContext.SetInt(StructurePostProcess, "bSTART_intermediate", offsets.intermediateStructStart);
        gpuContext.SetInt(StructurePostProcess, "bSTART_binHeads", offsets.binHeadsStart);
        gpuContext.SetInt(StructurePostProcess, "bSTART_binList", offsets.binListStart);
        gpuContext.SetInt(StructurePostProcess, "bSTART_maxDepths", offsets.maxDepthsStart);
        gpuContext.SetInt(StructurePostProcess, "bSTART_danglingDepths", offsets.danglingDepthsStart);

        kernel = StructurePostProcess.FindKernel("EmitFinalStructures");
        gpuContext.SetBuffer(StructurePostProcess, kernel, "counters", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePostProcess, kernel, "intermediateStructures", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePostProcess, kernel, "finalStructures", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePostProcess, kernel, "maxDepths", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(StructurePostProcess, kernel, "danglingDepths", gpuContext.Work.Scratch);
        gpuContext.SetInt(StructurePostProcess, "bCOUNT_paths", offsets.anchorPathCounter);
        gpuContext.SetInt(StructurePostProcess, "bCOUNT_intermediate", offsets.intermediateStructCounter);
        gpuContext.SetInt(StructurePostProcess, "bCOUNT_final", offsets.finalStructsCounter);
        gpuContext.SetInt(StructurePostProcess, "bSTART_capCounts", offsets.batchSocketCapCounter);
        gpuContext.SetInt(StructurePostProcess, "capCapacity", capSectionCapacity);
        gpuContext.SetInt(StructurePostProcess, "bSTART_intermediate", offsets.intermediateStructStart);
        gpuContext.SetInt(StructurePostProcess, "bSTART_final", offsets.finalStructsStart);
        gpuContext.SetInt(StructurePostProcess, "bSTART_maxDepths", offsets.maxDepthsStart);
        gpuContext.SetInt(StructurePostProcess, "bSTART_danglingDepths", offsets.danglingDepthsStart);
        structureCreator.SetStructIDSettings(StructurePostProcess);
        Shader.SetGlobalInt("_StructTestDot", Config.CURRENT.Generation.Structures.value.StructureDictionary.RetrieveIndex("Dot"));
    }

    public static bool PlanStructureSystems(int chunkSize, int depth, int3 CCoord) {
        GraphicsResourceContext gpuContext = GraphicsGeneration;
        if (depth > jigsaw.MaxSystemLoD) return false;

        int counterStart = offsets.countersRange.x;
        int counterEnd = offsets.countersRange.y;
        gpuContext.Work.ClearRange(
            gpuContext.Work.Scratch,
            counterEnd - counterStart,
            counterStart
        );
        SampleSystemAnchors(chunkSize, depth, CCoord);
        ConnectGraphAnchors(chunkSize, depth, CCoord);
        SanitateComputeBatches(chunkSize, depth, CCoord);
        PreparePathPlannerBatches(chunkSize, depth);
        PopulatePathsWithStructures(chunkSize, depth, CCoord);
        PruneIntersectionsAndEmitFinalStructures(chunkSize, depth, CCoord);

        gpuContext.Work.CopyBufferRegion(
            Generator.offsets.finalStructsCounter,
            Generator.offsets.finalStructsStart,
            Structure.Creator.offsets.structureCounter,
            Structure.Creator.offsets.structureStart,
            Creator.STRUCTURE_STRIDE_WORD
        );
        return true;
    }

    public static void SampleSystemAnchors(int chunkSize, int depth, int3 CCoord) {
        GraphicsResourceContext gpuContext = GraphicsGeneration;
        int worldChunkSize = chunkSize * (1 << depth);
        int paddedChunkSize = worldChunkSize + jigsaw.MaxConnectionDist * 4;

        int cellsPerChunk = chunkSize / jigsaw.CellSize;
        int numCellsPerAxis = Mathf.CeilToInt((float)paddedChunkSize / jigsaw.CellSize);
        gpuContext.SetInts(AnchorSampler, "oCCoord", new int[] {CCoord.x, CCoord.y, CCoord.z});
        gpuContext.SetInt(AnchorSampler, "cellsPerChunk", cellsPerChunk);
        gpuContext.SetInt(AnchorSampler, "numPointsPerAxis", numCellsPerAxis);
        gpuContext.Work.SetSampleData(AnchorSampler, (float3)(CCoord * chunkSize), 1);

        int kernel = AnchorSampler.FindKernel("SamplePoints");
        AnchorSampler.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out uint _, out _);
        int numGroupsPerAxis = Mathf.CeilToInt(numCellsPerAxis / (float)threadGroupSize);

        gpuContext.Dispatch(AnchorSampler, kernel, numGroupsPerAxis, numGroupsPerAxis, numGroupsPerAxis);
        kernel = AnchorSampler.FindKernel("PoissonPrune");
        gpuContext.Dispatch(AnchorSampler, kernel, numGroupsPerAxis, numGroupsPerAxis, numGroupsPerAxis);
    }

    public static void ConnectGraphAnchors(int chunkSize, int depth, int3 CCoord) {
        GraphicsResourceContext gpuContext = GraphicsGeneration;
        int worldChunkSize = chunkSize * (1 << depth);
        int paddedChunkSize = worldChunkSize + jigsaw.MaxConnectionDist * 4;

        int cellsPerChunk = chunkSize / jigsaw.CellSize;
        int numCellsPerAxis = Mathf.CeilToInt((float)paddedChunkSize / jigsaw.CellSize);
        gpuContext.SetInts(GraphConnector, "oCCoord", new int[] {CCoord.x, CCoord.y, CCoord.z});
        gpuContext.SetInt(GraphConnector, "cellsPerChunk", cellsPerChunk);
        gpuContext.SetInt(GraphConnector, "numPointsPerAxis", numCellsPerAxis);

        int kernel = GraphConnector.FindKernel("ClearSockets");
        ComputeBuffer args = gpuContext.Args.CountToArgs(GraphConnector, gpuContext.Work.Scratch, offsets.anchorDictCounter, kernel);
        gpuContext.DispatchIndirect(GraphConnector, kernel, args);

        kernel = GraphConnector.FindKernel("SetSocketConnections");
        args = gpuContext.Args.CountToArgs(GraphConnector, gpuContext.Work.Scratch, offsets.anchorDictCounter, kernel);
        gpuContext.DispatchIndirect(GraphConnector, kernel, args);

        kernel = GraphConnector.FindKernel("ConnectGraph");
        args = gpuContext.Args.CountToArgs(GraphConnector, gpuContext.Work.Scratch, offsets.anchorDictCounter, kernel);
        gpuContext.DispatchIndirect(GraphConnector, kernel, args);
    }

    public static void SanitateComputeBatches(int chunkSize, int depth, int3 CCoord) {
        GraphicsResourceContext gpuContext = GraphicsGeneration;
        gpuContext.Work.SetSampleData(SanitateBatches, (float3)(CCoord * chunkSize), 1);

        int kernel = SanitateBatches.FindKernel("SelectAnchorPieces");
        ComputeBuffer args = gpuContext.Args.CountToArgs(SanitateBatches, gpuContext.Work.Scratch, offsets.anchorDictCounter, kernel);
        gpuContext.DispatchIndirect(SanitateBatches, kernel, args);

        kernel = SanitateBatches.FindKernel("GetRealEndpoints");
        args = gpuContext.Args.CountToArgs(SanitateBatches, gpuContext.Work.Scratch, offsets.anchorPathCounter, kernel);
        gpuContext.DispatchIndirect(SanitateBatches, kernel, args);
    }

    private static void PreparePathPlannerBatches(int chunkSize, int depth) {
        GraphicsResourceContext gpuContext = GraphicsGeneration;
        int worldChunkSize = chunkSize * (1 << depth);
        worldChunkSize += jigsaw.MaxConnectionDist * 4;

        gpuContext.SetInt(PathBatchPlanner, "maxVisitedNodesPerBatch", offsets.maxVisitedNodesPerBatch);
        gpuContext.SetInt(PathBatchPlanner, "maxPathsPerBatch", offsets.maxPathsPerBatch);
        gpuContext.SetInt(PathBatchPlanner, "numVoxelsPerChunk", worldChunkSize);

        int kernel = PathBatchPlanner.FindKernel("CountPathSizes");
        ComputeBuffer args = gpuContext.Args.CountToArgs(PathBatchPlanner, gpuContext.Work.Scratch, offsets.anchorPathCounter, kernel);
        gpuContext.DispatchIndirect(PathBatchPlanner, kernel, args);

        kernel = PathBatchPlanner.FindKernel("FinalizePathPlanner");
        gpuContext.Dispatch(PathBatchPlanner, kernel, 1, 1, 1);
    }

    private static void LogAppendBufferRegionCounts(string phase) {
        GraphicsResourceContext gpuContext = GraphicsGeneration;
        const int pathMeetWord = 2;

        int counterStart = offsets.anchorDictCounter;
        int counterEnd = offsets.binListCounter;
        int counterLength = counterEnd - counterStart + 1;
        if (counterLength <= 0)
            return;

        int[] counters = new int[counterLength];
        gpuContext.Work.Scratch.GetData(counters, 0, counterStart, counterLength);

        int anchorDictCount = Mathf.Max(0, counters[offsets.anchorDictCounter - counterStart]);
        int anchorPathCount = Mathf.Max(0, counters[offsets.anchorPathCounter - counterStart]);
        int intermediateStructCount = Mathf.Max(0, counters[offsets.intermediateStructCounter - counterStart]);
        int finalStructCount = Mathf.Max(0, counters[offsets.finalStructsCounter - counterStart]);
        int socketCapCount = Mathf.Max(0, counters[offsets.batchSocketCapCounter - counterStart]);
        int binListCount = Mathf.Max(0, counters[offsets.binListCounter - counterStart]);
        int metPathCount = 0;

        if (anchorPathCount > 0) {
            uint[] pathMeetData = new uint[anchorPathCount * pathMeetWord];
            gpuContext.Work.Scratch.GetData(pathMeetData, 0, offsets.pathMeetStart * pathMeetWord, pathMeetData.Length);
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
        GraphicsResourceContext gpuContext = GraphicsGeneration;
        int worldChunkSize = chunkSize * (1 << depth);
        worldChunkSize += jigsaw.MaxConnectionDist * 4;

        int plannerBatchCount = math.min(offsets.maxBatchesPerChunk, jigsaw.MaxBatchExecute);
        gpuContext.Work.SetSampleData(PathSetupRetriever, (float3)(CCoord * chunkSize), 1);
        gpuContext.Work.SetSampleData(StructurePathfinder, (float3)(CCoord * chunkSize), 1);
        gpuContext.SetInt(PathSetupRetriever, "numPointsPerAxis", worldChunkSize);
        gpuContext.SetInt(StructurePathfinder, "numPointsPerAxis", worldChunkSize);
        gpuContext.SetInt(PathSetupRetriever, "numVoxelsPerChunk", worldChunkSize);
        gpuContext.SetInt(StructurePathfinder, "numVoxelsPerChunk", worldChunkSize);

        int kernel = PathBatchPlanner.FindKernel("ClearVisitedPathIds");
        gpuContext.SetInt(PathBatchPlanner, "clearNodeCount", offsets.maxVisitedNodesPerBatch);
        PathBatchPlanner.GetKernelThreadGroupSizes(kernel, out uint clearThreads, out uint _, out _);
        int clearGroups = Mathf.CeilToInt(offsets.maxVisitedNodesPerBatch / (float)clearThreads);
        gpuContext.Dispatch(PathBatchPlanner, kernel, clearGroups, 1, 1);

        for (int index = 0; index < plannerBatchCount; index++) {
            kernel = StructurePathfinder.FindKernel("BatchPathfind");
            gpuContext.SetInt(StructurePathfinder, "batchIndex", index);
            uint pathArgsOffsetBytes = GetPlannerDispatchArgsOffsetBytes(index, 0);
            gpuContext.DispatchIndirect(StructurePathfinder, kernel, gpuContext.Work.Scratch, pathArgsOffsetBytes);

            kernel = PathSetupRetriever.FindKernel("BacktrackGridPath");
            gpuContext.SetInt(PathSetupRetriever, "batchIndex", index);
            uint backtrackArgsOffsetBytes = GetPlannerDispatchArgsOffsetBytes(index, 1);
            gpuContext.DispatchIndirect(PathSetupRetriever, kernel, gpuContext.Work.Scratch, backtrackArgsOffsetBytes);
        }

        gpuContext.Work.SetSampleData(SanitateBatches, (float3)(CCoord * chunkSize), 1);
        gpuContext.SetInt(SanitateBatches, "numPointsPerAxis", 1);
        gpuContext.SetInt(SanitateBatches, "numVoxelsPerChunk", worldChunkSize);

        kernel = SanitateBatches.FindKernel("CountAnchorConnections");
        ComputeBuffer args = gpuContext.Args.CountToArgs(SanitateBatches, gpuContext.Work.Scratch, offsets.anchorPathCounter, kernel);
        gpuContext.DispatchIndirect(SanitateBatches, kernel, args);

        kernel = SanitateBatches.FindKernel("FilterPathAnchors");
        args = gpuContext.Args.CountToArgs(SanitateBatches, gpuContext.Work.Scratch, offsets.anchorDictCounter, kernel);
        gpuContext.DispatchIndirect(SanitateBatches, kernel, args);

        kernel = PathSetupRetriever.FindKernel("CapDanglingSockets");
        ComputeBuffer capArgs = gpuContext.Args.CountToArgs(PathSetupRetriever, gpuContext.Work.Scratch, offsets.batchSocketCapCounter, kernel);
        gpuContext.DispatchIndirect(PathSetupRetriever, kernel, capArgs);
        //
        //LogAppendBufferRegionCounts("PopulatePathsWithStructures");
    }

    private static void PruneIntersectionsAndEmitFinalStructures(int chunkSize, int depth, int3 CCoord) {
        GraphicsResourceContext gpuContext = GraphicsGeneration;
        int worldChunkSize = chunkSize * (1 << depth);
        worldChunkSize += jigsaw.MaxConnectionDist * 4;

        int binSize = jigsaw.StructureColoringBinSize;
        int numBinsPerAxis = Mathf.CeilToInt(worldChunkSize / (float)binSize);
        int maxCellsPerChunkAxis = Mathf.CeilToInt((float)worldChunkSize / jigsaw.CellSize);
        int maxCellsPerChunk = maxCellsPerChunkAxis * maxCellsPerChunkAxis * maxCellsPerChunkAxis;
        int maxPathsPerChunk = maxCellsPerChunk * 6;
        int binListCapacity = maxPathsPerChunk * 3;

        gpuContext.Work.SetSampleData(StructurePostProcess, (float3)(CCoord * chunkSize), 1);
        gpuContext.SetInt(StructurePostProcess, "numVoxelsPerChunk", worldChunkSize);
        gpuContext.SetInt(StructurePostProcess, "numBinsPerAxis", numBinsPerAxis);
        gpuContext.SetInt(StructurePostProcess, "binListCapacity", binListCapacity);
        gpuContext.SetInts(StructurePostProcess, "oCCoord", new int[] { CCoord.x, CCoord.y, CCoord.z });

        int kernel = StructurePostProcess.FindKernel("InitStructurePruneState");
        StructurePostProcess.GetKernelThreadGroupSizes(kernel, out uint binThreads, out uint _, out _);
        int binGroupsPerAxis = Mathf.CeilToInt(numBinsPerAxis / (float)binThreads);
        gpuContext.Dispatch(StructurePostProcess, kernel, binGroupsPerAxis, binGroupsPerAxis, binGroupsPerAxis);

        kernel = StructurePostProcess.FindKernel("InitDepthTables");
        ComputeBuffer args = gpuContext.Args.CountToArgs(StructurePostProcess, gpuContext.Work.Scratch, offsets.anchorPathCounter, kernel);
        gpuContext.DispatchIndirect(StructurePostProcess, kernel, args);

        kernel = StructurePostProcess.FindKernel("BuildStructureBins");
        args = gpuContext.Args.CountToArgs(StructurePostProcess, gpuContext.Work.Scratch, offsets.intermediateStructCounter, kernel);
        gpuContext.DispatchIndirect(StructurePostProcess, kernel, args);

        kernel = StructurePostProcess.FindKernel("CalculateMinDepths");
        args = gpuContext.Args.CountToArgs(StructurePostProcess, gpuContext.Work.Scratch, offsets.intermediateStructCounter, kernel);
        gpuContext.DispatchIndirect(StructurePostProcess, kernel, args);

        kernel = StructurePostProcess.FindKernel("EmitFinalStructures");
        args = gpuContext.Args.CountToArgs(StructurePostProcess, gpuContext.Work.Scratch, offsets.intermediateStructCounter, kernel);
        gpuContext.DispatchIndirect(StructurePostProcess, kernel, args);
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
        public int anchorConnectionStart;
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
        const int ANCHOR_CONNECTION_WORD = 1;
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

            anchorConnectionStart = Mathf.CeilToInt((float)counterEnd / ANCHOR_CONNECTION_WORD);
            int AnchorConnectionEndInd_W = (anchorConnectionStart + maxCellsPerChunk) * ANCHOR_CONNECTION_WORD;

            batchPrefixStart = Mathf.CeilToInt((float)AnchorConnectionEndInd_W / BATCH_PREFIX_WORD);
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
                "AnchorConnections",
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
                maxCellsPerChunk * ANCHOR_CONNECTION_WORD,
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
            //LogLargestBufferRegions(regionNames, regionWordCounts, offsetEnd, topCount);
        }
    }
}
}
