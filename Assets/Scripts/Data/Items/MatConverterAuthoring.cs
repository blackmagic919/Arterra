using System;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using WorldConfig.Generation.Material;
using static PlayerInteraction;
using MapStorage;


namespace WorldConfig.Generation.Item
{
    //This type inherits ToolItemAuthoring which inherits AuthoringTemplate<ToolItem>  which
    //inherits Authoring which inherits Category<Authoring>
    [CreateAssetMenu(menuName = "Generation/Items/MatConverter")]
    public class MatConverterAuthoring : PlaceableTemplate<MatConverterItem>{
        /// <summary> The radius, in grid space, of the spherical region around the user's
        /// cursor that will be modified when the user terraforms the terrain. </summary>
        public int TerraformRadius = 1;
        /// <summary> The tag used to determine what a material is converted to and how 
        /// to convert it. </summary>
        public TagRegistry.Tags ConverterTag;
    }

    [Serializable]
    public class MatConverterItem : IItem {
        public uint data;
        protected static Catalogue<Authoring> ItemInfo => Config.CURRENT.Generation.Items;
        protected static Catalogue<MaterialData> MatInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        private static Catalogue<TextureContainer> TextureAtlas => Config.CURRENT.Generation.Textures;

        [JsonIgnore]
        public bool IsStackable => true;
        [JsonIgnore]
        public int TexIndex => TextureAtlas.RetrieveIndex(ItemInfo.Retrieve(Index).TextureName);

        [JsonIgnore]
        public int Index {
            get => (int)(data >> 16) & 0x7FFF;
            set => data = (data & 0x0000FFFF) | (((uint)value & 0x7FFF) << 16);
        }
        [JsonIgnore]
        public string Display {
            get => ((data & 0xFFFF) / (float)0xFF).ToString();
            set => data = (data & 0xFFFF0000) | (((uint)Mathf.Round(uint.Parse(value) * 0xFF)) & 0xFFFF);
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
        public object Clone() => new MatConverterItem { data = data };
        public void Create(int Index, int AmountRaw) {
            this.Index = Index;
            this.AmountRaw = AmountRaw;
        }
        public void UpdateEItem() { }

        protected int[] KeyBinds;
        public void OnEnter(ItemContext cxt) {
            if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
            InputPoller.AddKeyBindChange(() => {
                KeyBinds = new int[1];
                KeyBinds[0] = (int)InputPoller.AddBinding(new InputPoller.ActionBind(
                    "Place",
                    _ => PlayerModifyTerrain(cxt),
                    InputPoller.ActionBind.Exclusion.ExcludeLayer), "5.0::GamePlay");
            });
        }

        public void OnLeave(ItemContext cxt) {
            if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
            InputPoller.AddKeyBindChange(() => {
                if (KeyBinds == null) return;
                InputPoller.RemoveKeyBind((uint)KeyBinds[0], "5.0::GamePlay");
                KeyBinds = null;
            });
        }

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
            amount.text = ((data & 0xFFFF) / (float)0xFF).ToString();
        }

        public void ClearDisplay(Transform parent) {
            if (display == null) return;
            Indicators.StackableItems.Release(display);
            display = null;
        }


        private void UpdateDisplay() {
            if (display == null) return;
            TMPro.TextMeshProUGUI amount = display.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            amount.text = ((data & 0xFFFF) / (float)0xFF).ToString();
        }

        private MatConverterAuthoring settings => ItemInfo.Retrieve(Index) as MatConverterAuthoring;

        private void PlayerModifyTerrain(ItemContext cxt) {
            if (!cxt.TryGetHolder(out PlayerStreamer.Player player)) return;
            if (!RayTestSolid(player, out float3 hitPt)) return;
            if (EntityManager.ESTree.FindClosestAlongRay(player.position, hitPt, player.info.entityId, out var _))
                return;
            if (settings.MaterialName == null) {
                Debug.LogError("MaterialName is not set for MatConverterItem at index " + Index);
                return;
            }

            int curMat = MatInfo.RetrieveIndex(settings.MaterialName);
            bool ModifySolid(int3 GCoord, float speed) {
                MapData mapData = CPUMapManager.SampleMap(GCoord);
                
                if (mapData.material == curMat) return false;
                if (!IMaterialConverting.CanConvert(mapData, GCoord, settings.ConverterTag, out ConvertibleToolTag tag))
                    return false;
                if (UnityEngine.Random.Range(0.0f, 1.0f) > tag.TerraformSpeed)
                    return false;
                if (!MaterialData.SwapMaterial(GCoord, curMat, out IItem origItem))
                    return false;
                if (tag.GivesItem) InventoryController.DropItem(origItem, GCoord);

                AmountRaw -= GetStaggeredDelta(AmountRaw, -tag.ToolDamage * mapData.density, IItem.MaxAmountRaw);
                return true;
            }

            CPUMapManager.Terraform(hitPt, settings.TerraformRadius, ModifySolid, CallOnMapRemoving);
            UpdateDisplay();

            if (AmountRaw > 0) return;
            //Removes itself
            cxt.TryRemove();
        }
    }
}
