using System;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using MapStorage;

namespace WorldConfig.Generation.Item{
    [CreateAssetMenu(menuName = "Generation/Items/Bow")] 
    public class BowItemAuthoring : AuthoringTemplate<BowItem> {
        /// <summary>  The maximum durability of the item, the durability it possesses when it is first
        /// created. Removing material with tools will decrease durability by the amount indicated
        /// in the tag <see cref="ToolTag"/> for more info. </summary>
        public float MaxDurability;
        public float MinDrawTime = 0.25f;
        public float FullDrawTime = 2f; //seconds to full draw
        public float MaxLaunchSpeed = 20.0f;
        public float MinLaunchSpeed = 5.0f;
        public TagRegistry.Tags ArrowItemTag;
    }

    public class BowItem : IItem {
        public uint data;
        public float durability;
        private static Catalogue<Authoring> ItemInfo => Config.CURRENT.Generation.Items;
        private static Catalogue<TextureContainer> TextureAtlas => Config.CURRENT.Generation.Textures;
        private static Catalogue<Material.MaterialData> MatInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        private BowItemAuthoring settings => ItemInfo.Retrieve(Index) as BowItemAuthoring;

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
            set => data = (data & 0x7FFF0000) | ((uint)value & 0xFFFF);
        }
        public IRegister GetRegistry() => Config.CURRENT.Generation.Items;

        public object Clone() {
            return new BowItem {
                data = data,
                durability = durability
            };
        }

        public void Create(int Index, int AmountRaw) {
            this.Index = Index;
            this.AmountRaw = AmountRaw;
            this.durability = settings.MaxDurability;
        }
        public void UpdateEItem() { }
        private int[] KeyBinds;
        public void OnEnter(ItemContext cxt) {
            if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
            InputPoller.AddKeyBindChange(() => {
                KeyBinds = new int[2];
                KeyBinds[0] = (int)InputPoller.AddBinding(new InputPoller.ActionBind(
                    "BowDraw", _ => StartDrawingBow(cxt),
                    InputPoller.ActionBind.Exclusion.ExcludeLayer), "5.0::GamePlay");
                KeyBinds[1] = (int)InputPoller.AddBinding(new InputPoller.ActionBind(
                    "BowRelease", _ => ReleaseBow(cxt),
                    InputPoller.ActionBind.Exclusion.ExcludeLayer), "5.0::GamePlay");
            });
            drawTime = 0;
        }
        public void OnLeave(ItemContext cxt) {
            if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
            InputPoller.AddKeyBindChange(() => {
                if (KeyBinds == null) return;
                InputPoller.RemoveKeyBind((uint)KeyBinds[0], "5.0::GamePlay");
                InputPoller.RemoveKeyBind((uint)KeyBinds[1], "5.0::GamePlay");
            });
        }

        private float drawTime;
        void StartDrawingBow(ItemContext cxt) {
            if (!HoldingArrowItems(cxt, out int slot)) {
                drawTime = 0;
                return;
            };
            drawTime += Time.deltaTime;
        }

        void ReleaseBow(ItemContext cxt) {
            float timeDraw = drawTime; drawTime = 0;
            if (!HoldingArrowItems(cxt, out int slot)) return;
            if(timeDraw < settings.MinDrawTime) return;
            float drawPercent = Mathf.InverseLerp(settings.MinDrawTime, settings.FullDrawTime, timeDraw);
            float launchSpeed = Mathf.Lerp(settings.MinLaunchSpeed, settings.MaxLaunchSpeed, drawPercent);
            if (!ShootArrow(cxt, slot, launchSpeed)) return;
            durability -= 1.0f;
            if (durability > 0) return;
            cxt.TryRemove();
        }

        private bool HoldingArrowItems(ItemContext cxt, out int slot) {
            int start = cxt.InvId; slot = 0;
            var settings = WorldConfig.Config.CURRENT.GamePlay.Inventory.value;
            if (!cxt.TryGetInventory(out InventoryController.Inventory inv))
                return false;
            for (slot = (start + 1) % settings.PrimarySlotCount;
                slot != start;
                slot = (slot + 1) % settings.PrimarySlotCount) {
                slot %= settings.PrimarySlotCount;
                if (inv.Info[slot] == null) continue;
                if (!ItemInfo.GetMostSpecificTag(this.settings.ArrowItemTag, inv.Info[slot].Index, out _))
                    continue;
                return true;
            }
            return false;
        }

        private bool ShootArrow(ItemContext cxt, int slot, float launchSpeed) {
            if (!cxt.TryGetInventory(out InventoryController.Inventory inv))
                return false;
            if (!cxt.TryGetHolder(out Entity.Entity h)) return false;
            if (!ItemInfo.GetMostSpecificTag(this.settings.ArrowItemTag, inv.Info[slot].Index, out object prop))
                return false;
            ProjectileTag tag = (ProjectileTag)prop;
            tag.LaunchProjectile(h, h.Forward * launchSpeed);
            inv.RemoveStackableSlot(slot, inv.Info[slot].UnitSize);
            return true;
        }


        protected GameObject display;
        public virtual void AttachDisplay(Transform parent)
        {
            if (display != null) {
                display.transform.SetParent(parent, false);
                return;
            }

            display = Indicators.ToolItems.Get();
            display.transform.SetParent(parent, false);
            display.transform.GetComponent<UnityEngine.UI.Image>().sprite = TextureAtlas.Retrieve(ItemInfo.Retrieve(Index).TextureName).self;
            UpdateDisplay();
        }

        public virtual void ClearDisplay(Transform parent) {
            if (display == null) return;
            Indicators.ToolItems.Release(display);
            display = null;
        }
        protected virtual void UpdateDisplay() {
            if (display == null) return;
            Transform durbBar = display.transform.Find("Bar");
            durbBar.GetComponent<UnityEngine.UI.Image>().fillAmount = durability / settings.MaxDurability;
        }
    }
}