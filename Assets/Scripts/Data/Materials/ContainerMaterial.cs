using System;
using System.Collections;
using System.Collections.Generic;
using MapStorage;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using WorldConfig.Gameplay;

namespace WorldConfig.Generation.Material
{
    /// <summary> A container material which can hold items in its meta-data
    /// and is accessible to users who click on the item. </summary>
    [CreateAssetMenu(menuName = "Generation/MaterialData/ContainerMat")]
    public class ContainerMaterial : MaterialData {
        /// <summary> The index within the <see cref="MaterialData.Names"> name registry </see> of the name within the external registry, 
        /// <see cref="Config.GenerationSettings.Items"/>, of the item to be given when the material is picked up when it is solid. 
        /// If the index does not point to a valid name (e.g. -1), no item will be picked up when the material is removed. </summary>
        public int SolidItem;
        /// <summary> The index within the <see cref="MaterialData.Names"> name registry </see> of the name within the external registry, 
        /// <see cref="Config.GenerationSettings.Items"/>, of the item to be given when the material is picked up when it is liquid. 
        /// If the index does not point to a valid name (e.g. -1), no item will be picked up when the material is removed. </summary>
        public int LiquidItem;
        /// <summary> The index within the <see cref="MaterialData.Names"> name registry </see> of the 
        /// texture within the texture registry of the icon displayed on the <see cref="PanelNavbarManager">Navbar</see>
        /// referring to the Container.  </summary>
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

        }

        /// <summary> See <see cref="MaterialData.AcquireItem"/> for more information. </summary>
        /// <param name="mapData">The map data indicating the amount of material removed
        /// and the state it was removed as</param>
        /// <returns>The item to give.</returns>
        public override Item.IItem AcquireItem(in MapData mapData) {
            return GenericMaterial.GenericItemFromMap(mapData, RetrieveKey(SolidItem), RetrieveKey(LiquidItem));
        }

        private ContainerInventory OpenedInventory = null;
        public override bool OnRemoving(int3 GCoord, Entity.Entity caller) {
            if (caller == null || caller.info.entityType != Config.CURRENT.Generation.Entities.RetrieveIndex("Player"))
                return false;
            if (PlayerMovement.IsSprinting) return false;
            MapData info = CPUMapManager.SampleMap(GCoord);
            if (info.IsNull || !info.IsSolid) return false;

            float progress = (info.SolidDensity - CPUMapManager.IsoValue) /
                             (255.0f - CPUMapManager.IsoValue);
            int SlotCount = Mathf.FloorToInt(MaxSlotCount * progress) + 1;
            if (!CPUMapManager.GetOrCreateMapMeta(GCoord, () => new ContainerInventory
                (GCoord, SlotCount), out OpenedInventory))
                return false;
            
            OpenedInventory.InitializeDisplay(this);
            InventoryController.Activate();
            InputPoller.AddKeyBindChange(() => {
                //Overrides the default inventory close window
                OpenedInventory.Fence = InputPoller.AddContextFence("3.0::Window", InputPoller.ActionBind.Exclusion.None);
                InputPoller.AddBinding(new InputPoller.ActionBind("Open Inventory",
                _ => DeactivateWindow(), InputPoller.ActionBind.Exclusion.ExcludeLayer), "3.0::Window");
                //Naturally propogate to inventory handles unless suspended manually
                InputPoller.AddBinding(new InputPoller.ActionBind("Select",
                _ => SelectDrag(OpenedInventory), InputPoller.ActionBind.Exclusion.None), "3.0::Window");
                InputPoller.AddBinding(new InputPoller.ActionBind("Deselect",
                _ => DeselectDrag(OpenedInventory), InputPoller.ActionBind.Exclusion.None), "3.0::Window");
            });
            PanelNavbarManager.Add(OpenedInventory, name);
            PanelNavbarManager.Activate(name);
            return true;
        }

