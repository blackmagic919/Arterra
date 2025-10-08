using UnityEngine;
using Newtonsoft.Json;

namespace WorldConfig.Generation.Item{
    [CreateAssetMenu(menuName = "Generation/Items/Material")]
    public class MaterialItemAuthoring : PlaceableTemplate<MaterialItem> {}

    [System.Serializable]
    public struct MaterialItem : IItem
    {
        public uint data;
        private static Catalogue<Authoring> ItemInfo => Config.CURRENT.Generation.Items;
        private static Catalogue<TextureContainer> TextureAtlas => Config.CURRENT.Generation.Textures;
        [JsonIgnore]
        public readonly bool IsStackable => true;
        [JsonIgnore]
        public readonly int TexIndex => TextureAtlas.RetrieveIndex(ItemInfo.Retrieve(Index).TextureName);

        [JsonIgnore]
        public int Index
        {
            readonly get => (int)(data >> 16) & 0x7FFF;
            set => data = (data & 0x0000FFFF) | (((uint)value & 0x7FFF) << 16);
        }
        [JsonIgnore]
        public string Display
        {
            readonly get => ((data & 0xFFFF) / (float)0xFF).ToString();
            set => data = (data & 0xFFFF0000) | (((uint)Mathf.Round(uint.Parse(value) * 0xFF)) & 0xFFFF);
        }
        [JsonIgnore]
        public int AmountRaw
        {
            readonly get => (int)(data & 0xFFFF);
            set
            {
                data = (data & 0x7FFF0000) | ((uint)value & 0xFFFF);
                UpdateDisplay();
            }
        }
        public IRegister GetRegistry() => Config.CURRENT.Generation.Items;
        public object Clone() => new MaterialItem { data = data };
        public void Create(int Index, int AmountRaw){
            this.Index = Index;
            this.AmountRaw = AmountRaw;
        }
        
        public readonly void OnEnter(ItemContext cxt) { }
        public readonly void OnLeave(ItemContext cxt) { }
        public readonly void UpdateEItem() { } 

        private GameObject display;
        public void AttachDisplay(Transform parent){
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

        public void ClearDisplay(Transform parent){
            if (display == null) return;
            Indicators.StackableItems.Release(display);
            display = null;
        }
        
        
        private void UpdateDisplay() {
            if (display == null) return;
            TMPro.TextMeshProUGUI amount = display.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            amount.text = ((data & 0xFFFF) / (float)0xFF).ToString();
        }
}
}
