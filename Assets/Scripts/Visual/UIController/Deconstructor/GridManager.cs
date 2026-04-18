using System;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Utils;

namespace Arterra.Editor {
    public class GridManager {
        public Material GridMaterial;
        public GridOffsets offsets;
        private Transform transform;
        private ComputeBuffer GeoBuffer;
        private ComputeBuffer SelectionBuffer;
        private ComputeShader GridConstructor;
        private Bounds boundsOS;
        private RenderParams renderParams;
        private uint3 GridSize;
        private uint GridPlaneCount {
            get {
                return (uint)(GridSize.x * (GridSize.y - 1) * (GridSize.z - 1) +
                            (GridSize.x - 1) * GridSize.y * (GridSize.z - 1) +
                            (GridSize.x - 1) * (GridSize.y - 1) * GridSize.z);
            }
        }

        public GridManager(uint3 GridSize, Transform transform, ComputeBuffer GeoBuffer, int bufferStart) {
            this.GridMaterial = new Material(Shader.Find("Unlit/GridShader"));
            this.GridConstructor = Resources.Load<ComputeShader>("Compute/CGeometry/Deconstructor/GridConstructor");
            this.offsets = new GridOffsets(new int3(GridSize), bufferStart, 4, 3);
            this.SelectionBuffer = null;
            this.GeoBuffer = GeoBuffer;
            this.GridSize = GridSize;
            this.transform = transform;

            Vector3 size = new Vector3(GridSize.x, GridSize.y, GridSize.z);
            boundsOS = new Bounds(size / 2f, size);
        }

        public GridManager(uint3 GridSize, Transform transform, ComputeBuffer GeoBuffer, int selLen, int bufferStart) {
            this.GridMaterial = new Material(Shader.Find("Unlit/GridShader"));
            this.GridConstructor = Resources.Load<ComputeShader>("Compute/CGeometry/Deconstructor/GridConstructor");
            this.offsets = new GridOffsets(new int3(GridSize), bufferStart, 4, 3);
            this.SelectionBuffer = new ComputeBuffer(math.max(selLen, 1), sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.Dynamic);
            this.GeoBuffer = GeoBuffer;
            this.GridSize = GridSize;
            this.transform = transform;

            Vector3 size = new Vector3(GridSize.x, GridSize.y, GridSize.z);
            boundsOS = new Bounds(size / 2f, size);
        }

        public void GenerateModel(Camera camera = null) {
            ConstructGridGeometry();
            SetupRenderParams(camera, out renderParams, transform);
            Render(); //Render to apply immediately
        }

        ~GridManager() { Release(); }
        public void Release() {
            GameObject.DestroyImmediate(GridMaterial);
            this.SelectionBuffer?.Release();
        }

        public void SetSelectionData(ref SelectionArray SelectionArray) {
            if (this.SelectionBuffer == null || SelectionArray.SelectionData == null) return;
            this.SelectionBuffer.SetData(SelectionArray.SelectionData);
        }

        public void Render() { Graphics.RenderPrimitives(renderParams, MeshTopology.Triangles, (int)GridPlaneCount * 4, 1); }

        void SetupRenderParams(Camera camera, out RenderParams rp, Transform transform) {
            Bounds BoundsWS = CustomUtility.TransformBounds(transform, boundsOS);
            rp = new RenderParams(GridMaterial) {
                worldBounds = BoundsWS,
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows = false,
                matProps = new MaterialPropertyBlock(),
                camera = camera
            };

            rp.matProps.SetBuffer("VertexBuffer", GeoBuffer);
            rp.matProps.SetBuffer("IndexBuffer", GeoBuffer);
            rp.matProps.SetInt("bSTART_index", offsets.indexStart);
            rp.matProps.SetInt("bSTART_vertex", offsets.vertexStart);
            if (SelectionBuffer != null) {
                rp.matProps.SetBuffer("SelectionBuffer", SelectionBuffer);
                rp.matProps.SetInt("_NoSelection", 0);
            } else rp.matProps.SetInt("_NoSelection", 1);
            rp.matProps.SetInt("MapSizeX", (int)GridSize.x); rp.matProps.SetInt("MapSizeY", (int)GridSize.y); rp.matProps.SetInt("MapSizeZ", (int)GridSize.z);
            rp.matProps.SetMatrix("_LocalToWorld", transform.localToWorldMatrix);
        }

