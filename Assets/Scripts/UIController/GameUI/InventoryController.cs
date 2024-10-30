
using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.PlayerLoop;
using UnityEngine.UI;


public class InventoryController : UpdateTask
{
    public static Inventory Primary; //Hotbar
    public static Inventory Secondary; //Inventory
    public static InventoryController Instance;
    public static Inventory.Slot Cursor;
    public static SlotDisplay CursorDisplay;

    
    public static GameObject Menu;
    public static InventDisplay PrimaryArea;
    public static SlotDisplay[] PrimaryDisplay;
    public static InventDisplay SecondaryArea;
    public static SlotDisplay[] SecondaryDisplay;
    public static Settings settings;

    private static Queue<(string, uint)> Fences;
    private static MaterialData[] textures;
    public static Inventory.Slot Selected=>Primary.Info[0];

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
        public GameObject Object;
        public RectTransform Transform;
        public GridLayoutGroup Grid;
        public InventDisplay(GameObject obj){
            Object = obj;
            Transform = obj.GetComponent<RectTransform>();
            Grid = obj.GetComponent<GridLayoutGroup>();
        }
    }

    public static void Initialize(){
        settings = WorldStorageHandler.WORLD_OPTIONS.GamePlay.Inventory.value;
        Menu = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Inventory"), UIOrigin.UIHandle.transform);
        PrimaryArea = new InventDisplay(Menu.transform.GetChild(0).GetChild(0).GetChild(0).gameObject);
        SecondaryArea = new InventDisplay(Menu.transform.GetChild(1).GetChild(0).GetChild(0).gameObject);
        PrimaryDisplay = new SlotDisplay[settings.PrimarySlotCount];
        SecondaryDisplay = new SlotDisplay[settings.SecondarySlotCount];

        GameObject slotDisplay = Resources.Load<GameObject>("Prefabs/GameUI/InventorySlot");
        for(int i = 0; i < settings.PrimarySlotCount; i++){
            GameObject slot = GameObject.Instantiate(slotDisplay, PrimaryArea.Object.transform);
            PrimaryDisplay[i] = new SlotDisplay(slot);
        }
        for(int i = 0; i < settings.SecondarySlotCount; i++){
            GameObject slot = GameObject.Instantiate(slotDisplay, SecondaryArea.Object.transform);
            SecondaryDisplay[i] = new SlotDisplay(slot);
        }
        Menu.SetActive(false);
        CursorDisplay = new SlotDisplay(GameObject.Instantiate(slotDisplay, Menu.transform));
        Cursor.IsNull = true;

        Fences = new Queue<(string, uint)>();
        InputPoller.AddBinding(new InputPoller.Binding("Open Inventory", "Control", InputPoller.BindPoll.Down, Activate));
        CraftingMenuController.Initialize();

        textures = WorldStorageHandler.WORLD_OPTIONS.Generation.Materials.value.MaterialDictionary.SerializedData;
    }

    public static void Release(){ CraftingMenuController.Release(); }

    private static void Activate(float _){
        InputPoller.AddKeyBindChange(() => {
            Fences.Enqueue(("Control", InputPoller.AddContextFence("Control")));
            Fences.Enqueue(("GamePlay", InputPoller.AddContextFence("GamePlay")));
            InputPoller.AddBinding(new InputPoller.Binding("Open Inventory", "GamePlay", InputPoller.BindPoll.Down, Deactivate));
            InputPoller.AddBinding(new InputPoller.Binding("Select", "GamePlay", InputPoller.BindPoll.Down, SelectDrag));
            InputPoller.AddBinding(new InputPoller.Binding("Select", "GamePlay", InputPoller.BindPoll.Up, DeselectDrag));
            InputPoller.AddBinding(new InputPoller.Binding("Craft", "GamePlay", InputPoller.BindPoll.Hold, CraftEntry));
            InputPoller.AddBinding(new InputPoller.Binding("Place Terrain", "GamePlay", InputPoller.BindPoll.Hold, CraftingMenuController.AddMaterial));
            InputPoller.AddBinding(new InputPoller.Binding("Remove Terrain", "GamePlay", InputPoller.BindPoll.Hold, CraftingMenuController.RemoveMaterial));
        });
        Instance = new InventoryController{active = true};
        OctreeTerrain.MainLoopUpdateTasks.Enqueue(Instance);
        CraftingMenuController.Activate();
        CursorDisplay.Object.SetActive(false);
        Menu.SetActive(true);
        InputPoller.SetCursorLock(false);
    }

    private static void Deactivate(float _){
        InputPoller.AddKeyBindChange(() => {
            while(Fences.Count > 0){
                var (context, fence) = Fences.Dequeue();
                InputPoller.RemoveContextFence(fence, context);
            }
        });
        CraftingMenuController.Deactivate();
        Instance.active = false;
        Menu.SetActive(false);
        InputPoller.SetCursorLock(true);
    }

    private static void SelectDrag(float _){
        if (!GetMouseSlot(out Inventory inv, out int index))
            return;

        //Swap the cursor with the selected slot
        Cursor = inv.Info[index];
        if(Cursor.IsNull) return;
        inv.RemoveEntry(index);
        CursorDisplay.Icon.sprite = textures[(int)Cursor.Index].texture.value;
        CursorDisplay.Amount.text = Cursor.Amount.ToString();
        CursorDisplay.Object.SetActive(true);
    }

     private static void DeselectDrag(float _){
        if(Cursor.IsNull) return;
        CursorDisplay.Object.SetActive(false);

        bool HasSlot = GetMouseSlot(out Inventory inv, out int index);
        if (HasSlot && inv.Info[index].IsNull){
            int prevSlot = index;
            if(inv.EntryDict.ContainsKey(Cursor.Id)) 
                prevSlot = (int)inv.EntryDict[Cursor.Id];
            inv.AddEntry(Cursor, (uint)index, (uint)prevSlot);   
        } else if(HasSlot && inv.Info[index].IsStackable && Cursor.Id == inv.Info[index].Id){ 
            //If we're trying to merge slots of same type
            inv.AddStackable(Cursor, (uint)index);
        } else{ 
            //If the slot is occupied, or not in inventory, just add to next available slot
            if(inv.EntryDict.ContainsKey(Cursor.Id)) 
                Secondary.AddEntry(Cursor, inv.EntryDict[Cursor.Id]);
            else Secondary.AddEntry(Cursor);
        }
        Cursor.IsNull = true;
    }

    private static int2 GetSlotIndex(InventDisplay display, float2 pos){
        float2 pOff = pos - ((float3)display.Transform.position).xy;
        pOff.x *= display.Transform.pivot.x * (-2) + 1;
        pOff.y *= display.Transform.pivot.y * (-2) + 1;
        int2 slotInd = (int2)math.floor(pOff / (float2)(display.Grid.cellSize + display.Grid.spacing));
        return slotInd;
    }
    private static bool GetMouseSlot(out Inventory inv, out int index){
        inv = Primary; index = 0;
        int2 size = new (1, settings.PrimarySlotCount);
        int2 slot = GetSlotIndex(PrimaryArea, ((float3)Input.mousePosition).xy);
        if(math.any(slot < 0) || math.any(slot >= size)) {
            slot = GetSlotIndex(SecondaryArea, ((float3)Input.mousePosition).xy);
            size = (int2)math.floor(SecondaryArea.Transform.rect.size / SecondaryArea.Grid.cellSize);
            inv = Secondary;
        } 
        if(math.any(slot < 0) || math.any(slot >= size)) 
            return false;
        //Encode this way because grid fills column first
        index = slot.y * size.x + slot.x;
        if(index < inv.Info.Length) 
            return true;
        return false;
    }


    private static void CraftEntry(float _){
        if(!CraftingMenuController.CraftRecipe(out Inventory.Slot result))
            return;
        if((result.IsStackable && Secondary.AddStackable(result) != -1) || 
            (!result.IsStackable && Secondary.AddEntry(result) != -1)){
            CraftingMenuController.Clear();
        };
    }

    //Place the material in the hotbar if in hotbar
    //otherwise place it in the inventory
    public static int AddMaterial(Inventory.Slot mat){
        int delta = 0;
        if(Primary.EntryDict.ContainsKey(mat.Id)){
            delta = Primary.AddStackable(mat);
            if(mat.AmountRaw == delta) 
                return delta;
            mat.AmountRaw -= delta;
        }
        return Secondary.AddStackable(mat) + delta;
    }
    public static int RemoveMaterial(int delta){
        if(Primary.Info[0].IsNull) return 0;
        if(Primary.Info[0].IsItem) return 0;
        return Primary.RemoveStackable(0, delta);
    }

    public static Inventory Serialize(Inventory a){
        for(int i = 0; i < a.Info.Length; i++){
            if(a.Info[i].data != 0)
                a.MakeDirty((uint)i);
        }
        return a;
    }

    public override void Update(MonoBehaviour mono){
        void ReflectInventory(SlotDisplay[] display, Inventory inventory){
            foreach(uint i in inventory.Dirty){
                SlotDisplay disp = display[i];
                Inventory.Slot slot = inventory.Info[i];
                if(slot.IsNull){
                    disp.Icon.sprite = null;
                    disp.Amount.text = "";
                } else {
                    disp.Icon.sprite = textures[(int)slot.Index].texture.value;
                    disp.Amount.text = slot.Amount.ToString();
                }
            } inventory.Dirty.Clear();
        }
        ReflectInventory(PrimaryDisplay, Primary);
        ReflectInventory(SecondaryDisplay, Secondary);
        CursorDisplay.Object.transform.position = Input.mousePosition;
    }

    [Serializable]
    public struct Settings{
        public int PrimarySlotCount;
        public int SecondarySlotCount;
    }


    public class Inventory{
        public Slot[] Info;
        public LLNode[] EntryLL;
        public Dictionary<uint, uint> EntryDict;
        public readonly uint capacity;
        public uint length;
        public uint tail;
        public HashSet<uint> Dirty;    

        public Inventory(int SlotCount){
            Info = new Slot[SlotCount];
            EntryDict = new Dictionary<uint, uint>();
            Dirty = new HashSet<uint>();
            EntryLL = new LLNode[SlotCount];
            capacity = (uint)SlotCount;
            tail = 0;
            length = 0;
            for(int i = 0; i < SlotCount; i++){
                int next = (i + 1) % SlotCount;
                int prev = (((i - 1) % SlotCount) + SlotCount) % SlotCount;
                EntryLL[i] = new LLNode((uint)prev, (uint)next);
                Info[i].IsNull = true;
            } 
        }

        //Input: The material id, amount to add, and index to add at
        //Returns: The actual amount added
        public int AddStackable(Slot mat, uint index){
            int delta = mat.AmountRaw; 
            if(Info[index].Id != mat.Id) return 0;
            if(!Info[index].IsStackable) return 0;

            delta = math.min(Info[index].AmountRaw + delta, 0x7FFF) 
                    - Info[index].AmountRaw;
            Info[index].AmountRaw += delta;
            mat.AmountRaw -= delta;
            MakeDirty(index);

            if(mat.AmountRaw <= 0) return (int)delta; 
            if(length == capacity-1) return (int)delta;
            //Add new entry and clear its contents
            Info[AddEntry(mat, index)].AmountRaw = 0;
            return AddMaterial(mat) + (int)delta;
        }


        //Input: The material id and amount to add
        //Returns: The actual amount added
        public int AddStackable(Slot mat){
            if(!mat.IsStackable) return 0;
            int delta = mat.AmountRaw; uint slotN = tail;
            if(EntryDict.ContainsKey(mat.Id)){
                slotN = EntryDict[mat.Id];
                delta = math.min(Info[slotN].AmountRaw + delta, 0x7FFF) 
                        - Info[slotN].AmountRaw;
                Info[slotN].AmountRaw += delta;
                mat.AmountRaw -= delta;
                MakeDirty(slotN);
            }
            if(mat.AmountRaw <= 0) return (int)delta; 
            if(length == capacity-1) return (int)delta;
            //Add new entry and clear its contents
            Info[AddEntry(mat, slotN)].AmountRaw = 0;

            return AddMaterial(mat) + (int)delta;
        }

        //Input: Request removing from a slot of a certain amount
        //Returns: The actual amount removed
        public int RemoveStackable(int SlotIndex, int delta){
            Slot mat = Info[SlotIndex];
            delta = mat.AmountRaw - math.max(mat.AmountRaw - delta, 0);
            Info[SlotIndex].AmountRaw -= delta;
            MakeDirty((uint)SlotIndex);

            if(Info[SlotIndex].AmountRaw == 0)
                RemoveEntry(SlotIndex);

            return delta;
        }

        public void RemoveEntry(int SlotIndex){
            uint Id = Info[SlotIndex].Id;
            if(SlotIndex == EntryDict[Id]){
                EntryDict[Id] = EntryLL[SlotIndex].n;
                if(SlotIndex == EntryDict[Id])
                    EntryDict.Remove(Id);
            }
            LLRemove((uint)SlotIndex);
            LLAdd((uint)SlotIndex, tail);
            MakeDirty((uint)SlotIndex);
            Info[SlotIndex].IsNull = true;
            tail = SlotIndex < tail ? (uint)SlotIndex : tail;
            length--;
        }

        public int AddEntry(Slot entry, uint prevSlot = 0xFFFFFFFF){
            if(length == capacity-1) return -1;
            if(prevSlot == 0xFFFFFFFF) prevSlot = tail;
            uint head = tail;
            tail = EntryLL[head].n; 
            length++;

            LLRemove(head);
            LLAdd(head, prevSlot);
            Info[head] = entry;
            EntryDict[entry.Id] = head;

            MakeDirty(head);
            return (int)head;
        }

        public void AddEntry(Slot entry, uint index, uint prevSlot){
            if(length == capacity-1) return;
            if(!Info[index].IsNull) return;
            if(index == tail) tail = EntryLL[tail].n;
            length++;

            LLRemove(index);
            LLAdd(index, prevSlot);
            Info[index] = entry;
            EntryDict[entry.Id] = index;
            MakeDirty(index);
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

        public void MakeDirty(uint index){
            Dirty.Add(index);
        }

        /*
            2 flag bits in beginning:
            00 -> Is a Liquid Material
            01 -> Is a Solid Material
            10 -> Is an Unstackable Item
            11 -> Is a Stackable Item
        */
        public struct Slot{
            public uint data;
            [JsonIgnore]
            public readonly bool IsStackable => !IsItem || (IsItem && IsSolid);
            [JsonIgnore]
            public bool IsItem{
                readonly get => (data & 0x80000000) != 0;
                set => data = value ? data | 0x80000000 : data & 0x7FFFFFFF;
            }
            [JsonIgnore]
            public int Index{
                readonly get => (int)(data >> 15) & 0x7FFF;
                set => data = (data & 0xC0007FFF) | ((uint)value << 15);
            }
            [JsonIgnore]
            public uint Id{
                readonly get => data & 0xFFFF8000;
                set => data = (data & 0x7FFF) | (value & 0xFFFF8000);
            }
            [JsonIgnore]
            public bool IsNull{
                readonly get => data == 0xFFFFFFFF;
                set => data = value ? 0xFFFFFFFF : 0;
            }

            //Slot-Type Specific Accessors
            [JsonIgnore]
            public bool IsSolid{
                readonly get => (data & 0x40000000) != 0;
                set => data = value ? data | 0x40000000 : data & 0xBFFFFFFF;
            }
            [JsonIgnore]
            public float Amount{
                readonly get => (data & 0x7FFF) / (float)0xFF;
                set => data = (data & 0xFFFF8000) | (((uint)Mathf.Round(value * 0xFF)) & 0x7FFF);
            }
            [JsonIgnore]
            public int AmountRaw{
                readonly get => (int)(data & 0x7FFF);
                set => data = (data & 0xFFFF8000) | ((uint)value & 0x7FFF);
            }
            [JsonIgnore]
            public float ItemAccuracy{
                readonly get => (data & 0x7FFF) / 0x7FFF;
                set => data = data & 0xFFFF8000 | (((uint)Mathf.Round(value * 0x7FFF)) & 0x7FFF);
            }
        }
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
