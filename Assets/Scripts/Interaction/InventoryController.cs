
using System;
using System.Collections.Generic;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using WorldConfig;
using WorldConfig.Generation.Item;

namespace WorldConfig.Gameplay{
    /// <summary> Settings controlling the size and apperance of the inventory,
    /// a system allowing the player to hold and use 
    /// <see cref="Generation.Item"> items </see>. </summary>
    [Serializable]
    public struct Inventory{
        /// <summary> The amount of slots in the primary inventory, the hotbar. This is
        /// equivalent to the maximum amount of items that can be held in the hotbar.  </summary>
        public int PrimarySlotCount;
        /// <summary> The amount of slots in the secondary inventory, the hidden inventory. This is
        /// equivalent to the maximum amount of items that can be held in the hidden inventory. </summary>
        public int SecondarySlotCount;

        /// <summary>
        /// The color of the selected slot in the <see cref="InventoryController.Primary">Primary Inventory</see>, or
        /// the hotbar. This color is used to indicate which item currently has the status of being <see cref="InventoryController.Selected">
        /// selected. </see> 
        /// </summary>
        public Color SelectedColor;
        /// <summary> The color of the base slot in the Inventory. The color 
        /// of the slot when it is empty (the item held by the slot is null). </summary>
        public Color BaseColor;
    }
}
public class InventoryController : UpdateTask
{
    public static WorldConfig.Gameplay.Inventory settings => Config.CURRENT.GamePlay.Inventory.value;
    public static Inventory Primary; //Hotbar
    public static Inventory Secondary; //Inventory
    public static InventoryController Instance;
    public static SlotDisplay CursorDisplay;

    
    public static InventDisplay PrimaryArea;
    public static SlotDisplay[] PrimaryDisplay;
    public static InventDisplay SecondaryArea;
    public static SlotDisplay[] SecondaryDisplay;

    private static uint Fence;
    private static Registry<Authoring> ItemSettings;
    private static Registry<Sprite> TextureAtlas;
    public static IItem Selected=>Primary.Info[SelectedIndex];
    public static Authoring SelectedSetting=>ItemSettings.Retrieve(Primary.Info[SelectedIndex].Index);
    public static IItem Cursor;
    private static int SelectedIndex = 0;

    public struct SlotDisplay{
        public GameObject Object;
        public Image Icon;
        public TextMeshProUGUI Amount;
        public SlotDisplay(GameObject obj){
            Object = obj;
            Icon = obj.transform.GetComponent<Image>();
            Amount = obj.transform.GetChild(0).GetComponent<TextMeshProUGUI>();
        }
    }
    public struct InventDisplay{
        public GameObject Region;
        public RectTransform RTransform;
        public GameObject Object;
        public RectTransform Transform;
        public GridLayoutGroup Grid;
        public InventDisplay(GameObject obj){
            Region = obj;
            RTransform = obj.GetComponent<RectTransform>();
            Object = obj.transform.GetChild(0).GetChild(0).gameObject;
            Transform = Object.GetComponent<RectTransform>();
            Grid = Object.GetComponent<GridLayoutGroup>();
        }
    }

    private static void OnEnterSecondary(int i){Secondary.Info[i].OnEnterSecondary();}
    private static void OnLeaveSecondary(int i){Secondary.Info[i].OnLeaveSecondary();}
    private static void OnEnterPrimary(int i){
        if(i == SelectedIndex) Selected.OnSelect();
        Primary.Info[i].OnEnterPrimary();
    }
    private static void OnLeavePrimary(int i){
        if(i == SelectedIndex) Selected.OnDeselect();
        Primary.Info[i].OnLeavePrimary();
    }

