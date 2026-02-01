using System.Collections;
using Arterra.Core.Storage;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Arterra.Configuration.Intrinsic.Furnace;
using Arterra.Configuration.Generation.Item;
using Arterra.Core.Terrain;
using Arterra.Core.Player;
using Utils;

namespace Arterra.Configuration.Generation.Material
{
    /// <summary> A furnace material which can hold fuel items, input items and output items in its meta-data
    /// and is accessible to users who click on the item. 
    /// A furnace is a type of container that can burn fuel items to smelt items and store the resulting output items. </summary>
    [CreateAssetMenu(menuName = "Generation/MaterialData/FurnaceMat")]
    public class FurnaceMaterial : PlaceableStructureMat {
        /// <summary> The index within the <see cref="MaterialData.Names"> name registry </see> of the 
        /// texture within the texture registry of the icon displayed on the <see cref="PanelNavbarManager">Navbar</see>
        /// referring to the Container.  </summary>
        [RegistryReference("Textures")]
        public int DisplayIcon;
        /// <summary> The tag that must be present on the material for furnace to use it as fuel.  
        /// The object associated with this tag in <see cref="TagRegistry"/> must be of type <see cref="ConvertableTag"/> </summary>
        public TagRegistry.Tags FuelTag;
        /// <summary> The name of the material that will be swapped with the current material
        /// when the furnace enters a "lit" state, this new material can be used to give a glowing effect  </summary>
        [RegistryReference("Materials")]
        public string LitFurnaceMaterial;
        /// <summary> The name of the material that will be swapped with the current material
        /// when the furnace enters an "unlit" state, this should generally be the base material  </summary>
        [RegistryReference("Materials")]
        public string UnlitFurnaceMaterial;
        /// <summary> The maximum amount of item slots this furnace will have for Input items </summary>
        public int MaxInputSlotCount;
        /// <summary> The maximum amount of item slots this furnace will have for Output items </summary>
        public int MaxOutputSlotCount;
        /// <summary> The maximum amount of item slots this furnace will have for Fuel items </summary>
        public int MaxFuelSlotCount;
        /// <summary> The maximum amount of formulas to display in the help menu </summary>
        public int MaxRecipeSlotCount;
        /// <summary>The event that is triggered when this furnace is opened.</summary>
        public Core.Events.GameEvent OpenEvent = Core.Events.GameEvent.Action_OpenFurnace;



