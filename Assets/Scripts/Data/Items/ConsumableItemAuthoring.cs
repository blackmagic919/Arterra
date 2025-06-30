using UnityEngine;
using Newtonsoft.Json;
using static PlayerInteraction;
using Unity.Mathematics;

namespace WorldConfig.Generation.Item{
[CreateAssetMenu(menuName = "Generation/Items/Consumable")] 
public class ConsumableItemAuthoring : AuthoringTemplate<ConsumbaleItem> {
    public float ConsumptionRate;
    public float NutritionValue;
}

[System.Serializable]
public class ConsumbaleItem : IItem{
    public uint data;
    private static Registry<Authoring> ItemInfo => Config.CURRENT.Generation.Items;
    private static Registry<TextureContainer> TextureAtlas => Config.CURRENT.Generation.Textures;
    [JsonIgnore]
    public bool IsStackable => true;
    [JsonIgnore]
    public int TexIndex => TextureAtlas.RetrieveIndex(ItemInfo.Retrieve(Index).TextureName);
    private ConsumableItemAuthoring settings => ItemInfo.Retrieve(Index) as ConsumableItemAuthoring;
    private uint[] KeyBinds;

    [JsonIgnore]
    public int Index{
        get => (int)(data >> 16) & 0x7FFF;
        set => data = (data & 0x0000FFFF) | (((uint)value & 0x7FFF) << 16);
    }
    [JsonIgnore]
    public int AmountRaw{
        get => (int)(data & 0xFFFF);
        set
        {
            data = (data & 0x7FFF0000) | ((uint)value & 0xFFFF);
            UpdateDisplay();
        }
    }
    public IRegister GetRegistry() => Config.CURRENT.Generation.Items;
    public object Clone() => new ConsumbaleItem{data = data};
    public void Create(int Index, int AmountRaw){
        this.Index = Index;
        this.AmountRaw = AmountRaw;
    }
    public void OnEnterSecondary() { } 
    public void OnLeaveSecondary(){}
    public void OnEnterPrimary(){} 
    public void OnLeavePrimary(){} 
    public void OnSelect(){
        InputPoller.AddKeyBindChange(() => {
            KeyBinds = new uint[1];
            KeyBinds[0] = InputPoller.AddBinding(new InputPoller.ActionBind("Place", ConsumeFood, InputPoller.ActionBind.Exclusion.ExcludeLayer), "5.0::GamePlay");
        });
    } 
    public void OnDeselect(){
        InputPoller.AddKeyBindChange(() => {
            if (KeyBinds == null) return;
            InputPoller.RemoveKeyBind((uint)KeyBinds[0], "5.0::GamePlay");
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

    public void ClearDisplay(){
        if (display == null) return;
        Indicators.StackableItems.Release(display);
        display = null;
    }
    public void UpdateEItem() { }

    private void ConsumeFood(float _)
    {
        if (AmountRaw == 0) return;
        int delta = GetStaggeredDelta(settings.ConsumptionRate);
        if (delta == 0) return;
        ref PlayerStreamer.Player player = ref PlayerHandler.data;
        if (player.vitality.healthPercent >= 1) return;

        delta = AmountRaw - math.max(AmountRaw - delta, 0);
        player.vitality.Heal(delta / 255.0f * settings.NutritionValue);
        InventoryController.RemoveMaterial(delta);
    }
    
    private void UpdateDisplay(){
        if(display == null) return;
        TMPro.TextMeshProUGUI amount = display.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        amount.text = ((data & 0xFFFF) / (float)0xFF).ToString();
    }
}
}
