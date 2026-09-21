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
    [CreateAssetMenu(menuName = "ShaderData/ShellTexture/Generator")]
    public class ProceduralShellTexturer : GeoShader
    {
        public Catalogue<ShellSetting> settings = default;
        public Option<List<ShellLevel>> detailLevels = default;
        [JsonIgnore]
        [UISetting(Ignore = true)]
        public Option<Material> material;
        [JsonIgnore] private ComputeShader shellCompute;
        [JsonIgnore] private ComputeBuffer variantTable;
        [JsonIgnore] private ComputeBuffer detailTable;

        public override Material GetMaterial() => material.value;
        public override IRegister GetRegistry() => settings;
        public override void SetRegistry(IRegister reg) => settings = (Catalogue<ShellSetting>)reg;

        public override void PresetData(int baseGeoStart, int baseGeoCount, int geoCounter, int geoStart, int geoInd)
        {
            GraphicsResourceContext gpuContext = GraphicsRendering;
            if (settings.Reg.Count == 0) return;
            ShellSetting.Data[] data = settings.Reg.Select(e => e.GetInfo()).ToArray();
            variantTable = new ComputeBuffer(data.Length, ShellSetting.DataSize, ComputeBufferType.Structured);
            detailTable = new ComputeBuffer(detailLevels.value.Count, ShellLevel.DataSize, ComputeBufferType.Structured);
            int mapChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
            gpuContext.SetBufferData(detailTable, detailLevels.value);
            gpuContext.SetBufferData(variantTable, data);

            shellCompute = Resources.Load<ComputeShader>("Compute/GeoShader/Registry/GrassLayers");
            int kernel = shellCompute.FindKernel("Main");
            gpuContext.SetInt(shellCompute, "bSTART_base", baseGeoStart);
            gpuContext.SetInt(shellCompute, "bCOUNT_base", baseGeoCount);
            gpuContext.SetInt(shellCompute, "bSTART_oGeo", geoStart);
            gpuContext.SetInt(shellCompute, "bCOUNT_oGeo", geoCounter);
            gpuContext.SetInt(shellCompute, "numPointsPerAxis", mapChunkSize);
            gpuContext.SetInt(shellCompute, "geoInd", geoInd);
            SubChunkShaderGraph.PresetSubChunkInfo(shellCompute);

            gpuContext.SetBuffer(shellCompute, kernel, "Counters", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(shellCompute, kernel, "DrawTriangles", gpuContext.Work.Scratch);
            gpuContext.SetBuffer(shellCompute, kernel, "VariantSettings", variantTable);
            gpuContext.SetBuffer(shellCompute, kernel, "DetailSettings", detailTable);
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
            int kernel = shellCompute.FindKernel("Main");
            ComputeBuffer vertSource = memoryHandle.GetBlockBuffer(vertAddress);
            ComputeBuffer triSource = memoryHandle.GetBlockBuffer(triAddress);
            GraphicsBuffer addresses = memoryHandle.Address;
            float invScale = 1.0f / (1 << parentDepth);

            ComputeBuffer args = gpuContext.Args.PrefixCountToArgs(shellCompute, gpuContext.Work.Scratch, baseGeoCount);

            gpuContext.SetBuffer(shellCompute, kernel, ShaderIDProps.SourceVertices, vertSource);
            gpuContext.SetBuffer(shellCompute, kernel, ShaderIDProps.SourceTriangles, triSource);
            gpuContext.SetBuffer(shellCompute, kernel, ShaderIDProps.AddressDict, addresses);
            gpuContext.SetInt(shellCompute, ShaderIDProps.VertAddress, vertAddress);
            gpuContext.SetInt(shellCompute, ShaderIDProps.TriAddress, triAddress);
            gpuContext.SetFloat(shellCompute, ShaderIDProps.ScaleInverse, invScale);

            gpuContext.DispatchIndirect(shellCompute, kernel, args);
        }
    }
}
