using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.Data.Models;
using Arterra.Utils;
using Unity.Mathematics;
using UnityEngine;
using static Arterra.Core.Storage.SharedResourceManager;

namespace Arterra.Engine.Terrain.MeshGeneration {
    public sealed class CustomModelRenderer {
        private static ComputeShader shader;
        private static int modelCount;
        private static int scatterKernel;
        private static int collectKernel;
        private static int emitKernel;

        static CustomModelRenderer() {
            shader = Resources.Load<ComputeShader>("Compute/TerrainGeneration/BaseGeneration/CustomModels");
            scatterKernel = shader.FindKernel("ScatterMetadata");
            collectKernel = shader.FindKernel("CollectModels");
            emitKernel = shader.FindKernel("EmitModels");
        }

        // Called with the mesh generator's other presets, after Map.Creator establishes its offsets.
        public static void PresetData(GraphicsContextId graphicsContext = GraphicsContextId.Rendering) {
            var context = Graphics(graphicsContext);
            var offsets = Map.Creator.bufferOffsets;
            BindPreset(context, shader, offsets, context.Work.Scratch, offsets.mapStart, 0);
        }

        private ComputeShader regionShader;
        private Map.Creator.GeoGenOffsets regionOffsets;
        private int metadataStart;

        public CustomModelRenderer() { }

        public CustomModelRenderer(GraphicsResourceContext context, Map.Creator.GeoGenOffsets offsets,
            ComputeBuffer map, int mapStart, int metadataStart, bool compactVertices = false, int flagsStart = -1, float? isoLevel = null) {
            regionShader = Object.Instantiate(shader);
            if (compactVertices) regionShader.EnableKeyword("COMPACT_MODEL_VERTICES");
            regionOffsets = offsets;
            this.metadataStart = metadataStart;
            BindPreset(context, regionShader, offsets, map, mapStart, metadataStart, flagsStart, isoLevel);
        }

        public void Release() {
            if (regionShader != null) Object.DestroyImmediate(regionShader);
            regionShader = null;
        }

        private static void BindPreset(GraphicsResourceContext context, ComputeShader shader,
            Map.Creator.GeoGenOffsets offsets, ComputeBuffer map, int mapStart, int metadataStart, int flagsStart = -1, float? isoLevel = null) {
            context.SetFloat(shader, "IsoLevel", isoLevel ?? Config.CURRENT.Quality.Terrain.value.IsoLevel);
            modelCount = Config.CURRENT.Generation.Models.Reg.Count;
            context.SetInt(shader, "numModels", modelCount);
            context.SetInt(shader, "numMaterials", Config.CURRENT.Generation.Materials.value.MaterialDictionary.Count());
            context.SetInt(shader, "useModelFlags", flagsStart >= 0 ? 1 : 0);
            context.SetInt(shader, "bSTART_modelFlags", math.max(0, flagsStart));
            context.SetInt(shader, "bSTART_map", mapStart);
            context.SetInt(shader, "bSTART_metadata", metadataStart);
            context.SetInt(shader, "bSTART_modelDict", offsets.modelLutStart);
            context.SetInt(shader, "bSTART_models", offsets.modelStart);
            context.SetInt(shader, "bSTART_verts", offsets.vertStart);
            context.SetInt(shader, "bSTART_modelT", offsets.modelTriStart);
            context.SetInt(shader, "bCOUNT_models", offsets.modelCounter);
            context.SetInts(shader, "counterInd", new int[] { offsets.vertexCounter, offsets.modelTriCounter });

            context.SetBuffer(shader, scatterKernel, "modelDict", context.Work.Scratch);
            context.SetBuffer(shader, scatterKernel, "Metadata", context.Work.Transfer);
            context.SetBuffer(shader, collectKernel, "MapData", map);
            context.SetBuffer(shader, collectKernel, "modelDict", context.Work.Scratch);
            context.SetBuffer(shader, collectKernel, "Metadata", context.Work.Transfer);
            context.SetBuffer(shader, collectKernel, "chunkModels", context.Work.Scratch);
            context.SetBuffer(shader, collectKernel, "counter", context.Work.Scratch);
            context.SetBuffer(shader, emitKernel, "MapData", map);
            context.SetBuffer(shader, emitKernel, "ModelFlags", context.Work.Transfer);
            context.SetBuffer(shader, emitKernel, "chunkModels", context.Work.Scratch);
            context.SetBuffer(shader, emitKernel, "vertexes", context.Work.Scratch);
            context.SetBuffer(shader, emitKernel, "triangles", context.Work.Scratch);
            context.SetBuffer(shader, emitKernel, "counter", context.Work.Scratch);
        }

        public void GenerateRegion(GraphicsResourceContext context, int3 size, List<uint> entries = null,
            int3? mapSize = null, int3? mapOffset = null) {
            if (modelCount <= 2) return;
            if (entries != null && entries.Count != 0)
                context.SetBufferData(context.Work.Transfer, entries.ToArray(), 0, metadataStart * 2, entries.Count);
            Dispatch(context, regionShader, regionOffsets, size, mapSize ?? size, mapOffset ?? int3.zero,
                (entries?.Count ?? 0) / 2, mapOffset ?? int3.zero);
        }

        public void Generate(GraphicsResourceContext context, int chunkSize, int3? chunkCoord = null) {
            if (modelCount <= 2) return;

            var entries = new List<uint>();
            if (chunkCoord.HasValue && CPUMapManager.TryGetChunkInfo(chunkCoord.Value, out var chunk)) {
                foreach (var pair in chunk.MapMetaData) {
                    if (!pair.Value.TryGet(out ModelRenderInfo info)) continue;
                    entries.Add(pair.Key);
                    entries.Add(info.Data);
                }
            }

            int count = entries.Count / 2;
            if (entries.Count != 0) context.SetBufferData(context.Work.Transfer, entries.ToArray());
            Dispatch(context, shader, Map.Creator.bufferOffsets, chunkSize, chunkSize + 3, new int3(1), count);
        }

        private static void Dispatch(GraphicsResourceContext context, ComputeShader shader,
            Map.Creator.GeoGenOffsets offsets, int3 size, int3 mapSize, int3 mapOffset, int count, int3 positionOffset = default) {
            context.SetInts(shader, ShaderIDProps.ModelPositionOffset, new int[] { positionOffset.x, positionOffset.y, positionOffset.z });
            context.SetInt(shader, ShaderIDProps.MetadataCount, count);
            context.SetInts(shader, ShaderIDProps.ModelGridSize, new int[] { size.x, size.y, size.z });
            context.SetInts(shader, ShaderIDProps.ModelMapSize, new int[] { mapSize.x, mapSize.y, mapSize.z });
            context.SetInts(shader, ShaderIDProps.ModelMapOffset, new int[] { mapOffset.x, mapOffset.y, mapOffset.z });

            if (count != 0) {
                shader.GetKernelThreadGroupSizes(scatterKernel, out uint threadGroupSize, out _, out _);
                int numThreads = Mathf.CeilToInt(count / (float)threadGroupSize);
                context.Dispatch(shader, scatterKernel, numThreads, 1, 1);
            }

            shader.GetKernelThreadGroupSizes(collectKernel, out uint tx, out uint ty, out uint tz);
            int3 groups = (int3)math.ceil((float3)size / new float3(tx, ty, tz));
            context.Dispatch(shader, collectKernel, groups.x, groups.y, groups.z);

            ComputeBuffer args = context.Args.CountToArgs(shader, context.Work.Scratch,
                countOffset: offsets.modelCounter, kernel: emitKernel);
            context.DispatchIndirect(shader, emitKernel, args);
        }
    }
}
