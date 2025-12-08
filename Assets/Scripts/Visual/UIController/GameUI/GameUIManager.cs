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
        PlayerCrosshair.Initialize();
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
    private Button previousButton;
    private Button nextButton;
    public T[] SlotEntries;
    private int NumSlots;
    private string SearchQuery = "";
    private int PageIndex = 0;

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
        SearchInput.DeactivateInputField();
        SearchInput.onValueChanged.AddListener(ProcessSearchRequest);
        SlotEntries = new T[GridContainer.Slots.Count()];
        NumSlots = math.min(SlotEntries.Length, registry.Count());
    }

    public void AddPaginateButtons(Button prev, Button next) {
        previousButton = prev;
        nextButton = next;
        prev.onClick.RemoveAllListeners();
        next.onClick.RemoveAllListeners();
        prev.onClick.AddListener(PreviousPage);
        next.onClick.AddListener(NextPage);
        ResetPaginateButtons();
        void NextPage() {
            ProcessSearchRequest(SearchQuery, PageIndex + 1);
            next.interactable = PageIndex < registry.Count() / NumSlots;
            prev.interactable = true;
        } void PreviousPage() {
            ProcessSearchRequest(SearchQuery, PageIndex - 1);
            prev.interactable = PageIndex > 0;
            next.interactable = true;
        }
    }

    private void ResetPaginateButtons() {
        previousButton.interactable = PageIndex > 0;
        nextButton.interactable = PageIndex < registry.Count() / NumSlots;
    }


    public void Activate() {
        SearchInput.ActivateInputField();
        ProcessSearchRequest("");
        ResetPaginateButtons();
        SearchMenu.gameObject.SetActive(true);
    }

    public void Deactivate() {
        ClearDisplay();
        SearchInput.DeactivateInputField();
        SearchMenu.gameObject.SetActive(false);
    }

    private void ProcessSearchRequest(string input) => ProcessSearchRequest(input, 0);
    private void ProcessSearchRequest(string input, int pageIndex = 0) {
        ClearDisplay();
        this.SearchQuery = input;
        this.PageIndex = math.clamp(pageIndex, 0, registry.Count() / NumSlots);
        List<int> closestEntries = FindClosestEntries(input, PageIndex);
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

    private List<int> FindClosestEntries(string input, int pageIndex) {
        int[] entryDist = new int[registry.Count()];
        for (int i = 0; i < entryDist.Length; i++) {
            entryDist[i] = CalculateEditDistance(input, registry.RetrieveName(i));
        }

        List<int> sortedIndices = Enumerable.Range(0, registry.Count()).ToList();
        sortedIndices.Sort((a, b) => entryDist[a].CompareTo(entryDist[b]));
        int end = math.min(registry.Count(), (pageIndex+1) * NumSlots);
        int start = math.max(end - NumSlots, 0);
        return sortedIndices.GetRange(start, end-start).ToList();
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
    private Func<GameObject> GetSlot;
    private Action<GameObject> ReleaseSlot;
    private Func<GameObject, GameObject> GetSlotDisplay;
    private bool2 reverseAxis;
    private int2 DisplaySlotSize {
        get {
            float2 rectSize = Transform.rect.size;
            float2 gridSize = Grid.cellSize + Grid.spacing;
            int2 slots = (int2)math.floor((rectSize + (float2)Grid.spacing) / gridSize);
            return math.max(slots, new int2(1, 1));
        }
    }

    public GridUIManager(
        GameObject GridUIComponent,
        Func<GameObject> GetSlot,
        Action<GameObject> ReleaseSlot,
        int slotCount,
        GameObject root = null, 
        Func<GameObject, GameObject> GetSlotDisplay = null
    ) {
        this.Object = GridUIComponent;
        this.Transform = Object.GetComponent<RectTransform>();
        this.Grid = Object.GetComponent<GridLayoutGroup>();
        this.reverseAxis = false;
        this.root = root;
        this.GetSlot = GetSlot;
        this.ReleaseSlot = ReleaseSlot;
        this.GetSlotDisplay = GetSlotDisplay;

        Slots = new GameObject[slotCount];
        for (int i = 0; i < slotCount; i++) {
            GameObject Slot = GetSlot.Invoke();
            Slot.transform.SetParent(Object.transform, false);
            Slots[i] = GetSlotDisplay?.Invoke(Slot) ?? Slot;
        }
    }

    public void Release() {
        foreach(GameObject Slot in Slots) {
            if (Slot == null) continue;
            ReleaseSlot(Slot);
        } GameObject.Destroy(this.root);
    }

    public void Resize(int SlotCount) {
        for (int i = SlotCount; i < Slots.Length; i++) {
            ReleaseSlot(Slots[i]);
        }

        GameObject[] NewSlots = new GameObject[SlotCount];
        Slots.ToList().CopyTo(0, NewSlots, 0, math.min(SlotCount, Slots.Length));
        for (int i = Slots.Length; i < SlotCount; i++) {
            GameObject Slot = GetSlot.Invoke();
            Slot.transform.SetParent(Object.transform, false);
            NewSlots[i] = GetSlotDisplay?.Invoke(Slot) ?? Slot;
        } Slots = NewSlots;
    }

    public bool GetMouseSelected(out int index) {
        int2 slot = GetSlotIndex(((float3)Input.mousePosition).xy);
        index = 0;

        int2 dispSize = DisplaySlotSize;
        if (math.any(slot < 0) || math.any(slot >= dispSize)) return false;
        if (reverseAxis.x) slot.x = (dispSize.x-1) - slot.x;
        if (reverseAxis.y) slot.y = (dispSize.y-1) - slot.y;
        index = slot.y * dispSize.x + slot.x;
        return index < Slots.Length;
    }

    public void ChangeAlginment(GridLayoutGroup.Corner corner) {
        Grid.startCorner = corner;
        reverseAxis = corner switch {
            GridLayoutGroup.Corner.UpperLeft => new bool2(false, false),
            GridLayoutGroup.Corner.UpperRight => new bool2(true, false),
            GridLayoutGroup.Corner.LowerLeft => new bool2(false, true),
            GridLayoutGroup.Corner.LowerRight => new bool2(true, true),
            _ => new bool2(false, false)
        };
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

/// <summary>
/// Dynamic three-column layout gen