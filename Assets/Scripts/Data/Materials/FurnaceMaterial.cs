using System;
using System.Collections;
using System.Collections.Generic;
using MapStorage;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using WorldConfig.Gameplay;
using System.Linq;
using UnityEngine.Rendering;
using WorldConfig.Generation.Furnace;
using TerrainGeneration;
using UnityEngine.Analytics;

namespace WorldConfig.Generation.Material
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
        /// <summary> The maximum amount of item slots this container will have
        /// if it is at maximum density. The actual amount of slots will scale between
        /// 3 and this depending on how dense the material is.  
        /// 3 is the minimum number of slots: 1 for fuel, 1 for input, and 1 for output. </summary>
        public int MaxSlotCount;

        /// <summary> The tag that must be present on the material for furnace to use it as fuel.  
        /// The object associated with this tag in <see cref="TagRegistry"/> must be of type <see cref="ConvertableTag"/> </summary>
        public TagRegistry.Tags FuelTag;

        /// <summary> The tag that must be present on the material for furnace to use it as input.  
        /// The object associated with this tag in <see cref="TagRegistry"/> must be of type <see cref="ConvertableTag"/> </summary>
        public TagRegistry.Tags InputTag;


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

        /// <summary> The handler controlling how materials are dropped when
        /// <see cref="OnRemoved"/> is called. See 
        /// <see cref="MaterialData.ItemLooter"/> for more info.  </summary>
        public ItemLooter MaterialDrops;

        private FurnaceInventory OpenedInventory = null;
        public override bool OnRemoving(int3 GCoord, Entity.Entity caller) {
            if (caller == null || caller.info.entityType != Config.CURRENT.Generation.Entities.RetrieveIndex("Player"))
                return false;
            if (PlayerMovement.IsSprinting) return false;
            MapData info = CPUMapManager.SampleMap(GCoord);
            if (info.IsNull || !info.IsSolid) return false;

            float progress = (info.SolidDensity - CPUMapManager.IsoValue) /
                             (255.0f - CPUMapManager.IsoValue);
            int SlotCount = Mathf.FloorToInt(MaxSlotCount * progress) * 3 + 3;
            if (!CPUMapManager.GetOrCreateMapMeta(GCoord, () => new FurnaceInventory
                (GCoord, SlotCount), out OpenedInventory))
                return false;

            OpenedInventory.InitializeDisplay(this);
            InventoryController.Activate();
            InputPoller.AddKeyBindChange(() => {
                //Overrides the default inventory close window
                InputPoller.AddContextFence("MAT::Container", "3.5::Window", ActionBind.Exclusion.None);
                InputPoller.AddBinding(new ActionBind("Open Inventory",
                _ => DeactivateWindow(), ActionBind.Exclusion.ExcludeLayer), "MAT::Furnace:CL", "3.5::Window");
                //Naturally propogate to inventory handles unless suspended manually
                InputPoller.AddBinding(new ActionBind("Deselect",
                _ => DeselectDrag(FuelTag), ActionBind.Exclusion.None), "MAT::Furnace:DS", "3.5::Window");
                InputPoller.AddBinding(new ActionBind("Select",
                _ => SelectDrag(FuelTag), ActionBind.Exclusion.None), "MAT::Furnace:SL", "3.5::Window");

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
            return null;
            /*
            MapData info = CPUMapManager.SampleMap(GCoord);
            if (info.IsNull || amount.IsNull) return null;
            if (!info.IsSolid) return MaterialDrops.LootItem(amount, Names);
            if (OpenedInventory != null && math.all(OpenedInventory.position == GCoord))
                DeactivateWindow();
            if (!CPUMapManager.TryGetExistingMapMeta(GCoord, out FurnaceInventory cont))
                return MaterialDrops.LootItem(amount, Names);
            int SolidDensity = info.SolidDensity - amount.SolidDensity;
            if (SolidDensity < CPUMapManager.IsoValue) {
                foreach (Item.IItem item in cont.inv.Info)
                    InventoryController.DropItem(item, GCoord);
                CPUMapManager.SetExistingMapMeta<object>(GCoord, null);
                return MaterialDrops.LootItem(amount, Names);
            }

            float progress = (SolidDensity - CPUMapManager.IsoValue) /
                             (255.0f - CPUMapManager.IsoValue);
            int SlotCount = Mathf.FloorToInt(MaxSlotCount * progress) + 1;
            cont.inv.ResizeInventory(SlotCount, Item => InventoryController.DropItem(Item, GCoord));
            return MaterialDrops.LootItem(amount, Names);
            */
        }

        public override void OnPlaced(int3 GCoord, in MapData amount) {
            MapData info = CPUMapManager.SampleMap(GCoord);
            if (info.IsNull || amount.IsNull) return;
            if (OpenedInventory != null && math.all(OpenedInventory.position == GCoord))
                DeactivateWindow();
            int SolidDensity = info.SolidDensity + amount.SolidDensity;
            if (SolidDensity < CPUMapManager.IsoValue) return;

            float progress = (SolidDensity - CPUMapManager.IsoValue) /
                             (255.0f - CPUMapManager.IsoValue);
            int SlotCount = Mathf.FloorToInt(MaxSlotCount * progress) + 1;
            if (!CPUMapManager.TryGetExistingMapMeta(GCoord, out FurnaceInventory cont))
                return;
            //if (cont.inv.capacity == SlotCount) return;
            //cont.inv.ResizeInventory(SlotCount, Item => InventoryController.DropItem(Item, GCoord));
        }

        private void DeactivateWindow() {
            //IMPORTANT: The order in which these functions are called is VERY important
            PanelNavbarManager.Remove(name);
            PanelNavbarManager.Deactivate();
            if (OpenedInventory != null) {
                InputPoller.AddKeyBindChange(() => {
                    InputPoller.RemoveContextFence("MAT::Furnace", "3.0::Window");
                    OpenedInventory = null;
                });
            }
            InventoryController.Deactivate();
        }

        private static InventoryController.CursorManager Cursor => InventoryController.Cursor;

        // Helper function to find which inventory and index is being selected
        private bool SelectInventorySlot(out InventoryController.Inventory inv, out int invIndex, out int slotIndex) {
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

        private void SelectDrag(TagRegistry.Tags fuelTag) {
            // Check if we are clicking on a valid inventory slot
            if (!SelectInventorySlot(out InventoryController.Inventory inv, out int invIndex, out int slotIndex)) {
                return;
            }

            if (!InventoryController.Select((out InventoryController.Inventory _inv, out int index) => {
                _inv = inv;
                index = slotIndex;
                return true;
            })) {
                return;
            }
            InputPoller.SuspendKeybindPropogation("Select", ActionBind.Exclusion.ExcludeLayer);
        }

        private void DeselectDrag(TagRegistry.Tags fuelTag) {
            // Check if we are clicking on a valid inventory slot
            if (!SelectInventorySlot(out InventoryController.Inventory inv, out int invIndex, out int slotIndex)) {
                //InventoryController.AddEntry(InventoryController.Cursor);
                //clearCursor();
                return;
            }

            // If we are clicking on the fuel inventory, check if the cursor item is valid fuel
            if (invIndex == FurnaceInventory.FuelInvIndex) {
                var itemInfo = Config.CURRENT.Generation.Items;
                if (Cursor == null || Cursor.Item == null) return;
                if (!itemInfo.GetMostSpecificTag(fuelTag, Cursor.Item.Index, out object prop)) {
                    return;
                }
                OpenedInventory.fuelTag = prop as CombustibleTag;
            }

            if (!InventoryController.DeselectDrag((out InventoryController.Inventory _inv, out int index) => {
                _inv = inv;
                index = slotIndex;
                return true;
            })) {
                return;
            }

            // Check for valid furnace formula
            FurnaceInventory cont = OpenedInventory;
            var fomula = FurnaceFormulas.GetMatchingFormula(
                cont.FuelItem, fuelTag, new List<Item.IItem>(cont.InputItems), new List<Item.IItem>(cont.OutputItems));

            if (fomula != null) {
                if (cont.OutputInventory.capacity < fomula.Outputs.Count) {
                    cont.OutputInventory.ResizeInventory(fomula.Outputs.Count + 1,
                        Item => InventoryController.DropItem(Item, cont.position));
                }
                var outputItems = fomula.CreateOutputItems();
                foreach (var outputItem in outputItems) {
                    cont.OutputInventory.AddEntry(outputItem, out int _);
                }
                cont.StartMeltingProcess();
                //cont.redrawOutputs();
                // Start smelting process
                Debug.Log("Started smelting with formula at temperature: " + fomula.Temperature);
            } else {
                // No valid formula found
                Debug.Log("No valid smelting formula found.");
            }
            InputPoller.SuspendKeybindPropogation("Deselect", ActionBind.Exclusion.ExcludeLayer);
        }


        // This object will be serialized into the map meta data to store the inventory of the furnace
        public class FurnaceInventory : PanelNavbarManager.INavPanel {
            public InventoryController.Inventory[] invs;
            //public InventoryController.Inventory inv;
            public int3 position;

            public CombustibleTag fuelTag;

            [JsonIgnore]
            public GameObject root;

            public const int FuelInvIndex = 0;
            public const int InputInvIndex = 1;
            public const int OutputInvIndex = 2;

            [JsonIgnore]
            public Item.IItem FuelItem => invs[FuelInvIndex].Info[0];

            [JsonIgnore]
            // from invs[InputInvIndex].Info array filter out nulls
            public Item.IItem[] InputItems => invs[InputInvIndex].Info.Where(item => item != null).ToArray();

            [JsonIgnore]
            public Item.IItem[] OutputItems => invs[OutputInvIndex].Info.Where(item => item != null).ToArray();

            [JsonIgnore]
            public InventoryController.Inventory FuelInventory => invs[FuelInvIndex];

            [JsonIgnore]
            public InventoryController.Inventory InputInventory => invs[InputInvIndex];

            [JsonIgnore]
            public InventoryController.Inventory OutputInventory => invs[OutputInvIndex];


            [JsonIgnore]
            private FurnaceMaterial settings;


            public FurnaceInventory() {
                //Do nothing: this is for newtonsoft deserializer
            }

            public void StartMeltingProcess() {
                OctreeTerrain.MainCoroutines.Enqueue(UpdateRoutine());
            }

            internal FurnaceInventory(int3 GCoord, int SlotCount) {
                var invFuel = new InventoryController.Inventory(1);
                var invInput = new InventoryController.Inventory(3);
                var invOutput = new InventoryController.Inventory(1);
                invs = new InventoryController.Inventory[] { invFuel, invInput, invOutput };
                position = GCoord;
            }

            internal void InitializeDisplay(FurnaceMaterial settings) {
                root = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Inventory/Furnace"));
                GameObject left = root.transform.GetChild(0).GetChild(0).Find("Fuel").gameObject;
                GameObject center = root.transform.GetChild(0).GetChild(0).Find("Input").gameObject;
                GameObject right = root.transform.GetChild(0).GetChild(0).Find("Output").gameObject;
                invs[0].InitializeDisplay(left);
                invs[1].InitializeDisplay(center);
                invs[2].InitializeDisplay(right);
                this.settings = settings;

                //OctreeTerrain.MainCoroutines.Enqueue(UpdateRoutine());
            }

            private IEnumerator UpdateRoutine() {
                if (fuelTag == null) {
                    yield break;
                }
                while (true) {
                    yield return new WaitForSeconds(1.0f); // Update every second

                    var formula = FurnaceFormulas.GetMatchingFormula(
                        FuelItem, settings.FuelTag, new List<Item.IItem>(InputItems), new List<Item.IItem>(OutputItems));

                    if (formula == null) {
                        yield break;
                    }

                    else {
                        // Update FuelItem
                        int newAmmount = FuelItem.AmountRaw - (int)(FuelItem.UnitSize * fuelTag.BurningRate);
                        FuelItem.AmountRaw = Math.Max(0, newAmmount);
                        if (newAmmount == 0) {
                            FuelInventory.RemoveEntry(FuelItem);
                            yield break;
                        }

                        foreach (var input in formula.Inputs)
                        {
                            foreach (var item in InputItems)
                            {
                                string matName = Config.CURRENT.Generation.Items.Retrieve(item.Index).Name;
                                if (matName == formula.Names.value[input.Index])
                                {
                                    int newAmount = Math.Max(0, item.AmountRaw - (int)(item.UnitSize * input.Rate));
                                    item.AmountRaw = newAmount;
                                    if (newAmount == 0)
                                    {
                                        InputInventory.RemoveEntry(item);
                                        yield break;
                                    }
                                }
                            }
                        }

                        formula.Outputs.ForEach(output => {
                            OutputItems.ToList().ForEach(item => {
                                string matName = Config.CURRENT.Generation.Items.Retrieve(item.Index).Name;
                                if (matName == formula.Names.value[output.Index]) {
                                    item.AmountRaw += (int)(item.UnitSize * output.Rate);
                                }
                            });
                        });
                    }

                }
            }

            public void redrawInputs() {
                invs[InputInvIndex].ReleaseDisplay();
                GameObject center = root.transform.GetChild(0).GetChild(0).Find("Input").gameObject;
                invs[InputInvIndex].InitializeDisplay(center);
            }

            public void redrawOutputs() {
                invs[OutputInvIndex].ReleaseDisplay();
                GameObject right = root.transform.GetChild(0).GetChild(0).Find("Output").gameObject;
                invs[OutputInvIndex].InitializeDisplay(right);
            }

            public Sprite GetNavIcon() => Config.CURRENT.Generation.Textures.Retrieve(
                settings.Names.value[settings.DisplayIcon]).self;
            public GameObject GetDispContent() => root;
            public void Release() {
                if (root == null) return;
                foreach (var inv in invs) {
                    inv.ReleaseDisplay();
                }
                GameObject.Destroy(root);
                root = null;
            }
            public void Activate() => root.SetActive(true);
            public void Deactivate() => root.SetActive(false);
        }

    }
}