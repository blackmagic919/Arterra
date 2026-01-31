using Newtonsoft.Json;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Configuration.Quality;
using System.Linq;
using System.Collections.Generic;
using Arterra.Utils;

namespace Arterra.Engine.Rendering
{
    [CreateAssetMenu(menuName = "ShaderData/QuadShader/Generator")]
    public class ProceduralInsetQuads : GeoShader
    {
        [Tooltip("A mesh to create foliage from")]
        [SerializeField] public Catalogue<QuadSetting> settings = default;
        public Option<List<QuadLevel>> detailLevels = default;
        [JsonIgnore]
        [UISetting(Ignore = true)]
        public Option<Material> material;
        [JsonIgnore] private ComputeShader quadCompute;
        [JsonIgnore] private ComputeBuffer variantTable;
        [JsonIgnore] private ComputeBuffer detailTable;

        public override Material GetMaterial() => material.value;
        public override IRegister GetRegistry() => settings;
        public override void SetRegistry(IRegister reg) => settings = (Catalogue<QuadSetting>)reg;

        public override void PresetData(int baseGeoStart, int baseGeoCount, int geoCounter, int geoStart, int geoInd)
        {
            if (settings.Reg.Count == 0) return;
            QuadSetting.Data[] data = settings.Reg.Select(e => e.GetInfo()).ToArray();
            variantTable = new ComputeBuffer(data.Length, QuadSetting.DataSize, ComputeBufferType.Structured);
            detailTable = new ComputeBuffer(detailLevels.value.Count, QuadLevel.DataSize, ComputeBufferType.Structured);
            int mapChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
            detailTable.SetData(detailLevels.value);
            variantTable.SetData(data);

            quadCompute = Resources.Load<ComputeShader>("Compute/GeoShader/Registry/FoliageQuads");

            int kernel = quadCompute.FindKernel("Main");
            quadCompute.SetBuffer(kernel, "Counters", UtilityBuffers.GenerationBuffer);
            quadCompute.SetBuffer(kernel, "DrawTriangles", UtilityBuffers.GenerationBuffer);
            quadCompute.SetBuffer(kernel, "VariantSettings", variantTable);
            quadCompute.SetBuffer(kernel, "DetailSettings", detailTable);
            quadCompute.SetInt("numPointsPerAxis", mapChunkSize);
            quadCompute.SetInt("bSTART_base", baseGeoStart);
            quadCompute.SetInt("bCOUNT_base", baseGeoCount);
            quadCompute.SetInt("bSTART_oGeo", geoStart);
            quadCompute.SetInt("bCOUNT_oGeo", geoCounter);
            quadCompute.SetInt("geoInd", geoInd);
            SubChunkShaderGraph.PresetSubChunkInfo(quadCompute);
            material.value.SetBuffer("VariantSettings", variantTable);
        }


        public override void Release()
        {
            variantTable?.Release();
            detailTable?.Release();
        }

        public override void ProcessGeoShader(MemoryBufferHandler memoryHandle, int vertAddress, int triAddress, int baseGeoCount, int parentDepth)
        {
            if (settings.Reg.Count == 0) return;
            int idFoliageKernel = quadCompute.FindKernel("Main");
            ComputeBuffer vertSource = memoryHandle.GetBlockBuffer(vertAddress);
            ComputeBuffer triSource = memoryHandle.GetBlockBuffer(triAddress);
            GraphicsBuffer addresses = memoryHandle.Address;
            float invScale = 1.0f / (1 << parentDepth);

            ComputeBuffer args = UtilityBuffers.PrefixCountToArgs(quadCompute, UtilityBuffers.GenerationBuffer, baseGeoCount);

            quadCompute.SetBuffer(idFoliageKernel, ShaderIDProps.SourceVertices, vertSource);
            quadCompute.SetBuffer(idFoliageKernel, ShaderIDProps.SourceTriangles, triSource);
            quadCompute.SetBuffer(idFoliageKernel, ShaderIDProps.AddressDict, addresses);
            quadCompute.SetInt(ShaderIDProps.VertAddress, vertAddress);
            quadCompute.SetInt(ShaderIDProps.TriAddress, triAddress);
            quadCompute.SetFloat(ShaderIDProps.ScaleInverse, invScale);
            quadCompute.DispatchIndirect(idFoliageKernel, args);
        }
    }
}

