using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Core.Player;

namespace Arterra.Configuration.Generation.Item{
[CreateAssetMenu(menuName = "Generation/Items/Boat")] 
public class BoatItemAuthoring : AuthoringTemplate<BoatItem> {}

public class BoatItem : IItem{
    public uint data;
    private static Catalogue<Authoring> ItemInfo => Config.CURRENT.Generation.Items;
    private static Catalogue<TextureContainer> TextureAtlas => Config.CURRENT.Generation.Textures;
    private static Catalogue<Material.MaterialData> MatInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;  
    private BoatItemAuthoring settings => ItemInfo.Retrieve(Index) as BoatItemAuthoring;

    [JsonIgnore]
    public int TexIndex => TextureAtlas.RetrieveIndex(ItemInfo.Retrieve(Index).TextureName);

    [JsonIgnore]
    public int Index{
        get => (int)(data >> 16) & 0x7FFF;
        set => data = (data & 0x0000FFFF) | (((uint)value & 0x7FFF) << 16);
    }
    [JsonIgnore]
    public int AmountRaw{
        get => (int)(data & 0xFFFF);
        set => data = (data & 0x7FFF0000) | ((uint)value & 0xFFFF);
    }
    public IRegister GetRegistry() => Config.CURRENT.Generation.Items;

    public object Clone(){ return new BoatItem{data = data};}
    
    public void Create(int Index, int AmountRaw){
        this.Index = Index;
        this.AmountRaw = AmountRaw;
    }
    public void UpdateEItem(){} 
    public void OnEnter(ItemContext cxt) {
        if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
        InputPoller.AddKeyBindChange(() => {
            InputPoller.AddBinding(new ActionBind(
                "Place Entity",
                _ => PlaceBoat(cxt),
                ActionBind.Exclusion.ExcludeLayer),
                "ITEM:Boat:PL", "5.0::GamePlay"
            );
        }); 
    } 
    public void OnLeave(ItemContext cxt) {
        if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
        InputPoller.AddKeyBindChange(() => {
            InputPoller.RemoveBinding("ITEM:Boat:PL", "5.0::GamePlay");
        });
    } 
    
    private GameObject display;
    public void AttachDisplay(Transform parent){
        if (display != null) {
            display.transform.SetParent(parent, false);
            return;
        }

        display = Indicators.StackableItems.Get();
        display.transform.SetParent(parent, false);
        display.transform.GetComponent<UnityEngine.UI.Image>().sprite = TextureAtlas.Retrieve(ItemInfo.Retrieve(Index).TextureName).self;
    }

    public void ClearDisplay(Transform parent){
        if (display == null) return;
        Indicators.StackableItems.Release(display);
        display = null;
    }

    private void PlaceBoat(ItemContext cxt){
        if (!cxt.TryGetHolder(out PlayerStreamer.Player player)) return;
        if (!PlayerInteraction.RayTestLiquid(out float3 hitPt)) return;
        uint eIndex = (uint)Config.CURRENT.Generation.Entities.RetrieveIndex("Boat");
        EntityManager.CreateEntity(hitPt, eIndex);
        cxt.TryRemove();
    }
}}