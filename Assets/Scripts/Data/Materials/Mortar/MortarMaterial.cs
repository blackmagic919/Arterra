using System.Collections;
using Arterra.Core.Storage;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Configuration.Generation.Item;
using Utils;
using Arterra.Configuration.Intrinsic.Mortar;

namespace Arterra.Configuration.Generation.Material
{
    /// <summary> A mortar material which can crush items put into it to produced
    /// crushed variants. </summary>
    [CreateAssetMenu(menuName = "Generation/MaterialData/MortarMat")]
    public class MortarMaterial : PlaceableStructureMat {
        /// <summary> The index within the <see cref="MaterialData.Names"> name registry </see> of the 
        /// texture within the texture registry of the icon displayed on the <see cref="PanelNavbarManager">Navbar</see>
        /// referring to the Container.  </summary>
        [RegistryReference("Textures")]
        public int DisplayIcon;
        /// <summary> The maximum amount of  slots this mortar will have for smashing items. Includes both inputs and outputs </summary>
        public int MaxSlotCount;
        /// <summary> The maximum amount of formulas to display in the help menu </summary>
        public int MaxRecipeSlotCount;



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

        /// <summary>Returns a mortar inventory created using the meta constructor.</summary>
        /// <param name="GCoord">The coordinate in grid space of the material</param>
        /// <param name="constructor">The constructor used to populate the inventory</param>
        /// <returns>The container instance</returns>
        public override object ConstructMetaData(int3 GCoord, MetaConstructor constructor) {
            return new MortarInventory(constructor, GCoord, MaxSlotCount);
        }

        /// <summary> The handler controlling how materials are dropped when
        /// <see cref="OnRemoved"/> is called. See 
        /// <see cref="MaterialData.ItemLooter"/> for more info.  </summary>
        public ItemLooter MaterialDrops;

        private MortarInventory OpenedInventory = null;
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
            if (!CPUMapManager.GetOrCreateMapMeta(GCoord, () => new MortarInventory(GCoord, MaxSlotCount), out OpenedInventory))
                return false;

