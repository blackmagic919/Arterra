using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Data.Models;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Utils;
using Arterra.Engine.Terrain.MeshGeneration;
using Arterra.Core.Storage;
using static Arterra.Core.Storage.SharedResourceManager;

namespace Arterra.Editor {
    public class ModelManager {
        private ComputeBuffer MapBuffer;
        private ComputeBuffer GeoBuffer;
        private ComputeShader ModelConstructor;
        private CustomModelRenderer modelRenderer;
        private List<uint> modelMetadata = new();
        private ComputeShader IndexLinker;
        private ComputeShader DrawArgsConstructor;
        public Engine.Terrain.Map.Creator.GeoGenOffsets offsets;
        private uint3 GridSize;
        private float IsoLevel;

        private Transform transform;
        private Material[] ModelMaterial =
        new Material[3];
        private Engine.Terrain.Readback.GeometryHandle[] GeoHandles =
        new Engine.Terrain.Readback.GeometryHandle[3];

        public const int VERTEX_STRIDE_WORD = 3 + 2;
        public const int TRI_STRIDE_WORD = 3;

        public ModelManager(uint3 GridSize, Transform transform, float IsoLevel, ComputeBuffer MapBuffer, ComputeBuffer GeoBuffer, int bufferStart,
            int customVertexCapacity, int customTriangleCapacity) {
            this.ModelConstructor = Resources.Load<ComputeShader>("Compute/CGeometry/Deconstructor/ModelConstructor");
            this.IndexLinker = Resources.Load<ComputeShader>("Compute/CGeometry/Deconstructor/ModelIndexLinker");
            this.DrawArgsConstructor = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Readback/MeshDrawArgs");
            this.GeoBuffer = GeoBuffer;
            this.MapBuffer = MapBuffer;
            this.GridSize = GridSize;
            this.IsoLevel = IsoLevel;
            this.transform = transform;

            this.offsets = new Arterra.Engine.Terrain.Map.Creator.GeoGenOffsets(new int3(GridSize), 0, bufferStart,
                VERTEX_STRIDE_WORD, customVertexCapacity, customTriangleCapacity);
            PresetData();
        }

        ~ModelManager() { Release(); }

        public void Release() {
            for (int i = 0; i < 3; i++) { GameObject.DestroyImmediate(ModelMaterial[i]); }
            ReleaseHandles();
            modelRenderer?.Release();
        }

        void ReleaseHandles() { for (int i = 0; i < 3; i++) { GeoHandles[i]?.Release(); } }

        public void Render() { for (int i = 0; i < 3; i++) { GeoHandles[i]?.Update(); } }


        public void GenerateModel(Camera camera = null) {
            ConstructModel();
            SetupRenderParams(camera);
            Render(); //Render to apply immediately
        }

        public void SetModelData(Dictionary<uint, ModelRenderInfo> metadata, MapData[] map) {
            GraphicsGeneration.SetBufferData(MapBuffer, map);
            modelMetadata = metadata?.SelectMany(pair => new[] { pair.Key, pair.Value.Data }).ToList() ?? new();
        }

        void SetupRenderParams(Camera camera) {
            /*For some Ridiculous reason, unity's Update Loop can run on different thread than OnSceneGUI,
                so releasing at the same time will cause errors in the way it's handled as it is trying to render
                between updates
            */

            GeoHandles[0]?.Release();
            GeoHandles[0] = SetupGeoHandle(camera, offsets.vertStart, offsets.baseTriStart, offsets.baseTriCounter, 0);
            GeoHandles[1]?.Release();
            GeoHandles[1] = SetupGeoHandle(camera, offsets.vertStart, offsets.waterTriStart, offsets.waterTriCounter, 1);
            GeoHandles[2]?.Release();
            GeoHandles[2] = SetupGeoHandle(camera, offsets.vertStart, offsets.modelTriStart, offsets.modelTriCounter, 2);
        }

        void ConstructModel() {
            GraphicsResourceContext gpuContext = GraphicsGeneration;
            //Construct Vertices
            gpuContext.Work.ClearRange(this.GeoBuffer, 5, offsets.bufferStart);
            int kernel = ModelConstructor.FindKernel("March");

            uint3 threadsPerAxis;
            ModelConstructor.GetKernelThreadGroupSizes(kernel, out threadsPerAxis.x, out threadsPerAxis.y, out threadsPerAxis.z);
            threadsPerAxis = new uint3(
                (uint)Mathf.CeilToInt((float)GridSize.x / threadsPerAxis.x),
                (uint)Mathf.CeilToInt((float)GridSize.y / threadsPerAxis.y),
                (uint)Mathf.CeilToInt((float)GridSize.z / threadsPerAxis.z)
            );

            gpuContext.Dispatch(ModelConstructor, kernel, (int)threadsPerAxis.x, (int)threadsPerAxis.y, (int)threadsPerAxis.z);
            LinkTriangles(offsets.baseTriStart, offsets.baseTriCounter);
            LinkTriangles(offsets.waterTriStart, offsets.waterTriCounter);
            modelRenderer.GenerateRegion(gpuContext, (int3)GridSize, modelMetadata);
        }