        /// <summary> Even though it does nothing, it needs to fufill the contract so
        /// that it can be used in the same way as other materials. </summary>
        /// <param name="GCoord">The coordinate in grid space of a map entry that is this material</param>
        /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
        public override void PropogateMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng) {

        }
        /// <summary> Even though it does nothing, it needs to fufill the contract so
        /// that it can be used in the same way as other materials. </summary>
        /// <param name="GCoord">The coordinate in grid space of a map entry that is this material</param>
        /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
        public override void RandomMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng) {

        }

        /// <summary>Returns a furnace inventory created using the meta constructor.</summary>
        /// <param name="GCoord">The coordinate in grid space of the material</param>
        /// <param name="constructor">The constructor used to populate the inventory</param>
        /// <returns>The container instance</returns>
        public override object ConstructMetaData(int3 GCoord, MetaConstructor constructor) {
            return new FurnaceInventory(constructor, GCoord, MaxInputSlotCount, MaxOutputSlotCount, MaxFuelSlotCount);
        }

        /// <summary> The handler controlling how materials are dropped when
        /// <see cref="OnRemoved"/> is called. See 
        /// <see cref="MaterialData.ItemLooter"/> for more info.  </summary>
        public ItemLooter MaterialDrops;

        private FurnaceInventory OpenedInventory = null;
        public override bool OnRemoving(int3 GCoord, Entity.Entity caller) {
            if (caller == null || caller.info.entityType != Config.CURRENT.Generation.Entities.RetrieveIndex("Player"))
                return false;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                return false;
            MapData info = CPUMapManager.SampleMap(GCoord);
            if (info.IsNull || !info.IsSolid) return false;

            if (!VerifyStructurePlaceement(GCoord)) {
                DropInventoryContent(GCoord);
                return false;
            }

            float progress = (info.SolidDensity - CPUMapManager.IsoValue) /
                             (255.0f - CPUMapManager.IsoValue);
            if (!CPUMapManager.GetOrCreateMapMeta(GCoord, () => new FurnaceInventory
                (GCoord, MaxInputSlotCount, MaxOutputSlotCount, MaxFuelSlotCount),
                out OpenedInventory))
                return false;

            OpenedInventory.InitializeDisplay(this);
            InventoryController.Activate();
            InputPoller.AddKeyBindChange(() => {
                //Overrides the default inventory close window
                InputPoller.AddContextFence("MAT::Container", "3.5::Window", ActionBind.Exclusion.None);
                InputPoller.AddBinding(new ActionBind("Open Inventory",
                _ => DeactivateWindow(), ActionBind.Exclusion.ExcludeLayer), "MAT::Furnace:CL", "3.5::Window");
                //Naturally propogate to inventory handles unless suspended manually
                InputPoller.AddBinding(new ActionBind("Deselect", DeselectDrag, ActionBind.Exclusion.None), "MAT::Furnace:DS", "3.5::Window");
                InputPoller.AddBinding(new ActionBind("Select", SelectDrag, ActionBind.Exclusion.None), "MAT::Furnace:SL", "3.5::Window");
                InputPoller.AddBinding(new ActionBind("SelectPartial", SelectPartial),  "MAT::Furnace:SELP", "3.5::Window");
                InputPoller.AddBinding(new ActionBind("SelectAll", SelectAll), "MAT::Furnace:SELA", "3.0::AllWindow");

            });
            PanelNavbarManager.Add(OpenedInventory, name);
            PanelNavbarManager.Activate(name);
            return true;
        }

        /// <summary> See <see cref="MaterialData.OnRemoved"/> for more information. </summary>
        /// <param name="amount">The map data indicating the amount of material removed
        /// and the state it was removed as</param>
        /// <param name="GCoord">The location of the map information being</param>
        /// <returns>The item to give.</returns>
        public override Item.IItem OnRemoved(int3 GCoord, in MapData amount) {
             MapData info = CPUMapManager.SampleMap(GCoord);
            if (info.IsNull || amount.IsNull) return null;
            if (!info.IsSolid) return MaterialDrops.LootItem(amount, Names);
            if (OpenedInventory != null && math.all(OpenedInventory.position == GCoord))
                DeactivateWindow();
            int SolidDensity = info.SolidDensity - amount.SolidDensity;
            if (!VerifyStructurePlaceement(GCoord) || SolidDensity < CPUMapManager.IsoValue)
                DropInventoryContent(GCoord);

            return MaterialDrops.LootItem(amount, Names);
        }

        private void DropInventoryContent(int3 GCoord) {
            if (!CPUMapManager.TryGetExistingMapMeta(GCoord, out FurnaceInventory cont))
                return;
            foreach (IItem item in cont.FuelInventory.Info)
                InventoryController.DropItem(item, GCoord);
            foreach (IItem item in cont.InputInventory.Info)
                InventoryController.DropItem(item, GCoord);
            foreach (IItem item in cont.OutputInventory.Info)
                InventoryController.DropItem(item, GCoord);
            CPUMapManager.SetExistingMapMeta<object>(GCoord, null);
        }


        public override void OnPlaced(int3 GCoord, in MapData amount) {
            MapData info = CPUMapManager.SampleMap(GCoord);
            if (info.IsNull || amount.IsNull) return;
            if (OpenedInventory != null && math.all(OpenedInventory.position == GCoord))
                DeactivateWindow();
        }

        private void DeactivateWindow() {
            //IMPORTANT: The order in which these functions are called is VERY important
            PanelNavbarManager.Remove(name);
            PanelNavbarManager.Deactivate();
            if (OpenedInventory != null) {
                InputPoller.AddKeyBindChange(() => {
                    InputPoller.RemoveBinding("MAT::Furnace:SELA", "3.0::AllWindow");
                    InputPoller.RemoveContextFence("MAT::Furnace", "3.5::Window");
                    OpenedInventory = null;
                });
            }
            InventoryController.Deactivate();
        }

        // Helper function to find which inventory and index is being selected
        private bool SelectInventorySlot(out IInventory inv, out int invIndex, out int slotIndex) {
            FurnaceInventory cont = OpenedInventory;
            inv = null;
            invIndex = -1;
            slotIndex = -1;
            for (int i = 0; i < cont.invs.Length; i++) { // last inventory is output only, skip it
                var subInv = cont.invs[i];
                if (subInv.Display.GetMouseSelected(out slotIndex)) {
                    inv = subInv;
                    invIndex = i;
                    return true;
                }
            }
            return false;
        }

        private void SelectPartial(float _ = 0) {
            if (!SelectInventorySlot(out IInventory inv, out int invIndex, out int slotIndex))
                return;

            if (!InventoryController.SelectPartial((out IInventory _inv, out int index) => {
                _inv = inv;
                index = slotIndex;
                return true;
            })) {
                return;
            }
            OpenedInventory.StartMeltingProcess(FuelTag);
            OpenedInventory.RecalculateTemperature();
            InputPoller.SuspendKeybindPropogation("SelectPartial", ActionBind.Exclusion.ExcludeLayer);
        }

        private void SelectDrag(float _ = 0) {
            // Check if we are clicking on a valid inventory slot
            if (!SelectInventorySlot(out IInventory inv, out int invIndex, out int slotIndex))
                return;

            if (!InventoryController.Select((out IInventory _inv, out int index) => {
                _inv = inv;
                index = slotIndex;
                return true;
            })) {
                return;
            }
            
            OpenedInventory.StartMeltingProcess(FuelTag);
            OpenedInventory.RecalculateTemperature();
            InputPoller.SuspendKeybindPropogation("Select", ActionBind.Exclusion.ExcludeLayer);
        }

        private void DeselectDrag(float _ = 0) {
            // Check if we are clicking on a valid inventory slot
            if (!SelectInventorySlot(out IInventory inv, out int invIndex, out int slotIndex))
                return;

            if (!InventoryController.DeselectDrag((out IInventory _inv, out int index) => {
                _inv = inv;
                index = slotIndex;
                return true;
            })) {
                return;
            }

            OpenedInventory.StartMeltingProcess(FuelTag);
            OpenedInventory.RecalculateTemperature();
            InputPoller.SuspendKeybindPropogation("Deselect", ActionBind.Exclusion.ExcludeLayer);
        }

        private void SelectAll(float _ = 0) {
            InventoryController.SelectAll(OpenedInventory.FuelInventory);
            InventoryController.SelectAll(OpenedInventory.InputInventory);
            InventoryController.SelectAll(OpenedInventory.OutputInventory);
            OpenedInventory.RecalculateTemperature();
        }

        public enum InvIndex {
            Input = 0,
            Output = 1,
            Fuel = 2,
        }

        // This object will be serialized into the map meta data to store the inventory of the furnace
        public class FurnaceInventory : MaterialInstance, PanelNavbarManager.INavPanel {
            public StackInventory[] invs;

            [JsonIgnore]
            public GameObject root;

            [JsonIgnore]
            public StackInventory FuelInventory => invs[(int)InvIndex.Fuel];

            [JsonIgnore]
            public StackInventory InputInventory => invs[(int)InvIndex.Input];

            [JsonIgnore]
            public StackInventory OutputInventory => invs[(int)InvIndex.Output];
            private TextMeshProUGUI Temperature;


            [JsonIgnore]
            private FurnaceMaterial settings;
            [JsonIgnore]
            private FurnaceRecipeSearch RecipeSearch;


            public FurnaceInventory() {
                //Do nothing: this is for newtonsoft deserializer
            }

            internal FurnaceInventory(int3 GCoord, int MaxInputSlotCount, int MaxOutputSlotCount, int MaxFuelSlotCount) : base(GCoord) {
                var invFuel = new StackInventory(MaxFuelSlotCount);
                var invInput = new StackInventory(MaxInputSlotCount);
                var invOutput = new StackInventory(MaxOutputSlotCount, 0);
                invs = new StackInventory[] { invInput, invOutput, invFuel };
            }

            public FurnaceInventory(MetaConstructor constructor, int3 GCoord,  int MaxInputSlotCount, int MaxOutputSlotCount, int MaxFuelSlotCount) : base(GCoord){
                var invFuel = new StackInventory(constructor, GCoord, MaxFuelSlotCount);
                var invInput = new StackInventory(constructor, GCoord, MaxInputSlotCount);
                var invOutput = new StackInventory(constructor, GCoord, MaxOutputSlotCount, 0);
                invs = new StackInventory[] { invInput, invOutput, invFuel };
            }
            
            public void StartMeltingProcess(TagRegistry.Tags fuelTag) {
                if (FurnaceFormula.GetMatchingFormula(fuelTag, FuelInventory, InputInventory, OutputInventory) == null)
                    return;
                LightFurnace();
                OctreeTerrain.MainCoroutines.Enqueue(UpdateRoutine());
            }

            internal void InitializeDisplay(FurnaceMaterial settings) {
                root = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Furnace/Furnace"));
                Temperature = root.transform.GetChild(0).GetChild(0).Find("Temperature").GetComponent<TextMeshProUGUI>();
                GameObject left = root.transform.GetChild(0).GetChild(0).Find("Fuel").gameObject;
                GameObject center = root.transform.GetChild(0).GetChild(0).Find("Input").gameObject;
                GameObject right = root.transform.GetChild(0).GetChild(0).Find("Output").gameObject;
                RecipeSearch = new FurnaceRecipeSearch(settings, root.transform, invs, settings.MaxRecipeSlotCount);
                invs[(int)InvIndex.Fuel].InitializeHorizontalDisplay(left, false);
                invs[(int)InvIndex.Input].InitializeHorizontalDisplay(center, false);
                invs[(int)InvIndex.Output].InitializeVerticalDisplay(right, true);
                this.settings = settings;

                if (FurnaceFormula.GetMatchingFormula(settings.FuelTag, FuelInventory, InputInventory, OutputInventory) == null) 
                    root.GetComponent<Image>().sprite = Resources.Load<Sprite>("Prefabs/GameUI/Furnace/FurnaceUnlit");
                else root.GetComponent<Image>().sprite = Resources.Load<Sprite>("Prefabs/GameUI/Furnace/FurnaceLit");
            }

            public void Release() {
                if (root == null) return;
                foreach (var inv in invs)
                    inv.ReleaseStackDisplay();
                RecipeSearch?.Deactivate();
                GameObject.Destroy(root);
                Temperature = null;
                root = null;
            }

            private IEnumerator UpdateRoutine() {
                while (true) {
                    yield return new WaitForSeconds(1.0f); // Update every second

                    var formula = FurnaceFormula.GetMatchingFormula(
                        settings.FuelTag, FuelInventory, InputInventory, OutputInventory);

                    if (formula == null) {
                        PutOutFurnace();
                        yield break;
                    }

                    // Update FuelItem
                    var itemInfo = Config.CURRENT.Generation.Items;
                    int sourceCount = 0; int delta; 
                    for(int i = 0; i < FuelInventory.count; i++) {
                        Item.IItem item = FuelInventory.PeekItem(i);
                        if (item == null) continue;
                        if (!itemInfo.GetMostSpecificTag(settings.FuelTag, item.Index, out object _))
                            continue;
                        sourceCount++;
                    } 
                    sourceCount = math.max(sourceCount, 1);
                    float consumeRate = 1.0f / sourceCount;
                    for(int i = 0; i < FuelInventory.count; i++) {
                        Item.IItem item = FuelInventory.PeekItem(i);
                        if (item == null) continue;
                        if (!itemInfo.GetMostSpecificTag(settings.FuelTag, item.Index, out object prop))
                            continue;
                        var combustible = prop as CombustibleTag;
                        delta = CustomUtility.GetStaggeredDelta(item.UnitSize * combustible.BurningRate * consumeRate);
                        FuelInventory.RemoveStackableKey(item.Index, delta);
                    }
                    

                    foreach (var input in formula.Inputs.value) {
                        IItem item = Config.CURRENT.Generation.Items.Retrieve(formula.Index(input)).Item;
                        delta = CustomUtility.GetStaggeredDelta(item.UnitSize * input.Rate);
                        delta = InputInventory.RemoveStackableKey(formula.Index(input), delta);
                    }

                    foreach(var output in formula.Outputs.value) {
                        IItem item = Config.CURRENT.Generation.Items.Retrieve(formula.Index(output)).Item;
                        item.Create(formula.Index(output),(int)math.min(item.UnitSize * output.Rate, item.StackLimit));
                        OutputInventory.AddStackable(item);
                        RecalculateTemperature();
                        //Run out of output space
                        if (item.AmountRaw > 0) {
                            PutOutFurnace();
                            yield break;
                        }
                    }
                }
            }

            public void RecalculateTemperature() {
                if (Temperature == null) return;
                float totalTemp = 0; int count = 0;
                for(int i = 0; i < FuelInventory.count; i++) {
                    Item.IItem item = FuelInventory.PeekItem(i);
                    if (item == null) continue;
                    if (!Config.CURRENT.Generation.Items.GetMostSpecificTag(settings.FuelTag, item.Index, out object prop))
                        continue;
                    var combustible = prop as CombustibleTag;
                    totalTemp += combustible.Temperature;
                    count++;
                }
                float fuelTemperature = totalTemp / math.max(count, 1);
                if (fuelTemperature == 0) Temperature.text = null;
                else Temperature.text = $"{fuelTemperature}°";
            }


            private void LightFurnace() {
                if (root != null) root.GetComponent<Image>().sprite = Resources.Load<Sprite>("Prefabs/GameUI/Furnace/FurnaceLit");
                var matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
                int litIndex = matInfo.RetrieveIndex(settings.LitFurnaceMaterial);
                MapData data = CPUMapManager.SampleMap(position);
                if (data.material == litIndex) return;
                data.material = litIndex;
                CPUMapManager.SetMap(data, position);
            }

            private void PutOutFurnace() {
                if (root != null) root.GetComponent<Image>().sprite = Resources.Load<Sprite>("Prefabs/GameUI/Furnace/FurnaceUnlit");
                var matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
                int unlitIndex = matInfo.RetrieveIndex(settings.UnlitFurnaceMaterial);
                MapData data = CPUMapManager.SampleMap(position);
                if (data.material == unlitIndex) return;
                data.material = unlitIndex;
                CPUMapManager.SetMap(data, position);
            }

            public Sprite GetNavIcon() => Config.CURRENT.Generation.Textures.Retrieve(
                settings.Names.value[settings.DisplayIcon]).self;
            public GameObject GetDispContent() => root;
            public void Activate(){
                root.SetActive(true);
                PlayerHandler.data.eventCtrl.RaiseEvent(settings.OpenEvent, PlayerHandler.data, null);
            }
            public void Deactivate() => root.SetActive(false);
        }
    }

    public class StackInventory : InventoryController.Inventory {
        private GameObject ExpandIcon;
        private bool HideOnEmpty;
        public int stackLimit;
        public int freeSlots;
        public int count;
        // Note: Do not name this parameter capacity or freeSlots, Newtonsoft then tries to supply it to the constructor
        // and will not overwrite it later even though it's a public field (this is stupid behavior)
        [JsonConstructor]
        public StackInventory(int stackLimit, int fs = 1) : base(fs) {
            this.stackLimit = stackLimit;
            this.freeSlots = fs;
            this.count = 0;
        }

        internal StackInventory(MaterialData.MetaConstructor constructor, int3 GCoord, int stackLimit, int fs = 1) : this(stackLimit, fs) {
            int seed = GCoord.x ^ GCoord.y ^ GCoord.z ^ this.GetHashCode();
            Unity.Mathematics.Random rng = Utils.CustomUtility.SeedRng(seed);

            var lootTable = constructor.LootTable.value;
            for(int i = 0; i < stackLimit; i++) {
                foreach(var loot in lootTable) {
                    if (rng.NextFloat() > loot.DropChance)
                        continue;
                    
                    IItem item = Config.CURRENT.Generation.Items.Retrieve(loot.DropItem).Item;
                    int amount = (int)math.round(rng.NextFloat() * item.UnitSize * loot.DropMultiplier * 2);
                    amount = math.min(amount, item.StackLimit);
                    if (amount <= 0) continue;

                    item.Create(item.Index, amount);
                    AddEntry(item, out int _);
                    break;
                }
            }
        }

        [JsonIgnore]
        public override int Capacity => stackLimit;
        public override IItem PeekItem(int index) {
            if (index < capacity) return base.PeekItem(index);
            return null;
        }

        public override void RemoveEntry(int slot) {
            if (Info[slot] == null) return;
            count--;
            if (count == 0 && HideOnEmpty && parent != null)
                parent.SetActive(false);

            base.RemoveEntry(slot);
            if (capacity <= freeSlots) return;
            slot++;
            while(slot < capacity && PeekItem(slot) != null) {
                IItem item = PeekItem(slot)?.Clone() as IItem;
                base.RemoveEntry(slot);
                base.AddEntry(item, slot - 1);
                slot++;
            }
            if (capacity - count <= freeSlots) return;
            ResizeStack((int)capacity - 1);
        }

        public override bool AddEntry(IItem entry, int index) {
            if (capacity < stackLimit) ResizeStack((int)capacity + 1);
            if(base.AddEntry(entry, index)) {
                if (entry == null) return true;
                if (HideOnEmpty && parent != null)
                    parent.SetActive(true);
                count++;
                return true;
            } else return false;
        }

        public override bool AddEntry(IItem entry, out int head){
            if (capacity < stackLimit) ResizeStack((int)capacity + 1);
            if(base.AddEntry(entry, out head)) {
                if (entry == null) return true;
                if (HideOnEmpty && parent != null)
                    parent.SetActive(true);
                count++;
                return true;
            } else return false;
        }

        private void ResizeStack(int SlotCount) {
            base.ResizeInventory(SlotCount);
            if(ExpandIcon == null) return;
            ExpandIcon.transform.SetAsLastSibling();
            ExpandIcon.SetActive(freeSlots > 0 && capacity < stackLimit);
        }

        public void ReleaseStackDisplay() {
            if (ExpandIcon != null) GameObject.Destroy(ExpandIcon);
            ReleaseDisplay();
        }

        public void InitializeHorizontalDisplay(GameObject parent, bool HideOnEmpty) {
            ExpandIcon = Resources.Load<GameObject>("Prefabs/GameUI/Furnace/RightArrow");
            InitializeDisplay(parent);
            ExpandIcon = GameObject.Instantiate(ExpandIcon, Display.Object.transform);
            ExpandIcon.SetActive(freeSlots > 0 && capacity < stackLimit);
            this.HideOnEmpty = HideOnEmpty;
            parent.SetActive(!HideOnEmpty || (count != 0));
        }
        
        private GameObject parent => Display?.root.transform.parent.gameObject;

        public void InitializeVerticalDisplay(GameObject parent, bool HideOnEmpty) {
            ExpandIcon = Resources.Load<GameObject>("Prefabs/GameUI/Furnace/DownArrow");
            InitializeDisplay(parent);
            //Set to expand upwards from bottom
            Display.Transform.pivot = new Vector2(0, 0);
            
            ExpandIcon = GameObject.Instantiate(ExpandIcon, parent.transform);
            ExpandIcon.SetActive(freeSlots > 0 && capacity < stackLimit);
            this.HideOnEmpty = HideOnEmpty;
            parent.SetActive(!HideOnEmpty || (count != 0));
            
        }


    }

}