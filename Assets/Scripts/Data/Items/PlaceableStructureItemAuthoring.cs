using Arterra.Data.Item;
using Arterra.Core.Events;
using System;
using UnityEngine;
using Newtonsoft.Json;
using Unity.Mathematics;
using System.Collections.Generic;
using Arterra.Utils;
using System.Linq;
using Arterra.Core.Storage;
using Arterra.Data.Material;
using Arterra.GamePlay.Interaction;
using Arterra.Configuration;
using Arterra.GamePlay.UI;
using Arterra.Data.Entity.Behavior;
using Arterra.Core;

namespace Arterra.Data.Item {
    [CreateAssetMenu(menuName = "Generation/Items/StructureItem")]
    public class PlaceableStructureItemAuthoring : PlaceableTemplate<PlaceableStructureItem> {}
    public class PlaceableStructureItem : IItem {
        public uint data;
        private static Catalogue<Authoring> ItemInfo => Config.CURRENT.Generation.Items;
        private static Catalogue<Material.MaterialData> MatInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        private static Catalogue<TextureContainer> TextureAtlas => Config.CURRENT.Generation.Textures;
        private PlaceableStructureItemAuthoring settings => ItemInfo.Retrieve(Index) as PlaceableStructureItemAuthoring;
        private InteractionHandler handler;


        [JsonIgnore]
        public int StackLimit => 0xFFFF;
        [JsonIgnore]
        public int UnitSize => 0xFF;
        [JsonIgnore]
        public int TexIndex => TextureAtlas.RetrieveIndex(ItemInfo.Retrieve(Index).TextureName);

        [JsonIgnore]
        public int Index {
            get => (int)(data >> 16) & 0x7FFF;
            set => data = (data & 0x0000FFFF) | (((uint)value & 0x7FFF) << 16);
        }
        [JsonIgnore]
        public int AmountRaw {
            get => (int)(data & 0xFFFF);
            set {
                data = (data & 0x7FFF0000) | ((uint)value & 0xFFFF);
                UpdateDisplay();
            }
        }
        public IRegister GetRegistry() => Config.CURRENT.Generation.Items;
        public object Clone() => new PlaceableStructureItem { data = data };
        public void Create(int Index, int AmountRaw) {
            this.Index = Index;
            this.AmountRaw = AmountRaw;
        }

