using UnityEngine;
using System;
using System.Threading.Tasks;
using UnityEngine.UI;
using TMPro;
using static SegmentedUIEditor;
using Arterra.Configuration;
using Unity.Mathematics;
using Arterra.Engine.Terrain;
using Arterra.Core.Storage;
using System.Collections;
using static Arterra.Core.Storage.World;
using Arterra.Utils;
using Arterra.Editor;
using static Arterra.Core.Storage.SharedResourceManager;
using Arterra.Core.Network;

namespace Arterra.Data.Intrinsic {
    /// <summary> Settings controlling how the world appears in
    /// the menu when selecting and viewing the world
    /// before entering </summary>
    [Serializable]
    public struct WorldApperance {
        /// <summary> The coordinate in Chunk Space of the chunk that will be
        /// used to generate the chunk display. If this chunk is not saved,
        /// any arbitrary saved chunk will be selected to be displayed. </summary>
        public int3 DisplayedChunk;
        /// <summary> The speed at which the camera rotates around this display chunk. </summary>
        public float3 RotateSpeed;
        /// <summary>
        /// The amount of
        /// </summary>
        public float GridScale;
        public float GridThickness;
    }
}

namespace Arterra.GamePlay.UI {
    public class OptionsHandler : MonoBehaviour {
        private static Animator sAnimator;
        private static GameObject infoContent;
        private static TMP_InputField WorldName;
        private static SingleChunkDisplay spinChunk;
        private static bool active = false;
        private void OnEnable() {
            SystemProtocol.Reset();
            sAnimator = this.gameObject.GetComponent<Animator>();
            infoContent = transform.GetChild(0).Find("Settings").GetComponent<ScrollRect>().content.gameObject;
            WorldName = transform.GetChild(0).Find("WorldName").GetComponent<TMP_InputField>();
            spinChunk = new SingleChunkDisplay();
            active = false;
        }

        private void OnDisable() {
            active = false;
            spinChunk?.Dispose();
        }

        public static async void Activate(Action callback = null) {
            if (active) return;
            active = true;
            var animator = sAnimator;
            try { await EnsureInitializedAsync(); }
            catch (Exception exception) { active = false; Debug.LogException(exception); return; }
            if (!active || !animator || animator != sAnimator) return;

            sAnimator.SetTrigger("Unmask");
            new AnimatorAwaitTask(sAnimator, "MaskRockBreak", InitializeDisplay).Invoke();
            new AnimatorAwaitTask(sAnimator, "UnmaskedAnimation", () => {
                sAnimator.ResetTrigger("Unmask");
                callback?.Invoke();
            }).Invoke();
        }
        public static void Deactivate(Action callback = null) {
            if (!active) return;
            active = false;
            spinChunk?.CancelLoad();

            _ = SaveOptions();
            sAnimator.SetTrigger("Mask");
            new AnimatorAwaitTask(sAnimator, "MaskedAnimation", () => {
                sAnimator.ResetTrigger("Mask");
                ReleaseDisplay(infoContent);
                callback?.Invoke();
            }).Invoke();
        }

        public static void TogglePanel() {
            if (active) Deactivate();
            else Activate();
        }


        public static void EditName() {
            WORLD_SELECTION.First.Value.Name = WorldName.text;
            Task.Run(() => SaveMeta());
        }

        public static void Delete() {
            if (!active) return;
            SelectionHandler.DeleteSelected();
            //Don't call deactivate because it will save the options
            //Which Delete already does
            sAnimator.SetTrigger("Mask");
            new AnimatorAwaitTask(sAnimator, "MaskedAnimation", () => {
                ReleaseDisplay(infoContent);
                MenuHandler.Activate();
            }).Invoke();
            active = false;
        }

        public static void InitializeDisplay() {
            WorldMeta cWorld = WORLD_SELECTION.First.Value;
            WorldName.text = cWorld.Name;
            WorldName.onEndEdit.RemoveAllListeners();
            WorldName.onEndEdit.AddListener((string value) => {
                cWorld.Name = value;
                WorldName.text = value;
            });

            spinChunk.InitializeDisplay();
            ReleaseDisplay(infoContent);
            CreateOptionDisplay(Config.CURRENT, infoContent, (ChildUpdate cb) => {
                object wo = Config.CURRENT;
                cb.Invoke(ref wo);
                Config.Broadcast();
            });
            infoContent.GetComponent<VerticalLayoutGroup>().padding.left = 0;
        }

        public void LateUpdate() {
            if (!active) return;
            spinChunk.Render(this);
        }

        // Called after host chunk storage initializes, even when no preview UI is open.
        public static void RefreshDisplayMap() => SingleChunkDisplay.RefreshDisplayMap();

        private class SingleChunkDisplay {
            private const uint DisplayMapReferenceId = 2;
            private static readonly NetworkVariable<uint[]> DisplayMap = new(DisplayMapReferenceId, retainValueOnDisconnect: true);

            // Chunk storage and the material registry must be initialized for the selected local world.
            public static void RefreshDisplayMap() {
                if (!NetworkManager.IsActingServer) return;
                var coord = Config.CURRENT.System.WorldApperance.value.DisplayedChunk;
                var map = Chunk.ReadChunkMap(coord, 0).map;
                if (map == null && Chunk.TryFindSavedMapChunk(out coord)) {
                    Config.CURRENT.System.WorldApperance.value.DisplayedChunk = coord;
                    Config.CURRENT.System.WorldApperance.IsDirty = true;
                    map = Chunk.ReadChunkMap(coord, 0).map;
                }
                DisplayMap.Value = map == null ? null : Array.ConvertAll(map, point => point.data);
            }

