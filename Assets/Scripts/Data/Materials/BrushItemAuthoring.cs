using System;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using static CPUMapManager;
using WorldConfig;

namespace WorldConfig.Generation.Item{
[CreateAssetMenu(menuName = "Generation/Items/Brush")] 
public class BrushItemAuthoring : AuthoringTemplate<BrushItem> {}

[Serializable]
public struct BrushItem : IItem{
    public uint data;

    public static Registry<Authoring> ItemInfo => Config.CURRENT.Generation.Items;
    public static Registry<Sprite> TextureAtlas => Config.CURRENT.Generation.Textures;

    public static TerraformController T => PlayerHandler.terrController;
    private static int SmoothBinding = -1;
    private static int TerraceBinding = -1;
    [JsonIgnore]
    public readonly bool IsStackable => false;
    [JsonIgnore]
    public readonly int TexIndex => TextureAtlas.RetrieveIndex(ItemInfo.Retrieve(Index).TextureName);

    [JsonIgnore]
    public int Index{
        readonly get => (int)(data >> 16) & 0x7FFF;
        set => data = (data & 0x8000FFFF) | (((uint)value & 0x7FFF) << 16);
    }
    [JsonIgnore]
    public readonly string Display{
        get => "Brush";
    }
    [JsonIgnore]
    public int AmountRaw{
        readonly get => (int)(data & 0xFFFF);
        set => data = (data & 0xFFFF0000) | ((uint)value & 0xFFFF);
    }
    [JsonIgnore]
    public bool IsDirty{
        readonly get => (data & 0x80000000) != 0;
        set => data = value ? data | 0x80000000 : data & 0x7FFFFFFF;
    }
    public IRegister GetRegistry() => Config.CURRENT.Generation.Items;

    public object Clone()
    {
        return new BrushItem{data = data};
    }

    public readonly void OnEnterSecondary(){} 
    public readonly void OnLeaveSecondary(){}
    public readonly void OnEnterPrimary(){} 
    public readonly void OnLeavePrimary(){} 

    public readonly void OnSelect(){
        InputPoller.AddKeyBindChange(() => {
            SmoothBinding = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Remove Terrain", OnTerrainSmooth, InputPoller.ActionBind.Exclusion.ExcludeLayer), "5.0::GamePlay");
            TerraceBinding = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Place Terrain", OnTerrainTerrace, InputPoller.ActionBind.Exclusion.ExcludeLayer), "5.0::GamePlay");
        });
    } 
    public readonly void OnDeselect(){
        InputPoller.AddKeyBindChange(() => {
            if(SmoothBinding != -1) InputPoller.RemoveKeyBind((uint)SmoothBinding, "5.0::GamePlay");
            if(TerraceBinding != -1) InputPoller.RemoveKeyBind((uint)TerraceBinding, "5.0::GamePlay");
            SmoothBinding = -1; TerraceBinding = -1;
        });
    } 
    public readonly void UpdateEItem(){} 

    /*
    y
    ^      0  5        z
    |      | /        /\
    |      |/         /
    | 4 -- c -- 2    /
    |     /|        /
    |    / |       /
    |   3  1      /
    +----------->x
    */
    readonly static int3[] dP = new int3[6]{
        new (0,1,0),
        new (0,-1,0),
        new (1,0,0),
        new (0,0,-1),
        new (-1,0,0),
        new (0,0,1),
    };

    private static void OnTerrainTerrace(float _){
        if(!T.hasHit) return;
        CPUMapManager.TerrainInteract(T.hitPoint, T.settings.terraformRadius, TerrainTerrace);
    }
    private static void AverageSolid(ref MapData min1, ref MapData max1, int delta){
        //make sure max is max liquid and min is min liquid
        if(min1.SolidDensity >= max1.SolidDensity){
            ref MapData temp = ref min1;
            min1 = ref max1;
            max1 = ref temp;
        }
        
        //Rounds down to the nearest integer
        int amount = math.min((max1.SolidDensity - min1.SolidDensity) >> 1, delta);
        //Ensures that the amount is within the bounds of the solid
        amount = math.min(min1.viscosity + amount, 255) - min1.viscosity;
        amount = max1.viscosity - math.max(max1.viscosity - amount, T.IsoLevel);
        min1.viscosity += amount; max1.viscosity -= amount;
        min1.density = math.min(min1.density + amount, 255);
    }

    private static void AverageGaseous(ref MapData min1, ref MapData max1, int delta){
        //make sure max is max liquid and min is min liquid
        if(min1.SolidDensity >= max1.SolidDensity){
            ref MapData temp = ref min1;
            min1 = ref max1;
            max1 = ref temp;
        }
        
        //Rounds down to the nearest integer
        int amount = math.min((max1.SolidDensity - min1.SolidDensity) >> 1, delta);
        //Ensures that the amount is within the bounds of the solid
        amount = math.min(min1.viscosity + amount, T.IsoLevel-1) - min1.viscosity;
        amount = max1.viscosity - math.max(max1.viscosity - amount, 0);
        min1.viscosity += amount; max1.viscosity -= amount;
        min1.density = math.min(min1.density + amount, 255);
    }
    private static bool TerrainTerrace(int3 GCoord, float brushStrength){
        brushStrength *= T.settings.terraformSpeed * Time.deltaTime;
        int delta = TerraformController.GetStaggeredDelta(brushStrength);
        MapData cur = SampleMap(GCoord); //Current 
        bool isProcessingSolid = cur.IsSolid;
        if(delta == 0) return true;

        for(int i = 0; i < 6; i++){
            MapData neighbor = SampleMap(GCoord + dP[i]);
            if(neighbor.IsSolid != isProcessingSolid) continue;
            if(isProcessingSolid) AverageSolid(ref cur, ref neighbor, delta);
            else AverageGaseous(ref cur, ref neighbor, delta);
            TerrainGeneration.TerrainUpdate.AddUpdate(GCoord + dP[i]);
            SetMap(neighbor, GCoord + dP[i]);
        } SetMap(cur, GCoord);

        return true;
    }

    private static void OnTerrainSmooth(float _){
        if(!T.hasHit) return;
        TerrainInteract(T.hitPoint, T.settings.terraformRadius, SmoothTerrain);
    }
    private static bool SmoothTerrain(int3 GCoord, float brushStrength){
        brushStrength *= T.settings.terraformSpeed * Time.deltaTime;
        int delta = TerraformController.GetStaggeredDelta(brushStrength);
        MapData cur = SampleMap(GCoord); //Current 
        if(!cur.IsSolid) return true;
        if(delta == 0) return true;

        for(int i = 1; i < 6; i++){
            MapData neighbor = SampleMap(GCoord + dP[i]);
            if(neighbor.IsSolid && neighbor.material != cur.material) continue;
            if(neighbor.IsGaseous) neighbor.material = cur.material;

            AverageSolid(ref cur, ref neighbor, delta);
            TerrainGeneration.TerrainUpdate.AddUpdate(GCoord + dP[i]);
            SetMap(neighbor, GCoord + dP[i]);
        } SetMap(cur, GCoord);

        return true;
    }
}}