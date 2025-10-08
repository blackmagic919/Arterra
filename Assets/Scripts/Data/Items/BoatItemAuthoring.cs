using System;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using MapStorage;

namespace WorldConfig.Generation.Item{
[CreateAssetMenu(menuName = "Generation/Items/Boat")] 
public class BoatItemAuthoring : AuthoringTemplate<BoatItem> {}

public class BoatItem : IItem{
    public uint data;
    private static Catalogue<Authoring> ItemInfo => Config.CURRENT.Generation.Items;
    private static Catalogue<TextureContainer> TextureAtlas => Config.CURRENT.Generation.Textures;
    private static Catalogue<Material.MaterialData> MatInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;  
    private BoatItemAuthoring settings => ItemInfo.Retrieve(Index) as BoatItemAuthoring;

    [JsonIgnore]
    public bool IsStackable => false;
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
    private int[] KeyBinds;
    public void OnEnter(ItemContext cxt) {
        if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
        InputPoller.AddKeyBindChange(() => {
            KeyBinds = new int[1];
            KeyBinds[0] = (int)InputPoller.AddBinding(new InputPoller.ActionBind(
                "Place",
                _ => PlaceBoat(cxt),
                InputPoller.ActionBind.Exclusion.ExcludeLayer),
                "5.0::GamePlay"
            );
        }); 
    } 
    public void OnLeave(ItemContext cxt) {
        if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
        InputPoller.AddKeyBindChange(() => {
            if (KeyBinds == null) return;
            InputPoller.RemoveKeyBind((uint)KeyBinds[0], "5.0::GamePlay");
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
        if (!PlayerInteraction.RayTestLiquid(player, out float3 hitPt)) return;
        uint eIndex = (uint)Config.CURRENT.Generation.Entities.RetrieveIndex("Boat");
        EntityManager.CreateEntity(hitPt, eIndex);
        InventoryController.Primary.RemoveEntry(InventoryController.SelectedIndex);
    }
}}