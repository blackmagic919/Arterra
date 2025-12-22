using Utils;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Core.Storage;
using Arterra.Core.Player;

namespace Arterra.Config.Generation.Item{
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
    private static Catalogue<Authoring> ItemInfo => Config.CURRENT.Generation.Items;
    private static Catalogue<TextureContainer> TextureAtlas => Config.CURRENT.Generation.Textures;
    private static Catalogue<Material.MaterialData> MatInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;  
    private BucketItemAuthoring settings => ItemInfo.Retrieve(Index) as BucketItemAuthoring;

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
    public void UpdateEItem(){} 
    public void OnEnter(ItemContext cxt) {
        if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
        InputPoller.AddKeyBindChange(() => {
            InputPoller.AddBinding(new ActionBind("Place", _ => PlaceLiquid(cxt), ActionBind.Exclusion.ExcludeLayer), "ITEM:Bucket:PL", "5.0::GamePlay");
            InputPoller.AddBinding(new ActionBind("Remove", _ => RemoveLiquid(cxt), ActionBind.Exclusion.ExcludeLayer), "ITEM:Bucket:RM", "5.0::GamePlay");
        }); 
    } 
    public void OnLeave(ItemContext cxt) {
        if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
        InputPoller.AddKeyBindChange(() => {
            InputPoller.RemoveBinding("ITEM:Bucket:PL", "5.0::GamePlay");
            InputPoller.RemoveBinding("ITEM:Bucket:RM", "5.0::GamePlay");
        });
    } 
    
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

    public void ClearDisplay(Transform parent){
        if (display == null) return;
        content?.ClearDisplay(display.transform);
        Indicators.HolderItems.Release(display);
        display = null;
    }

    private void PlaceLiquid(ItemContext cxt){
        var matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        
        if (!cxt.TryGetHolder(out PlayerStreamer.Player player)) return;
        if (content == null || !PlayerInteraction.RayTestLiquid(out float3 hitPt)) return;
        Authoring authoring = ItemInfo.Retrieve(content.Index);
        if (authoring is not PlaceableItem mat) return;
        if (mat.MaterialName == null || !matInfo.Contains(mat.MaterialName)) return;
        CPUMapManager.Terraform(hitPt, settings.TerraformRadius, AddFromBucket, PlayerInteraction.CallOnMapPlacing);
        if(content.AmountRaw != 0) return;
        content.ClearDisplay(display.transform);
        content = null;
    }

    private bool AddFromBucket(int3 GCoord, float brushStrength){
        float IsoLevel = Mathf.RoundToInt(Config.CURRENT.Quality.Terrain.value.IsoLevel * 255);
        brushStrength *= settings.TerraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return false;
        Authoring authoring = ItemInfo.Retrieve(content.Index);
        if (authoring is not PlaceableItem mat) return false;
        if (!mat.IsLiquid) return false;

        MapData pointInfo = CPUMapManager.SampleMap(GCoord);
        int selected = MatInfo.RetrieveIndex(mat.MaterialName);
        int liquidDensity = pointInfo.LiquidDensity;
        if(liquidDensity >= IsoLevel && pointInfo.material != selected)
            return false;

        MapData delta = pointInfo;
        delta.viscosity = 0;
        delta.density = CustomUtility.GetStaggeredDelta(pointInfo.density, brushStrength);
        delta.density = content.AmountRaw - math.max(content.AmountRaw - delta.density, 0);
        content.AmountRaw -= delta.density;

        pointInfo.density += delta.density;
        liquidDensity += delta.density;
        if(liquidDensity >= IsoLevel) pointInfo.material = selected;

        MatInfo.Retrieve(pointInfo.material).OnPlaced(GCoord, delta);
        CPUMapManager.SetMap(pointInfo, GCoord);
        return true;
    }

    private void RemoveLiquid(ItemContext cxt){
        if (!cxt.TryGetHolder(out PlayerStreamer.Player player)) return;
        if (!PlayerInteraction.RayTestLiquid(out float3 hitPt)) return;
        CPUMapManager.Terraform(hitPt, settings.TerraformRadius, RemoveToBucket, PlayerInteraction.CallOnMapRemoving);
    }

    private bool RemoveToBucket(int3 GCoord, float brushStrength){
        brushStrength *= settings.TerraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return false;

        MapData pointInfo = CPUMapManager.SampleMap(GCoord);
        MapData delta = pointInfo;
        delta.viscosity = 0;
        int liquidDensity = pointInfo.LiquidDensity;
        delta.density = math.min(CustomUtility.GetStaggeredDelta(liquidDensity, -brushStrength), MapData.MaxDensity);
        Material.MaterialData material = MatInfo.Retrieve(pointInfo.material);
        IItem newItem = material.OnRemoved(GCoord, delta);
        if (liquidDensity >= CPUMapManager.IsoValue){
            if (content == null) {
                content = newItem;
                if (content == null) {
                    return false;
                } AttachChildDisplay();
            } else {
                if (content.AmountRaw >= content.StackLimit || newItem == null || newItem.Index != content.Index) {
                    material.OnPlaced(GCoord, delta); //Undo our action
                    return false;
                } content.AmountRaw = math.min(content.AmountRaw + newItem.AmountRaw, content.StackLimit);
            }
        }

        pointInfo.density -= delta.density;
        CPUMapManager.SetMap(pointInfo, GCoord);
        return true;
    }
    
    private void AttachChildDisplay(){
        if(display == null) return;
        if(content == null) return;
        Transform child = display.transform.Find("Item");
        content?.AttachDisplay(child);
    }
    
}}