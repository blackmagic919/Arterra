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
    [CreateAssetMenu(menuName = "ShaderData/Tesselation/Generator")]
    public class ProceduralTesselator : GeoShader
    {
        public Catalogue<TesselSettings> settings = default;
        public Option<List<TesselLevel>> detailLevels = default;
        [JsonIgnore]
        [UISetting(Ignore = true)]
        public Option<Material> material;
        [JsonIgnore] private ComputeShader tesselCompute;
        [JsonIgnore] private ComputeBuffer variantTable;
        [JsonIgnore] private ComputeBuffer detailTable;

        // Start is called before the first frame update
        public override Material GetMaterial() => material.value;
        public override IRegister GetRegistry() => settings;
        public override void SetRegistry(IRegister reg) => settings = (Catalogue<TesselSettings>)reg;

        public override void PresetData(int baseGeoStart, int baseGeoCount, int geoCounter, int geoStart, int geoInd)
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            if (settings.Reg.Count == 0) return;
            TesselSettings.Data[] data = settings.Reg.Select(e => e.info).ToArray();
            variantTable = new ComputeBuffer(data.Length, TesselSettings.DataSize, ComputeBufferType.Structured);
            detailTable = new ComputeBuffer(detailLevels.value.Count, ShellLevel.DataSize, ComputeBufferType.Structured);
            int mapChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
            gpuContext.SetBufferData(detailTable, detailLevels.value);
            gpuContext.SetBufferData(variantTable, data);

            tesselCompute = Resources.Load<ComputeShader>("Compute/GeoShader/Registry/SnowTessel");
            int kernel = tesselCompute.FindKernel("Main");
            gpuContext.SetBuffer(tesselCompute, kernel, "Counters", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(tesselCompute, kernel, "DrawTriangles", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(tesselCompute, kernel, "VariantSettings", variantTable);
            gpuContext.SetBuffer(tesselCompute, kernel, "DetailSettings", detailTable);
            SubChunkShaderGraph.PresetSubChunkInfo(tesselCompute);
            gpuContext.SetInt(tesselCompute, "numPointsPerAxis", mapChunkSize);
            gpuContext.SetInt(tesselCompute, "bSTART_base", baseGeoStart);
            gpuContext.SetInt(tesselCompute, "bCOUNT_base", baseGeoCount);
            gpuContext.SetInt(tesselCompute, "bSTART_oGeo", geoStart);
            gpuContext.SetInt(tesselCompute, "bCOUNT_oGeo", geoCounter);
            gpuContext.SetInt(tesselCompute, "geoInd", geoInd);
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
            int kernel = tesselCompute.FindKernel("Main");
            ComputeBuffer vertSource = memoryHandle.GetBlockBuffer(vertAddress);
            ComputeBuffer triSource = memoryHandle.GetBlockBuffer(triAddress);
            GraphicsBuffer addresses = memoryHandle.Address;

            ComputeBuffer args = gpuContext.Args.PrefixCountToArgs(tesselCompute, gpuContext.Work.Scratch, baseGeoCount);

            gpuContext.SetBuffer(tesselCompute, kernel, ShaderIDProps.SourceVertices, vertSource);
            gpuContext.SetBuffer(tesselCompute, kernel, ShaderIDProps.SourceTriangles, triSource);
            gpuContext.SetBuffer(tesselCompute, kernel, ShaderIDProps.AddressDict, addresses);
            gpuContext.SetInt(tesselCompute, ShaderIDProps.VertAddress, vertAddress);
            gpuContext.SetInt(tesselCompute, ShaderIDProps.TriAddress, triAddress);
            gpuContext.DispatchIndirect(tesselCompute, kernel, args);
        }

    }
}
