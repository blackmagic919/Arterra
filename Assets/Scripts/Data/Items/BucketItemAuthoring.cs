using System;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using MapStorage;

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
    private static Registry<Authoring> ItemInfo => Config.CURRENT.Generation.Items;
    private static Registry<TextureContainer> TextureAtlas => Config.CURRENT.Generation.Textures;
    private static Registry<Material.MaterialData> MatInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;  
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
            KeyBinds[0] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Place", PlaceLiquid, InputPoller.ActionBind.Exclusion.ExcludeLayer), "5.0::GamePlay");
            KeyBinds[1] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Remove", RemoveLiquid, InputPoller.ActionBind.Exclusion.ExcludeLayer), "5.0::GamePlay");
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
        CPUMapManager.Terraform(hitPt, settings.TerraformRadius, AddFromBucket, PlayerInteraction.CallOnMapPlacing);
        if(content.AmountRaw != 0) return;
        content.ClearDisplay();
        content = null;
    }

    private bool AddFromBucket(int3 GCoord, float brushStrength){
        float IsoLevel = Mathf.RoundToInt(Config.CURRENT.Quality.Terrain.value.IsoLevel * 255);
        brushStrength *= settings.TerraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return false;
        Authoring cSettings = ItemInfo.Retrieve(content.Index);
        if(!cSettings.IsLiquid) return false;

        MapData pointInfo = CPUMapManager.SampleMap(GCoord);
        int selected = MatInfo.RetrieveIndex(cSettings.MaterialName);
        int liquidDensity = pointInfo.LiquidDensity;
        if(liquidDensity >= IsoLevel && pointInfo.material != selected)
            return false;

        MapData delta = pointInfo;
        delta.viscosity = 0;
        delta.density = PlayerInteraction.GetStaggeredDelta(pointInfo.density, brushStrength);
        delta.density = content.AmountRaw - math.max(content.AmountRaw - delta.density, 0);
        content.AmountRaw -= delta.density;

        pointInfo.density += delta.density;
        liquidDensity += delta.density;
        if(liquidDensity >= IsoLevel) pointInfo.material = selected;

        CPUMapManager.SetMap(pointInfo, GCoord);
        MatInfo.Retrieve(pointInfo.material).OnPlaced(GCoord, delta);
        return true;
    }

    private void RemoveLiquid(float _){
        if(!PlayerInteraction.RayTestLiquid(PlayerHandler.data, out float3 hitPt)) return;
        CPUMapManager.Terraform(hitPt, settings.TerraformRadius, RemoveToBucket, PlayerInteraction.CallOnMapRemoving);
    }

    private bool RemoveToBucket(int3 GCoord, float brushStrength){
        brushStrength *= settings.TerraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return false;

        MapData pointInfo = CPUMapManager.SampleMap(GCoord);
        MapData delta = pointInfo;
        int liquidDensity = pointInfo.LiquidDensity;
        delta.density = math.min(PlayerInteraction.GetStaggeredDelta(liquidDensity, -brushStrength), 0xFFFF);
        Material.MaterialData material = MatInfo.Retrieve(pointInfo.material);
        delta.viscosity = 0;
        if (liquidDensity >= CPUMapManager.IsoValue){
            if (content == null) {
                content = material.AcquireItem(delta);
                if (content == null) return false;
                AttachChildDisplay();
            } else {
                if (content.AmountRaw == 0xFFFF) return false;
                IItem newMat = material.AcquireItem(delta);
                if (newMat == null) return false;
                if (newMat.Index != content.Index) return false;
                content.AmountRaw = math.min(content.AmountRaw + newMat.AmountRaw, 0xFFFF);
            }
        }

        pointInfo.density -= delta.density;
        CPUMapManager.SetMap(pointInfo, GCoord);
        material.OnRemoved(GCoord, delta);
        return true;
    }
    
    private void AttachChildDisplay(){
        if(display == null) return;
        if(content == null) return;
        Transform child = display.transform.Find("Item");
        content?.AttachDisplay(child);
    }
    
}}