        public void OnEnter(ItemContext cxt) {
            cxt.RegisterEvent(this, GameEvent.Apply_PlaceStructure,
                (context, argument) => InteractionHandler.Apply(this, context, argument));
            if (!cxt.IsOwner || cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
            handler?.Release();
            handler = InteractionHandler.Create(this, cxt);
        }
        public void OnLeave(ItemContext cxt) {
            cxt.UnregisterEvent(GameEvent.Apply_PlaceStructure);
            if (!cxt.IsOwner || cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
            handler?.Release();
            handler = null;
        }
        public void UpdateEItem() { }

        private GameObject display;
        public void AttachDisplay(Transform parent, int itemInd) {
            if (display != null) {
                display.transform.SetParent(parent, false);
                return;
            }

            display = Indicators.StackableItems.Get();
            display.transform.SetParent(parent, false);
            display.transform.GetComponent<UnityEngine.UI.Image>().sprite = TextureAtlas.Retrieve(ItemInfo.Retrieve(Index).TextureName).self;
            TMPro.TextMeshProUGUI amount = display.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            amount.text = ((data & 0xFFFF) / (float)UnitSize).ToString();
        }

        public void ClearDisplay(Transform parent) {
            if (display == null) return;
            Indicators.StackableItems.Release(display);
            display = null;
        }

        private void UpdateDisplay() {
            if (display == null) return;
            TMPro.TextMeshProUGUI amount = display.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            amount.text = ((data & 0xFFFF) / (float)UnitSize).ToString();
        }

        private class InteractionHandler : ArterraRuntime.IUpdateSubscriber{
            private bool active;
            public bool Active {
                get => active;
                set => active = value;
            }
            private ItemContext cxt;
            private PlaceableStructureItem item;
            private RegionReconstructor display;
            private int3 Location;
            private int3 Rotation;
            private int LayoutIndex;
            private bool IsLocked;

            public static InteractionHandler Create(PlaceableStructureItem item, ItemContext cxt) {
                InteractionHandler h = new InteractionHandler();
                h.display = new RegionReconstructor();
                h.IsLocked = false;
                h.Location = 0;
                h.item = item;
                h.cxt = cxt;
                h.active = true;
                ArterraRuntime.MainLoopUpdateTasks.Enqueue(h);
                
                InputPoller.AddBinding(new ActionBind("Place", h.PlaceStructure, ActionBind.Exclusion.ExcludeLayer),
                "ITEM::PlaceableStct:PL", "5.0::GamePlay");
                InputPoller.AddBinding(new ActionBind("Place", (_) => h.IsLocked = false,
                    ActionBind.Exclusion.ExcludeLayer, ActionBind.Policy.Up),
                "ITEM::PlaceableStct:FP", "5.0::GamePlay");            
                return h;
            }
            public void Release() {
                active = false;
                item = null;
                display.Release();
                InputPoller.RemoveBinding("ITEM::PlaceableStct:PL", "5.0::GamePlay");
                InputPoller.RemoveBinding("ITEM::PlaceableStct:FP", "5.0::GamePlay");
                
            }

            public void Update(MonoBehaviour _) {
                if (!cxt.TryGetHolder(out BehaviorEntity.Animal player)) return;
                if (!player.Is(out PlayerInteractionBehavior interact)) return;
                if (IsLocked) {
                    display.ReflectMesh(DeserializeStruct(), Location, Rotation);
                    return;
                }

                if (!FindTargetPoint(player, interact, out float3 hitPt, out int3 rotation, out int layout)) {
                    display.ClearMesh();
                    return;
                }

                SetTarget(hitPt, rotation, layout);
                display.ReflectMesh(DeserializeStruct(), Location, Rotation);
            }

            private void PlaceStructure(float _) {
                if (!cxt.TryGetHolder(out BehaviorEntity.Animal player)) return;
                if (!player.Is(out PlayerInteractionBehavior interact)) return;
                if (!IsLocked) {
                    if (!FindTargetPoint(player, interact, out float3 hitPt, out int3 rotation, out int layout)) return;
                    SetTarget(hitPt, rotation, layout);
                    IsLocked = true;
                }

                cxt.RaiseEvent(GameEvent.Apply_PlaceStructure, (Location, Rotation, LayoutIndex));
            }

            private void SetTarget(float3 hitPt, int3 rotation, int layout) {
                MatInfo.Retrieve(item.settings.MaterialName).Is(out PlaceableStructureMat structure);
                var placement = structure.GetLayout(layout);
                LayoutIndex = layout;
                Rotation = rotation;
                Location = placement.GetOrigin((int3)math.round(hitPt), rotation);
            }

            private bool FindTargetPoint(BehaviorEntity.Animal player, PlayerInteractionBehavior interact,
                out float3 hitPt, out int3 rotation, out int layout) {
                layout = 0;
                rotation = int3.zero;
                if (!interact.RayTestSolid(out hitPt)) return false;
                int material = MatInfo.RetrieveIndex(item.settings.MaterialName);
                if (!MatInfo.Retrieve(material).Is(out PlaceableStructureMat structure)) return false;

                int3 target = (int3)math.round(hitPt);
                int3 targetRotation = structure.GetLayout(0).GetRotation(player.head - target);
                int targetLayout = 0;
                bool snapped = Terraforming.FindTerraformable(hitPt, PlayerInteractionSettings.GetSingleton().TerraformRadius, coord => {
                    MapData sample = CPUMapManager.SampleMap(coord);
                    if (sample.IsNull || sample.SolidDensity == 0) return false;
                    if (sample.material != material) {
                        if (!CPUMapManager.TryGetExistingMapMeta(coord, out StructureLayoutInfo _)) return false;
                        if (!MatInfo.Retrieve(sample.material).Is(out PlaceableStructureMat member) || member != structure) return false;
                    }

                    int3 keystone = coord;
                    int3 candidateRotation = targetRotation;
                    if (CPUMapManager.TryGetExistingMapMeta(coord, out StructurePartInfo part)) {
                        keystone += part.Delta;
                        candidateRotation = part.Rotation;
                    }

                    PlaceableStructureMat owner = structure;
                    MapData keystoneSample = CPUMapManager.SampleMap(keystone);
                    if (!keystoneSample.IsNull && keystoneSample.SolidDensity > 0
                        && MatInfo.Retrieve(keystoneSample.material).Is(out PlaceableStructureMat keystoneStructure))
                        owner = keystoneStructure;
                    if (owner.VerifyStructurePlacement(coord)) return false;

                    target = keystone;
                    targetRotation = candidateRotation;
                    targetLayout = structure.GetPlacedLayout(coord);
                    return true;
                }, out _);

                if (!snapped) {
                    if (!Terraforming.FindTerraformable(hitPt, PlayerInteractionSettings.GetSingleton().TerraformRadius, coord => {
                        if (!structure.TryResolveLayout(coord, player.head - coord,
                            out int3 candidateRotation, out int candidateLayout)) return false;
                        target = coord;
                        targetRotation = candidateRotation;
                        targetLayout = candidateLayout;
                        return true;
                    }, out _)) return false;
                }

                if (targetLayout < 0 || targetLayout >= structure.LayoutCount) return false;
                var placement = structure.GetLayout(targetLayout);
                int3 origin = placement.GetOrigin(target, targetRotation);
                if (!structure.CanPlaceLayout(origin, targetRotation, targetLayout)) return false;

                hitPt = target;
                rotation = targetRotation;
                layout = targetLayout;
                return true;
            }

            public static void Apply(PlaceableStructureItem item, ItemContext context, object argument) {
                if (argument is not ValueTuple<int3, int3, int> placement) return;
                if (!context.TryGetHolder(out BehaviorEntity.Animal player)) return;
                int3 rotation = placement.Item2;
                if (math.any(rotation < 0) || math.any(rotation > 3)) return;
                if (!MatInfo.Retrieve(item.settings.MaterialName).Is(out PlaceableStructureMat structure)) return;
                if (placement.Item3 < 0 || placement.Item3 >= structure.LayoutCount) return;
                if (!structure.CanPlaceLayout(placement.Item1, rotation, placement.Item3)) return;
                var operation = new InteractionHandler { item = item, cxt = context, LayoutIndex = placement.Item3 };
                List<MapPlacement> edit = operation.DeserializeStruct();
                if (edit == null) return;
                if (operation.RemoveConflictMats(player, edit, placement.Item1, rotation)) return;
                operation.PlaceNewMats(player, edit, placement.Item1, rotation);
            }

            private List<MapPlacement> DeserializeStruct() {
                MaterialData placeMat = MatInfo.Retrieve(item.settings.MaterialName);
                if (!placeMat.Is(out PlaceableStructureMat key)) return null;
                return key.GetLayout(LayoutIndex).Structure.Select(s => {
                    if (s.map.HasMaterialCheck) s.map.material = (uint)MatInfo.RetrieveIndex(
                        key.RetrieveKey((int)s.map.material));
                    return s;
                }).ToList();
            }

            private delegate bool CheckIterate(MapPlacement check, MapData sample, MapData delta, int3 GCoord);
            private bool RemoveConflictMats(Entity.Entity holder, List<MapPlacement> edit, int3 GCoord, int3 rot) {
                if (!holder.Is(out PlayerInteractionBehavior interact)) interact = null;
                int totalRemAmount = 0; int totalSolidAmt = 0;
                if (!IterateStructRemove(edit, GCoord, rot, (c, s, d, gc) => {
                    totalSolidAmt += d.SolidDensity;
                    totalRemAmount += d.density;
                    //We have to fulfill this contract as we will be removing it
                    return MatInfo.Retrieve(s.material).OnRemoving(gc, holder);
                })) return true;

                if (totalRemAmount <= 0) return false;
                IterateStructRemove(edit, GCoord, rot, (c, s, d, gc) => {
                    if (d.density <= 0) return false;
                    ToolTag tag = PlayerInteractionSettings.GetSingleton().DefaultTerraform;
                    if (MatInfo.GetMostSpecificTag(TagRegistry.Tags.BareHand, d.material, out object prop))
                        tag = prop as ToolTag;
                    if (d.viscosity > 0) {
                        float strength = math.pow((float)d.viscosity / totalRemAmount, 0.5f);
                        interact?.HandleRemoveSolid(ref s, gc, strength * tag.TerraformSpeed, tag.GivesItem);
                        d.density -= d.viscosity; d.viscosity = 0;
                    }
                    if (d.density > 0) {
                        float strength = math.pow((float)d.density / totalRemAmount, 0.5f);
                        interact?.HandleRemoveLiquid(ref s, gc, strength * tag.TerraformSpeed, tag.GivesItem);
                    }
                    return false;
                }); 
                //Don't stop liquid conflicts, because liquid will keep flowing in
                //If we don't start placing other liquids
                return totalSolidAmt > 0;
            }

            private bool PlaceNewMats(Entity.Entity holder, List<MapPlacement> edit, int3 GCoord, int3 rot) {
                if (!holder.Is(out PlayerInteractionBehavior interact)) return false;
                int totPlaceAmt = 0; int selfPlaceAmt = 0; bool PlaceSelf = true;
                if (!MatInfo.Retrieve(item.settings.MaterialName).Is(out PlaceableStructureMat structure)) return false;
                MapSamplePoint keystone = edit[structure.GetLayout(LayoutIndex).SelfIndex].map;
                int self = (int)keystone.material;
                int3 keystonePosition = GCoord + math.mul(CustomUtility.RotationLookupTable[rot.y, rot.x, rot.z], keystone.Offset);
                if (!IterateStructPlace(edit, GCoord, rot, (c, s, d, gc) => {
                    int placeMaterial = -1;
                    if (!s.IsGaseous) placeMaterial = s.material;
                    if (c.map.HasMaterialCheck) placeMaterial = (int)c.map.material;

                    if (structure.GetPlacementItemMaterial(placeMaterial, LayoutIndex) == self) {
                        selfPlaceAmt += d.density;
                    } else {
                        totPlaceAmt += d.density;
                        if (s.LiquidDensity < c.map.check.bounds.MinLiquid) PlaceSelf = false;
                        if (s.SolidDensity < c.map.check.bounds.MinSolid) PlaceSelf = false;
                    }
                    //We have to fulfill this contract as we will be placing it
                    return MatInfo.Retrieve(s.material).OnPlacing(gc, holder);
                })) return true;

                if (PlaceSelf) totPlaceAmt += selfPlaceAmt;
                if (totPlaceAmt <= 0) return false;

                float placeSpeed = PlayerInteractionSettings.GetSingleton().DefaultTerraform.value.TerraformSpeed;
                IterateStructPlace(edit, GCoord, rot, (c, s, d, gc) => {
                    if (d.density <= 0) return false;

                    int placeMaterial = -1; //Allow any material of same state to be chosen
                    if (!s.IsGaseous) placeMaterial = s.material; //Must be same as existing material
                    if (c.map.HasMaterialCheck) placeMaterial = (int)c.map.material;
                    if (placeMaterial == self && !PlaceSelf) return false;

                    if (d.viscosity > 0 && FindNextMatIndex(out IItem nextSlot, structure.GetPlacementItemMaterial(placeMaterial, LayoutIndex), PlaceableItem.State.Solid)) {
                        float strength = math.pow((float)d.viscosity / totPlaceAmt, 0.5f);
                        if (interact.HandleAddSolid(nextSlot, gc, strength * placeSpeed, out MapData placed, placeMaterial)
                            && placed.SolidDensity > s.SolidDensity) {
                            if (placed.material == self || structure.LinksAllMembers)
                                structure.InstallPlacementMetadata(gc, keystonePosition, rot, LayoutIndex);
                            if (c.modelInfo.Enabled)
                                CPUMapManager.SetExistingMapMeta(gc, c.modelInfo.Value.Rotate(rot));
                        }
                        d.density -= d.viscosity; d.viscosity = 0;
                    }
                    if (d.density > 0 && FindNextMatIndex(out nextSlot, placeMaterial, PlaceableItem.State.Liquid)) {
                        float strength = math.pow((float)d.density / totPlaceAmt, 0.5f);
                        interact.HandleAddLiquid(nextSlot, gc, strength * placeSpeed, out _);
                    }
                    return false;
                }); return true;

            }

            private bool IterateStructRemove(List<MapPlacement> edit, int3 GCoord, int3 rot, CheckIterate OnRemove) {
                bool any0 = false;
                for (int i = 0; i < edit.Count; i++) {
                    MapSamplePoint pt = edit[i].map;
                    if (pt.check.OrFlag) { //Only fill first or flag
                        if (any0) continue;
                        else any0 = true;
                    }

                    int3 sCoord = GCoord + math.mul(CustomUtility.RotationLookupTable[rot.y, rot.x, rot.z], pt.Offset);
                    MapData sample = CPUMapManager.SampleMap(sCoord);
                    MapData remAmount = new MapData { data = 0, material = sample.material };

                    if (pt.HasMaterialCheck && !sample.IsGaseous && pt.material != sample.material) {
                        if (sample.LiquidDensity >= CPUMapManager.IsoValue)
                            remAmount.density = math.max(1, sample.LiquidDensity - (int)CPUMapManager.IsoValue);
                        if (sample.SolidDensity >= CPUMapManager.IsoValue)
                            remAmount.viscosity = math.max(1, sample.SolidDensity - (int)CPUMapManager.IsoValue);
                    }
                    if (pt.check.bounds.MaxSolid < sample.SolidDensity)
                        remAmount.viscosity = math.max(remAmount.viscosity, sample.SolidDensity - (int)pt.check.bounds.MaxSolid);
                    if (pt.check.bounds.MaxLiquid < sample.LiquidDensity)
                        remAmount.density = math.max(remAmount.density, sample.LiquidDensity - (int)pt.check.bounds.MaxLiquid);
                    if (remAmount.density <= 0) continue;
                    if (OnRemove.Invoke(edit[i], sample, remAmount, sCoord)) return false;
                }
                return true;
            }


            private bool IterateStructPlace(List<MapPlacement> edit, int3 GCoord, int3 rot, CheckIterate OnAdd) {
                bool any0 = false;
                for (int i = 0; i < edit.Count; i++) {
                    MapSamplePoint pt = edit[i].map;
                    if (pt.check.OrFlag) { //Only fill first or flag
                        if (any0) continue;
                        else any0 = true;
                    }

                    int3 sCoord = GCoord + math.mul(CustomUtility.RotationLookupTable[rot.y, rot.x, rot.z], pt.Offset);
                    MapData sample = CPUMapManager.SampleMap(sCoord);
                    MapData addAmt = new MapData { data = 0, material = sample.material };
                    if (pt.HasMaterialCheck && !sample.IsGaseous && pt.material != sample.material)
                        continue;

                    uint targetLiquid = pt.check.bounds.MinLiquid;
                    uint targetSolid = pt.check.bounds.MinSolid + (pt.check.bounds.MaxSolid - pt.check.bounds.MinSolid) / 2;
                    if (targetLiquid > sample.LiquidDensity)
                        addAmt.density = (int)targetLiquid - sample.LiquidDensity;
                    if (targetSolid > sample.SolidDensity)
                        addAmt.viscosity = (int)targetSolid - sample.SolidDensity;
                    if (addAmt.density == 0) continue;
                    if (OnAdd.Invoke(edit[i], sample, addAmt, sCoord)) return false;
                }
                return true;
            }

            private bool FindNextMatIndex(out IItem slotItem, int matIndex, PlaceableItem.State state) {
                int start = cxt.InvId;
                slotItem = null;

                if (!cxt.TryGetInventory(out Inventory inv))
                    return false;

                int capacity = (int)inv.capacity;
                int slot = start % capacity;

                do {
                    slotItem = inv.Info[slot];
                    if (slotItem != null) {
                        Authoring authoring = ItemInfo.Retrieve(slotItem.Index);
                        if (authoring is PlaceableItem mSettings) {
                            if ((matIndex == -1 || MatInfo.RetrieveIndex(mSettings.MaterialName) == matIndex) &&
                                mSettings.MaterialState == state) {
                                return true;
                            }
                        }
                    }
                    slot = (slot + 1) % capacity;
                } while (slot != start);

                return false;
            }
        }
    }
}
