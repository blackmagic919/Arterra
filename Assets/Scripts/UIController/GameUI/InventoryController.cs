
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;


public class InventoryController : UpdateTask
{
    public static Inventory Primary; //Hotbar
    public static Inventory Secondary; //Inventory
    public static InventoryController Instance;
    public static Inventory.Slot Cursor;
    public static SlotDisplay CursorDisplay;

    
    public static InventDisplay PrimaryArea;
    public static SlotDisplay[] PrimaryDisplay;
    public static InventDisplay SecondaryArea;
    public static SlotDisplay[] SecondaryDisplay;
    public static Settings settings;

    private static Queue<(string, uint)> Fences;
    private static MaterialData[] textures;
    public static Inventory.Slot Selected=>Primary.Info[SelectedIndex];
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

    public static void Initialize(){
        settings = WorldStorageHandler.WORLD_OPTIONS.GamePlay.Inventory.value;
        GameObject Menu = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Inventory"), PlayerHandler.UIHandle.transform);
        PrimaryArea = new InventDisplay(Menu.transform.GetChild(0).GetChild(0).gameObject);
        SecondaryArea = new InventDisplay(Menu.transform.GetChild(0).GetChild(1).gameObject);
        PrimaryDisplay = new SlotDisplay[settings.PrimarySlotCount];
        SecondaryDisplay = new SlotDisplay[settings.SecondarySlotCount];
        textures = WorldStorageHandler.WORLD_OPTIONS.Generation.Materials.value.MaterialDictionary.SerializedData;

        GameObject slotDisplay = Resources.Load<GameObject>("Prefabs/GameUI/InventorySlot");
        for(int i = 0; i < settings.PrimarySlotCount; i++){
            GameObject slot = GameObject.Instantiate(slotDisplay, PrimaryArea.Object.transform);
            PrimaryDisplay[i] = new SlotDisplay(slot);
        }
        for(int i = 0; i < settings.SecondarySlotCount; i++){
            GameObject slot = GameObject.Instantiate(slotDisplay, SecondaryArea.Object.transform);
            SecondaryDisplay[i] = new SlotDisplay(slot);
        }
        CursorDisplay = new SlotDisplay(GameObject.Instantiate(slotDisplay, Menu.transform)); Cursor.IsNull = true;
        SecondaryArea.Region.SetActive(false);
        CursorDisplay.Object.SetActive(false);

        AddBaseKeybinds();
        Fences = new Queue<(string, uint)>();
        CraftingMenuController.Initialize();
        Instance = new InventoryController{active = true};
        OctreeTerrain.MainLoopUpdateTasks.Enqueue(Instance);
    }

    public static void Release(){ CraftingMenuController.Release(); }
    private static void Activate(float _){
        InputPoller.AddKeyBindChange(() => {
            Fences.Enqueue(("Control", InputPoller.AddContextFence("Control")));
            Fences.Enqueue(("GamePlay", InputPoller.AddContextFence("GamePlay")));
            InputPoller.AddBinding("Open Inventory", "GamePlay", Deactivate);
            InputPoller.AddBinding("Select", "GamePlay", SelectDrag);
            InputPoller.AddBinding("Deselect", "GamePlay", DeselectDrag);
            InputPoller.AddBinding("Craft", "GamePlay", CraftEntry);
            InputPoller.AddBinding("Place Terrain", "GamePlay", CraftingMenuController.AddMaterial);
            InputPoller.AddBinding("Remove Terrain", "GamePlay", CraftingMenuController.RemoveMaterial);
        });
        CraftingMenuController.Activate();
        CursorDisplay.Object.SetActive(false);
        SecondaryArea.Region.SetActive(true);
        PrimaryArea.Transform.anchoredPosition = new Vector2(0, 0);
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
        SecondaryArea.Region.SetActive(false);
        InputPoller.SetCursorLock(true);
    }

