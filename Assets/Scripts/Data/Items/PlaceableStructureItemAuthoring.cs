using UnityEngine;
using Newtonsoft.Json;
using Unity.Mathematics;
using System.Collections.Generic;
using Utils;
using System.Linq;
using Arterra.Core.Storage;
using Arterra.Configuration.Generation.Material;
using Arterra.Core.Player;
using static Arterra.Core.Player.PlayerInteraction;

namespace Arterra.Configuration.Generation.Item {
    [CreateAssetMenu(menuName = "Generation/Items/StructureItem")]
    public class PlaceableStructureItemAuthoring : PlaceableTemplate<PlaceableStructureItem> {}
    public class PlaceableStructureItem : IItem {
        public uint data;
        private static Catalogue<Authoring> ItemInfo => Config.CURRENT.Generation.Items;
        private static Catalogue<Material.MaterialData> MatInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        private static Catalogue<TextureContainer> TextureAtlas => Config.CURRENT.Generation.Textures;
        private PlaceableStructureItemAuthoring settings => ItemInfo.Retrieve(Index) as PlaceableStructureItemAuthoring;
        private InteractionHandler handler;

        private static Gameplay.Player.Interaction interaction => Config.CURRENT.GamePlay.Player.value.Interaction;

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
            if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
            handler?.Release();
            handler = InteractionHandler.Create(this, cxt);
        }
        public void OnLeave(ItemContext cxt) {
            if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
            handler.Release();
            handler = null;
        }
        public void UpdateEItem() { }

        private GameObject display;
        public void AttachDisplay(Transform parent) {
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

        private class InteractionHandler : IUpdateSubscriber{
            private bool active;
            public bool Active {
                get => active;
                set => active = value;
            }
            private ItemContext cxt;
            private PlaceableStructureItem item;
            private RegionReconstructor display;
            private int3 Location;
            private bool IsLocked;

            public static InteractionHandler Create(PlaceableStructureItem item, ItemContext cxt) {
                InteractionHandler h = new InteractionHandler();
                h.display = new RegionReconstructor();
                h.IsLocked = false;
                h.Location = 0;
                h.item = item;
                h.cxt = cxt;
                h.active = true;
                Core.Terrain.OctreeTerrain.MainLoopUpdateTasks.Enqueue(h);
                InputPoller.AddKeyBindChange(() => {
                    InputPoller.AddBinding(new ActionBind("Place", h.PlaceStructure, ActionBind.Exclusion.ExcludeLayer),
                    "ITEM::PlaceableStct:PL", "5.0::GamePlay");
                    InputPoller.AddBinding(new ActionBind("GetPlaceFocus", (_) => h.IsLocked = false, ActionBind.Exclusion.ExcludeLayer),
                    "ITEM::PlaceableStct:FP", "5.0::GamePlay");
                });
                return h;
            }
            public void Release() {
                active = false;
                item = null;
                display.Release();
                InputPoller.AddKeyBindChange(() => {
                    InputPoller.RemoveBinding("ITEM::PlaceableStct:PL", "5.0::GamePlay");
                    InputPoller.RemoveBinding("ITEM::PlaceableStct:FP", "5.0::GamePlay");
                });
            }

            public void Update(MonoBehaviour _) {
                if (!cxt.TryGetHolder(out PlayerStreamer.Player player)) return;
                if (!RayTestSolid(out float3 hitPt)) return;
                
                if (math.cmax(math.abs(Location - hitPt)) > 2 * interaction.TerraformRadius)
                    IsLocked = false;
                if (!IsLocked) {
                    Location = (int3)math.round(hitPt);
                    IsLocked = true;
                }

                display.ReflectMesh(DeserializeStruct(), Location, GetRotation(player.head - Location));
            }

            private void PlaceStructure(float _) {
                if (!cxt.TryGetHolder(out PlayerStreamer.Player player)) return;
                if (!IsLocked) {
                    if (!RayTestSolid(out float3 hitPt)) return;
                    Location = (int3)math.round(hitPt);
                    IsLocked = true;
                }

                MaterialData placeMat = MatInfo.Retrieve(item.settings.MaterialName);
                if (placeMat is not PlaceableStructureMat stctMat) return;
                int3 rot = GetRotation(player.head - Location);
                List<ConditionedGrowthMat.MapSamplePoint> edit = DeserializeStruct();
                if (RemoveConflictMats(player, edit, Location, rot)) return;
                PlaceNewMats(player, edit, Location, rot);
            }

            private int3 GetRotation(float3 direction) {
                MaterialData placeMat = MatInfo.Retrieve(item.settings.MaterialName);
                if (placeMat is not PlaceableStructureMat mat) return int3.zero;
                float3 angleRot = Quaternion.LookRotation(direction).eulerAngles;
                //clamp to 360 via modulo
                angleRot = (angleRot % 360 + 360) % 360; int3 rot = int3.zero; 
                if (mat.randYRot) rot.y = Mathf.FloorToInt(angleRot.y / 90.0f);
                if (mat.randXRot) rot.x = Mathf.FloorToInt(angleRot.x / 90.0f);
                if (mat.randZRot) rot.z = Mathf.FloorToInt(angleRot.z / 90.0f);
                return rot;
            }

            private List<ConditionedGrowthMat.MapSamplePoint> DeserializeStruct() {
                MaterialData placeMat = MatInfo.Retrieve(item.settings.MaterialName);
                if (placeMat is not PlaceableStructureMat key) return null;
                return key.Structure.value.Select(s => {
                    if (s.HasMaterialCheck) s.material = (uint)MatInfo.RetrieveIndex(
                        key.RetrieveKey((int)s.material));
                    return s;
                }).ToList();
            }
            private delegate bool CheckIterate(Material.ConditionedGrowthMat.MapSamplePoint check, MapData sample, MapData delta, int3 GCoord);
            private bool RemoveConflictMats(Entity.Entity holder, List<ConditionedGrowthMat.MapSamplePoint> edit, int3 GCoord, int3 rot) {
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
                    ToolTag tag = interaction.DefaultTerraform;
                    if (MatInfo.GetMostSpecificTag(TagRegistry.Tags.BareHand, d.material, out object prop))
                        tag = prop as ToolTag;
                    if (d.viscosity > 0) {
                        float strength = math.pow((float)d.viscosity / totalRemAmount, 0.5f);
                        HandleRemoveSolid(ref s, gc, strength * tag.TerraformSpeed, tag.GivesItem);
                        d.density -= d.viscosity; d.viscosity = 0;
                    }
                    if (d.density > 0) {
                        float strength = math.pow((float)d.density / totalRemAmount, 0.5f);
                        HandleRemoveLiquid(ref s, gc, strength * tag.TerraformSpeed, tag.GivesItem);
                    }
                    return false;
                }); 
                //Don't stop liquid conflicts, because liquid will keep flowing in
                //If we don't start placing other liquids
                return totalSolidAmt > 0;
            }