        void ConstructGridGeometry() {
            UtilityBuffers.ClearRange(GeoBuffer, 1, offsets.bufferStart);
            int kernel = GridConstructor.FindKernel("CSMain");

            GridConstructor.SetBuffer(kernel, "counter", GeoBuffer);
            GridConstructor.SetBuffer(kernel, "VertexBuffer", GeoBuffer);
            GridConstructor.SetBuffer(kernel, "IndexBuffer", GeoBuffer);
            GridConstructor.SetInt("bCOUNTER_index", offsets.indexCounter);
            GridConstructor.SetInt("bSTART_index", offsets.indexStart);
            GridConstructor.SetInt("bSTART_vertex", offsets.vertexStart);

            GridConstructor.SetInts("GridSize", new int[] { (int)GridSize.x, (int)GridSize.y, (int)GridSize.z });

            uint3 threadsPerAxis;
            GridConstructor.GetKernelThreadGroupSizes(kernel, out threadsPerAxis.x, out threadsPerAxis.y, out threadsPerAxis.z);
            threadsPerAxis = new uint3(
                (uint)Mathf.CeilToInt((float)GridSize.x / threadsPerAxis.x),
                (uint)Mathf.CeilToInt((float)GridSize.y / threadsPerAxis.y),
                (uint)Mathf.CeilToInt((float)GridSize.z / threadsPerAxis.z)
            );

            GridConstructor.Dispatch(kernel, (int)threadsPerAxis.x, (int)threadsPerAxis.y, (int)threadsPerAxis.z);
        }

        public struct SelectionArray {
            public uint[] SelectionData;
            public SelectionArray(int size) {
                // Initialize the array with the required size
                SelectionData = new uint[(size + 31) / 32];
            }

            public SelectionArray Clone() {
                return new SelectionArray {
                    SelectionData = SelectionData == null ? null : (uint[])SelectionData.Clone()
                };
            }

            public bool this[int index] {
                get {
                    if (index >= SelectionData.Length * 32) throw new ArgumentOutOfRangeException(nameof(index));
                    return (SelectionData[index / 32] & (1 << (index % 32))) != 0;
                }
                set {
                    if (index >= SelectionData.Length * 32) throw new ArgumentOutOfRangeException(nameof(index));
                    if (value) SelectionData[index / 32] |= (uint)(1 << (index % 32));
                    else SelectionData[index / 32] &= ~(uint)(1 << (index % 32));
                }
            }
        }

        public struct GridOffsets : BufferOffsets {
            public int indexCounter;
            public int indexStart;
            public int vertexStart;
            private int offsetStart; private int offsetEnd;
            public int bufferStart { get { return offsetStart; } }
            public int bufferEnd { get { return offsetEnd; } }
            public GridOffsets(int3 GridSize, int bufferStart, int indexStride, int vertexStride) {
                int numPoints = GridSize.x * GridSize.y * GridSize.z;
                int GridPlaneCount = (GridSize.x * (GridSize.y - 1) * (GridSize.z - 1) +
                        (GridSize.x - 1) * GridSize.y * (GridSize.z - 1) +
                        (GridSize.x - 1) * (GridSize.y - 1) * GridSize.z);

                this.offsetStart = bufferStart;
                this.indexCounter = bufferStart;

                this.indexStart = Mathf.CeilToInt((float)(indexCounter + 1) / indexStride);
                int indexEnd = indexStart * indexStride + GridPlaneCount * indexStride;

                this.vertexStart = Mathf.CeilToInt((float)indexEnd / vertexStride);
                int vertexEnd = vertexStart * vertexStride + numPoints * vertexStride;

                this.offsetEnd = vertexEnd;
            }
        }
    }
}
