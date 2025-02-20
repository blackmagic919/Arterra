using System;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using static CPUMapManager;
using static PlayerInteraction;

namespace WorldConfig.Generation.Item{
[CreateAssetMenu(menuName = "Generation/Items/Bucket")] 
public class BucketItemAuthoring : AuthoringTemplate<BucketItem> {}

public class BucketItem : IItem{
    public uint data;
    public IItem content;
    public static Registry<Authoring> ItemInfo => Config.CURRENT.Generation.Items;
    public static Registry<Sprite> TextureAtlas => Config.CURRENT.Generation.Textures;
    public static Registry<Material.MaterialData> MatInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;  

    [JsonIgnore]
    public bool IsStackable => false;
    [JsonIgnore]
    public int TexIndex => content != null ? content.TexIndex : 
    TextureAtlas.RetrieveIndex(ItemInfo.Retrieve(Index).TextureName);

    [JsonIgnore]
    public int Index{
        get => (int)(data >> 16) & 0x7FFF;
        set => data = (data & 0x8000FFFF) | (((uint)value & 0x7FFF) << 16);
    }
    [JsonIgnore]
    public string Display{ get => content == null ? "Empty" : content.Display; }
    [JsonIgnore]
    public int AmountRaw{
        get => (int)(data & 0xFFFF);
        set => data = (data & 0xFFFF0000) | ((uint)value & 0xFFFF);
    }
    [JsonIgnore]
    public bool IsDirty{
        get => (data & 0x80000000) != 0;
        set => data = value ? data | 0x80000000 : data & 0x7FFFFFFF;
    }
    public IRegister GetRegistry() => Config.CURRENT.Generation.Items;

    public object Clone(){ return new BucketItem{
        data = data, content = content == null ? 
        null : (IItem)content.Clone()
    };}

    public void OnEnterSecondary(){} 
    public void OnLeaveSecondary(){}
    public void OnEnterPrimary(){} 
    public void OnLeavePrimary(){} 

    private static int PlaceBinding = -1; 
    private static int RemoveBinding = -1; 
    public void OnSelect(){
        InputPoller.AddKeyBindChange(() => {
            PlaceBinding = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Place Terrain", PlaceLiquid, InputPoller.ActionBind.Exclusion.ExcludeLayer), "5.0::GamePlay");
            RemoveBinding = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Remove Terrain", RemoveLiquid, InputPoller.ActionBind.Exclusion.ExcludeLayer), "5.0::GamePlay");
        }); 
    } 
    public void OnDeselect(){
        InputPoller.AddKeyBindChange(() => {
            if(PlaceBinding != -1) InputPoller.RemoveKeyBind((uint)PlaceBinding, "5.0::GamePlay");
            if(RemoveBinding != -1) InputPoller.RemoveKeyBind((uint)RemoveBinding, "5.0::GamePlay");
            PlaceBinding = -1; RemoveBinding = -1;
        });
    } 
    public void UpdateEItem(){} 

    private void PlaceLiquid(float _){
        var matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        
        if(content == null || !RayTestLiquid(PlayerHandler.data, out float3 hitPt)) return;
        Authoring mat = ItemInfo.Retrieve(content.Index);
        if(mat.MaterialName == null || !matInfo.Contains(mat.MaterialName)) return;
        CPUMapManager.Terraform(hitPt, settings.terraformRadius, AddFromBucket);
        if(content.AmountRaw == 0) content = null;
        IsDirty = true;
    }

    private MapData AddFromBucket(MapData pointInfo, float brushStrength){
        float IsoLevel = Mathf.RoundToInt(Config.CURRENT.Quality.Terrain.value.IsoLevel * 255);
        brushStrength *= settings.terraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;
        Authoring cSettings = ItemInfo.Retrieve(content.Index);
        if(!cSettings.IsLiquid) return pointInfo;


        int selected = MatInfo.RetrieveIndex(cSettings.MaterialName);
        int liquidDensity = pointInfo.LiquidDensity;
        if(liquidDensity < IsoLevel || pointInfo.material == selected){
            //If adding liquid density, only change if not solid
            int deltaDensity = GetStaggeredDelta(pointInfo.density, brushStrength);
            deltaDensity = content.AmountRaw - math.max(content.AmountRaw - deltaDensity, 0);
            content.AmountRaw -= deltaDensity;

            pointInfo.density += deltaDensity;
            liquidDensity += deltaDensity;
            if(liquidDensity >= IsoLevel) pointInfo.material = selected;
        }
        return pointInfo;
    }

    private void RemoveLiquid(float _){
        if(!!RayTestLiquid(PlayerHandler.data, out float3 hitPt)) return;
        CPUMapManager.Terraform(hitPt, settings.terraformRadius, RemoveToBucket);
        IsDirty = true; 
    }

    private MapData RemoveToBucket(MapData pointInfo, float brushStrength){
        float IsoLevel = Mathf.RoundToInt(Config.CURRENT.Quality.Terrain.value.IsoLevel * 255);
        brushStrength *= settings.terraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;

        int selMat = -1;
        if(content != null){
            Authoring cSettings = ItemInfo.Retrieve(content.Index);
            if(!cSettings.IsLiquid) content = null; //has to be liquid
            else selMat = MatInfo.RetrieveIndex(cSettings.MaterialName);
        }
        
        int liquidDensity = pointInfo.LiquidDensity;
        if (liquidDensity >= IsoLevel && (selMat == -1 || pointInfo.material == selMat)){
            int deltaDensity = math.min(GetStaggeredDelta(liquidDensity, -brushStrength), 0xFFFF);

            if(content == null){
                WorldConfig.Generation.Material.MaterialData material = MatInfo.Retrieve(pointInfo.material);
                string liquidItem = material.RetrieveKey(material.LiquidItem);
                if(!ItemInfo.Contains(liquidItem)) return pointInfo;
                int itemIndex = ItemInfo.RetrieveIndex(liquidItem);
                content = ItemInfo.Retrieve(itemIndex).Item;
                content.Index = itemIndex;
                content.AmountRaw = deltaDensity;
            } else {
                deltaDensity = math.min(content.AmountRaw + deltaDensity, 0xFFFF) - content.AmountRaw;
                content.AmountRaw += deltaDensity;
            }

            pointInfo.density -= deltaDensity;
        }
        return pointInfo;
    }
    
}}