    private static void AddBaseKeybinds(){
        static void ChangeSelected(int index){
            Primary.MakeDirty((uint)SelectedIndex);
            SelectedIndex = index % settings.PrimarySlotCount;
        }
        InputPoller.AddBinding("Open Inventory", "Control", Activate);

        InputPoller.AddBinding("Hotbar1", "UI", (float _) => ChangeSelected(0));
        InputPoller.AddBinding("Hotbar2", "UI", (float _) => ChangeSelected(1));
        InputPoller.AddBinding("Hotbar3", "UI", (float _) => ChangeSelected(2));
        InputPoller.AddBinding("Hotbar4", "UI", (float _) => ChangeSelected(3));
        InputPoller.AddBinding("Hotbar5", "UI", (float _) => ChangeSelected(4));
        InputPoller.AddBinding("Hotbar6", "UI", (float _) => ChangeSelected(5));
        InputPoller.AddBinding("Hotbar7", "UI", (float _) => ChangeSelected(6));
        InputPoller.AddBinding("Hotbar8", "UI", (float _) => ChangeSelected(7));
        InputPoller.AddBinding("Hotbar9", "UI", (float _) => ChangeSelected(8));
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

        if (GetMouseSlot(out Inventory inv, out int index)){
            if(inv.Info[index].IsNull)
                inv.AddEntry(Cursor, (uint)index);   
            else if(inv.Info[index].IsStackable && Cursor.Id == inv.Info[index].Id) 
                inv.AddStackable(Cursor, (uint)index);
            else{
                if(inv.EntryDict.ContainsKey(Cursor.Id)) 
                    Secondary.AddEntry(Cursor, inv.EntryDict[Cursor.Id]);
                else Secondary.AddEntry(Cursor, out uint _);
            }
        } else {
            EntitySerial Entity = new EntitySerial();
            Entity.data = new EItem.EItemEntity(new TerrainColliderJob.Transform{
                position = CPUDensityManager.WSToGS(PlayerHandler.player.transform.position),
                rotation = PlayerHandler.player.transform.rotation,
            }, Cursor);
            Entity.type = "EntityItem";
            Entity.guid = Guid.NewGuid().ToString();
            EntityManager.AddHandlerEvent(() => EntityManager.CreateEntity(Entity));
        }
        Cursor.IsNull = true;
    }

    private static int2 GetSlotIndex(InventDisplay display, float2 pos){
        float2 pOff = pos - ((float3)display.RTransform.position).xy;
        pOff.x *= display.RTransform.pivot.x * (-2) + 1;
        pOff.y *= display.RTransform.pivot.y * (-2) + 1;
        int2 slotInd = (int2)math.floor(pOff / (float2)(display.Grid.cellSize + display.Grid.spacing));
        return slotInd;
    }
    private static bool GetMouseSlot(out Inventory inv, out int index){
        inv = Primary; index = 0;
        int2 size = new (1, settings.PrimarySlotCount);
        int2 slot = GetSlotIndex(PrimaryArea, ((float3)Input.mousePosition).xy);
        if(math.any(slot < 0) || math.any(slot >= size)) {
            slot = GetSlotIndex(SecondaryArea, ((float3)Input.mousePosition).xy);
            //Add 2 to size to account for the border
            float2 rectSize = SecondaryArea.Transform.rect.size + 2 * SecondaryArea.Grid.spacing;
            float2 gridSize = SecondaryArea.Grid.cellSize + SecondaryArea.Grid.spacing;
            size = (int2)math.floor(rectSize / gridSize);
            inv = Secondary;
        } 
        if(math.any(slot < 0) || math.any(slot >= size)) 
            return false;
        //Encode this way because grid fills column first
        index = slot.y * size.x + slot.x;
        return index < inv.Info.Length;
    }


    private static void CraftEntry(float _){
        if(!CraftingMenuController.CraftRecipe(out Inventory.Slot result))
            return;
        if((result.IsStackable && Secondary.AddStackable(result) != 0) || 
            (!result.IsStackable && Secondary.AddEntry(result, out uint _))){
            CraftingMenuController.Clear();
        };
    }

    //Returns the amount added, 0 if none added
    //if stackable, add to primary then secondary, else add to secondary then primary
    public static int AddEntry(Inventory.Slot e){
        int delta = 0;
        if(e.IsStackable){
            if(Primary.EntryDict.ContainsKey(e.Id)){
                delta = Primary.AddStackable(e);
                if(e.AmountRaw == delta) 
                    return delta;
                e.AmountRaw -= delta;
            }
            return Secondary.AddStackable(e) + delta;
        } else {
            delta = Secondary.AddEntry(e, out uint _) || Primary.AddEntry(e, out uint _) ? 1 : 0;
            return delta;
        }
    }
    public static int RemoveMaterial(int delta){
        if(Selected.IsNull) return 0;
        if(Selected.IsItem) return 0;
        return Primary.RemoveStackable(SelectedIndex, delta);
    }

