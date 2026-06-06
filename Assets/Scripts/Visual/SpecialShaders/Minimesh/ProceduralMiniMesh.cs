using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Configuration.Quality;
using Arterra.Utils;

namespace Arterra.Engine.Rendering
{
    [CreateAssetMenu(menuName = "ShaderData/MiniMesh/Generator")]
    public class ProceduralMiniMesh : GeoShader
    {
        [SerializeField] public Catalogue<MiniMeshSetting> settings = default;

        [JsonIgnore]
        [UISetting(Ignore = true)]
        public Option<Material> material;

        [JsonIgnore] private ComputeShader miniMeshCompute;
        [JsonIgnore] private ComputeBuffer levelTable;
        [JsonIgnore] private ComputeBuffer meshTable;
        [JsonIgnore] private ComputeBuffer meshVertexTable;
        [JsonIgnore] private int levelsPerVariant;
        [JsonIgnore] private int variantCount;

        public override Material GetMaterial() => material.value;
        public override IRegister GetRegistry() => settings;
        public override void SetRegistry(IRegister reg) => settings = (Catalogue<MiniMeshSetting>)reg;

        public override void PresetData(int baseGeoStart, int baseGeoCount, int geoCounter, int geoStart, int geoInd)
        {
            Release();
            if (settings.Reg.Count == 0)
            {
                return;
            }

            levelsPerVariant = ResolveLevelsPerVariant();
            variantCount = settings.Reg.Count;
            List<MiniMeshLevelCompact> levels = new List<MiniMeshLevelCompact>();
            List<MiniMeshCompact> options = new List<MiniMeshCompact>();
            List<MeshVertex> meshVertices = new List<MeshVertex>();

            foreach (MiniMeshSetting setting in settings.Reg)
            {
                setting.GetInfo(levels, options, meshVertices, levelsPerVariant);
            }

            if (levels.Count == 0)
            {
                return;
            }

            miniMeshCompute = Resources.Load<ComputeShader>("Compute/GeoShader/Registry/MiniMesh");
            int kernel = miniMeshCompute.FindKernel("Main");

            levelTable = new ComputeBuffer(levels.Count, MiniMeshLevelCompact.DataSize, ComputeBufferType.Structured);
            meshTable = new ComputeBuffer(Mathf.Max(options.Count, 1), MiniMeshCompact.DataSize, ComputeBufferType.Structured);
            meshVertexTable = new ComputeBuffer(Mathf.Max(meshVertices.Count, 1), MeshVertex.DataSize, ComputeBufferType.Structured);

            levelTable.SetData(levels);
            if (options.Count > 0)
            {
                meshTable.SetData(options);
            }
            if (meshVertices.Count > 0)
            {
                meshVertexTable.SetData(meshVertices);
            }

            int mapChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
            miniMeshCompute.SetInt("numPointsPerAxis", mapChunkSize);
            miniMeshCompute.SetInt("bSTART_base", baseGeoStart);
            miniMeshCompute.SetInt("bCOUNT_base", baseGeoCount);
            miniMeshCompute.SetInt("bSTART_oGeo", geoStart);
            miniMeshCompute.SetInt("bCOUNT_oGeo", geoCounter);
            miniMeshCompute.SetInt("geoInd", geoInd);
            miniMeshCompute.SetInt("levelsPerVariant", levelsPerVariant);
            miniMeshCompute.SetInt("variantCount", variantCount);

            SubChunkShaderGraph.PresetSubChunkInfo(miniMeshCompute);

            miniMeshCompute.SetBuffer(kernel, "Counters", UtilityBuffers.GenerationBuffer);
            miniMeshCompute.SetBuffer(kernel, "DrawTriangles", UtilityBuffers.GenerationBuffer);
            miniMeshCompute.SetBuffer(kernel, "LevelSettings", levelTable);
            miniMeshCompute.SetBuffer(kernel, "VariantSettings", meshTable);
            miniMeshCompute.SetBuffer(kernel, "MeshVertices", meshVertexTable);

            if (material.value != null)
            {
                material.value.SetBuffer("VariantSettings", meshTable);
                material.value.SetBuffer("MeshVertices", meshVertexTable);
            }
        }

        public override void ProcessGeoShader(MemoryBufferHandler memoryHandle, int vertAddress, int triAddress, int baseGeoCount, int parentDepth)
        {
            if (settings.Reg.Count == 0 || miniMeshCompute == null)
            {
                return;
            }

            int kernel = miniMeshCompute.FindKernel("Main");
            ComputeBuffer vertSource = memoryHandle.GetBlockBuffer(vertAddress);
            ComputeBuffer triSource = memoryHandle.GetBlockBuffer(triAddress);
            GraphicsBuffer addresses = memoryHandle.Address;

            float invScale = 1.0f / (1 << parentDepth);
            ComputeBuffer args = UtilityBuffers.PrefixCountToArgs(miniMeshCompute, UtilityBuffers.GenerationBuffer, baseGeoCount);

            miniMeshCompute.SetBuffer(kernel, ShaderIDProps.SourceVertices, vertSource);
            miniMeshCompute.SetBuffer(kernel, ShaderIDProps.SourceTriangles, triSource);
            miniMeshCompute.SetBuffer(kernel, ShaderIDProps.AddressDict, addresses);
            miniMeshCompute.SetInt(ShaderIDProps.VertAddress, vertAddress);
            miniMeshCompute.SetInt(ShaderIDProps.TriAddress, triAddress);
            miniMeshCompute.SetFloat(ShaderIDProps.ScaleInverse, invScale);

            miniMeshCompute.DispatchIndirect(kernel, args);
        }

        public override void Release()
        {
            levelTable?.Release();
            meshTable?.Release();
            meshVertexTable?.Release();
            levelTable = null;
            meshTable = null;
            meshVertexTable = null;
        }

        private static int ResolveLevelsPerVariant()
        {
            List<GeoShaderSettings.DetailLevel> globalLevels = Config.CURRENT.Quality.GeoShaders.value.levels.value;
            return Mathf.Max(globalLevels?.Count ?? 0, 1);
        }
    }
}
