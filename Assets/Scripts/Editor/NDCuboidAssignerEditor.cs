using Arterra.Data.Biome;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Arterra.Editor {
    [CustomEditor(typeof(NDCuboidAssigner))]
    public class NDCuboidAssignerEditor : UnityEditor.Editor {
        private readonly int[] axisDimensions =
        { 0, 1, 2 };
        private readonly int[] boundarySliderControlIds = new int[6];
        private float[] sliceCoordinates;
        private int selectedRegionIndex = -1;
        private int selectedAxisIndex;
        private int selectedBoundarySide;
        private bool hasSelectedBoundary;
        private bool isSceneSelectionLocked = true;
        private bool isBoundarySnappingEnabled;
        private const float BoundarySnapDistance = 0.025f;
        private List<float> boundarySnapTargets;
        private Vector2 sidebarScrollPosition;
        private string validationError;
        private int boundaryDragUndoGroup = -1;
        private BiomePlacement previousCategory;
        private List<BiomeRegion> regionsBeforeDrag;
        private int draggedRegionIndex;
        private string categoryStateRestoredByUndo;

        private NDCuboidAssigner Tool => (NDCuboidAssigner)target;
        private BiomePlacement Category => Tool.Category;
        private Rect SidebarRect => new Rect(12, 12, 280, Mathf.Max(180, SceneView.currentDrawingSceneView.position.height - 45));

        private void OnEnable() {
            Undo.undoRedoPerformed += HandlePartitionUndoRedo;
            RefreshValidationAndSelection();
        }

        private void OnDisable() {
            Undo.undoRedoPerformed -= HandlePartitionUndoRedo;
            CompleteBoundaryDrag();
        }

        private void HandlePartitionUndoRedo() {
            // Let Undo restore a culled region until the user next edits the category.
            categoryStateRestoredByUndo = Category ? JsonUtility.ToJson(Category) : null;
            RefreshValidationAndSelection();
        }

        private bool HasZeroVolume(BiomeRegion region) {
            if (region?.Spans == null || region.Spans.Length != Category.numDimensions) {
                return false;
            }

            bool hasCollapsedDimension = false;
            foreach (var span in region.Spans) {
                // Malformed bounds should remain visible to validation, not silently disappear.
                if (float.IsNaN(span.x) || float.IsNaN(span.y) ||
                    float.IsInfinity(span.x) || float.IsInfinity(span.y) ||
                    span.x < 0 || span.y > 1 || span.x > span.y) {
                    return false;
                }

                hasCollapsedDimension |= span.x == span.y;
            }

            return hasCollapsedDimension;
        }

        private void CullZeroVolumeRegions() {
            if (!Category || Category.Biomes == null || boundaryDragUndoGroup >= 0) {
                return;
            }

            if (categoryStateRestoredByUndo != null) {
                if (JsonUtility.ToJson(Category) == categoryStateRestoredByUndo) {
                    return;
                }

                categoryStateRestoredByUndo = null;
            }

            if (!Category.Biomes.Any(HasZeroVolume)) {
                return;
            }

            var selectedRegion = HasSelectedRegion ? Category.Biomes[selectedRegionIndex] : null;
            Undo.RecordObject(Category, "Cull zero-volume biome regions");
            Category.Biomes.RemoveAll(HasZeroVolume);

            selectedRegionIndex = selectedRegion == null ? -1 : Category.Biomes.IndexOf(selectedRegion);
            if (selectedRegionIndex < 0) {
                hasSelectedBoundary = false;
            }

            validationError = Category.ValidationError();
            MarkCategoryDirtyAndRepaint();
        }

        private void RefreshValidationAndSelection() {
            validationError = Category ? Category.ValidationError() : null;
            selectedRegionIndex = -1;
            hasSelectedBoundary = false;
            SceneView.RepaintAll();
            Repaint();
        }

        private void CompleteBoundaryDrag() {
            if (boundaryDragUndoGroup < 0) {
                return;
            }

            int completedUndoGroup = boundaryDragUndoGroup;
            boundaryDragUndoGroup = -1;
            regionsBeforeDrag = null;
            boundarySnapTargets = null;

            // Keep collapse cleanup in the same Undo operation as the movement.
            CullZeroVolumeRegions();
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(completedUndoGroup);
        }

        private List<float> CollectBoundarySnapTargets(int axisIndex, int movingSide) {
            int dimension = axisDimensions[axisIndex];
            var targets = new SortedSet<float> { 0, 1 };

            for (int i = 0; i < regionsBeforeDrag.Count; i++) {
                var region = regionsBeforeDrag[i];
                if (!IsRegionVisibleInSlice(region)) {
                    continue;
                }

                for (int side = 0; side < 2; side++) {
                    if (i != draggedRegionIndex || side != movingSide) {
                        targets.Add(region.Spans[dimension][side]);
                    }
                }
            }

            return targets.ToList();
        }

        private float ResolveDraggedBoundaryPosition(float position, int dimension, int side) {
            var originalSpan = regionsBeforeDrag[draggedRegionIndex].Spans[dimension];
            float minimum = side == 0 ? 0f : originalSpan.x;
            float maximum = side == 0 ? originalSpan.y : 1f;

            if (isBoundarySnappingEnabled) {
                float closestDistance = BoundarySnapDistance;
                float snappedPosition = position;
                bool foundTarget = false;

                // Targets come from the initial slice, not fragments created during this drag.
                foreach (float target in boundarySnapTargets) {
                    float distance = Mathf.Abs(position - target);
                    if (target < minimum || target > maximum || distance > closestDistance) {
                        continue;
                    }

                    closestDistance = distance;
                    snappedPosition = target;
                    foundTarget = true;
                }

                if (foundTarget) {
                    return snappedPosition;
                }
            }

            // Only an exact snap can collapse the region; ordinary dragging retains a width.
            float inset = Mathf.Min(0.0001f, (originalSpan.y - originalSpan.x) * 0.5f);
            return Mathf.Clamp(position, minimum + inset, maximum - inset);
        }

        public override void OnInspectorGUI() {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("Select this object in Scene view. Create an ND Partition asset, assign it, then initialize its unit cube. Add biome names and colors on that asset.", MessageType.Info);
            if (GUILayout.Button("Refresh partition validation")) {
                RefreshValidationAndSelection();
            }
        }

        private void MarkCategoryDirtyAndRepaint() {
            EditorUtility.SetDirty(Category);
            SceneView.RepaintAll();
        }

        private void InitializeSliceViewIfNeeded() {
            if (previousCategory != Category) {
                previousCategory = Category;
                categoryStateRestoredByUndo = null;
                sliceCoordinates = null;
                RefreshValidationAndSelection();
            }

            if (sliceCoordinates != null && sliceCoordinates.Length == Category.numDimensions) {
                return;
            }

            sliceCoordinates = Enumerable.Repeat(0.5f, Mathf.Max(3, Category.numDimensions)).ToArray();
            axisDimensions[0] = 0;
            axisDimensions[1] = 1;
            axisDimensions[2] = 2;
            RefreshValidationAndSelection();
        }

        private bool IsRegionVisibleInSlice(BiomeRegion region) {
            for (int dimension = 0; dimension < Category.numDimensions; dimension++) {
                if (Array.IndexOf(axisDimensions, dimension) >= 0) {
                    continue;
                }

                var span = region.Spans[dimension];
                if (sliceCoordinates[dimension] < span.x || (sliceCoordinates[dimension] >= span.y && !(sliceCoordinates[dimension] == 1 && span.y == 1))) {
                    return false;
                }
            }

            return true;
        }

        private Color GetRegionColor(BiomeRegion region) {
            var registry = Category.BiomeColors.Reg;
            if (registry != null) {
                foreach (var pair in registry) {
                    if (pair.Name == region.BiomeName) {
                        // Palette alpha should not make authoring outlines or pick handles disappear.
                        Color color = pair.Value;
                        color.a = 1f;
                        return color;
                    }
                }
            }

            return Color.gray;
        }

        private Vector3 GetLocalAxisDirection(int axisIndex) => axisIndex == 0 ? Vector3.right : axisIndex == 1 ? Vector3.up : Vector3.forward;

        private Vector3 TransformNormalizedPointToWorld(Vector3 point) => Tool.transform.position + Tool.transform.rotation * (point * Tool.DisplaySize);

        private Vector3 GetWorldAxisDirection(int axisIndex) => Tool.transform.rotation * GetLocalAxisDirection(axisIndex);

        private Vector3 GetAxisHandleWorldPosition(int axisIndex, float normalizedPosition) {
            var position = Vector3.one * -0.08f;
            position[axisIndex] = normalizedPosition;
            return TransformNormalizedPointToWorld(position);
        }

        private List<float> GetVisibleBoundaryPositions(int axis) {
            var values = new SortedSet<float>
            { 0, 1 };
            foreach (var region in Category.Biomes.Where(IsRegionVisibleInSlice)) {
                values.Add(region.Spans[axisDimensions[axis]].x);
                values.Add(region.Spans[axisDimensions[axis]].y);
            }

            return values.ToList();
        }

        private bool HasSelectedRegion => selectedRegionIndex >= 0 && selectedRegionIndex < Category.Biomes.Count;

        private void OnSceneGUI() {
            if (!Category) {
                return;
            }

            InitializeSliceViewIfNeeded();
            // Change the region list before IMGUI layout, never partway through a drag.
            if (Event.current.type == EventType.Layout) {
                CullZeroVolumeRegions();
            }

            HandleSceneSelectionLock();

            // Reserve before variable region controls so splitting during a drag keeps hotControl stable.
            for (int i = 0; i < boundarySliderControlIds.Length; i++) {
                boundarySliderControlIds[i] = GUIUtility.GetControlID(0x42494F + i, FocusType.Passive);
            }

            // Revalidate externally edited data before indexing it; quadratic overlap checking is manual/Undo only.
            bool hasValidRegionDimensions = Category.numDimensions >= 3 &&
                Category.Biomes != null &&
                Category.Biomes.All(region => region?.Spans != null &&
                    region.Spans.Length == Category.numDimensions);
            if (hasValidRegionDimensions && validationError == null &&
                HasSelectedRegion && !IsRegionVisibleInSlice(Category.Biomes[selectedRegionIndex])) {
                selectedRegionIndex = -1;
                hasSelectedBoundary = false;
            }

            // Popup results are routed back by control ID. Allocate sidebar controls before
            // scene controls, whose count changes when the pointer enters/leaves the sidebar.
            DrawSidebarArea(hasValidRegionDimensions);

            if (hasValidRegionDimensions && validationError == null) {
                bool isPointerOverSidebar = SidebarRect.Contains(Event.current.mousePosition);
                DrawVisibleRegionCuboids(isPointerOverSidebar);
                DrawAxisMarkersAndBoundaryHandles(isPointerOverSidebar);
            }

            if (Event.current.rawType == EventType.MouseUp) {
                CompleteBoundaryDrag();
            }
        }

        private void DrawSidebarArea(bool hasValidRegionDimensions) {
            Handles.BeginGUI();
            GUILayout.BeginArea(SidebarRect, "Biome partition", GUI.skin.window);
            if (GUILayout.Button(isSceneSelectionLocked ? "Unlock view" : "Lock view")) {
                isSceneSelectionLocked = !isSceneSelectionLocked;
                SceneView.RepaintAll();
            }

            GUILayout.Label(isSceneSelectionLocked ? "Scene selection locked; camera navigation remains available." : "Scene selection unlocked.", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button(isBoundarySnappingEnabled ? "Snapping: On (0.025)" : "Snapping: Off")) {
                isBoundarySnappingEnabled = !isBoundarySnappingEnabled;
                SceneView.RepaintAll();
            }

            sidebarScrollPosition = GUILayout.BeginScrollView(sidebarScrollPosition);
            DrawSidebarContents(hasValidRegionDimensions);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private void HandleSceneSelectionLock() {
            // Keep empty-space clicks from changing Unity's selection and destroying this editor.
            // A default control loses to actual handles, so cuboid picking and dragging still work.
            int backgroundControl = GUIUtility.GetControlID(0x42494E, FocusType.Passive);
            Event sceneEvent = Event.current;
            if (isSceneSelectionLocked && !sceneEvent.alt && !Tools.viewToolActive) {
                if (sceneEvent.type == EventType.Layout) {
                    HandleUtility.AddDefaultControl(backgroundControl);
                }

                if (sceneEvent.type == EventType.MouseDown && sceneEvent.button == 0 &&
                    GUIUtility.hotControl == 0 &&
                    HandleUtility.nearestControl == backgroundControl &&
                    !SidebarRect.Contains(sceneEvent.mousePosition)) {
                    sceneEvent.Use();
                }
            }
        }

        private void DrawVisibleRegionCuboids(bool isPointerOverSidebar) {
            for (int i = 0; i < Category.Biomes.Count; i++) {
                var region = Category.Biomes[i];
                if (!IsRegionVisibleInSlice(region)) {
                    continue;
                }

                var minimum = new Vector3();
                var maximum = new Vector3();
                for (int axisIndex = 0; axisIndex < 3; axisIndex++) {
                    minimum[axisIndex] = region.Spans[axisDimensions[axisIndex]].x;
                    maximum[axisIndex] = region.Spans[axisDimensions[axisIndex]].y;
                }

                var corners = new Vector3[8];
                for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++) {
                    corners[cornerIndex] = TransformNormalizedPointToWorld(new Vector3((cornerIndex & 1) == 0 ? minimum.x : maximum.x, (cornerIndex & 2) == 0 ? minimum.y : maximum.y, (cornerIndex & 4) == 0 ? minimum.z : maximum.z));
                }

                Color color = GetRegionColor(region);
                int[][] faces = {
                    new[]
                    { 0, 1, 3, 2 },
                    new[]
                    { 4, 6, 7, 5 },
                    new[]
                    { 0, 4, 5, 1 },
                    new[]
                    { 2, 3, 7, 6 },
                    new[]
                    { 0, 2, 6, 4 },
                    new[]
                    { 1, 5, 7, 3 }
                };
                foreach (var face in faces) {
                    Handles.DrawSolidRectangleWithOutline(face.Select(cornerIndex => corners[cornerIndex]).ToArray(), new Color(color.r, color.g, color.b, i == selectedRegionIndex ? 0.14f : 0.04f), color);
                }

                Vector3 center = TransformNormalizedPointToWorld((minimum + maximum) * 0.5f);
                using (new Handles.DrawingScope(i == selectedRegionIndex ? Color.yellow : color)) {
                    float size = HandleUtility.GetHandleSize(center) * 0.055f;
                    if (!isPointerOverSidebar && Handles.Button(center, Quaternion.identity, size, size, Handles.SphereHandleCap)) {
                        selectedRegionIndex = i;
                        hasSelectedBoundary = false;
                        Repaint();
                    }
                }

                if (i == selectedRegionIndex) {
                    Handles.Label(center, string.IsNullOrEmpty(region.BiomeName) ? "Unassigned" : region.BiomeName);
                }
            }
        }

        private void DrawAxisMarkersAndBoundaryHandles(bool isPointerOverSidebar) {
            for (int axisIndex = 0; axisIndex < 3; axisIndex++) {
                using (new Handles.DrawingScope(axisIndex == 0 ? Color.red : axisIndex == 1 ? Color.green : Color.cyan)) {
                    Handles.DrawLine(GetAxisHandleWorldPosition(axisIndex, 0), GetAxisHandleWorldPosition(axisIndex, 1));
                    Handles.Label(GetAxisHandleWorldPosition(axisIndex, 1.06f), Category.DimensionName(axisDimensions[axisIndex]));
                    foreach (float value in GetVisibleBoundaryPositions(axisIndex)) {
                        var position = GetAxisHandleWorldPosition(axisIndex, value);
                        Handles.DotHandleCap(0, position, Quaternion.identity, HandleUtility.GetHandleSize(position) * 0.025f, EventType.Repaint);
                    }

                    Handles.Label(GetAxisHandleWorldPosition(axisIndex, 0), "0");
                    Handles.Label(GetAxisHandleWorldPosition(axisIndex, 1), "1");
                }
            }

            if (!HasSelectedRegion || isPointerOverSidebar) {
                return;
            }

            for (int axisIndex = 0; axisIndex < 3; axisIndex++) {
                for (int side = 0; side < 2; side++) {
                    var region = Category.Biomes[selectedRegionIndex];
                    float value = region.Spans[axisDimensions[axisIndex]][side];
                    var position = GetAxisHandleWorldPosition(axisIndex, value);
                    float size = HandleUtility.GetHandleSize(position) * 0.055f;
                    // A separate pick target allows selecting fixed endpoint handles too.
                    var pick = position + GetWorldAxisDirection((axisIndex + 1) % 3) * size * 2;
                    using (new Handles.DrawingScope(hasSelectedBoundary && selectedAxisIndex == axisIndex && selectedBoundarySide == side ? Color.yellow : Color.white)) {
                        if (Handles.Button(pick, Quaternion.identity, size * 0.6f, size, Handles.DotHandleCap)) {
                            selectedAxisIndex = axisIndex;
                            selectedBoundarySide = side;
                            hasSelectedBoundary = true;
                            Repaint();
                        }

                        bool isDraggingThisBoundary = GUIUtility.hotControl == boundarySliderControlIds[axisIndex * 2 + side];
                        if ((value <= 0 || value >= 1) && !isDraggingThisBoundary) {
                            continue;
                        }

                        EditorGUI.BeginChangeCheck();
                        var moved = Handles.Slider(boundarySliderControlIds[axisIndex * 2 + side], position, GetWorldAxisDirection(axisIndex), size, Handles.CubeHandleCap, 0.001f * Tool.DisplaySize);
                        if (!EditorGUI.EndChangeCheck()) {
                            continue;
                        }

                        if (boundaryDragUndoGroup < 0) {
                            Undo.IncrementCurrentGroup();
                            boundaryDragUndoGroup = Undo.GetCurrentGroup();
                            Undo.SetCurrentGroupName("Move biome boundary");
                            regionsBeforeDrag = Category.Biomes.Select(r => r.Copy()).ToList();
                            draggedRegionIndex = selectedRegionIndex;
                            boundarySnapTargets = CollectBoundarySnapTargets(axisIndex, side);
                        }

                        Undo.RecordObject(Category, "Move biome boundary");
                        var replacement = regionsBeforeDrag[draggedRegionIndex].Copy();
                        float normalizedPosition = Vector3.Dot(moved - GetAxisHandleWorldPosition(axisIndex, 0), GetWorldAxisDirection(axisIndex)) / Tool.DisplaySize;
                        replacement.Spans[axisDimensions[axisIndex]][side] =
                            ResolveDraggedBoundaryPosition(normalizedPosition, axisDimensions[axisIndex], side);
                        Category.Biomes = regionsBeforeDrag.Select(r => r.Copy()).ToList();
                        selectedRegionIndex = Category.ReplaceRegion(draggedRegionIndex, replacement);
                        selectedAxisIndex = axisIndex;
                        selectedBoundarySide = side;
                        hasSelectedBoundary = true;
                        MarkCategoryDirtyAndRepaint();
                        return;
                    }
                }
            }
        }

        private void DrawDimensionMappingAndSliceControls(string[] names) {
            for (int axisIndex = 0; axisIndex < 3; axisIndex++) {
                int chosen = EditorGUILayout.Popup("XYZ"[axisIndex].ToString(), axisDimensions[axisIndex], names);
                if (chosen == axisDimensions[axisIndex]) {
                    continue;
                }

                int other = Array.IndexOf(axisDimensions, chosen);
                if (other >= 0) {
                    axisDimensions[other] = axisDimensions[axisIndex];
                }

                axisDimensions[axisIndex] = chosen;
                hasSelectedBoundary = false;
                SceneView.RepaintAll();
            }

            for (int dimension = 0; dimension < Category.numDimensions; dimension++) {
                if (Array.IndexOf(axisDimensions, dimension) >= 0) {
                    continue;
                }

                EditorGUI.BeginChangeCheck();
                sliceCoordinates[dimension] = EditorGUILayout.Slider(names[dimension], sliceCoordinates[dimension], 0, 1);
                if (EditorGUI.EndChangeCheck()) {
                    SceneView.RepaintAll();
                }
            }
        }

        private void DrawBiomeAssignmentDropdown() {
            var options = new List<string> {
                "Unassigned"
            };
            var biomeNames = new List<string> {
                ""
            };
            if (Category.BiomeColors.Reg != null) {
                foreach (var pair in Category.BiomeColors.Reg) {
                    if (string.IsNullOrWhiteSpace(pair.Name) || biomeNames.Contains(pair.Name)) {
                        continue;
                    }

                    options.Add(pair.Name);
                    biomeNames.Add(pair.Name);
                }
            }

            var selectedRegion = Category.Biomes[selectedRegionIndex];
            int currentBiomeIndex = biomeNames.IndexOf(selectedRegion.BiomeName ?? "");
            if (currentBiomeIndex < 0) {
                options.Add($"Missing: {selectedRegion.BiomeName}");
                biomeNames.Add(selectedRegion.BiomeName);
                currentBiomeIndex = options.Count - 1;
            }

            int chosenBiomeIndex = EditorGUILayout.Popup("Name", currentBiomeIndex, options.ToArray());
            if (chosenBiomeIndex != currentBiomeIndex) {
                Undo.RecordObject(Category, "Assign biome");
                selectedRegion.BiomeName = biomeNames[chosenBiomeIndex];
                MarkCategoryDirtyAndRepaint();
            }
        }

        // Resolve coincident projected markers through the selected region's cross-section center.
        private int FindRegionInNextInterval(BiomeRegion selectedRegion, int splitDimension,
            float boundary, float nextBoundary) {
            if (nextBoundary <= boundary) {
                return -1;
            }

            return Category.Biomes.FindIndex(region => {
                if (!IsRegionVisibleInSlice(region) ||
                    region.Spans[splitDimension].x > boundary ||
                    region.Spans[splitDimension].y < nextBoundary) {
                    return false;
                }

                for (int axisIndex = 0; axisIndex < 3; axisIndex++) {
                    int dimension = axisDimensions[axisIndex];
                    if (dimension == splitDimension) {
                        continue;
                    }

                    float center = (selectedRegion.Spans[dimension].x + selectedRegion.Spans[dimension].y) * 0.5f;
                    if (center < region.Spans[dimension].x || center >= region.Spans[dimension].y) {
                        return false;
                    }
                }

                return true;
            });
        }

        private void DrawSidebarContents(bool hasValidRegionDimensions) {
            if (GUILayout.Button("Validate / refresh")) {
                RefreshValidationAndSelection();
            }

            if (GUILayout.Button("Initialize unit cube")) {
                if (Category.numDimensions >= 3 && (Category.Biomes == null || Category.Biomes.Count == 0 || EditorUtility.DisplayDialog("Replace partition?", "Existing regions will be replaced by one unassigned unit cuboid. Undo is available.", "Replace", "Cancel"))) {
                    Undo.RecordObject(Category, "Initialize biome partition");
                    Category.InitializePartition();
                    MarkCategoryDirtyAndRepaint();
                    RefreshValidationAndSelection();
                }
            }

            if (!hasValidRegionDimensions || validationError != null) {
                EditorGUILayout.HelpBox(validationError ?? "Invalid region dimensions. Refresh validation or initialize the partition.", MessageType.Warning);
                return;
            }

            string[] names = Enumerable.Range(0, Category.numDimensions).Select(Category.DimensionName).ToArray();
            DrawDimensionMappingAndSliceControls(names);
            GUILayout.Label($"{Category.Biomes.Count(IsRegionVisibleInSlice)} visible / {Category.Biomes.Count} regions");
            EditorGUILayout.HelpBox("Click a cuboid center to assign its biome. Axis dots show this slice's boundaries; white cubes edit the selected region. Edits affect its full hidden-dimension span.", MessageType.None);
            if (!HasSelectedRegion) {
                return;
            }

            GUILayout.Space(8);
            GUILayout.Label("Selected biome", EditorStyles.boldLabel);
            DrawBiomeAssignmentDropdown();
            var selectedRegion = Category.Biomes[selectedRegionIndex];
            if (!hasSelectedBoundary) {
                GUILayout.Label("Select an axis handle's small round button.");
                return;
            }

            int dimension = axisDimensions[selectedAxisIndex];
            float boundary = selectedRegion.Spans[dimension][selectedBoundarySide];
            GUILayout.Label($"{names[dimension]}: {boundary:0.####}");
            var boundaryPositions = GetVisibleBoundaryPositions(selectedAxisIndex);
            float nextBoundary = boundaryPositions.FirstOrDefault(v => v > boundary);
            int regionToSplitIndex = FindRegionInNextInterval(selectedRegion, dimension, boundary, nextBoundary);

            using (new EditorGUI.DisabledScope(regionToSplitIndex < 0)) {
                if (GUILayout.Button("Split next interval")) {
                    Undo.RecordObject(Category, "Split biome region");
                    selectedRegionIndex = Category.SplitRegion(regionToSplitIndex, dimension, (boundary + nextBoundary) * 0.5f);
                    selectedBoundarySide = 0;
                    MarkCategoryDirtyAndRepaint();
                }
            }

            using (new EditorGUI.DisabledScope(boundary <= 0 || boundary >= 1)) {
                if (GUILayout.Button("Delete boundary (expand selected)")) {
                    Undo.RecordObject(Category, "Delete biome boundary");
                    var replacement = selectedRegion.Copy();
                    replacement.Spans[dimension][selectedBoundarySide] = selectedBoundarySide == 0
                        ? boundaryPositions.Last(value => value < boundary)
                        : boundaryPositions.First(value => value > boundary);
                    selectedRegionIndex = Category.ReplaceRegion(selectedRegionIndex, replacement);
                    MarkCategoryDirtyAndRepaint();
                }
            }

            EditorGUILayout.HelpBox("Delete expands the selected biome to the adjacent boundary. Shared boundaries in other regions remain independent. Split preserves the biome name on both halves.", MessageType.None);
        }
    }
}