    public static void Initialize(){
        GameObject Menu = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Inventory"), PlayerHandler.UIHandle.transform);
        PrimaryArea = new InventDisplay(Menu.transform.GetChild(0).GetChild(0).gameObject);
        SecondaryArea = new InventDisplay(Menu.transform.GetChild(0).GetChild(1).gameObject);
        PrimaryDisplay = new SlotDisplay[settings.PrimarySlotCount];
        SecondaryDisplay = new SlotDisplay[settings.SecondarySlotCount];
        ItemSettings = Config.CURRENT.Generation.Items;
        TextureAtlas = Config.CURRENT.Generation.Textures;

        GameObject slotDisplay = Resources.Load<GameObject>("Prefabs/GameUI/InventorySlot");
        for(int i = 0; i < settings.PrimarySlotCount; i++){
            GameObject slot = GameObject.Instantiate(slotDisplay, PrimaryArea.Object.transform);
            PrimaryDisplay[i] = new SlotDisplay(slot);
        }
        for(int i = 0; i < settings.SecondarySlotCount; i++){
            GameObject slot = GameObject.Instantiate(slotDisplay, SecondaryArea.Object.transform);
            SecondaryDisplay[i] = new SlotDisplay(slot);
        } Cursor = null;
        CursorDisplay = new SlotDisplay(GameObject.Instantiate(slotDisplay, Menu.transform)); 
        SecondaryArea.Region.SetActive(false);
        CursorDisplay.Object.SetActive(false);

        InputPoller.AddBinding(new InputPoller.ActionBind("Open Inventory", Activate), "3.0::Window");
        AddHotbarKeybinds();
        ReApplyHandles();

        CraftingMenuController.Initialize();
        Instance = new InventoryController{active = true};
        TerrainGeneration.OctreeTerrain.MainLoopUpdateTasks.Enqueue(Instance);

        Secondary.AddCallbacks(OnEnterSecondary, OnLeaveSecondary);
        Primary.AddCallbacks(OnEnterPrimary, OnLeavePrimary);
    }

    public static void Release(){ CraftingMenuController.Release(); }
    private static void Activate(float _){
        InputPoller.AddStackPoll(new InputPoller.ActionBind("Frame:Inventory", (float _) => InputPoller.SetCursorLock(false)), "CursorLock");
        InputPoller.AddKeyBindChange(() => {
            Fence = InputPoller.AddContextFence("3.0::Window", InputPoller.ActionBind.Exclusion.ExcludeAll);
            InputPoller.AddBinding(new InputPoller.ActionBind("Open Inventory", Deactivate), "3.0::Window");
            InputPoller.AddBinding(new InputPoller.ActionBind("Select", SelectDrag), "3.0::Window");
            InputPoller.AddBinding(new InputPoller.ActionBind("Deselect", DeselectDrag), "3.0::Window");
            InputPoller.AddBinding(new InputPoller.ActionBind("Craft", CraftEntry), "3.0::Window");
            InputPoller.AddBinding(new InputPoller.ActionBind("Place Terrain", CraftingMenuController.AddMaterial), "3.0::Window");
            InputPoller.AddBinding(new InputPoller.ActionBind("Remove Terrain", CraftingMenuController.RemoveMaterial), "3.0::Window");
        });
        CraftingMenuController.Activate();
        CursorDisplay.Object.SetActive(false);
        SecondaryArea.Region.SetActive(true);
        PrimaryArea.Transform.anchoredPosition = new Vector2(0, 0);
    }

    private static void Deactivate(float _){
        InputPoller.RemoveStackPoll("Frame:Inventory", "CursorLock");
        InputPoller.AddKeyBindChange(() => InputPoller.RemoveContextFence(Fence, "3.0::Window"));
        CraftingMenuController.Deactivate();
        SecondaryArea.Region.SetActive(false);
    }

    private static void ReApplyHandles(){
        //When the inventory is loaded from a save
        for(int i = 0; i < Primary.Info.Length; i++){
            if(Primary.Info[i] == null) continue;
            Primary.Info[i].OnEnterPrimary();
        }
        for(int i = 0; i < Secondary.Info.Length; i++){
            if(Secondary.Info[i] == null) continue;
            Secondary.Info[i].OnEnterSecondary();
        }
        Selected?.OnSelect();
    }

    private static void AddHotbarKeybinds(){
        static void ChangeSelected(int index){
            if(Selected != null) Selected.IsDirty = true;
            Selected?.OnDeselect();
            SelectedIndex = index % settings.PrimarySlotCount;
            Selected?.OnSelect();
            if(Selected != null) Selected.IsDirty = true;
        }
        InputPoller.AddBinding(new InputPoller.ActionBind("Hotbar1", (float _) => ChangeSelected(0)), "2.0::Subscene");
        InputPoller.AddBinding(new InputPoller.ActionBind("Hotbar2", (float _) => ChangeSelected(1)), "2.0::Subscene");
        InputPoller.AddBinding(new InputPoller.ActionBind("Hotbar3", (float _) => ChangeSelected(2)), "2.0::Subscene");
        InputPoller.AddBinding(new InputPoller.ActionBind("Hotbar4", (float _) => ChangeSelected(3)), "2.0::Subscene");
        InputPoller.AddBinding(new InputPoller.ActionBind("Hotbar5", (float _) => ChangeSelected(4)), "2.0::Subscene");
        InputPoller.AddBinding(new InputPoller.ActionBind("Hotbar6", (float _) => ChangeSelected(5)), "2.0::Subscene");
        InputPoller.AddBinding(new InputPoller.ActionBind("Hotbar7", (float _) => ChangeSelected(6)), "2.0::Subscene");
        InputPoller.AddBinding(new InputPoller.ActionBind("Hotbar8", (float _) => ChangeSelected(7)), "2.0::Subscene");
        InputPoller.AddBinding(new InputPoller.ActionBind("Hotbar9", (float _) => ChangeSelected(8)), "2.0::Subscene");
    }

