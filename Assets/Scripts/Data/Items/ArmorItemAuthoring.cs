using System;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Configuration.Generation.Material;

namespace Arterra.Configuration.Generation.Item
{
    [CreateAssetMenu(menuName = "Generation/Items/Armor")]
    public class ArmorItemAuthoring : AuthoringTemplate<ArmorItem> {
        public ArmorInventory.EquipInfo EquipInfo;
        public float MaxDurability;
        [Range(0, 1)]
        public float DamageReduction;
        [Range(0, 1)]
        public float KnockbackReduction;
    }

    [Serializable]
    public class ArmorItem : IItem, ArmorInventory.IArmorItem
    {
        public uint data;
        public float durability;
        private ItemContext cxt;
        protected static Catalogue<Authoring> ItemInfo => Config.CURRENT.Generation.Items;
        protected static Catalogue<MaterialData> MatInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        protected static Catalogue<TextureContainer> TextureAtlas => Config.CURRENT.Generation.Textures;
        [JsonIgnore]
        public int TexIndex => TextureAtlas.RetrieveIndex(ItemInfo.Retrieve(Index).TextureName);
        private ArmorItemAuthoring settings => ItemInfo.Retrieve(Index) as ArmorItemAuthoring;

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
        public virtual object Clone() => new ArmorItem { data = data, durability = durability };
        public void Create(int Index, int AmountRaw)
        {
            this.Index = Index;
            this.AmountRaw = AmountRaw;
            this.durability = settings.MaxDurability;
        }
        public void UpdateEItem() { }
        public void OnEnter(ItemContext cxt){ this.cxt = cxt; }
        public void OnLeave(ItemContext cxt){ this.cxt = null; }
        public ArmorInventory.EquipInfo GetEquipInfo() { return settings.EquipInfo; }
        public void OnDamaged(ref float dmg, ref float3 knockback, ref Entity.Entity attacker) {
            durability -= dmg;
            dmg *= 1 - settings.DamageReduction;
            knockback *= 1 - settings.KnockbackReduction;

            if (durability > 0) return;
            cxt.TryRemove();
        }
        public void OnEquipped(string armorName) {}
        public void OnUnequipped(string armorName) {}

        protected GameObject display;
        public virtual void AttachDisplay(Transform parent)
        {
            if (display != null)
            {
                display.transform.SetParent(parent, false);
                return;
            }

            display = Indicators.ToolItems.Get();
            display.transform.SetParent(parent, false);
            display.transform.GetComponent<UnityEngine.UI.Image>().sprite = TextureAtlas.Retrieve(ItemInfo.Retrieve(Index).TextureName).self;
            UpdateDisplay();
        }

        public virtual void ClearDisplay(Transform parent)
        {
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