        public override void OnRemoved(int3 GCoord, in MapData amount) {
            MapData info = CPUMapManager.SampleMap(GCoord);
            if (info.IsNull || amount.IsNull) return;
            if (!info.IsSolid) return;
            if (OpenedInventory != null && math.all(OpenedInventory.position == GCoord))
                DeactivateWindow();
            if (!CPUMapManager.TryGetExistingMapMeta(GCoord, out ContainerInventory cont))
                return;
            int SolidDensity = info.SolidDensity - amount.SolidDensity;
            if (SolidDensity < CPUMapManager.IsoValue) {
                foreach (Item.IItem item in cont.inv.Info)
                    InventoryController.DropItem(item, GCoord);
                CPUMapManager.SetExistingMapMeta<object>(GCoord, null);
                return;
            }

            float progress = (SolidDensity - CPUMapManager.IsoValue) /
                             (255.0f - CPUMapManager.IsoValue);
            int SlotCount = Mathf.FloorToInt(MaxSlotCount * progress) + 1;
            cont.inv.ResizeInventory(SlotCount, Item => InventoryController.DropItem(Item, GCoord));
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

        private void DeactivateWindow() { 
            //IMPORTANT: The order in which these functions are called is VERY important
            PanelNavbarManager.Remove(name);
            PanelNavbarManager.Deactivate();
            if (OpenedInventory != null) {
                InputPoller.AddKeyBindChange(() => {
                    InputPoller.RemoveContextFence(OpenedInventory.Fence, "3.0::Window");
                    OpenedInventory = null;
                });
            }
            InventoryController.Deactivate();
        }

        private static void SelectDrag(ContainerInventory cont) {
            if (!cont.inv.Display.GetMouseSelected(out int index))
                return;

            //Swap the cursor with the selected slot
            InventoryController.Cursor = cont.inv.Info[index];
            if (InventoryController.Cursor == null) return;
            cont.inv.RemoveEntry(index);

            InventoryController.Cursor.AttachDisplay(InventoryController.CursorDisplay.Object.transform);
            InventoryController.CursorDisplay.Object.SetActive(true);
            InputPoller.SuspendKeybindPropogation("Select", InputPoller.ActionBind.Exclusion.ExcludeLayer);
        }

        private static void DeselectDrag(ContainerInventory cont) {
            if (InventoryController.Cursor == null) return;
            if (!cont.inv.Display.GetMouseSelected(out int index)) return;

            InventoryController.CursorDisplay.Object.SetActive(false);
            Item.IItem cursor = InventoryController.Cursor;
            cursor.ClearDisplay(InventoryController.CursorDisplay.Object.transform);
            InventoryController.Cursor = null;

            if (cont.inv.Info[index] == null)
                cont.inv.AddEntry(cursor, index);
            else if (cont.inv.Info[index].IsStackable && cursor.Index == cont.inv.Info[index].Index) {
                cont.inv.AddStackable(cursor, index);
            } else if (cont.inv.EntryDict.ContainsKey(cursor.Index) && cont.inv.Info[index].IsStackable) {
                cont.inv.AddStackable(cursor);
            } else if (!cont.inv.AddEntry(cursor, out int _)) InventoryController.DropItem(cursor);
            InputPoller.SuspendKeybindPropogation("Deselect", InputPoller.ActionBind.Exclusion.ExcludeLayer);
        }


        public class ContainerInventory : PanelNavbarManager.INavPanel {
            public InventoryController.Inventory inv;
            public int3 position;
            [JsonIgnore]
            private ContainerMaterial settings;
            [JsonIgnore]
            internal uint Fence;
            public ContainerInventory() {
                //Do nothing: this is for newtonsoft deserializer
            }
            internal ContainerInventory(int3 GCoord, int SlotCount) {
                inv = new InventoryController.Inventory(SlotCount);
                position = GCoord;
            }

            internal void InitializeDisplay(ContainerMaterial settings) {
                inv.InitializeDisplay(GameUIManager.UIHandle);
                this.settings = settings;
            }

            public Sprite GetNavIcon() => Config.CURRENT.Generation.Textures.Retrieve(
                settings.Names[settings.DisplayIcon]).self;
            public GameObject GetDispContent() => inv.Display.root;
            public void Release() => inv.ReleaseDisplay();
            public void Activate() => inv.Display.root.SetActive(true);
            public void Deactivate() => inv.Display.root.SetActive(false);
        }
    }
}