            OpenedInventory.InitializeDisplay(this);
            InventoryController.Activate();
            InputPoller.AddKeyBindChange(() => {
                //Overrides the default inventory close window
                InputPoller.AddContextFence("MAT::Container", "3.5::Window", ActionBind.Exclusion.None);
                InputPoller.AddBinding(new ActionBind("Open Inventory",
                _ => DeactivateWindow(), ActionBind.Exclusion.ExcludeLayer), "MAT::Mortar:CL", "3.5::Window");
                //Naturally propogate to inventory handles unless suspended manually
                InputPoller.AddBinding(new ActionBind("Deselect", DeselectDrag, ActionBind.Exclusion.None), "MAT::Mortar:DS", "3.5::Window");
                InputPoller.AddBinding(new ActionBind("Select", SelectDrag, ActionBind.Exclusion.None), "MAT::Mortar:SL", "3.5::Window");
                InputPoller.AddBinding(new ActionBind("SelectPartial", SelectPartial),  "MAT::Mortar:SELP", "3.5::Window");
                InputPoller.AddBinding(new ActionBind("SelectAll", SelectAll), "MAT::Mortar:SELA", "3.0::AllWindow");

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
            if (!CPUMapManager.TryGetExistingMapMeta(GCoord, out MortarInventory cont))
                return;
            foreach (IItem item in cont.Inventory.Info)
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
                    InputPoller.RemoveBinding("MAT::Mortar:SELA", "3.0::AllWindow");
                    InputPoller.RemoveContextFence("MAT::Mortar", "3.5::Window");
                    OpenedInventory = null;
                });
            }
            InventoryController.Deactivate();
        }

        // Helper function to find which inventory and index is being selected
        private bool SelectInventorySlot(out IInventory inv, out int slotIndex) {
            MortarInventory cont = OpenedInventory;
            inv = null;

            if (cont.Inventory.Display.GetMouseSelected(out slotIndex)) {
                inv = cont.Inventory;
                return true;
            }
            
            return false;
        }

        private void SelectPartial(float _ = 0) {
            if (!SelectInventorySlot(out IInventory inv, out int slotIndex))
                return;

            if (!InventoryController.SelectPartial((out IInventory _inv, out int index) => {
                _inv = inv;
                index = slotIndex;
                return true;
            })) {
                return;
            }
            OpenedInventory.StartAutoCrushingProcess();
            InputPoller.SuspendKeybindPropogation("SelectPartial", ActionBind.Exclusion.ExcludeLayer);
        }

        private void SelectDrag(float _ = 0) {
            // Check if we are clicking on a valid inventory slot
            if (!SelectInventorySlot(out IInventory inv, out int slotIndex))
                return;

            if (!InventoryController.Select((out IInventory _inv, out int index) => {
                _inv = inv;
                index = slotIndex;
                return true;
            })) {
                return;
            }
            
            OpenedInventory.StartAutoCrushingProcess();
            InputPoller.SuspendKeybindPropogation("Select", ActionBind.Exclusion.ExcludeLayer);
        }

        private void DeselectDrag(float _ = 0) {
            // Check if we are clicking on a valid inventory slot
            if (!SelectInventorySlot(out IInventory inv, out int slotIndex))
                return;

            if (!InventoryController.DeselectDrag((out IInventory _inv, out int index) => {
                _inv = inv;
                index = slotIndex;
                return true;
            })) {
                return;
            }

            OpenedInventory.StartAutoCrushingProcess();
            InputPoller.SuspendKeybindPropogation("Deselect", ActionBind.Exclusion.ExcludeLayer);
        }

        private void SelectAll(float _ = 0) {
            InventoryController.SelectAll(OpenedInventory.Inventory);
        }

        public enum InvIndex {
            Input = 0,
            Output = 1,
        }


        // This object will be serialized into the map meta data to store the inventory of the Mortar
        public class MortarInventory : PanelNavbarManager.INavPanel {
            private static WaitForSeconds _waitForSeconds1_0 = new WaitForSeconds(1.0f);

            //public InventoryController.Inventory inv;
            public int3 position;
            public StackInventory Inventory;

            [JsonIgnore]
            public GameObject root;


            [JsonIgnore]
            private MortarMaterial settings;
            [JsonIgnore]
            private MortarRecipeSearch RecipeSearch;

            [JsonConstructor]
            public MortarInventory() {
                //Do nothing: this is for newtonsoft deserializer
            }

            internal MortarInventory(int3 GCoord, int MaxSlotCount) {
                Inventory = new StackInventory(MaxSlotCount);
                position = GCoord;
            }

            public MortarInventory(MetaConstructor constructor, int3 GCoord,  int MaxSlotCount) {
                Inventory = new StackInventory(MaxSlotCount);
                position = GCoord;
            }

            //Auto Grinding: To Be Added in Future
            public void StartAutoCrushingProcess() {
                return;/* Add once adding ether crystals
                if (MortarFormula.GetMatchingFormula(Inventory) == null)
                    return;
                OctreeTerrain.MainCoroutines.Enqueue(UpdateRoutine());*/
            }

            private IEnumerator UpdateRoutine() {
                while (true) {
                    yield return _waitForSeconds1_0; // Update every second
                    if (!TryGrind(1.0f)) yield break;
                }
            }

            internal void InitializeDisplay(MortarMaterial settings) {
                root = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Mortar/Mortar"));
                Transform mortRegion = root.transform.GetChild(0).GetChild(0);
                GameObject center = mortRegion.Find("Input").gameObject;
                RecipeSearch = new MortarRecipeSearch(settings, root.transform, Inventory, settings.MaxRecipeSlotCount);

                Animator mortarAnimator = root.GetComponent<Animator>();
                HoldDownUI holdTrigger = mortRegion.Find("Pestle").GetComponent<HoldDownUI>();

                holdTrigger.ClearAllListeners();
                mortarAnimator.SetBool("IsGrinding", false); 
                holdTrigger.AddHeldListener(() => {
                    TryGrind(Time.deltaTime);
                });
                holdTrigger.AddDownListener(() => {
                   mortarAnimator.SetBool("IsGrinding", true); 
                });
                holdTrigger.AddUpListener(() => {
                   mortarAnimator.SetBool("IsGrinding", false); 
                });

                Inventory.InitializeHorizontalDisplay(center, false);
                this.settings = settings;
            }

            public void Release() {
                if (root == null) return;
                Inventory.ReleaseStackDisplay();
                RecipeSearch?.Deactivate();
                GameObject.Destroy(root);
                root = null;
            }

            public bool TryGrind(float deltaTime) {
                var formula = MortarFormula.GetMatchingFormula(Inventory);
                if (formula == null) return false;

                int delta;
                foreach (var input in formula.Inputs.value) {
                    IItem item = Config.CURRENT.Generation.Items.Retrieve(formula.Index(input)).Item;
                    delta = CustomUtility.GetStaggeredDelta(item.UnitSize * input.Rate * deltaTime);
                    delta = Inventory.RemoveStackableKey(formula.Index(input), delta);
                }

                foreach(var output in formula.Outputs.value) {
                    IItem item = Config.CURRENT.Generation.Items.Retrieve(formula.Index(output)).Item;
                    int amount = CustomUtility.GetStaggeredDelta(item.UnitSize * output.Rate * deltaTime);
                    item.Create(formula.Index(output),(int)math.min(amount, item.StackLimit));
                    Inventory.AddStackable(item);
                    //Run out of output space
                    if (item.AmountRaw > 0)
                        return false;
                }
                return true;
            }


            public Sprite GetNavIcon() => Config.CURRENT.Generation.Textures.Retrieve(
                settings.Names.value[settings.DisplayIcon]).self;
            public GameObject GetDispContent() => root;
            public void Activate() => root.SetActive(true);
            public void Deactivate() => root.SetActive(false);
        }
    }

}