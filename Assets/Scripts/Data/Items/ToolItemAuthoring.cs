using System;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using WorldConfig.Generation.Material;
using static PlayerInteraction;
using MapStorage;

namespace WorldConfig.Generation.Item
{
    [CreateAssetMenu(menuName = "Generation/Items/Tool")]
    public class ToolItemAuthoring : AuthoringTemplate<ToolItem>
    {
        /// <summary> The radius, in grid space, of the spherical region around the user's
        /// cursor that will be modified when the user terraforms the terrain. </summary>
        public int TerraformRadius = 1;
        /// <summary>  The maximum durability of the item, the durability it possesses when it is first
        /// created. Removing material with tools will decrease durability by the amount indicated
        /// in the tag <see cref="ToolTag"/> for more info. </summary>
        public float MaxDurability;
        /// <summary> The tag used to determine how each material's removal should be handled.
        /// A material's most specific tag that fits this enum will be used to determine
        /// how this tool effects it. </summary>
        public TagRegistry.Tags ToolTag;
    }

    [Serializable]
    public class ToolItem : IItem
    {
        public uint data;
        public float durability;
        private static Registry<Authoring> ItemInfo => Config.CURRENT.Generation.Items;
        private static Registry<MaterialData> MatInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        private static Registry<TextureContainer> TextureAtlas => Config.CURRENT.Generation.Textures;
        [JsonIgnore]
        public bool IsStackable => false;
        [JsonIgnore]
        public int TexIndex => TextureAtlas.RetrieveIndex(ItemInfo.Retrieve(Index).TextureName);
        private ToolItemAuthoring settings => ItemInfo.Retrieve(Index) as ToolItemAuthoring;

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
        public object Clone() => new ToolItem { data = data, durability = durability };
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

        private int[] KeyBinds;
        public void OnSelect()
        {
            InputPoller.AddKeyBindChange(() =>
            {
                KeyBinds = new int[1];
                KeyBinds[0] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Remove", TerrainRemove, InputPoller.ActionBind.Exclusion.ExcludeLayer), "5.0::GamePlay");
            });
        }

        public void OnDeselect()
        {
            InputPoller.AddKeyBindChange(() =>
            {
                if (KeyBinds == null) return;
                InputPoller.RemoveKeyBind((uint)KeyBinds[0], "5.0::GamePlay");
            });
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

        private void TerrainRemove(float _)
        {
            if (!RayTestSolid(PlayerHandler.data, out float3 hitPt)) return;
            if (EntityManager.ESTree.FindClosestAlongRay(PlayerHandler.data.position, hitPt, PlayerHandler.data.info.entityId, out var _))
                return;
            var localSettings = settings;
            bool RemoveSolid(int3 GCoord, float speed)
            {
                MapData mapData = CPUMapManager.SampleMap(GCoord);
                int material = mapData.material; ToolTag tag = PlayerInteraction.settings.DefaultTerraform;
                if (MatInfo.GetMostSpecificTag(localSettings.ToolTag, material, out TagRegistry.IProperty prop))
                    tag = prop as ToolTag;

                MapData prev = mapData;
                if (!HandleRemoveSolid(ref mapData, GCoord, speed * tag.TerraformSpeed, tag.GivesItem))
                    return false;
                float delta = math.abs(prev.SolidDensity - mapData.SolidDensity);
                durability -= tag.ToolDamage * (delta / 255.0f);
                return true;
            }

            CPUMapManager.Terraform(hitPt, localSettings.TerraformRadius, RemoveSolid);
            UpdateDisplay();

            if (durability > 0) return;
            //Removes itself
            InventoryController.Primary.RemoveEntry(InventoryController.SelectedIndex);
        }

        private void UpdateDisplay() {
            if (display == null) return;
            Transform durbBar = display.transform.Find("Bar");
            durbBar.GetComponent<UnityEngine.UI.Image>().fillAmount = durability / settings.MaxDurability;
        }
    }
}
