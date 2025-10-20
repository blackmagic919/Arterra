using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

public static class GameUIManager {
    public static GameObject UIHandle;
    public static void Initialize() {
        UIHandle = GameObject.Find("MainUI");
        LoadingHandler.Initialize();
        PanelNavbarManager.Initialize();
        InventoryController.Initialize();
        
        PauseHandler.Initialize();
        GameOverHandler.Initialize();
        DayNightContoller.Initialize();
        PlayerStatDisplay.Initialize();
    }

}


/// <summary> An interface to abstractify the creation and 
/// handling of UI 'slots' </summary>
public interface ISlot {
    /// <summary> Attaches the UI panel to be displayed representing the Item to the UI object. </summary>
    public void AttachDisplay(Transform pSlot);
    /// <summary> This handle is called when the Display UI is to be cleared  </summary>
    public void ClearDisplay(Transform pSlot);
}


public class RegistrySearchDisplay<T> where T : ISlot {
    private Registry<T> registry;
    private Transform SearchMenu;
    public TMP_InputField SearchInput;
    public GridUIManager GridContainer;
    public T[] SlotEntries;
    private int NumSlots;

    public RegistrySearchDisplay(
        Registry<T> registry,
        Transform SearchMenu,
        TMP_InputField SearchInput,
        GridUIManager GridContainer
    ) {
        this.registry = registry;
        this.SearchMenu = SearchMenu;
        this.SearchInput = SearchInput;
        this.GridContainer = GridContainer;
        SearchInput.onValueChanged.AddListener(ProcessSearchRequest);
        SearchInput.DeactivateInputField();
        SlotEntries = new T[GridContainer.Slots.Count()];
        NumSlots = math.min(SlotEntries.Length, registry.Count());
    }

    public void Activate() {
        SearchInput.ActivateInputField();
        ProcessSearchRequest("");
        SearchMenu.gameObject.SetActive(true);
    }

    public void Deactivate() {
        ClearDisplay();
        SearchInput.DeactivateInputField();
        SearchMenu.gameObject.SetActive(false);
    }

    private void ProcessSearchRequest(string input) {
        List<int> closestEntries = FindClosestEntries(input);
        ClearDisplay();
        for (int i = 0; i < closestEntries.Count; i++) {
            SlotEntries[i] = registry.Retrieve(closestEntries[i]);
            SlotEntries[i].AttachDisplay(GridContainer.Slots[i].transform);
        }
    }

    private void ClearDisplay() {
        for (int i = 0; i < NumSlots; i++) {
            T slot = SlotEntries[i];
            if (slot == null) continue;
            slot.ClearDisplay(GridContainer.Slots[i].transform);
        }
    }

    private List<int> FindClosestEntries(string input) {
        int[] entryDist = new int[registry.Count()];
        for (int i = 0; i < entryDist.Length; i++) {
            entryDist[i] = CalculateEditDistance(input, registry.RetrieveName(i));
        }

        List<int> sortedIndices = Enumerable.Range(0, registry.Count()).ToList();
        sortedIndices.Sort((a, b) => entryDist[a].CompareTo(entryDist[b]));
        return sortedIndices.GetRange(0, NumSlots).ToList();
    }
    private int CalculateEditDistance(string a, string b) {
        int[,] dp = new int[a.Length + 1, b.Length + 1];
        for (int i = a.Length; i >= 0; i--) dp[i, b.Length] = a.Length - i;
        for (int j = b.Length; j >= 0; j--) dp[a.Length, j] = b.Length - j;
        for (int i = a.Length - 1; i >= 0; i--) {
            for (int j = b.Length - 1; j >= 0; j--) {
                if (a[i] == b[j]) dp[i, j] = dp[i + 1, j + 1];
                else {
                    int best = dp[i + 1, j + 1];
                    best = math.min(best, dp[i + 1, j]);
                    best = math.min(best, dp[i, j + 1]);
                    dp[i, j] = best + 1;
                }
            }
        }
        return dp[0, 0];
    }
}


public class GridUIManager {
    public GameObject parent => root?.transform.parent.gameObject;
    public GameObject root = null;
    public GameObject Object;
    public RectTransform Transform;
    public GridLayoutGroup Grid;
    public GameObject[] Slots;
    private int2 DisplaySlotSize {
        get {
            float2 rectSize = Transform.rect.size + 2 * Grid.spacing;
            float2 gridSize = Grid.cellSize + Grid.spacing;
            return math.max((int2)math.floor(rectSize / gridSize), new int2(1, 1));
        }
    }

    public GridUIManager(
        GameObject GridUIComponent,
        Func<GameObject> GetSlot,
        int slotCount,
        GameObject root = null
    ) {
        this.Object = GridUIComponent;
        this.Transform = Object.GetComponent<RectTransform>();
        this.Grid = Object.GetComponent<GridLayoutGroup>();
        this.root = root;

        Slots = new GameObject[slotCount];
        for (int i = 0; i < slotCount; i++) {
            Slots[i] = GetSlot.Invoke();
            Slots[i].transform.SetParent(Object.transform);
        }
    }

    public bool GetMouseSelected(out int index) {
        int2 slot = GetSlotIndex(((float3)Input.mousePosition).xy);
        index = 0;

        int2 dispSize = DisplaySlotSize;
        if (math.any(slot < 0) || math.any(slot >= dispSize)) return false;
        index = slot.y * dispSize.x + slot.x;
        return index < Slots.Length;
    }

    private int2 GetSlotIndex(float2 posSC) {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            Transform, posSC, null, out Vector2 pOff);
        pOff.x *= Transform.pivot.x * (-2) + 1;
        pOff.y *= Transform.pivot.y * (-2) + 1;
        int2 slotInd = (int2)math.floor(pOff / (Grid.cellSize + Grid.spacing));
        return slotInd;
    }
}