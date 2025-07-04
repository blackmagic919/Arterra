using System;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using WorldConfig.Generation.Material;
using MapStorage;

namespace WorldConfig.Generation.Item
{
    [CreateAssetMenu(menuName = "Generation/Items/Pen")]
    public class PenItemAuthoring : AuthoringTemplate<PenItem>
    {
        public float SelectionRefreshDist;
        public float MaximumSelectionVolume;
        public float MaxDurability = 100;
    }

    [Serializable]
    public class PenItem : IItem
    {
        public uint data;
        public float durability;
        private static Registry<Authoring> ItemInfo => Config.CURRENT.Generation.Items;
        private static Registry<TextureContainer> TextureAtlas => Config.CURRENT.Generation.Textures;
        [JsonIgnore]
        public bool IsStackable => false;
        [JsonIgnore]
        public int TexIndex => TextureAtlas.RetrieveIndex(ItemInfo.Retrieve(Index).TextureName);
        internal PenItemAuthoring settings => ItemInfo.Retrieve(Index) as PenItemAuthoring;
        private InteractionHandler handler;

        [JsonIgnore]
        public int Index
        {
            get => (int)(data >> 16) & 0x7FFF;
            set => data = (data & 0x0000FFFF) | (((uint)value & 0x7FFF) << 16);
        }
        [JsonIgnore]
        public int AmountRaw
        {
            get => (int)(data & 0xFFFF);
            set => data = (data & 0x7FFF0000) | ((uint)value & 0xFFFF);
        }
        public IRegister GetRegistry() => Config.CURRENT.Generation.Items;

        public object Clone() => new PenItem { data = data, durability = durability };
        public void Create(int Index, int AmountRaw)
        {
            this.Index = Index;
            this.AmountRaw = AmountRaw;
            this.durability = settings.MaxDurability;
        }
        public void OnEnterSecondary() { }
        public void OnLeaveSecondary() { }
        public void OnEnterPrimary() { }
        public void OnLeavePrimary() { }
        public void UpdateEItem() { }
        public void OnSelect()
        {
            if (handler != null) handler.Release();
            handler = InteractionHandler.Create(this);
        }

        public void OnDeselect()
        {
            handler.Release();
            handler = null;
        }

        private GameObject display;
        public void AttachDisplay(Transform parent)
        {
            if (display != null)
            {
                display.transform.SetParent(parent, false);
                return;
            }

            display = Indicators.HolderItems.Get();
            display.transform.SetParent(parent, false);
            display.transform.GetComponent<UnityEngine.UI.Image>().sprite = TextureAtlas.Retrieve(ItemInfo.Retrieve(Index).TextureName).self;
            UpdateDisplay();
        }

        public void ClearDisplay()
        {
            if (display == null) return;
            Indicators.HolderItems.Release(display);
            display = null;
        }

        internal void UpdateDisplay()
        {
            if (display == null) return;
            Transform durbBar = display.transform.Find("Bar");
            durbBar.GetComponent<UnityEngine.UI.Image>().fillAmount = durability / settings.MaxDurability;
        }
    }
    class InteractionHandler
    {
        private int[] KeyBinds;
        private Bounds SelectBounds; //Inclusive
        private GameObject Selector;
        private uint SelectedCorner;
        private PenItem item;

        public static Registry<WorldConfig.Generation.Item.Authoring> ItemInfo => WorldConfig.Config.CURRENT.Generation.Items;
        public static Registry<MaterialData> MatInfo => WorldConfig.Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        public static InteractionHandler Create(PenItem item)
        {
            InteractionHandler h = new InteractionHandler();
            h.SelectedCorner = 0;
            h.item = item; //Watch out, this can create a circular reference

            InputPoller.AddKeyBindChange(() =>
            {
                h.KeyBinds = new int[4];
                h.KeyBinds[0] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Interact", h.DragPoint, InputPoller.ActionBind.Exclusion.ExcludeLayer), "5.0::GamePlay");
                h.KeyBinds[1] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Start Interact", h.SelectPoint, InputPoller.ActionBind.Exclusion.ExcludeLayer), "5.0::GamePlay");
                //When SelectPoint is triggered, DragPoint will also be triggered on the same frame, make sure SelectPoint happens first 
                h.KeyBinds[2] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Remove", h.OnTerrainRemove, InputPoller.ActionBind.Exclusion.ExcludeLayer), "5.0::GamePlay");
                h.KeyBinds[3] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Place", h.OnTerrainAdd, InputPoller.ActionBind.Exclusion.ExcludeLayer), "5.0::GamePlay");
            });
            return h;
        }

