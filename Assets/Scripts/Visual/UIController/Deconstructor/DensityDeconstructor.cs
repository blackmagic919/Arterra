using System.Collections.Generic;
using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEditor;
using Arterra.Utils;
using System.Linq;
using Arterra.Data.Structure;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.Engine.Terrain;

namespace Arterra.Editor {
    [ExecuteInEditMode]
    public class DensityDeconstructor : MonoBehaviour {
#if UNITY_EDITOR
        [Header("Misc")]
        public float IsoLevel = 0.5f;
        [SerializeField]
        public StructureData Structure;

        [Header("Create")]
        public string loadPath;
        public float3 SDFoffset;

        Queue<uint> Selected;
        GridManager gridManager;
        ModelManager modelManager;
        private bool initialized = false;
        private bool showGrid = false;
        private bool showModel = false;

        private bool isBoxSelecting = false;
        private Vector2 boxSelectStart;
        private Vector2 boxSelectCurrent;
        private bool boxSelectShift;
        private const float BOX_SELECT_CLICK_THRESHOLD = 4.0f;
        private const int HISTORY_CAPACITY = 2;

        private struct EditorState {
            public uint3 GridSize;
            public GridManager.SelectionArray SelectedArray;
            public List<StructureData.PointInfo> MapData;
            public StructureData.PointInfo CurData;
            public StructureData.PointInfo PrevData;

            public EditorState Clone() {
                return new EditorState {
                    GridSize = GridSize,
                    SelectedArray = SelectedArray.Clone(),
                    MapData = MapData == null ? null : MapData.ToList(),
                    CurData = CurData,
                    PrevData = PrevData
                };
            }
        }

        private sealed class UndoRedoHistory {
            private readonly int capacity;
            private readonly List<EditorState> undoHistory = new List<EditorState>();
            private readonly List<EditorState> redoHistory = new List<EditorState>();

            public UndoRedoHistory(int capacity) {
                this.capacity = math.max(capacity, 1);
            }

            private void Trim(List<EditorState> history) {
                while (history.Count > capacity) history.RemoveAt(0);
            }

            public void Clear() {
                undoHistory.Clear();
                redoHistory.Clear();
            }

            public void Push(EditorState state) {
                undoHistory.Add(state);
                Trim(undoHistory);
                redoHistory.Clear();
            }

            public bool TryUndo(EditorState currentState, out EditorState targetState) {
                targetState = default;
                if (undoHistory.Count == 0) return false;

                targetState = undoHistory[undoHistory.Count - 1];
                undoHistory.RemoveAt(undoHistory.Count - 1);
                redoHistory.Add(currentState);
                Trim(redoHistory);
                return true;
            }

            public bool TryRedo(EditorState currentState, out EditorState targetState) {
                targetState = default;
                if (redoHistory.Count == 0) return false;

                targetState = redoHistory[redoHistory.Count - 1];
                redoHistory.RemoveAt(redoHistory.Count - 1);
                undoHistory.Add(currentState);
                Trim(undoHistory);
                return true;
            }
        }

        private readonly UndoRedoHistory undoRedoHistory = new UndoRedoHistory(HISTORY_CAPACITY);
        private EditorState currentState;

        private StructureData.PointInfo curData { get { return currentState.CurData; } set { currentState.CurData = value; } }
        private StructureData.PointInfo prevData { get { return currentState.PrevData; } set { currentState.PrevData = value; } }

        //Don't ask me why the conversion is like this..
        private Vector2 _MousePos { get { return new Vector2(Event.current.mousePosition.x * 2, Camera.current.scaledPixelHeight - Event.current.mousePosition.y * 2); } }
        private uint3 GridSize { get { return currentState.GridSize; } }
        readonly int3[] adjDelta = new int3[6] {
        new int3(-1, 0, 0),
        new int3(1, 0, 0),
        new int3(0, -1, 0),
        new int3(0, 1, 0),
        new int3(0, 0, -1),
        new int3(0, 0, 1)
    };

