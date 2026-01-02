using Utils;
using UnityEngine;
using Newtonsoft.Json;
using Unity.Mathematics;
using Arterra.Core.Player;

namespace Arterra.Configuration.Generation.Item{
[CreateAssetMenu(menuName = "Generation/Items/Consumable")] 
public class ConsumableItemAuthoring : AuthoringTemplate<ConsumbaleItem> {
    public float ConsumptionRate;
    public float NutritionValue;
}

[System.Serializable]
public class ConsumbaleItem : IItem{
    public uint data;
    private static Catalogue<Authoring> ItemInfo => Config.CURRENT.Generation.Items;
    private static Catalogue<TextureContainer> TextureAtlas => Config.CURRENT.Generation.Textures;
    [JsonIgnore]
    public int StackLimit => 0xFFFF;
    [JsonIgnore]
    public int UnitSize => 0xFF;
    [JsonIgnore]
    public int TexIndex => TextureAtlas.RetrieveIndex(ItemInfo.Retrieve(Index).TextureName);
    private ConsumableItemAuthoring settings => ItemInfo.Retrieve(Index) as ConsumableItemAuthoring;

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
    public void UpdateEItem() { }
    public void OnEnter(ItemContext cxt) {
        if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
        InputPoller.AddKeyBindChange(() => {
            InputPoller.AddBinding(new ActionBind(
                "Consume",
                _ => ConsumeFood(cxt),
                ActionBind.Exclusion.ExcludeLayer),
                "ITEM::Consumable:EAT", "5.0::GamePlay"
            );
        });
    } 
    public void OnLeave(ItemContext cxt) {
        if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
        InputPoller.AddKeyBindChange(() => {
            InputPoller.RemoveBinding("ITEM::Consumable:EAT", "5.0::GamePlay");
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
        amount.text = ((data & 0xFFFF) / (float)UnitSize).ToString();
    }

    public void ClearDisplay(Transform parent){
        if (display == null) return;
        Indicators.StackableItems.Release(display);
        display = null;
    }

    private void ConsumeFood(ItemContext cxt)
    {
        if (AmountRaw == 0) return;
        int delta = CustomUtility.GetStaggeredDelta(settings.ConsumptionRate);
        if (delta == 0) return;
        if (!cxt.TryGetHolder(out PlayerStreamer.Player player)) return;
        if (player.vitality.healthPercent >= 1) return;

        delta = AmountRaw - math.max(AmountRaw - delta, 0);
        player.eventCtrl.RaiseEvent(Core.Events.GameEvent.Item_ConsumeFood, player, this, ref delta);

        player.vitality.Heal(delta * settings.NutritionValue * UnitSize);
        AmountRaw -= delta;
        if(AmountRaw == 0) cxt.TryRemove();//
    }
    
    private void UpdateDisplay(){
        if(display == null) return;
        TMPro.TextMeshProUGUI amount = display.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        amount.text = ((data & 0xFFFF) / (float)UnitSize).ToString();
    }
}
}