        void LinkTriangles(int start, int counter) {
            GraphicsResourceContext gpuContext = GraphicsGeneration;
            int kernel = IndexLinker.FindKernel("CSMain");
            ComputeBuffer args = gpuContext.Args.CountToArgs(IndexLinker, this.GeoBuffer, counter);
            gpuContext.SetInt(IndexLinker, "bCOUNT_Tri", counter);
            gpuContext.SetInt(IndexLinker, "bSTART_Tri", start);
            gpuContext.DispatchIndirect(IndexLinker, kernel, args);
        }

        Arterra.Engine.Terrain.Readback.GeometryHandle SetupGeoHandle(Camera camera, int vertStart, int indexStart, int indexCounter, int matInd) {
            GraphicsResourceContext gpuContext = GraphicsGeneration;
            uint drawArgs = gpuContext.Args.DrawArgs.Allocate();

            int kernel = DrawArgsConstructor.FindKernel("CSMain");
            gpuContext.SetBuffer(DrawArgsConstructor, kernel, "counter", this.GeoBuffer);
            gpuContext.SetInt(DrawArgsConstructor, "bCOUNTER", indexCounter);
            gpuContext.SetInt(DrawArgsConstructor, "argOffset", (int)drawArgs);
            gpuContext.SetBuffer(DrawArgsConstructor, kernel, "_IndirectArgsBuffer", gpuContext.Args.DrawArgs.Get());
            gpuContext.Dispatch(DrawArgsConstructor, kernel, 1, 1, 1);

            Vector3 size = new Vector3(GridSize.x, GridSize.y, GridSize.z);
            Bounds BoundsWS = CustomUtility.TransformBounds(transform, new Bounds((size - Vector3.one) / 2f, size));

            RenderParams rp = new RenderParams(this.ModelMaterial[matInd]) {
                worldBounds = BoundsWS,
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows = false,
                matProps = new MaterialPropertyBlock(),
                camera = camera
            };

            rp.matProps.SetBuffer("Vertices", this.GeoBuffer);
            rp.matProps.SetBuffer("Triangles", this.GeoBuffer);
            rp.matProps.SetInt("triAddress", indexStart);
            rp.matProps.SetInt("vertAddress", vertStart);
            rp.matProps.SetMatrix("_LocalToWorld", transform.localToWorldMatrix);

            return new Arterra.Engine.Terrain.Readback.GeometryHandle(rp, default, 0, drawArgs, matInd);
        }

        void PresetData() {
            GraphicsResourceContext gpuContext = GraphicsGeneration;
            this.ModelMaterial[0] = new Material(Shader.Find("Unlit/ModelTerrain"));
            this.ModelMaterial[1] = new Material(Shader.Find("Unlit/ModelLiquid"));
            this.ModelMaterial[2] = new Material(Shader.Find("Unlit/ModelCustom"));
            int metadataStart = ((int)(GridSize.x * GridSize.y * GridSize.z) + 1) / 2;
            modelRenderer = new CustomModelRenderer(gpuContext, offsets, this.MapBuffer, 0, metadataStart, compactVertices: true, isoLevel: IsoLevel);

            int kernel = ModelConstructor.FindKernel("March");
            gpuContext.SetInt(ModelConstructor, "numMaterials", Config.CURRENT.Generation.Materials.value.MaterialDictionary.Count());
            gpuContext.SetBuffer(ModelConstructor, kernel, "MapInfo", this.MapBuffer);
            gpuContext.SetBuffer(ModelConstructor, kernel, "vertexes", this.GeoBuffer);
            gpuContext.SetBuffer(ModelConstructor, kernel, "triangles", this.GeoBuffer);
            gpuContext.SetBuffer(ModelConstructor, kernel, "triangleDict", this.GeoBuffer);
            gpuContext.SetBuffer(ModelConstructor, kernel, "counter", this.GeoBuffer);

            gpuContext.SetInts(ModelConstructor, "counterInd", new int[] { offsets.vertexCounter, offsets.baseTriCounter, offsets.waterTriCounter });
            gpuContext.SetInts(ModelConstructor, "GridSize", new int[] { (int)GridSize.x, (int)GridSize.y, (int)GridSize.z });

            gpuContext.SetInt(ModelConstructor, "bSTART_dict", offsets.dictStart);
            gpuContext.SetInt(ModelConstructor, "bSTART_verts", offsets.vertStart);
            gpuContext.SetInt(ModelConstructor, "bSTART_baseT", offsets.baseTriStart);
            gpuContext.SetInt(ModelConstructor, "bSTART_waterT", offsets.waterTriStart);
            gpuContext.SetFloat(ModelConstructor, "IsoLevel", IsoLevel);

            kernel = IndexLinker.FindKernel("CSMain");
            gpuContext.SetBuffer(IndexLinker, kernel, "triDict", this.GeoBuffer);
            gpuContext.SetBuffer(IndexLinker, kernel, "counter", this.GeoBuffer);
            gpuContext.SetBuffer(IndexLinker, kernel, "BaseTriangles", this.GeoBuffer);
            gpuContext.SetInt(IndexLinker, "bSTART_Dict", offsets.dictStart);
        }
    }
}