        public void Release()
        {
            Selector?.SetActive(false);
            Selector = null;
            item = default;
            InputPoller.AddKeyBindChange(() =>
            {
                if (KeyBinds == null) return;
                InputPoller.RemoveKeyBind((uint)KeyBinds[0], "5.0::GamePlay");
                InputPoller.RemoveKeyBind((uint)KeyBinds[1], "5.0::GamePlay");
                InputPoller.RemoveKeyBind((uint)KeyBinds[2], "5.0::GamePlay");
                InputPoller.RemoveKeyBind((uint)KeyBinds[3], "5.0::GamePlay");
                KeyBinds = null;
            });
        }

        public void SelectPoint(float _)
        {
            if (Selector != null)
            {
                Ray cRay = new Ray(PlayerHandler.data.position, PlayerHandler.camera.forward);
                float crnDist = GetDistClosestCorner(SelectBounds, cRay, out SelectedCorner);
                if (crnDist > item.settings.SelectionRefreshDist)
                {
                    Selector.SetActive(false);
                    Selector = null;
                }
            }
            if (Selector != null) return;

            if (!FindNearest(out int3 hitCoord)) return;
            SelectBounds = new Bounds((float3)hitCoord, float3.zero);
            Selector = Indicators.SelectionIndicator;
            Selector.SetActive(true);
            Selector.gameObject.transform.position = CPUMapManager.GSToWS(hitCoord);
        }

        public void DragPoint(float _)
        {
            if (Selector == null) return;
            Ray cRay = new Ray(PlayerHandler.data.position, PlayerHandler.camera.forward);
            float3 projection = GetProjOntoRay(cRay, GetCorner(SelectBounds, SelectedCorner));
            Bounds nBounds = SetCorner(SelectBounds, ref SelectedCorner, math.round(projection));
            if (GetVolume(nBounds.size + new Vector3(1, 1, 1)) < item.settings.MaximumSelectionVolume)
                SelectBounds = nBounds;
            Selector.transform.position = CPUMapManager.GSToWS(SelectBounds.center);
            Selector.transform.localScale = SelectBounds.size + new Vector3(1, 1, 1);
        }

        public void OnTerrainAdd(float _)
        {
            if (Selector == null)
            {
                if (!FindNearest(out int3 hitCoord)) return;
                if (!FindNextSolidMat(out int nextSlot)) return;
                AddNextSolidWithDurability(hitCoord, nextSlot);
                return;
            }
            int3 coord = 0;
            for (coord.x = (int)SelectBounds.min.x; coord.x <= SelectBounds.max.x; coord.x++)
            {
                for (coord.y = (int)SelectBounds.min.y; coord.y <= SelectBounds.max.y; coord.y++)
                {
                    for (coord.z = (int)SelectBounds.min.z; coord.z <= SelectBounds.max.z; coord.z++)
                    {
                        if (!FindNextSolidMat(out int slot)) return;
                        AddNextSolidWithDurability(coord, slot);
                    }
                }
            }
        }


        public void OnTerrainRemove(float _)
        {
            if (Selector == null)
            {
                if (!FindNearest(out int3 hitCoord)) return;
                RemoveSolidWithDurability(hitCoord);
                return;
            }

            int3 coord = 0;
            for (coord.x = (int)SelectBounds.min.x; coord.x <= SelectBounds.max.x; coord.x++)
            {
                for (coord.y = (int)SelectBounds.min.y; coord.y <= SelectBounds.max.y; coord.y++)
                {
                    for (coord.z = (int)SelectBounds.min.z; coord.z <= SelectBounds.max.z; coord.z++)
                    {
                        RemoveSolidWithDurability(coord);
                    }
                }
            }
        }

        private static float GetVolume(float3 bounds) => bounds.x * bounds.y * bounds.z;
        private static float GetDistFromRay(Ray ray, Vector3 point) => Vector3.Cross(ray.direction, point - ray.origin).magnitude;
        private static float3 GetProjOntoRay(Ray ray, float3 point)
        {
            float3 origin = ray.origin;
            float3 dir = math.normalize(ray.direction); // ensure it's a unit vector
            float3 toPoint = point - origin;
            float t = math.dot(toPoint, dir);
            return origin + t * dir;
        }
        private static bool FindNearest(out int3 hitCoord)
        {
            hitCoord = int3.zero;
            if (!PlayerInteraction.RayTestSolid(PlayerHandler.data, out float3 hitPt)) return false;
            Ray ray = new Ray(PlayerHandler.data.position, PlayerHandler.camera.forward);
            int3 hitOrig = (int3)math.floor(hitPt);

            hitCoord = hitOrig;
            for (int i = 0; i < 8; i++)
            {
                int3 hitCorner = hitOrig + new int3(i & 0x1, (i >> 1) & 0x1, (i >> 2) & 0x1);
                float curDist = GetDistFromRay(ray, (float3)hitCorner);
                float bestDist = GetDistFromRay(ray, (float3)hitCoord);
                if (curDist < bestDist) hitCoord = hitCorner;
            }
            return true;
        }

