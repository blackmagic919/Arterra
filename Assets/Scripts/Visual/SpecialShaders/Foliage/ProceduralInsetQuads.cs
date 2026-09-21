using Newtonsoft.Json;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Configuration.Quality;
using System.Linq;
using System.Collections.Generic;
using Arterra.Utils;
using Arterra.Core.Storage;
using static Arterra.Core.Storage.SharedResourceManager;

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
            GraphicsResourceContext gpuContext = GraphicsRendering;
            if (settings.Reg.Count == 0) return;
            QuadSetting.Data[] data = settings.Reg.Select(e => e.GetInfo()).ToArray();
            variantTable = new ComputeBuffer(data.Length, QuadSetting.DataSize, ComputeBufferType.Structured);
            detailTable = new ComputeBuffer(detailLevels.value.Count, QuadLevel.DataSize, ComputeBufferType.Structured);
            int mapChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
            gpuContext.SetBufferData(detailTable, detailLevels.value);
            gpuContext.SetBufferData(variantTable, data);

            quadCompute = Resources.Load<ComputeShader>("Compute/GeoShader/Registry/FoliageQuads");

            int kernel = quadCompute.FindKernel("Main");
            gpuContext.SetBuffer(quadCompute, kernel, "Counters", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(quadCompute, kernel, "DrawTriangles", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(quadCompute, kernel, "VariantSettings", variantTable);
            gpuContext.SetBuffer(quadCompute, kernel, "DetailSettings", detailTable);
            gpuContext.SetInt(quadCompute, "numPointsPerAxis", mapChunkSize);
            gpuContext.SetInt(quadCompute, "bSTART_base", baseGeoStart);
            gpuContext.SetInt(quadCompute, "bCOUNT_base", baseGeoCount);
            gpuContext.SetInt(quadCompute, "bSTART_oGeo", geoStart);
            gpuContext.SetInt(quadCompute, "bCOUNT_oGeo", geoCounter);
            gpuContext.SetInt(quadCompute, "geoInd", geoInd);
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
            GraphicsResourceContext gpuContext = GraphicsRendering;
            if (settings.Reg.Count == 0) return;
            int idFoliageKernel = quadCompute.FindKernel("Main");
            ComputeBuffer vertSource = memoryHandle.GetBlockBuffer(vertAddress);
            ComputeBuffer triSource = memoryHandle.GetBlockBuffer(triAddress);
            GraphicsBuffer addresses = memoryHandle.Address;
            float invScale = 1.0f / (1 << parentDepth);

            ComputeBuffer args = gpuContext.Args.PrefixCountToArgs(quadCompute, gpuContext.Work.Scratch, baseGeoCount);

            gpuContext.SetBuffer(quadCompute, idFoliageKernel, ShaderIDProps.SourceVertices, vertSource);
            gpuContext.SetBuffer(quadCompute, idFoliageKernel, ShaderIDProps.SourceTriangles, triSource);
            gpuContext.SetBuffer(quadCompute, idFoliageKernel, ShaderIDProps.AddressDict, addresses);
            gpuContext.SetInt(quadCompute, ShaderIDProps.VertAddress, vertAddress);
            gpuContext.SetInt(quadCompute, ShaderIDProps.TriAddress, triAddress);
            gpuContext.SetFloat(quadCompute, ShaderIDProps.ScaleInverse, invScale);
            gpuContext.DispatchIndirect(quadCompute, idFoliageKernel, args);
        }
    }
}

