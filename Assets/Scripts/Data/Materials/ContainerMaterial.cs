using Arterra.Core.Storage;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Config.Generation.Item;

namespace Arterra.Config.Generation.Material
{
    /// <summary> A container material which can hold items in its meta-data
    /// and is accessible to users who click on the item. </summary>
    [CreateAssetMenu(menuName = "Generation/MaterialData/ContainerMat")]
    public class ContainerMaterial : PlaceableStructureMat {
        /// <summary> The index within the <see cref="MaterialData.Names"> name registry </see> of the 
        /// texture within the texture registry of the icon displayed on the <see cref="PanelNavbarManager">Navbar</see>
        /// referring to the Container.  </summary>
        [RegistryReference("Textures")] 
        public int DisplayIcon;
        /// <summary> The maximum amount of item slots this container will have
        /// if it is at maximum density. The actual amount of slots will scale between
        /// one and this depending on how dense the material is.  </summary>
        public int MaxSlotCount;


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
            if (!VerifyStructurePlaceement(GCoord))
                DropInventoryContent(GCoord);
        }

        /// <summary> The handler controlling how materials are dropped when
        /// <see cref="OnRemoved"/> is called. See 
        /// <see cref="MaterialData.ItemLooter"/> for more info.  </summary>
        public ItemLooter MaterialDrops;

        private ContainerInventory OpenedInventory = null;
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

            if (!CPUMapManager.GetOrCreateMapMeta(GCoord, () => new ContainerInventory
                (GCoord, MaxSlotCount), out OpenedInventory))
                return false;
            
            OpenedInventory.InitializeDisplay(this);
            InventoryController.Activate();
            InputPoller.AddKeyBindChange(() => {
                //Overrides the default inventory close window
                InputPoller.AddContextFence("MAT::Container", "3.5::Window", ActionBind.Exclusion.None);
                InputPoller.AddBinding(new ActionBind("Open Inventory",
                    DeactivateWindow, ActionBind.Exclusion.ExcludeLayer), "MAT::Container:CL", "3.5::Window");
                //Naturally propogate to inventory handles unless suspended manually
                InputPoller.AddBinding(new ActionBind("Deselect",
                    DeselectDrag, ActionBind.Exclusion.None), "MAT::Container:DS", "3.5::Window");
                InputPoller.AddBinding(new ActionBind("Select",
                    Select, ActionBind.Exclusion.None), "MAT::Container:SL", "3.5::Window");
                InputPoller.AddBinding(new ActionBind("SelectPartial", SelectPartial),  "MAT::Container:SELP", "3.5::Window");
                InputPoller.AddBinding(new ActionBind("SelectAll", SelectAll), "MAT::Container:SELA", "3.0::AllWindow");
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
            if (!CPUMapManager.TryGetExistingMapMeta(GCoord, out ContainerInventory cont))
                return;
            if (cont.inv.capacity == SlotCount) return;
            cont.inv.ResizeInventory(SlotCount, Item => InventoryController.DropItem(Item, GCoord));
        }

        private void DeactivateWindow(float _ = 0) {
            //IMPORTANT: The order in which these functions are called is VERY important
            PanelNavbarManager.Remove(name);
            PanelNavbarManager.Deactivate();
            if (OpenedInventory != null) {
                InputPoller.AddKeyBindChange(() => {
                    InputPoller.RemoveBinding("MAT::Container:SELA", "3.0::AllWindow");
                    InputPoller.RemoveContextFence("MAT::Container", "3.5::Window");
                    OpenedInventory = null;
                });
            }
            InventoryController.Deactivate();
        }


        private bool GetMouseSelected(out IInventory inv, out int index) {
            inv = OpenedInventory.inv;
            return OpenedInventory.inv.Display.GetMouseSelected(out index);
        }
        private void Select(float _ = 0) {
            if (!InventoryController.Select(GetMouseSelected)) return;
            InputPoller.SuspendKeybindPropogation("Select", ActionBind.Exclusion.ExcludeLayer);
        }

        private void DeselectDrag(float _ = 0) {
            if (!InventoryController.DeselectDrag(GetMouseSelected)) return;
            InputPoller.SuspendKeybindPropogation("Deselect", ActionBind.Exclusion.ExcludeLayer);
        }
        private void SelectPartial(float _ = 0) {
            if (!InventoryController.SelectPartial(GetMouseSelected)) return;
            InputPoller.SuspendKeybindPropogation("SelectPartial", ActionBind.Exclusion.ExcludeLayer);
        }
        private void SelectAll(float _ = 0) => InventoryController.SelectAll(OpenedInventory.inv);

        private void DropInventoryContent(int3 GCoord) {
            if (!CPUMapManager.TryGetExistingMapMeta(GCoord, out ContainerInventory cont))
                return;
            foreach (IItem item in cont.inv.Info)
                InventoryController.DropItem(item, GCoord);
            CPUMapManager.SetExistingMapMeta<object>(GCoord, null);
        }
        public class ContainerInventory : PanelNavbarManager.INavPanel {
            public InventoryController.Inventory inv;
            public int3 position;
            [JsonIgnore]
            private ContainerMaterial settings;
            public ContainerInventory() {
                //Do nothing: this is for newtonsoft deserializer
            }
            internal ContainerInventory(int3 GCoord, int SlotCount) {
                inv = new InventoryController.Inventory(SlotCount);
                position = GCoord;
            }

            //Ideally we should provide a callback in OnAddElement and OnRemoveElement
            //But it is difficult reapply and applying the hooks whenever the material is closed
            internal void InitializeDisplay(ContainerMaterial settings) {
                inv.InitializeDisplay(GameUIManager.UIHandle);
                this.settings = settings;
            }

            public Sprite GetNavIcon() => Config.CURRENT.Generation.Textures.Retrieve(
                settings.Names.value[settings.DisplayIcon]).self;
            public GameObject GetDispContent() => inv.Display.root;
            public void Release() => inv.ReleaseDisplay();
            public void Activate() => inv.Display.root.SetActive(true);
            public void Deactivate() => inv.Display.root.SetActive(false);
        }
    }
}