        private static float3 GetCorner(Bounds bounds, uint index)
        {
            float3 min = bounds.min;
            float3 max = bounds.max;
            return new((index & 0x1) == 0 ? min.x : max.x,
                    (index & 0x2) == 0 ? min.y : max.y,
                    (index & 0x3) == 0 ? min.z : max.z);
        }

        private static Bounds SetCorner(Bounds bounds, ref uint index, float3 pos)
        {
            float3 min = bounds.min;
            float3 max = bounds.max;
            // For each axis, if the index bit is 0, that axis corresponds to min; if 1, it’s max
            if ((index & 0x1) == 0) min.x = pos.x; else max.x = pos.x;
            if ((index & 0x2) == 0) min.y = pos.y; else max.y = pos.y;
            if ((index & 0x4) == 0) min.z = pos.z; else max.z = pos.z;
            index ^= ((min.x > max.x) ? 0x1u : 0) | ((min.y > max.y) ? 0x2u : 0) |
            ((min.z > max.z) ? 0x4u : 0);

            // Fix if min > max on any axis (swap if necessary)
            float3 realMin = math.min(min, max);
            float3 realMax = math.max(min, max);

            return new Bounds { min = realMin, max = realMax };
        }

        private static float GetDistClosestCorner(Bounds bounds, Ray ray, out uint CornerIndex)
        {
            CornerIndex = 0;
            float closest = float.PositiveInfinity;
            for (uint i = 0; i < 8; i++)
            {
                float3 corner = GetCorner(bounds, i);
                float dist = GetDistFromRay(ray, corner);
                if (dist > closest) continue;
                CornerIndex = i;
                closest = dist;
            }
            return closest;
        }

        void AddNextSolidWithDurability(int3 hitCoord, int nextSlot)
        {
            MapData orig = CPUMapManager.SampleMap(hitCoord);
            ToolTag prop = PlayerInteraction.settings.DefaultTerraform.value;
            if (MatInfo.GetMostSpecificTag(TagRegistry.Tags.BareHand, orig.material, out TagRegistry.IProperty tag))
                prop = tag as ToolTag;

            if (!PlayerInteraction.HandleAddSolid(InventoryController.Primary.Info[nextSlot], hitCoord, prop.TerraformSpeed, out MapData change))
                return;
            int delta = math.abs(change.SolidDensity - orig.SolidDensity);
            item.durability -= prop.ToolDamage * delta;
            item.UpdateDisplay();

            if (item.durability > 0) return;
            InventoryController.Primary.RemoveEntry(InventoryController.SelectedIndex);
        }

        void RemoveSolidWithDurability(int3 hitCoord)
        {
            MapData orig = CPUMapManager.SampleMap(hitCoord);
            ToolTag prop = PlayerInteraction.settings.DefaultTerraform.value;
            if (MatInfo.GetMostSpecificTag(TagRegistry.Tags.BareHand, orig.material, out TagRegistry.IProperty tag))
                prop = tag as ToolTag;

            MapData prev = orig;
            if (!PlayerInteraction.HandleRemoveSolid(ref orig, hitCoord, prop.TerraformSpeed))
                return;
            
            int delta = math.abs(prev.SolidDensity - orig.SolidDensity);
            item.durability -= prop.ToolDamage * delta;
            item.UpdateDisplay();

            if (item.durability > 0) return;
            InventoryController.Primary.RemoveEntry(InventoryController.SelectedIndex);
        }

        private static bool FindNextSolidMat(out int slot)
        {
            int start = InventoryController.SelectedIndex;
            var settings = WorldConfig.Config.CURRENT.GamePlay.Inventory.value;
            for (slot = (start + 1) % settings.PrimarySlotCount;
                slot != start;
                slot = (slot + 1) % settings.PrimarySlotCount)
            {
                slot %= settings.PrimarySlotCount;
                if (InventoryController.Primary.Info[slot] == null) continue;
                Authoring authoring = ItemInfo.Retrieve(InventoryController.Primary.Info[slot].Index);
                if (!MatInfo.Contains(authoring.MaterialName) || !authoring.IsSolid) continue;
                return true;
            }
            return false;
        }
    }

}