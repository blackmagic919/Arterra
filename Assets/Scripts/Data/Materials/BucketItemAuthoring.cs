using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Mathematics;
using Unity.Services.Analytics;
using UnityEngine;
using static CPUDensityManager;

[CreateAssetMenu(menuName = "Generation/Items/Bucket")] 
public class BucketItemAuthoring : ItemAuthoringTemplate<BucketItem> {}

public class BucketItem : IItem{
    public uint data;
    public IItem content;
    public static Registry<ItemAuthoring> Register => WorldOptions.CURRENT.Generation.Items;
    public static Registry<MaterialData> MatInfo => WorldOptions.CURRENT.Generation.Materials.value.MaterialDictionary;  
    public static TerraformController T => PlayerHandler.terrController;
    private static int PlaceBinding = -1; 
    private static int RemoveBinding = -1; 

    [JsonIgnore]
    public bool IsStackable => false;
    [JsonIgnore]
    public int TexIndex => content == null ? Index : content.TexIndex;

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

    public void Serialize(Func<string, int> lookup){
        string bucketName = Register.RetrieveName(Index);
        string contentName = content == null ? "EMPTY" : Register.RetrieveName(content.Index);
        Index = lookup($"{bucketName}::{contentName}");
    }

    public void Deserialize(Func<int, string> lookup){ 
        string[] portions = lookup(Index).Split("::");
        Index = Register.RetrieveIndex(portions[0]);
        if(content != null) content.Index = Register.RetrieveIndex(portions[1]);
    }

    public object Clone(){ return new BucketItem{
        data = data, content = content == null ? 
        null : (IItem)content.Clone()
    };}

    public void OnEnterSecondary(){} 
    public void OnLeaveSecondary(){}
    public void OnEnterPrimary(){} 
    public void OnLeavePrimary(){} 
    public void OnSelect(){
        InputPoller.AddStackPoll(new InputPoller.ActionBind("Bucket", (float _) => T.CursorPlace = T.RayTestLiquid), "CursorPlacement");
        InputPoller.AddKeyBindChange(() => {
            PlaceBinding = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Place Terrain", PlaceLiquid, InputPoller.ActionBind.Exclusion.ExcludeLayer), "5.0::GamePlay");
            RemoveBinding = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Remove Terrain", RemoveLiquid, InputPoller.ActionBind.Exclusion.ExcludeLayer), "5.0::GamePlay");
        }); 
    } 
    public void OnDeselect(){
        InputPoller.RemoveStackPoll("Bucket", "CursorPlacement");
        InputPoller.AddKeyBindChange(() => {
            if(PlaceBinding != -1) InputPoller.RemoveKeyBind((uint)PlaceBinding, "5.0::GamePlay");
            if(RemoveBinding != -1) InputPoller.RemoveKeyBind((uint)RemoveBinding, "5.0::GamePlay");
            PlaceBinding = -1; RemoveBinding = -1;
        });
    } 
    public void UpdateEItem(){} 

    private void PlaceLiquid(float _){
        var matInfo = WorldOptions.CURRENT.Generation.Materials.value.MaterialDictionary;
        
        if(!T.hasHit || content == null) return;
        ItemAuthoring mat = Register.Retrieve(content.Index);
        if(mat.MaterialName == null || !matInfo.Contains(mat.MaterialName)) return;
        CPUDensityManager.Terraform(T.hitPoint, T.settings.terraformRadius, AddFromBucket);
        if(content.AmountRaw == 0) content = null;
        IsDirty = true;
    }

    private MapData AddFromBucket(MapData pointInfo, float brushStrength){
        float IsoLevel = Mathf.RoundToInt(WorldOptions.CURRENT.Quality.Rendering.value.IsoLevel * 255);
        brushStrength *= T.settings.terraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;
        ItemAuthoring cSettings = Register.Retrieve(content.Index);
        if(!cSettings.IsLiquid) return pointInfo;


        int selected = MatInfo.RetrieveIndex(cSettings.MaterialName);
        int liquidDensity = pointInfo.LiquidDensity;
        if(liquidDensity < IsoLevel || pointInfo.material == selected){
            //If adding liquid density, only change if not solid
            int deltaDensity = TerraformController.GetStaggeredDelta(pointInfo.density, brushStrength);
            deltaDensity = content.AmountRaw - math.max(content.AmountRaw - deltaDensity, 0);
            content.AmountRaw -= deltaDensity;

            pointInfo.density += deltaDensity;
            liquidDensity += deltaDensity;
            if(liquidDensity >= IsoLevel) pointInfo.material = selected;
        }
        return pointInfo;
    }

    private void RemoveLiquid(float _){
        if(!T.hasHit) return;
        CPUDensityManager.Terraform(T.hitPoint, T.settings.terraformRadius, RemoveToBucket);
        IsDirty = true; 
    }

    private MapData RemoveToBucket(MapData pointInfo, float brushStrength){
        float IsoLevel = Mathf.RoundToInt(WorldOptions.CURRENT.Quality.Rendering.value.IsoLevel * 255);
        brushStrength *= T.settings.terraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;

        int selMat = -1;
        if(content != null){
            ItemAuthoring cSettings = Register.Retrieve(content.Index);
            if(!cSettings.IsLiquid) content = null; //has to be liquid
            else selMat = MatInfo.RetrieveIndex(cSettings.MaterialName);
        }
        
        int liquidDensity = pointInfo.LiquidDensity;
        if (liquidDensity >= IsoLevel && (selMat == -1 || pointInfo.material == selMat)){
            int deltaDensity = math.min(TerraformController.GetStaggeredDelta(liquidDensity, -brushStrength), 0xFFFF);

            if(content == null){
                string liquidItem = MatInfo.Retrieve(pointInfo.material).LiquidItem;
                if(!Register.Contains(liquidItem)) return pointInfo;
                int itemIndex = Register.RetrieveIndex(liquidItem);
                content = Register.Retrieve(itemIndex).Item;
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
    
}