    public override void Update(MonoBehaviour mono){
        ReflectInventory(PrimaryDisplay, Primary); 
        ReflectInventory(SecondaryDisplay, Secondary);
        CursorDisplay.Object.transform.position = Input.mousePosition;
        PrimaryDisplay[SelectedIndex].Icon.color = settings.SelectedColor;
    }

    private static void ReflectInventory(SlotDisplay[] display, Inventory inventory){
        foreach(uint i in inventory.Dirty){
            SlotDisplay disp = display[i];
            Inventory.Slot slot = inventory.Info[i];
            if(slot.IsNull){
                disp.Icon.sprite = null;
                disp.Amount.text = "";
                disp.Icon.color = settings.BaseColor;
            } else {
                if(slot.Index >= textures.Length || slot.Index < 0) Debug.Log(slot.Index);
                disp.Icon.sprite = textures[(int)slot.Index].texture.value;
                disp.Icon.color = Color.white; //1,1,1,1
                disp.Amount.text = slot.Amount.ToString();
            } 
        } inventory.Dirty.Clear();
    }


    [Serializable]
    public struct Settings{
        public int PrimarySlotCount;
        public int SecondarySlotCount;

        //This is the theta angle the color is rotated by
        public Color SelectedColor;
        public Color BaseColor;
    }


    public class Inventory{
        public Slot[] Info;
        public LLNode[] EntryLL;
        public Dictionary<uint, uint> EntryDict;
        public readonly uint capacity;
        public uint length;
        public uint tail;
        [JsonIgnore]
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
            if(Info[index].Id != mat.Id) return 0;
            if(!Info[index].IsStackable) return 0;
            
            int delta = math.min(Info[index].AmountRaw + mat.AmountRaw, 0x7FFF) 
                    - Info[index].AmountRaw;
            Info[index].AmountRaw += delta;
            mat.AmountRaw -= delta;
            MakeDirty(index);

            if(mat.AmountRaw <= 0) return delta; 
            if(length == capacity-1) return delta;
            if(AddEntry(mat, out uint ind)) return delta;
            //Add new entry and clear its contents
            Info[ind].AmountRaw = 0;
            return AddStackable(mat) + delta;
        }


        //Input: The material id and amount to add
        //Returns: The actual amount added
        public int AddStackable(Slot mat){
            if(!mat.IsStackable) return 0;
            int delta = 0; 
            if(EntryDict.ContainsKey(mat.Id)){
                uint slotN = EntryDict[mat.Id];
                delta = math.min(Info[slotN].AmountRaw + mat.AmountRaw, 0x7FFF) 
                        - Info[slotN].AmountRaw;
                Info[slotN].AmountRaw += delta;
                mat.AmountRaw -= delta;
                MakeDirty(slotN);
            }
            if(mat.AmountRaw <= 0) return delta; 
            if(!AddEntry(mat, out uint ind)) return delta;
            //Add new entry and clear its contents
            Info[ind].AmountRaw = 0;

            return AddStackable(mat) + delta;
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

        public bool AddEntry(Slot entry, out uint head){
            head = tail;
            if(length == capacity-1) return false;
            tail = EntryLL[head].n; 
            length++;

            uint prevSlot = head;
            if(EntryDict.ContainsKey(entry.Id)) 
                prevSlot = EntryDict[entry.Id];
            
            LLRemove(head);
            LLAdd(head, prevSlot);
            Info[head] = entry;
            EntryDict[entry.Id] = head;

            MakeDirty(head);
            return true;
        }

        public bool AddEntry(Slot entry, uint index){
            if(length == capacity-1) return false;
            if(!Info[index].IsNull) return false; //Slot has to be null
            if(index == tail) tail = EntryLL[tail].n;
            length++;

            uint prevSlot = index;
            if(EntryDict.ContainsKey(entry.Id)) 
                prevSlot = EntryDict[entry.Id];

            LLRemove(index);
            LLAdd(index, prevSlot);
            Info[index] = entry;
            EntryDict[entry.Id] = index;
            MakeDirty(index);
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
                set => data = (data & 0xC0007FFF) | (((uint)value & 0x7FFF) << 15);
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