    private static void SelectDrag(float _){
        Inventory Inv;
        if (GetMousePrimary(out int index)){
            Inv = Primary;
        } else if(GetMouseSecondary(out index)){
            Inv = Secondary;
        } else return;

        //Swap the cursor with the selected slot
        Cursor = Inv.Info[index];
        if(Cursor == null) return;
        Inv.RemoveEntry(index);

        CursorDisplay.Icon.sprite = TextureAtlas.Retrieve(Cursor.TexIndex);
        CursorDisplay.Amount.text = Cursor.Display;
        CursorDisplay.Object.SetActive(true);
    }

    private static void DeselectDrag(float _){
        static void DropItem(IItem item){
            WorldConfig.Generation.Entity.EntitySerial Entity = new();
            Entity.type = "EntityItem";
            Entity.data.info.entityId = Guid.NewGuid();
            Entity.data = new EItem.EItemEntity(new TerrainColliderJob.Transform{
                position = CPUDensityManager.WSToGS(PlayerHandler.player.transform.position),
                rotation = PlayerHandler.player.transform.rotation,
                
            }, item);

            EntityManager.AddHandlerEvent(() => EntityManager.CreateEntity(Entity));
        }

        if(Cursor == null) return;
        CursorDisplay.Object.SetActive(false);

        IItem cursor = Cursor; 
        Cursor = null; Inventory Inv = null;
        if (GetMousePrimary(out int index)){
            Inv = Primary;
        } else if(GetMouseSecondary(out index)){
            Inv = Secondary;
        } else { DropItem(cursor); return;}

        if(Inv.Info[index] == null)
            Inv.AddEntry(cursor, index);   
        else if(Inv.Info[index].IsStackable && cursor.Index == Inv.Info[index].Index) {
            Inv.AddStackable(cursor, index);
        } else if(Primary.EntryDict.ContainsKey(cursor.Index) && Inv.Info[index].IsStackable) {
            Primary.AddStackable(cursor);
        } else if(!Secondary.AddEntry(cursor, out int _)){ DropItem(cursor); return; };
    }

    private static int2 GetSlotIndex(InventDisplay display, float2 pos){
        float2 pOff = pos - ((float3)display.RTransform.position).xy;
        pOff.x *= display.RTransform.pivot.x * (-2) + 1;
        pOff.y *= display.RTransform.pivot.y * (-2) + 1;
        int2 slotInd = (int2)math.floor(pOff / (float2)(display.Grid.cellSize + display.Grid.spacing));
        return slotInd;
    }

    private static bool GetMousePrimary(out int index){
        int2 size = new (1, settings.PrimarySlotCount);
        int2 slot = GetSlotIndex(PrimaryArea, ((float3)Input.mousePosition).xy);
        index = 0;

        if(math.any(slot < 0) || math.any(slot >= size)) return false;
        index = slot.y * size.x + slot.x;
        return index < Primary.Info.Length;
    }

    private static bool GetMouseSecondary(out int index){
        int2 slot = GetSlotIndex(SecondaryArea, ((float3)Input.mousePosition).xy);
        //Add 2 to size to account for the border
        float2 rectSize = SecondaryArea.Transform.rect.size + 2 * SecondaryArea.Grid.spacing;
        float2 gridSize = SecondaryArea.Grid.cellSize + SecondaryArea.Grid.spacing;
        int2 size = (int2)math.floor(rectSize / gridSize);
        index = 0;
        
        if(math.any(slot < 0) || math.any(slot >= size)) return false;
        index = slot.y * size.x + slot.x;
        return index < Secondary.Info.Length;
    }

    private static void CraftEntry(float _){
        if(!CraftingMenuController.CraftRecipe(out IItem result) || result == null)
            return;
        AddEntry(result);
        CraftingMenuController.Clear();
    }

    //The Slot is changed to reflect what remains
    //if stackable, add to primary then secondary, else add to secondary then primary
    public static void AddEntry(IItem e){
        if(e.IsStackable){
            if(Primary.EntryDict.ContainsKey(e.Index)){
                Primary.AddStackable(e);
                if(e.AmountRaw == 0) return;
            } Secondary.AddStackable(e);
        } else {
            if(!Secondary.AddEntry(e, out int _))
                Primary.AddEntry(e, out int _);
        }
    }
    public static int RemoveMaterial(int delta){
        if(Selected == null) return 0;
        if(SelectedSetting.MaterialName == null) return 0;
        return Primary.RemoveStackableSlot(SelectedIndex, delta);
    }