        public void OnEnable() {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        public void OnDisable() {
            SceneView.duringSceneGui -= OnSceneGUI;
            Release();
        }

        private void ReleaseRuntime(bool clearHistory) {
            if (!initialized) return;
            initialized = false;

            gridManager.Release();
            modelManager.Release();
            Selected?.Clear();
            if (clearHistory) undoRedoHistory.Clear();
        }

        public void Release() {
            ReleaseRuntime(true);
        }

        private void InitializeGrid(bool reloadFromStructure = true) {
            if (reloadFromStructure || currentState.MapData == null) {
                if (Config.CURRENT == null) World.Activate();
                IRegister.Setup(Config.CURRENT); //Initialize Register LUTS
                SystemProtocol.MinimalStartup(); // Initialize Material Information
                Structure.Initialize();
                DeserializeMaterials();

                currentState = new EditorState {
                    GridSize = Structure.settings.value.GridSize,
                    MapData = Structure.map.value.ToList(),
                    CurData = default,
                    PrevData = default,
                    SelectedArray = default
                };
            }

            if (GridSize.x == 0 || GridSize.y == 0 || GridSize.z == 0)
                throw new Exception("Grid size cannot be zero");
            if (initialized) ReleaseRuntime(false);

            int numPoints = (int)(GridSize.x * GridSize.y * GridSize.z);
            currentState.SelectedArray = new GridManager.SelectionArray(numPoints);
            Selected = new Queue<uint>();

            gridManager = new GridManager(GridSize, this.gameObject.transform, UtilityBuffers.GenerationBuffer, numPoints, 0);
            modelManager = new ModelManager(GridSize, this.gameObject.transform, IsoLevel, UtilityBuffers.TransferBuffer, UtilityBuffers.GenerationBuffer, gridManager.offsets.bufferEnd);
            gridManager.GenerateModel();

            initialized = true;
            showGrid = true;
            showModel = true;
        }

        public void SaveData() {
            if (currentState.MapData != null) {
                Structure.map.value = currentState.MapData.ToList();
                Structure.settings.value.GridSize = currentState.GridSize;
            }

            SerializeMaterials();
            EditorUtility.SetDirty(Structure);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Exit edit mode after save so the serialized local material indices
            // are not accidentally edited as if they were global registry indices.
            Release();
        }

        public void LoadData() {
            InitializeGrid(true);
            //Immediately Render Model
            this.UpdateMapData();
            this.modelManager.GenerateModel();
        }

        public void ResizeStructure() {
            InitializeGrid(true);

            int3 offset = (int3)math.floor(SDFoffset);
            int3 nGridSize = math.max((int3)GridSize + offset, int3.zero);
            StructureData.PointInfo[] NewStructure = CustomUtility.RescaleLinearMap(
                currentState.MapData.ToArray(),
                (int3)GridSize,
                offset, 0
            );

            currentState.MapData = NewStructure.ToList();
            currentState.GridSize = (uint3)nGridSize;
            InitializeGrid(false);
            this.UpdateMapData();
            this.modelManager.GenerateModel();
        }

        public void ShiftStructure() {
            InitializeGrid(true);

            int3 offset = (int3)math.floor(SDFoffset);
            StructureData.PointInfo[] NewStructure = CustomUtility.RescaleLinearMap(
                currentState.MapData.ToArray(),
                (int3)GridSize,
                0, offset
            );

            currentState.MapData = NewStructure.ToList();
            InitializeGrid(false);
            this.UpdateMapData();
            this.modelManager.GenerateModel();
        }

        public void ConvertMesh() {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/" + loadPath + ".fbx").GetComponent<MeshFilter>().sharedMesh;
            if (mesh == null) throw new Exception("Mesh not found");
            InitializeGrid(true);

            GetDataFromMesh(mesh);
            this.UpdateMapData();
            this.modelManager.GenerateModel();
        }

        public void LoadChunk() {
            InitializeGrid(true);
            MapData[] map = Chunk.ReadChunkBin(loadPath, 0, out _);
            if (map == null) return;

            Arterra.Configuration.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain;
            currentState.MapData = CustomUtility.RescaleLinearMap(map, rSettings.mapChunkSize, 2, 1)
                .Select(s => new StructureData.PointInfo { data = s.data, preserve = false }).ToList();
            currentState.GridSize = new uint3((uint)rSettings.mapChunkSize,
                (uint)rSettings.mapChunkSize, (uint)rSettings.mapChunkSize) + 2;
            InitializeGrid(false);
            this.UpdateMapData();
            this.modelManager.GenerateModel();
        }

        public void Start() {
            if (Application.isPlaying) this.gameObject.SetActive(false);
        }

        public void Update() {
            if (!initialized) return;

            if (showGrid) this.gridManager.Render();
            if (showModel) this.modelManager.Render();
        }

        public void UpdateMapData() {
            if (currentState.MapData == null) return;
            UtilityBuffers.TransferBuffer.SetData(currentState.MapData.ToArray());
        }
        public void OnSceneGUI(SceneView sceneView) {
            if (!initialized) return;

            Rect GUIBox = new Rect(10, 10, 200, 400);
            GUILayout.BeginArea(GUIBox);
            var headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

            if (Selected.Count > 0) { //Map Data Editor
                GUILayout.Label("MapData", headerStyle);
                GUILayout.BeginHorizontal();
                var reg = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
                StructureData.PointInfo uiData = curData;
                GUILayout.Label("Material");
                DrawMaterialDropdown(reg, uiData.material, GUILayout.Width(100));
                GUILayout.EndHorizontal(); GUILayout.BeginHorizontal();
                GUILayout.Label("Density"); uiData.density = (int)(GUILayout.HorizontalSlider(uiData.density / 255.0f, 0.0f, 1.0f, GUILayout.Width(100)) * 255);
                GUILayout.EndHorizontal(); GUILayout.BeginHorizontal();
                GUILayout.Label("Viscosity"); uiData.viscosity = (int)(GUILayout.HorizontalSlider(uiData.viscosity / 255.0f, 0.0f, 1.0f, GUILayout.Width(100)) * 255);
                GUILayout.EndHorizontal(); GUILayout.BeginHorizontal();
                GUILayout.Label("Preserve Viscosity"); uiData.preserve = GUILayout.Toggle(uiData.preserve, "");
                GUILayout.EndHorizontal();

                // Keep any material chosen in popup while applying slider/toggle edits.
                uiData.material = curData.material;
                curData = uiData;
            }
            GUILayout.Label("Editor Settings", headerStyle);
            showGrid = GUILayout.Button("Toggle Grid") ? !showGrid : showGrid;
            showModel = GUILayout.Button("Toggle Model") ? !showModel : showModel;

            GUILayout.EndArea();

            if (isBoxSelecting || !GUIBox.Contains(_MousePos)) HandleInputs(sceneView);
            if (isBoxSelecting) DrawSelectionRect();
            if (Selected.Count > 0) { HandleMapChange(); }
        }

        private void DrawMaterialDropdown(IRegister registry, int currentMaterialIndex, params GUILayoutOption[] layoutOptions) {
            string currentMaterial = (currentMaterialIndex >= 0 && currentMaterialIndex < registry.Count())
                ? registry.RetrieveName(currentMaterialIndex)
                : "";

            int selectedIndex = 0;
            if (!string.IsNullOrEmpty(currentMaterial) && registry.Contains(currentMaterial))
                selectedIndex = registry.RetrieveIndex(currentMaterial) + 1;

            string buttonText = string.IsNullOrEmpty(currentMaterial) ? "[Select]" : currentMaterial;
            if (!GUILayout.Button(buttonText, EditorStyles.popup, layoutOptions))
                return;

            List<string> options = new List<string>() { "" };
            for (int i = 0; i < registry.Count(); i++) {
                options.Add(registry.RetrieveName(i));
            }

            SearchablePopup popup = new SearchablePopup(
                options,
                selectedIndex,
                (i, val) => {
                    if (string.IsNullOrEmpty(val) || !registry.Contains(val))
                        return;

                    StructureData.PointInfo data = curData;
                    data.material = registry.RetrieveIndex(val);
                    curData = data;
                });

            PopupWindow.Show(new Rect(Event.current.mousePosition.x, Event.current.mousePosition.y, 300, 0), popup);
        }

        private void HandleMapChange() {

            if (curData.data != prevData.data) {
                PushUndoState();
                int deltaDensity = curData.density - prevData.density;
                int deltaViscosity = curData.viscosity - prevData.viscosity;

                UpdateSelected((e) => {
                    e.density = Mathf.Clamp(e.density + deltaDensity, 0, 255);
                    e.viscosity = Mathf.Clamp(e.viscosity + deltaViscosity, 0, e.density);
                    e.material = curData.material == prevData.material ? e.material : curData.material;
                    e.preserve = curData.preserve == prevData.preserve ? e.preserve : curData.preserve;
                    return e;
                });

                this.UpdateMapData();
                this.modelManager.GenerateModel();
                prevData = curData;
            }
        }

        void SetDisplayMapData(StructureData.PointInfo data) {
            curData = data; prevData = data;
        }

        void HandleInputs(SceneView sceneView) {
            bool selectionChanged = false;
            Event currentEvent = Event.current;

            if (currentEvent.type == EventType.KeyDown) {
                if (currentEvent.command && currentEvent.keyCode == KeyCode.Z) {
                    if (currentEvent.shift) Redo();
                    else Undo();
                } else if (currentEvent.keyCode == KeyCode.F) {
                    FocusSelection(sceneView);
                } else if (currentEvent.command) {
                    PushUndoState();
                    if (currentEvent.keyCode == KeyCode.I) InvertSelection();
                    else if (currentEvent.keyCode == KeyCode.N) SwapSelection();
                    else if (currentEvent.keyCode == KeyCode.D) SelectDensity();
                    else if (currentEvent.keyCode == KeyCode.M) SelectMaterial();
                    else if (currentEvent.keyCode == KeyCode.W) SelectWalkable();
                    else return;
                    selectionChanged = true;
                } else return;
            } else if (HandlePrimaryMouseInput(currentEvent, out bool mouseSelectionChanged)) {
                selectionChanged = mouseSelectionChanged;
            } else return;

            currentEvent.Use(); //Prevent propogation of default behavior
            if (selectionChanged) {
                this.gridManager.SetSelectionData(ref currentState.SelectedArray);
                FlushSelection();
            }
        }

        private bool HandlePrimaryMouseInput(Event currentEvent, out bool selectionChanged) {
            selectionChanged = false;
            if (currentEvent.alt || currentEvent.button != 0) return false;

            if (currentEvent.type == EventType.MouseDown) {
                isBoxSelecting = true;
                boxSelectStart = currentEvent.mousePosition;
                boxSelectCurrent = boxSelectStart;
                boxSelectShift = currentEvent.shift;
                return true;
            }

            if (currentEvent.type == EventType.MouseDrag && isBoxSelecting) {
                boxSelectCurrent = currentEvent.mousePosition;
                return true;
            }

            if (currentEvent.type == EventType.MouseUp && isBoxSelecting) {
                boxSelectCurrent = currentEvent.mousePosition;
                PushUndoState();
                if ((boxSelectCurrent - boxSelectStart).sqrMagnitude <= BOX_SELECT_CLICK_THRESHOLD * BOX_SELECT_CLICK_THRESHOLD)
                    SelectPoint(boxSelectStart, boxSelectShift);
                else SelectRect(NormalizeRect(boxSelectStart, boxSelectCurrent), boxSelectShift);

                isBoxSelecting = false;
                selectionChanged = true;
                return true;
            }

            return false;
        }

        private void ApplyState(EditorState state) {
            if (state.SelectedArray.SelectionData == null || state.MapData == null) return;

            bool gridChanged = math.any(currentState.GridSize != state.GridSize);
            GridManager.SelectionArray selectionState = state.SelectedArray.Clone();

            currentState = state.Clone();

            if (gridChanged) {
                InitializeGrid(false);
            }

            currentState.SelectedArray = selectionState;

            this.UpdateMapData();
            this.modelManager.GenerateModel();
            this.gridManager.SetSelectionData(ref currentState.SelectedArray);
            FlushSelection();
        }

        private void PushUndoState() {
            if (!initialized || currentState.MapData == null || currentState.SelectedArray.SelectionData == null) return;
            undoRedoHistory.Push(currentState.Clone());
        }

        private bool Undo() {
            if (!initialized) return false;

            EditorState presentState = currentState.Clone();
            if (!undoRedoHistory.TryUndo(presentState, out EditorState targetState)) return false;
            ApplyState(targetState);
            return true;
        }

        private bool Redo() {
            if (!initialized) return false;

            EditorState presentState = currentState.Clone();
            if (!undoRedoHistory.TryRedo(presentState, out EditorState targetState)) return false;
            ApplyState(targetState);
            return true;
        }

        private void DrawSelectionRect() {
            Rect dragRect = NormalizeRect(boxSelectStart, boxSelectCurrent);
            Handles.BeginGUI();
            EditorGUI.DrawRect(dragRect, new Color(0.2f, 0.55f, 1.0f, 0.15f));
            Handles.color = new Color(0.2f, 0.55f, 1.0f, 0.9f);
            Handles.DrawAAPolyLine(2.0f,
                new Vector3(dragRect.xMin, dragRect.yMin),
                new Vector3(dragRect.xMax, dragRect.yMin),
                new Vector3(dragRect.xMax, dragRect.yMax),
                new Vector3(dragRect.xMin, dragRect.yMax),
                new Vector3(dragRect.xMin, dragRect.yMin));
            Handles.EndGUI();
        }

        private static Rect NormalizeRect(Vector2 from, Vector2 to) {
            float xMin = Mathf.Min(from.x, to.x);
            float xMax = Mathf.Max(from.x, to.x);
            float yMin = Mathf.Min(from.y, to.y);
            float yMax = Mathf.Max(from.y, to.y);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        bool FocusSelection(SceneView sceneView) {
            if (sceneView == null || Selected == null || Selected.Count == 0) return false;

            Vector3 min = default;
            Vector3 max = default;
            bool hasPoint = false;
            for (int i = 0, count = Selected.Count; i < count; i++) {
                uint index = Selected.Dequeue();
                int3 coord = new int3((int)(index / (GridSize.y * GridSize.z)), (int)((index / GridSize.z) % GridSize.y), (int)(index % GridSize.z));
                Vector3 worldPoint = this.transform.localToWorldMatrix.MultiplyPoint3x4(new Vector3(coord.x, coord.y, coord.z));

                if (!hasPoint) {
                    min = worldPoint;
                    max = worldPoint;
                    hasPoint = true;
                } else {
                    min = Vector3.Min(min, worldPoint);
                    max = Vector3.Max(max, worldPoint);
                }

                Selected.Enqueue(index);
            }

            if (!hasPoint) return false;

            Bounds focusBounds = new Bounds((min + max) * 0.5f, max - min);
            if (focusBounds.size.sqrMagnitude < 0.0001f) focusBounds.Expand(1.0f);
            else focusBounds.Expand(0.5f);

            sceneView.Frame(focusBounds, false);
            return true;
        }

        bool RayIntersectsSphere(Ray ray, Vector3 sphereCenter, float sphereRadius) {
            Vector3 oc = ray.origin - sphereCenter;

            float a = Vector3.Dot(ray.direction, ray.direction);
            float b = 2.0f * Vector3.Dot(oc, ray.direction);
            float c = Vector3.Dot(oc, oc) - sphereRadius * sphereRadius;
            float discriminant = b * b - 4 * a * c;

            return discriminant >= 0;
        }

        private Vector2 SceneGUIToCameraPixel(Vector2 mousePos) {
            return new Vector2(mousePos.x * 2, Camera.current.scaledPixelHeight - mousePos.y * 2);
        }

        private void SelectPoint(Vector2 mousePos, bool shiftSelect) {
            Camera sceneCamera = Camera.current;
            if (sceneCamera == null) return;

            Ray SelectionWS = sceneCamera.ScreenPointToRay(SceneGUIToCameraPixel(mousePos));
            if (!shiftSelect) ReleaseSelection();

            Ray SelectionOS = new Ray(this.transform.worldToLocalMatrix.MultiplyPoint3x4(SelectionWS.origin),
                                        this.transform.TransformDirection(SelectionWS.direction));

            int closestIndex = -1;
            float minDepth = float.PositiveInfinity;
            int3 point;
            for (point.x = 0; point.x < GridSize.x; point.x++) {
            for (point.y = 0; point.y < GridSize.y; point.y++) {
            for (point.z = 0; point.z < GridSize.z; point.z++) {
                if (RayIntersectsSphere(SelectionOS, new Vector3(point.x, point.y, point.z), 0.05f)) {
                    int index = CustomUtility.irregularIndexFromCoord(point, new int2(GridSize.yz));
                    Vector3 worldPoint = this.transform.localToWorldMatrix.MultiplyPoint3x4(new Vector3(point.x, point.y, point.z));
                    float depth = Vector3.Dot(sceneCamera.transform.forward, worldPoint - sceneCamera.transform.position);
                    if (depth < 0.0f) continue;
                    if (depth >= minDepth) continue;
                    minDepth = depth;
                    closestIndex = index;
                }
            }}}

            if (closestIndex < 0) return;
            currentState.SelectedArray[closestIndex] = shiftSelect ? !currentState.SelectedArray[closestIndex] : true;
            SetDisplayMapData(currentState.MapData[closestIndex]);
        }

        private void SelectRect(Rect selectionRect, bool shiftSelect) {
            if (!shiftSelect) ReleaseSelection();

            bool hasSelection = false;
            int lastIndex = -1;
            int3 point;
            for (point.x = 0; point.x < GridSize.x; point.x++) {
            for (point.y = 0; point.y < GridSize.y; point.y++) {
            for (point.z = 0; point.z < GridSize.z; point.z++) {
                Vector3 worldPoint = this.transform.localToWorldMatrix.MultiplyPoint3x4(new Vector3(point.x, point.y, point.z));
                Vector2 guiPoint = HandleUtility.WorldToGUIPoint(worldPoint);
                if (!selectionRect.Contains(guiPoint)) continue;

                int index = CustomUtility.irregularIndexFromCoord(point, new int2(GridSize.yz));
                currentState.SelectedArray[index] = shiftSelect ? !currentState.SelectedArray[index] : true;
                hasSelection = true;
                lastIndex = index;
            }}}

            if (hasSelection && lastIndex >= 0) SetDisplayMapData(currentState.MapData[lastIndex]);
        }

        private void FlushSelection() {
            Selected.Clear();
            for (uint i = 0; i < currentState.MapData.Count; i++) {
                if (currentState.SelectedArray[(int)i]) Selected.Enqueue(i);
            }
        }

        private void ReleaseSelection() {
            UpdateSelectionState((e) => { return false; });
            Selected.Clear();
        }

        private void InvertSelection() {
            for (int i = 0; i < currentState.MapData.Count; i++)
                currentState.SelectedArray[i] = !currentState.SelectedArray[i];
        }

        private void SwapSelection() {
            UpdateSelectionState((e) => { return false; });

            uint numPoints = GridSize.x * GridSize.y * GridSize.z;
            UpdateSelectionState((ind) => {
                for (int i = 0; i < 6; i++) {
                    int newInd = (int)ind + CustomUtility.irregularIndexFromCoord(adjDelta[i], new int2(GridSize.yz));
                    if (newInd >= numPoints || newInd < 0) return;
                    currentState.SelectedArray[newInd] = currentState.MapData[newInd].material != currentState.MapData[(int)ind].material;
                }
            });
        }

        private void SelectDensity() {
            FloodFill((ind) => {
                if ((currentState.MapData[ind].density < IsoLevel * 255) != (curData.density < IsoLevel * 255)) return false;
                if (currentState.SelectedArray[ind]) return false;
                currentState.SelectedArray[ind] = true;
                return true;
            });
        }
        private void SelectMaterial() {
            FloodFill((ind) => {
                if (currentState.MapData[ind].material != curData.material) return false;
                if (currentState.SelectedArray[ind]) return false;
                currentState.SelectedArray[ind] = true;
                return true;
            });
        }

        private void SelectWalkable() {
            bool VerifyProfile(Arterra.Data.Entity.Authoring info, int3 BaseCoord) {
                bool allC = true; bool anyC = false; bool any0 = false;
                uint3 dC = new(0);
                Arterra.Data.Entity.EntitySetting.ProfileInfo p = info.Setting.profile;
                int3 gridSize = (int3)GridSize;
                for (dC.x = 0; dC.x < p.bounds.x; dC.x++) {
                    for (dC.y = 0; dC.y < p.bounds.y; dC.y++) {
                        for (dC.z = 0; dC.z < p.bounds.z; dC.z++) {
                            uint index = dC.x * p.bounds.y * p.bounds.z + dC.y * p.bounds.z + dC.z;
                            Arterra.Data.Entity.ProfileE profile = info.Profile.value[(int)index];
                            if (math.any(BaseCoord + (int3)dC >= gridSize)) return false;
                            int3 rCoord = BaseCoord + (int3)dC;
                            int rIndex = rCoord.x * gridSize.y * gridSize.z + rCoord.y * gridSize.z + rCoord.z;
                            bool valid = profile.bounds.Contains(new MapData { data = currentState.MapData[rIndex].data });
                            allC = allC && (valid || !profile.AndFlag);
                            anyC = anyC || (valid && profile.OrFlag);
                            any0 = any0 || profile.OrFlag;
                        }
                    }
                }
                if (allC && (!any0 || anyC)) return true;
                else return false;
            }
            var info = Config.CURRENT.Generation.Entities.Retrieve("Player");
            if (info == null) return;
            int3 gridSize = (int3)GridSize;
            for (int ind = 0; ind < gridSize.x * gridSize.y * gridSize.z; ind++) {
                int3 coord = new int3(ind / (gridSize.y * gridSize.z), (ind / gridSize.z) % gridSize.y, ind % gridSize.z);
                if (!VerifyProfile(info, coord)) continue;
                currentState.SelectedArray[ind] = true;
            }
        }

        void UpdateSelected(Func<StructureData.PointInfo, StructureData.PointInfo> action) {
            for (int i = 0, count = Selected.Count; i < count; i++) {
                uint index = Selected.Dequeue();
                currentState.MapData[(int)index] = action.Invoke(currentState.MapData[(int)index]);
                Selected.Enqueue(index);
            }
        }

        void UpdateSelectionState(Action<uint> action) {
            for (int i = 0, count = Selected.Count; i < count; i++) {
                uint index = Selected.Dequeue();
                action.Invoke(index);
                Selected.Enqueue(index);
            }
        }

        void UpdateSelectionState(Func<bool, bool> action) {
            for (int i = 0, count = Selected.Count; i < count; i++) {
                uint index = Selected.Dequeue();
                currentState.SelectedArray[(int)index] = action.Invoke(currentState.SelectedArray[(int)index]);
                Selected.Enqueue(index);
            }
        }

        void FloodFill(Func<int, bool> action) {
            Queue<int3> queue = new Queue<int3>(Selected.Select(e => new int3((int)(e / (GridSize.y * GridSize.z)), (int)((e / GridSize.z) % GridSize.y), (int)(e % GridSize.z))));

            while (queue.Count > 0) {
                int3 point = queue.Dequeue();
                for (int i = 0; i < 6; i++) {
                    int3 newPoint = point + adjDelta[i];
                    if (newPoint.x < 0 || newPoint.x >= GridSize.x ||
                        newPoint.y < 0 || newPoint.y >= GridSize.y ||
                        newPoint.z < 0 || newPoint.z >= GridSize.z) continue;
                    if (action.Invoke(CustomUtility.irregularIndexFromCoord(newPoint, new int2(GridSize.yz))))
                        queue.Enqueue(newPoint);
                }
            }
        }

        private void SerializeMaterials() {
            if (Structure.Names.value != null && Structure.Names.value.Count > 0)
                return; //Already serialized
            Dictionary<int, int> MaterialDict = new Dictionary<int, int>();
            for (int i = 0; i < Structure.map.value.Count; i++) {
                StructureData.PointInfo p = Structure.map.value[i];
                MaterialDict.TryAdd(p.material, MaterialDict.Count);
                p.material = MaterialDict[p.material];
                Structure.map.value[i] = p;
            }
            string[] materials = new string[MaterialDict.Count];
            var reg = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
            foreach (var pair in MaterialDict) {
                materials[pair.Value] = reg.RetrieveName(pair.Key);
            }
            Structure.Names.value = materials.ToList();
        }

        private void DeserializeMaterials() {
            if (Structure.Names.value == null || Structure.Names.value.Count == 0)
                return;
            List<int> MaterialLUT = new List<int>();
            var reg = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
            for (int i = 0; i < Structure.Names.value.Count; i++) {
                if (reg.Contains(Structure.Names.value[i]))
                    MaterialLUT.Add(reg.RetrieveIndex(Structure.Names.value[i]));
                else MaterialLUT.Add(-1);
            }
            //Mark it as already deserialized so we don't double deserialize
            Structure.Names.value = null;
            for (int i = 0; i < Structure.map.value.Count; i++) {
                StructureData.PointInfo p = Structure.map.value[i];
                p.material = MaterialLUT[math.clamp(p.material, 0, MaterialLUT.Count() - 1)];
                Structure.map.value[i] = p;
            }
        }

        void GetDataFromMesh(Mesh mesh) {
            ComputeShader SDFConstructor = Resources.Load<ComputeShader>("Compute/CGeometry/Deconstructor/MeshDeconstructor");
            ComputeBuffer vertexBuffer = new ComputeBuffer(mesh.vertexCount, sizeof(float) * 3);
            ComputeBuffer indexBuffer = new ComputeBuffer(mesh.triangles.Length, sizeof(uint));
            vertexBuffer.SetData(mesh.vertices); indexBuffer.SetData(mesh.triangles);

            float3 max = new float3(float.MinValue, float.MinValue, float.MinValue);
            float3 min = new float3(float.MaxValue, float.MaxValue, float.MaxValue);
            foreach (Vector3 vert in mesh.vertices) {
                max = math.max(max, vert);
                min = math.min(min, vert);
            }
            ;
            Debug.Log($"Bounding Box: {min} to {max}");

            int kernel = SDFConstructor.FindKernel("GetSDF");
            SDFConstructor.SetBuffer(kernel, "Vertices", vertexBuffer); //Assume one data stream
            SDFConstructor.SetBuffer(kernel, "Indices", indexBuffer);
            SDFConstructor.SetInt("numInds", (int)mesh.GetIndexCount(0)); //Assume one submesh
            SDFConstructor.SetFloats("offset", SDFoffset.x, SDFoffset.y, SDFoffset.z);

            SDFConstructor.SetBuffer(kernel, "Distance", UtilityBuffers.TransferBuffer);
            SDFConstructor.SetInts("GridSize", new int[] { (int)GridSize.x, (int)GridSize.y, (int)GridSize.z });

            uint3 threadsPerAxis;
            SDFConstructor.GetKernelThreadGroupSizes(kernel, out threadsPerAxis.x, out threadsPerAxis.y, out threadsPerAxis.z);
            threadsPerAxis = new uint3(
                (uint)Mathf.CeilToInt((float)GridSize.x / threadsPerAxis.x),
                (uint)Mathf.CeilToInt((float)GridSize.y / threadsPerAxis.y),
                (uint)Mathf.CeilToInt((float)GridSize.z / threadsPerAxis.z)
            );
            SDFConstructor.Dispatch(kernel, (int)threadsPerAxis.x, (int)threadsPerAxis.y, (int)threadsPerAxis.z);
            vertexBuffer.Dispose();
            indexBuffer.Dispose();

            kernel = SDFConstructor.FindKernel("GetMap");
            SDFConstructor.SetBuffer(kernel, "Distance", UtilityBuffers.TransferBuffer);
            SDFConstructor.SetBuffer(kernel, "MapData", UtilityBuffers.TransferBuffer);
            SDFConstructor.SetFloat("IsoLevel", IsoLevel);

            int numPoints = (int)(GridSize.x * GridSize.y * GridSize.z);
            SDFConstructor.GetKernelThreadGroupSizes(kernel, out threadsPerAxis.x, out _, out _);
            threadsPerAxis.x = (uint)Mathf.CeilToInt((float)numPoints / threadsPerAxis.x);
            SDFConstructor.Dispatch(kernel, (int)threadsPerAxis.x, 1, 1);

            StructureData.PointInfo[] newMap = currentState.MapData.ToArray();
            UtilityBuffers.TransferBuffer.GetData(newMap);
            currentState.MapData = newMap.ToList();
        }

#endif
    }
}
