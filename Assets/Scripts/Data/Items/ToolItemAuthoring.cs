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
    public class ToolItemAuthoring : AuthoringTemplate<ToolItem> {
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
        public Optional<string> OnUseAnim;
        public Optional<GameObject> Model;
    }

    [Serializable]
    public class ToolItem : IItem
    {
        public uint data;
        public float durability;
        protected static Catalogue<Authoring> ItemInfo => Config.CURRENT.Generation.Items;
        protected static Catalogue<MaterialData> MatInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        protected static Catalogue<TextureContainer> TextureAtlas => Config.CURRENT.Generation.Textures;
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
        public virtual object Clone() => new ToolItem { data = data, durability = durability };
        public void Create(int Index, int AmountRaw)
        {
            this.Index = Index;
            this.AmountRaw = AmountRaw;
            this.durability = settings.MaxDurability;
        }
        public void UpdateEItem() { }
        public virtual void OnEnter(ItemContext cxt) {
            if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
            if (cxt.TryGetHolder(out IActionEffect effect) && settings.Model.Enabled)
                effect.Play("HoldItem", settings.Model.Value);
            InputPoller.AddKeyBindChange(() => {
                InputPoller.AddBinding(new ActionBind("Remove",
                    _ => PlayerRemoveTerrain(cxt)),
                    "ITEM::Tool:RM", "5.0::GamePlay");
            });
        }
        public virtual void OnLeave(ItemContext cxt) {
            if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
            if (cxt.TryGetHolder(out IActionEffect effect) && settings.Model.Enabled)
                effect.Play("UnHoldItem", settings.Model.Value);
            InputPoller.AddKeyBindChange(() => InputPoller.RemoveBinding("ITEM::Tool:RM", "5.0::GamePlay"));
        }

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


        protected void PlayerRemoveTerrain(ItemContext cxt) {
            if (settings.ToolTag == TagRegistry.Tags.None) return;
            if (!cxt.TryGetHolder(out PlayerStreamer.Player player)) return;
            if (!RayTestSolid(player, out float3 hitPt)) return;
            if (EntityManager.ESTree.FindClosestAlongRay(player.head, hitPt, player.info.entityId, out var _))
                return;
            bool RemoveSolid(int3 GCoord, float speed) {
                MapData mapData = CPUMapManager.SampleMap(GCoord);
                int material = mapData.material; ToolTag tag = PlayerInteraction.settings.DefaultTerraform;
                if (MatInfo.GetMostSpecificTag(settings.ToolTag, material, out object prop))
                    tag = prop as ToolTag;

                MapData prev = mapData;
                if (!HandleRemoveSolid(ref mapData, GCoord, speed * tag.TerraformSpeed, tag.GivesItem))
                    return false;
                float delta = math.abs(prev.SolidDensity - mapData.SolidDensity);
                durability -= tag.ToolDamage * (delta / 255.0f);
                return true;
            }

            CPUMapManager.Terraform(hitPt, settings.TerraformRadius, RemoveSolid, CallOnMapRemoving);
            UpdateDisplay();

            if (settings.OnUseAnim.Enabled && player is IActionEffect effectable)
                effectable.Play(settings.OnUseAnim.Value);

            InputPoller.SuspendKeybindPropogation("Remove", ActionBind.Exclusion.ExcludeLayer);
            if (durability > 0) return;
            //Removes itself
            cxt.TryRemove();
        }

        protected virtual void UpdateDisplay() {
            if (display == null) return;
            Transform durbBar = display.transform.Find("Bar");
            durbBar.GetComponent<UnityEngine.UI.Image>().fillAmount = durability / settings.MaxDurability;
        }
    }
}