    public override void Update(MonoBehaviour mono){
        ReflectInventory(PrimaryDisplay, Primary); 
        ReflectInventory(SecondaryDisplay, Secondary);
        CursorDisplay.Object.transform.position = Input.mousePosition;
        PrimaryDisplay[SelectedIndex].Icon.color = settings.SelectedColor;
    }

    private static void ReflectInventory(SlotDisplay[] display, Inventory inventory){
        for(int i = 0; i < display.Length; i++){
            SlotDisplay disp = display[i];
            IItem slot = inventory.Info[i];
            if(slot == null){
                disp.Icon.sprite = null;
                disp.Amount.text = "";
                disp.Icon.color = settings.BaseColor;
            } else if(slot.IsDirty){
                disp.Icon.sprite = TextureAtlas.Retrieve(slot.TexIndex);
                disp.Icon.color = Color.white; //1,1,1,1
                disp.Amount.text = slot.Display;
                slot.IsDirty = false;
            } 
        } 
    }


    public class Inventory{
        public IItem[] Info;
        public LLNode[] EntryLL;
        public Dictionary<int, int> EntryDict;
        public readonly uint capacity;
        public uint length;
        public uint tail;
        private Action<int> OnAddElement;
        private Action<int> OnRemoveElement;   

        public Inventory(int SlotCount){
            Info = new IItem[SlotCount];
            EntryDict = new Dictionary<int, int>();
            EntryLL = new LLNode[SlotCount];
            capacity = (uint)SlotCount;
            tail = 0;
            length = 0;
            for(int i = 0; i < SlotCount; i++){
                int next = (i + 1) % SlotCount;
                int prev = (((i - 1) % SlotCount) + SlotCount) % SlotCount;
                EntryLL[i] = new LLNode((uint)prev, (uint)next);
            } 
        }

        public void AddCallbacks(Action<int> OnAddItem = null, Action<int> OnRemoveItem = null){
            OnAddElement = OnAddItem;
            OnRemoveElement = OnRemoveItem;
        }

        //Input: The material id, amount to add, and index to add at
        //Input is modified with the remaineder of the amount
        public void AddStackable(IItem mat, int SlotIndex){
            if(Info[SlotIndex].Index != mat.Index) return;
            if(!Info[SlotIndex].IsStackable) return;
            
            IItem sMat = Info[SlotIndex];
            if(sMat.AmountRaw != 0xFFFF){ 
                int delta = math.min(sMat.AmountRaw + mat.AmountRaw, 0xFFFF) - sMat.AmountRaw;
                sMat.AmountRaw += delta;
                mat.AmountRaw -= delta;
                sMat.IsDirty = true;
                DictUpdate(SlotIndex);
            }

            if(mat.AmountRaw <= 0) return; 
            AddEntry(mat, out int ind);
        }


        //Input: The material id and amount to add
        //Input is modified with the remaineder of the amount
        public void AddStackable(IItem mat){
            if(!mat.IsStackable) return;

            while(mat.AmountRaw > 0){
                int SlotIndex = DictPeek(mat.Index);
                if(SlotIndex == -1) break;
                IItem sMat = Info[SlotIndex];
                if(sMat.AmountRaw == 0xFFFF) break;

                int delta = math.min(sMat.AmountRaw + mat.AmountRaw, 0xFFFF) - sMat.AmountRaw;
                sMat.AmountRaw += delta;
                mat.AmountRaw -= delta;
                sMat.IsDirty = true;
                DictUpdate(SlotIndex);
            }
            if(mat.AmountRaw <= 0) return;
            AddEntry(mat, out int ind);
        }

        //Input: Request a certain amount of material from a slot
        //Returns: The actual amount removed
        public int RemoveStackableSlot(int SlotIndex, int delta){
            IItem mat = Info[SlotIndex];
            delta = mat.AmountRaw - math.max(mat.AmountRaw - delta, 0);
            Info[SlotIndex].AmountRaw -= delta;
            mat.IsDirty = true;

            if(mat.AmountRaw != 0) DictUpdate(SlotIndex); 
            else RemoveEntry(SlotIndex); 

            return delta;
        }

