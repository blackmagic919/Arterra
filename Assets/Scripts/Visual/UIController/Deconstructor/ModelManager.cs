using Unity.Mathematics;
using UnityEngine;
using Arterra.Utils;

namespace Arterra.Editor {
    public class ModelManager {
        private ComputeBuffer MapBuffer;
        private ComputeBuffer GeoBuffer;
        private ComputeShader ModelConstructor;
        private ComputeShader IndexLinker;
        private ComputeShader DrawArgsConstructor;
        public Arterra.Engine.Terrain.Map.Generator.GeoGenOffsets offsets;
        private uint3 GridSize;
        private float IsoLevel;

        private Transform transform;
        private Material[] ModelMaterial =
        new Material[2];
        private Arterra.Engine.Terrain.Readback.GeometryHandle[] GeoHandles =
        new Arterra.Engine.Terrain.Readback.GeometryHandle[2];

        public const int VERTEX_STRIDE_WORD = 3 + 2;
        public const int TRI_STRIDE_WORD = 3;

        public ModelManager(uint3 GridSize, Transform transform, float IsoLevel, ComputeBuffer MapBuffer, ComputeBuffer GeoBuffer, int bufferStart) {
            this.ModelConstructor = Resources.Load<ComputeShader>("Compute/CGeometry/Deconstructor/ModelConstructor");
            this.IndexLinker = Resources.Load<ComputeShader>("Compute/CGeometry/Deconstructor/ModelIndexLinker");
            this.DrawArgsConstructor = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Readback/MeshDrawArgs");
            this.GeoBuffer = GeoBuffer;
            this.MapBuffer = MapBuffer;
            this.GridSize = GridSize;
            this.IsoLevel = IsoLevel;
            this.transform = transform;

            this.offsets = new Arterra.Engine.Terrain.Map.Generator.GeoGenOffsets(new int3(GridSize), 0, bufferStart, VERTEX_STRIDE_WORD);
            PresetData();
        }

        ~ModelManager() { Release(); }

        public void Release() {
            for (int i = 0; i < 2; i++) { GameObject.DestroyImmediate(ModelMaterial[i]); }
            ReleaseHandles();
        }

        void ReleaseHandles() { for (int i = 0; i < 2; i++) { GeoHandles[i]?.Release(); } }

        public void Render() { for (int i = 0; i < 2; i++) { GeoHandles[i]?.Update(); } }


        public void GenerateModel(Camera camera = null) {
            ConstructModel();
            SetupRenderParams(camera);
            Render(); //Render to apply immediately
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
        }

        void ConstructModel() {
            //Construct Vertices
            UtilityBuffers.ClearRange(this.GeoBuffer, 3, offsets.bufferStart);
            int kernel = ModelConstructor.FindKernel("March");

            uint3 threadsPerAxis;
            ModelConstructor.GetKernelThreadGroupSizes(kernel, out threadsPerAxis.x, out threadsPerAxis.y, out threadsPerAxis.z);
            threadsPerAxis = new uint3(
                (uint)Mathf.CeilToInt((float)GridSize.x / threadsPerAxis.x),
                (uint)Mathf.CeilToInt((float)GridSize.y / threadsPerAxis.y),
                (uint)Mathf.CeilToInt((float)GridSize.z / threadsPerAxis.z)
            );

            ModelConstructor.Dispatch(kernel, (int)threadsPerAxis.x, (int)threadsPerAxis.y, (int)threadsPerAxis.z);
            LinkTriangles(offsets.baseTriStart, offsets.baseTriCounter);
            LinkTriangles(offsets.waterTriStart, offsets.waterTriCounter);
        }

        void LinkTriangles(int start, int counter) {
            int kernel = IndexLinker.FindKernel("CSMain");
            ComputeBuffer args = UtilityBuffers.CountToArgs(IndexLinker, this.GeoBuffer, counter);
            IndexLinker.SetInt("bCOUNT_Tri", counter);
            IndexLinker.SetInt("bSTART_Tri", start);
            IndexLinker.DispatchIndirect(kernel, args);
        }

        Arterra.Engine.Terrain.Readback.GeometryHandle SetupGeoHandle(Camera camera, int vertStart, int indexStart, int indexCounter, int matInd) {
            uint drawArgs = UtilityBuffers.DrawArgs.Allocate();

            int kernel = DrawArgsConstructor.FindKernel("CSMain");
            DrawArgsConstructor.SetBuffer(kernel, "counter", this.GeoBuffer);
            DrawArgsConstructor.SetInt("bCOUNTER", indexCounter);
            DrawArgsConstructor.SetInt("argOffset", (int)drawArgs);
            DrawArgsConstructor.SetBuffer(kernel, "_IndirectArgsBuffer", UtilityBuffers.DrawArgs.Get());
            DrawArgsConstructor.Dispatch(kernel, 1, 1, 1);

            Vector3 size = new Vector3(GridSize.x, GridSize.y, GridSize.z);
            Bounds BoundsWS = CustomUtility.TransformBounds(transform, new Bounds(size / 2f, size));

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
            this.ModelMaterial[0] = new Material(Shader.Find("Unlit/ModelTerrain"));
            this.ModelMaterial[1] = new Material(Shader.Find("Unlit/ModelLiquid"));

            int kernel = ModelConstructor.FindKernel("March");
            ModelConstructor.SetBuffer(kernel, "MapInfo", this.MapBuffer);
            ModelConstructor.SetBuffer(kernel, "vertexes", this.GeoBuffer);
            ModelConstructor.SetBuffer(kernel, "triangles", this.GeoBuffer);
            ModelConstructor.SetBuffer(kernel, "triangleDict", this.GeoBuffer);
            ModelConstructor.SetBuffer(kernel, "counter", this.GeoBuffer);

            ModelConstructor.SetInts("counterInd", new int[] { offsets.vertexCounter, offsets.baseTriCounter, offsets.waterTriCounter });
            ModelConstructor.SetInts("GridSize", new int[] { (int)GridSize.x, (int)GridSize.y, (int)GridSize.z });

            ModelConstructor.SetInt("bSTART_dict", offsets.dictStart);
            ModelConstructor.SetInt("bSTART_verts", offsets.vertStart);
            ModelConstructor.SetInt("bSTART_baseT", offsets.baseTriStart);
            ModelConstructor.SetInt("bSTART_waterT", offsets.waterTriStart);
            ModelConstructor.SetFloat("IsoLevel", IsoLevel);

            kernel = IndexLinker.FindKernel("CSMain");
            IndexLinker.SetBuffer(kernel, "triDict", this.GeoBuffer);
            IndexLinker.SetBuffer(kernel, "counter", this.GeoBuffer);
            IndexLinker.SetBuffer(kernel, "BaseTriangles", this.GeoBuffer);
            IndexLinker.SetInt("bSTART_Dict", offsets.dictStart);
        }
    }
}
