using System;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using static CPUMapManager;
namespace WorldConfig.Generation.Item{
[CreateAssetMenu(menuName = "Generation/Items/Bucket")] 
public class BucketItemAuthoring : AuthoringTemplate<BucketItem> {
    /// <summary> The radius, in grid space, of the spherical region around the user's
    /// cursor that will be modified when the user terraforms the terrain. </summary>
    public int TerraformRadius = 1;
    /// <summary> The speed at which the user can terraform the terrain. As terraforming is a 
    /// continuous process, the speed is measured in terms of change in density per frame. </summary>
    public float TerraformSpeed = 50;
}

public class BucketItem : IItem{
    public uint data;
    public IItem content;
    public static Registry<Authoring> ItemInfo => Config.CURRENT.Generation.Items;
    public static Registry<TextureContainer> TextureAtlas => Config.CURRENT.Generation.Textures;
    public static Registry<Material.MaterialData> MatInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;  
    private BucketItemAuthoring settings => ItemInfo.Retrieve(Index) as BucketItemAuthoring;

    [JsonIgnore]
    public bool IsStackable => false;
    [JsonIgnore]
    public int TexIndex => content != null ? content.TexIndex : 
    TextureAtlas.RetrieveIndex(ItemInfo.Retrieve(Index).TextureName);

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

    public object Clone(){ return new BucketItem{
        data = data, content = content == null ? 
        null : (IItem)content.Clone()
    };}
    
    public void Create(int Index, int AmountRaw){
        this.Index = Index;
        this.AmountRaw = AmountRaw;
    }
    
    public void OnEnterSecondary() { } 
    public void OnLeaveSecondary(){}
    public void OnEnterPrimary(){} 
    public void OnLeavePrimary(){} 

    private int[] KeyBinds;
    public void OnSelect(){
        InputPoller.AddKeyBindChange(() => {
            KeyBinds = new int[2];
            KeyBinds[0] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Place Terrain", PlaceLiquid, InputPoller.ActionBind.Exclusion.ExcludeLayer), "5.0::GamePlay");
            KeyBinds[1] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Remove Terrain", RemoveLiquid, InputPoller.ActionBind.Exclusion.ExcludeLayer), "5.0::GamePlay");
        }); 
    } 
    public void OnDeselect(){
        InputPoller.AddKeyBindChange(() => {
            if (KeyBinds == null) return;
            InputPoller.RemoveKeyBind((uint)KeyBinds[0], "5.0::GamePlay");
            InputPoller.RemoveKeyBind((uint)KeyBinds[1], "5.0::GamePlay");
        });
    } 
    public void UpdateEItem(){} 
    
    private GameObject display;
    public void AttachDisplay(Transform parent) {
        if (display != null) {
            display.transform.SetParent(parent, false);
            return;
        }

        display = Indicators.HolderItems.Get();
        display.transform.SetParent(parent, false);
        display.transform.GetComponent<UnityEngine.UI.Image>().sprite = TextureAtlas.Retrieve(ItemInfo.Retrieve(Index).TextureName).self;
        AttachChildDisplay();
    }

    public void ClearDisplay(){
        if (display == null) return;
        content?.ClearDisplay();
        Indicators.HolderItems.Release(display);
        display = null;
    }

    private void PlaceLiquid(float _){
        var matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        
        if(content == null || !PlayerInteraction.RayTestLiquid(PlayerHandler.data, out float3 hitPt)) return;
        Authoring mat = ItemInfo.Retrieve(content.Index);
        if(mat.MaterialName == null || !matInfo.Contains(mat.MaterialName)) return;
        CPUMapManager.Terraform(hitPt, settings.TerraformRadius, AddFromBucket);
        if(content.AmountRaw != 0) return;
        content.ClearDisplay();
        content = null;
    }

    private MapData AddFromBucket(MapData pointInfo, float brushStrength){
        float IsoLevel = Mathf.RoundToInt(Config.CURRENT.Quality.Terrain.value.IsoLevel * 255);
        brushStrength *= settings.TerraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;
        Authoring cSettings = ItemInfo.Retrieve(content.Index);
        if(!cSettings.IsLiquid) return pointInfo;


        int selected = MatInfo.RetrieveIndex(cSettings.MaterialName);
        int liquidDensity = pointInfo.LiquidDensity;
        if(liquidDensity < IsoLevel || pointInfo.material == selected){
            //If adding liquid density, only change if not solid
            int deltaDensity = PlayerInteraction.GetStaggeredDelta(pointInfo.density, brushStrength);
            deltaDensity = content.AmountRaw - math.max(content.AmountRaw - deltaDensity, 0);
            content.AmountRaw -= deltaDensity;

            pointInfo.density += deltaDensity;
            liquidDensity += deltaDensity;
            if(liquidDensity >= IsoLevel) pointInfo.material = selected;
        }
        return pointInfo;
    }

    private void RemoveLiquid(float _){
        if(!PlayerInteraction.RayTestLiquid(PlayerHandler.data, out float3 hitPt)) return;
        CPUMapManager.Terraform(hitPt, settings.TerraformRadius, RemoveToBucket);
    }

    private MapData RemoveToBucket(MapData pointInfo, float brushStrength){
        float IsoLevel = Mathf.RoundToInt(Config.CURRENT.Quality.Terrain.value.IsoLevel * 255);
        brushStrength *= settings.TerraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;

        int selMat = -1;
        if(content != null){
            Authoring cSettings = ItemInfo.Retrieve(content.Index);
            if(!cSettings.IsLiquid) content = null; //has to be liquid
            else selMat = MatInfo.RetrieveIndex(cSettings.MaterialName);
        }
        
        int liquidDensity = pointInfo.LiquidDensity;
        int deltaDensity = math.min(PlayerInteraction.GetStaggeredDelta(liquidDensity, -brushStrength), 0xFFFF);
        if (liquidDensity >= IsoLevel && (selMat == -1 || pointInfo.material == selMat)){
            if(content == null){
                WorldConfig.Generation.Material.MaterialData material = MatInfo.Retrieve(pointInfo.material);
                string liquidItem = material.RetrieveKey(material.LiquidItem);
                if(!ItemInfo.Contains(liquidItem)) return pointInfo;
                int itemIndex = ItemInfo.RetrieveIndex(liquidItem);
                content = ItemInfo.Retrieve(itemIndex).Item;
                content.Create(itemIndex, deltaDensity);
                AttachChildDisplay();
            } else {
                deltaDensity = math.min(content.AmountRaw + deltaDensity, 0xFFFF) - content.AmountRaw;
                content.AmountRaw += deltaDensity;
            }
        } pointInfo.density -= deltaDensity;
        return pointInfo;
    }
    
    private void AttachChildDisplay(){
        if(display == null) return;
        if(content == null) return;
        Transform child = display.transform.Find("Item");
        content?.AttachDisplay(child);
    }
    
}}