        //Input: Request a certain type of stackable of a certain amount
        //Returns: The actual amount removed
        public int RemoveStackableKey(int KeyIndex, int delta){
            int start = delta; int remainder = start; 
            while(remainder > 0){
                int SlotIndex = DictPeek(KeyIndex);
                if(SlotIndex == -1) break;

                IItem mat = Info[SlotIndex];
                delta = mat.AmountRaw - math.max(mat.AmountRaw - remainder, 0);
                mat.AmountRaw -= delta; remainder -= delta;
                mat.IsDirty = true;
                if(mat.AmountRaw != 0) DictUpdate(SlotIndex);
                else RemoveEntry(SlotIndex);
            }
            return start - remainder;
        }

        public void RemoveEntry(int SlotIndex){
            int Index = Info[SlotIndex].Index;
            if(EntryDict.ContainsKey(Index) && SlotIndex == EntryDict[Index]){
                EntryDict[Index] = (int)EntryLL[SlotIndex].n;
                if(SlotIndex == EntryDict[Index]) EntryDict.Remove(Index);
            }
            LLRemove((uint)SlotIndex);
            LLAdd((uint)SlotIndex, tail);
            Info[SlotIndex].IsDirty = true;
            OnRemoveElement?.Invoke(SlotIndex);

            Info[SlotIndex] = null;
            tail = math.min(tail, (uint)SlotIndex);
            length--;
        }

        public bool AddEntry(IItem entry, out int head){
            head = (int)tail; 
            if(length == capacity-1) return false;
            tail = EntryLL[head].n; 
            length++;

            LLRemove((uint)head);
            Info[head] = entry.Clone() as IItem;
            entry.AmountRaw = 0;
            DictEnqueue(head);
            Info[head].IsDirty = true;
            OnAddElement?.Invoke(head);

            return true;
        }

        public bool AddEntry(IItem entry, int index){
            if(length == capacity-1) return false;
            if(Info[index] != null) return false; //Slot has to be null
            if(index == tail) tail = EntryLL[tail].n;
            length++;

            LLRemove((uint)index);
            Info[index] = entry.Clone() as IItem;
            entry.AmountRaw = 0;
            DictEnqueue(index);
            Info[index].IsDirty = true;
            OnAddElement?.Invoke(index);

            return true;
        }

        private void LLAdd(uint index, uint prev){
            if(prev == index) EntryLL[prev].n = index;
            EntryLL[index] = new LLNode(prev, EntryLL[prev].n);
            EntryLL[prev].n = index;
            EntryLL[EntryLL[index].n].p = index;
        }
        private void LLRemove(uint index){
            EntryLL[EntryLL[index].p].n = EntryLL[index].n;
            EntryLL[EntryLL[index].n].p = EntryLL[index].p;
        }

        //Caller already has added the entry to the inventory
        //Adds the entry to sorted LinkedList
        private void DictEnqueue(int index){
            IItem item = Info[index];
            if(item == null) return;
            int KeyIndex = item.Index;
            if(!EntryDict.ContainsKey(KeyIndex)){
                EntryDict.Add(KeyIndex, index);
                LLAdd((uint)index, (uint)index);
                return;
            }

            int next = EntryDict[KeyIndex]; 
            int end = (int)EntryLL[next].p;
            while(Info[next].AmountRaw < item.AmountRaw){
                if(next == EntryLL[end].n)
                    break;
                next = (int)EntryLL[next].n;
            }  uint prev = EntryLL[next].p; //get the previous node
            LLAdd((uint)index, prev);
            EntryDict[KeyIndex] = (int)EntryLL[end].n;
        }

        //There is no DictDequeue because caller should just call RemoveEntry
        private int DictPeek(int KeyIndex){
            if(!EntryDict.ContainsKey(KeyIndex)) return -1;
            int index = EntryDict[KeyIndex];
            return index;
        }

        private void DictUpdate(int index){
            IItem item = Info[index];
            if(item == null) return;
            int KeyIndex = item.Index;

            int next = EntryDict[KeyIndex]; 
            int end = (int)EntryLL[next].p;
            while(Info[next].AmountRaw < item.AmountRaw){
                if(next == EntryLL[end].n)
                    break;
                next = (int)EntryLL[next].n;
            } uint prev = EntryLL[next].p; //get the previous node

            LLRemove((uint)index);
            LLAdd((uint)index, prev);
            EntryDict[KeyIndex] = (int)EntryLL[end].n;
        }

        /*
            2 flag bits in beginning:
            00 -> Is a Liquid Material
            01 -> Is a Solid Material
            10 -> Is an Unstackable Item
            11 -> Is a Stackable Item
        */
        public struct LLNode{
            public uint p;
            public uint n;
            public LLNode(uint prev, uint next){
                p = prev;
                n = next;
            }
        }
    }
}