            private int loadVersion;

            private WorldMeta shownWorld;
            private GameObject ChunkDisplay;
            private GameObject CameraController;
            private ModelManager ChunkModel;
            private GridManager ChunkGrid;
            private bool active = false;
            private bool updatedIcon = false;
            public SingleChunkDisplay() {
                ChunkDisplay = GameObject.Find("ChunkDisplay");
                CameraController = ChunkDisplay.transform.Find("Controller").gameObject;
                shownWorld = null;
                updatedIcon = false;
                active = false;
            }

            public async void InitializeDisplay() {
                var meta = WORLD_SELECTION.First.Value;
                if (ReferenceEquals(meta, shownWorld) && active) return;
                int version = ++loadVersion;
                Release();

                try {
                    if (NetworkManager.IsActingServer) {
                        SystemProtocol.MinimalStartup();
                        RefreshDisplayMap();
                    }
                    uint[] packed = await DisplayMap.GetAsync();
                    if (version != loadVersion || !ChunkDisplay ||
                        !ReferenceEquals(meta, WORLD_SELECTION.First.Value)) return;

                    var chunk = packed == null ? null : Array.ConvertAll(packed, bits => new MapData { data = bits });
                    LoadWorldChunkDisplay(chunk);
                    shownWorld = meta;
                } catch (Exception exception) {
                    if (version == loadVersion) Debug.LogException(exception);
                }
            }

            public void CancelLoad() => ++loadVersion;
            public void Dispose() {
                CancelLoad();
                Release();
            }

            private void Release() {
                if (!active) return;
                active = false;
                updatedIcon = false;

                SystemProtocol.Shutdown();
                CameraController.transform.localRotation = Quaternion.identity;
                Transform grid = ChunkDisplay.transform.Find("Grid");
                grid.localPosition = Vector3.zero;
                grid.localScale = Vector3.one;

                Transform model = ChunkDisplay.transform.Find("Model");
                model.localPosition = Vector3.one;

                ChunkModel?.Release();
                ChunkGrid?.Release();
                ChunkModel = null;
                ChunkGrid = null;
            }

            public void Render(MonoBehaviour self) {
                if (!active) return;

                float3 rotSpeed = Config.CURRENT.System.WorldApperance.value.RotateSpeed;
                CameraController.transform.Rotate(rotSpeed * Time.deltaTime);
                ChunkModel?.Render();
                ChunkGrid?.Render();

                if (updatedIcon || shownWorld?.Type != GalaxyType.Local) return;
                updatedIcon = true;
                self.StartCoroutine(CaptureAtEndOfFrame());
            }

            private IEnumerator CaptureAtEndOfFrame() {
                var meta = shownWorld;
                int version = loadVersion;
                yield return new WaitForEndOfFrame();
                if (!active || version != loadVersion || !ReferenceEquals(meta, WORLD_SELECTION.First.Value)) yield break;

                var cam = CameraController.GetComponentInChildren<Camera>();
                string savePath = meta.Path + DisplayChunkPath;

                SaveTextureToFileUtility.SaveRenderTextureToFile(
                    cam.targetTexture,
                    savePath
                );
            }


            private void LoadWorldChunkDisplay(MapData[] chunk) {
                Release();
                SystemProtocol.MinimalStartup();
                active = true;

                GraphicsResourceContext gpuContext = GraphicsGeneration;
                Configuration.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain;
                Data.Intrinsic.WorldApperance wSettings = Config.CURRENT.System.WorldApperance;
                uint chunkSize = (uint)rSettings.mapChunkSize + 2;

                uint3 gridSize = (uint3)Mathf.CeilToInt(chunkSize * wSettings.GridScale);
                Transform grid = ChunkDisplay.transform.Find("Grid");
                grid.position -= (Vector3)(float3)chunkSize / 2.0f;
                grid.localScale = chunkSize / (float3)(gridSize - 1);

                ChunkGrid = new GridManager(gridSize, grid.transform, gpuContext.Work.Scratch, 0);
                ChunkGrid.GridMaterial.SetFloat("_WireframeWidth", ChunkGrid.GridMaterial.GetFloat("_WireframeWidth") * wSettings.GridThickness);
                ChunkGrid.GridMaterial.SetFloat("_VertexSize", ChunkGrid.GridMaterial.GetFloat("_VertexSize") * wSettings.GridThickness);
                ChunkGrid.GenerateModel();

                if (chunk == null) return;
                chunk = CustomUtility.RescaleLinearMap(chunk, rSettings.mapChunkSize, 2, 1);
                Transform model = ChunkDisplay.transform.Find("Model");
                model.position -= (Vector3)(float3)chunkSize / 2.0f;
                ChunkModel = new ModelManager(
                    chunkSize, model,
                    rSettings.IsoLevel, gpuContext.Work.Transfer,
                    gpuContext.Work.Scratch, ChunkGrid.offsets.bufferEnd
                );

                gpuContext.SetBufferData(gpuContext.Work.Transfer, chunk);
                ChunkModel.GenerateModel();
            }

        }

    }
}
