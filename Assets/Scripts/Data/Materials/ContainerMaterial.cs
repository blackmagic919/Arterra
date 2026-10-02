using Arterra.Core.Network;
using Arterra.Core.Events;
using System;
using Arterra.Data.Entity.Behavior;
using Arterra.Core.Storage;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Data.Item;
using Arterra.Configuration;
using Arterra.GamePlay.Interaction;
using Arterra.Editor;
using Arterra.GamePlay.UI;
using Arterra.GamePlay;

namespace Arterra.Data.Material
{
    /// <summary> A container material which can hold items in its meta-data
    /// and is accessible to users who click on the item. </summary>
    [CreateAssetMenu(menuName = "Generation/MaterialData/Behaviors/ContainerMat")]
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
        /// <summary>The event that is triggered when this chest is opened.</summary>
        public Core.Events.GameEvent OpenEvent = Core.Events.GameEvent.Action_OpenChest;

        /// <summary> Even though it does nothing, it needs to fufill the contract so
        /// that it can be used in the same way as other materials. </summary>
        /// <param name="GCoord">The coordinate in grid space of a map entry that is this material</param>
        /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
        public override void RandomMaterialUpdate(int3 GCoord, ref Unity.Mathematics.Random prng) {
            if (!VerifyStructurePlaceement(GCoord))
                DropInventoryContent(GCoord);
        }

        public override void Preset(MaterialData materialData) {
            materialData.Register(typeof(PlaceableStructureMat), this);
        }

        /// <summary>Returns a container inventory created using the meta constructor.</summary>
        /// <param name="GCoord">The coordinate in grid space of the material</param>
        /// <param name="constructor">The constructor used to populate the inventory</param>
        /// <returns>The container instance</returns>
        public override object ConstructMetaData(int3 GCoord, MaterialData.MetaConstructor constructor) {
            return new ContainerInventory(constructor, GCoord, MaxSlotCount);
        }

        public override bool OnRemoving(int3 GCoord, Entity.Entity caller) {
            if (!NetworkManager.IsActingServer || caller == null || !caller.Is(out PlayerInventoriesBehavior player)) return false;
            MapData info = CPUMapManager.SampleMap(GCoord);
            if (info.IsNull || !info.IsSolid) return false;
            if (!VerifyStructurePlaceement(GCoord)) { DropInventoryContent(GCoord); return false; }
            if (!CPUMapManager.GetOrCreateMapMeta(GCoord, () => new ContainerInventory(GCoord, MaxSlotCount), out var inventory)) return false;
            player.OpenMaterial(inventory);
            return true;
        }

        /// <summary> See <see cref="MaterialData.OnRemoved"/> for more information. </summary>
        /// <param name="amount">The map data indicating the amount of material removed
        /// and the state it was removed as</param>
        /// <param name="GCoord">The location of the map information being</param>
        /// <returns>The item to give.</returns>
        public override IItem OnRemoved(int3 GCoord, in MapData amount) {
            MapData info = CPUMapManager.SampleMap(GCoord);
            if (info.IsNull || amount.IsNull) return null;
            if (!info.IsSolid) return null;

            int SolidDensity = info.SolidDensity - amount.SolidDensity;
            if (!VerifyStructurePlaceement(GCoord) || SolidDensity < CPUMapManager.IsoValue)
                DropInventoryContent(GCoord);

            return null;
        }

        public override void OnPlaced(int3 GCoord, in MapData amount) {
            MapData info = CPUMapManager.SampleMap(GCoord);
            if (info.IsNull || amount.IsNull) return;
            int SolidDensity = info.SolidDensity + amount.SolidDensity;
            if (SolidDensity < CPUMapManager.IsoValue) return;

            float progress = (SolidDensity - CPUMapManager.IsoValue) /
                             (255.0f - CPUMapManager.IsoValue);
            int SlotCount = Mathf.FloorToInt(MaxSlotCount * progress) + 1;
            if (!CPUMapManager.TryGetExistingMapMeta(GCoord, out ContainerInventory cont))
                return;
            if (cont.inv.capacity == SlotCount) return;
            cont.inv.ResizeInventory(SlotCount, Item => PlayerInventoriesBehavior.SpawnDrop(Item, GCoord));
            CPUMapManager.MarkMetadataDirty(GCoord);
            cont.PublishAll();
        }

        private void DropInventoryContent(int3 GCoord) {
            if (!CPUMapManager.TryGetExistingMapMeta(GCoord, out ContainerInventory cont))
                return;
            foreach (IItem item in cont.inv.Info)
                PlayerInventoriesBehavior.SpawnDrop(item, GCoord);
            CPUMapManager.SetExistingMapMeta<object>(GCoord, null);
        }
        public class ContainerInventory : MaterialInventory {
            public Inventory inv;
            [JsonIgnore] private ContainerMaterial settings;

            [JsonIgnore] protected override Inventory[] Inventories => new[] { inv };
            protected override void InitializeSimulation() {
                Authoring.Is(out PlaceableStructureMat material);
                settings = (ContainerMaterial)material;
            }

            [JsonConstructor]
            public ContainerInventory() {/*Do nothing: this is for newtonsoft deserializer*/}
            internal ContainerInventory(int3 GCoord, int SlotCount) : base(GCoord) {
                inv = new Inventory(SlotCount);
            }

            internal ContainerInventory(MaterialData.MetaConstructor constructor, int3 GCoord, int SlotCount) : base(GCoord)  {
                inv = new Inventory(constructor, GCoord, SlotCount);
            }

            protected override void CreateDisplay() {
                inv.InitializeDisplay(GameUIManager.UIHandle);

            }

            protected override void ReleaseDisplay() => inv.ReleaseDisplay();
            protected override GameEvent OpenEvent => settings.OpenEvent;
            public override Sprite GetNavIcon() => Config.CURRENT.Generation.Textures.Retrieve(
                settings.Names.value[settings.DisplayIcon]).self;
            public override GameObject GetDispContent() => inv.Display?.root;

        }
    }
}