            private bool PlaceNewMats(Entity.Entity holder, List<ConditionedGrowthMat.MapSamplePoint> edit, int3 GCoord, int3 rot) {
                int totPlaceAmt = 0; int selfPlaceAmt = 0; bool PlaceSelf = true;
                int self = MatInfo.RetrieveIndex(item.settings.MaterialName);
                if (!IterateStructPlace(edit, GCoord, rot, (c, s, d, gc) => {
                    int placeMaterial = -1;
                    if (!s.IsGaseous) placeMaterial = s.material;
                    if (c.HasMaterialCheck) placeMaterial = (int)c.material;

                    if (placeMaterial == self) {
                        selfPlaceAmt += d.density;
                    } else {
                        totPlaceAmt += d.density;
                        if (s.LiquidDensity < c.check.bounds.MinLiquid) PlaceSelf = false;
                        if (s.SolidDensity < c.check.bounds.MinSolid) PlaceSelf = false;
                    }
                    //We have to fulfill this contract as we will be placing it
                    return MatInfo.Retrieve(s.material).OnPlacing(gc, holder);
                })) return true;

                if (PlaceSelf) totPlaceAmt += selfPlaceAmt;
                if (totPlaceAmt <= 0) return false;

                float placeSpeed = interaction.DefaultTerraform.value.TerraformSpeed;
                IterateStructPlace(edit, GCoord, rot, (c, s, d, gc) => {
                    if (d.density <= 0) return false;

                    int placeMaterial = -1; //Allow any material of same state to be chosen
                    if (!s.IsGaseous) placeMaterial = s.material; //Must be same as existing material
                    if (c.HasMaterialCheck) placeMaterial = (int)c.material;
                    if (placeMaterial == self && !PlaceSelf) return false;

                    if (d.viscosity > 0 && FindNextMatIndex(out IItem nextSlot, placeMaterial, PlaceableItem.State.Solid)) {
                        float strength = math.pow((float)d.viscosity / totPlaceAmt, 0.5f);
                        HandleAddSolid(nextSlot, gc, strength * placeSpeed, out _);
                        d.density -= d.viscosity; d.viscosity = 0;
                    }
                    if (d.density > 0 && FindNextMatIndex(out nextSlot, placeMaterial, PlaceableItem.State.Liquid)) {
                        float strength = math.pow((float)d.density / totPlaceAmt, 0.5f);
                        HandleAddLiquid(nextSlot, gc, strength * placeSpeed, out _);
                    }
                    return false;
                }); return true;

            }

            private bool IterateStructRemove(List<ConditionedGrowthMat.MapSamplePoint> edit, int3 GCoord, int3 rot, CheckIterate OnRemove) {
                bool any0 = false;
                for (int i = 0; i < edit.Count; i++) {
                    Material.ConditionedGrowthMat.MapSamplePoint pt = edit[i];
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
                    if (OnRemove.Invoke(pt, sample, remAmount, sCoord)) return false;
                }
                return true;
            }


            private bool IterateStructPlace(List<ConditionedGrowthMat.MapSamplePoint> edit, int3 GCoord, int3 rot, CheckIterate OnAdd) {
                bool any0 = false;
                for (int i = 0; i < edit.Count; i++) {
                    Material.ConditionedGrowthMat.MapSamplePoint pt = edit[i];
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
                    if (OnAdd.Invoke(pt, sample, addAmt, sCoord)) return false;
                }
                return true;
            }

            private bool FindNextMatIndex(out IItem slotItem, int matIndex, PlaceableItem.State state) {
                int start = cxt.InvId;
                slotItem = null;

                if (!cxt.TryGetInventory(out InventoryController.Inventory inv))
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