using System;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using static CPUMapManager;
using WorldConfig.Generation.Material;

namespace WorldConfig.Generation.Item{
[CreateAssetMenu(menuName = "Generation/Items/Pen")] 
public class PenItemAuthoring : AuthoringTemplate<PenItem> {}

[Serializable]
public struct PenItem : IItem{
    public uint data;
    public static Registry<Authoring> ItemInfo => Config.CURRENT.Generation.Items;
    public static Registry<MaterialData> MatInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;
    public static Registry<Sprite> TextureAtlas => Config.CURRENT.Generation.Textures;
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
        get => "Pen";
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
        return new PenItem{data = data};
    }

    public readonly void OnEnterSecondary(){} 
    public readonly void OnLeaveSecondary(){}
    public readonly void OnEnterPrimary(){} 
    public readonly void OnLeavePrimary(){} 

    private static int[] KeyBinds;
    private static int[] PrevHit;
    public readonly void OnSelect(){
        InputPoller.AddKeyBindChange(() => {
            KeyBinds = InputPoller.KeyBindSaver.Rent(2);
            KeyBinds[0] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Remove Terrain", OnTerrainRemove, InputPoller.ActionBind.Exclusion.ExcludeLayer), "5.0::GamePlay");
            KeyBinds[1] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Place Terrain", OnTerrainAdd, InputPoller.ActionBind.Exclusion.ExcludeLayer), "5.0::GamePlay");
            PrevHit = InputPoller.KeyBindSaver.Rent(4); PrevHit[3] = -1;
        });
    } 
    public readonly void OnDeselect(){
        InputPoller.AddKeyBindChange(() => {
            if(KeyBinds == null) return;
            InputPoller.RemoveKeyBind((uint)KeyBinds[0], "5.0::GamePlay");
            InputPoller.RemoveKeyBind((uint)KeyBinds[1], "5.0::GamePlay");
            InputPoller.KeyBindSaver.Return(KeyBinds);
            InputPoller.KeyBindSaver.Return(PrevHit);
            KeyBinds = null;
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

    private static void OnTerrainRemove(float _){
        int3 hitCoord;
        if(PrevHit[3] < Time.frameCount-1){
            if(!FindNearest(out hitCoord, true)) return;
            PrevHit[0] = hitCoord.x; PrevHit[1] = hitCoord.y; PrevHit[2] = hitCoord.z;  
        } PrevHit[3] = Time.frameCount;
        hitCoord.x = PrevHit[0]; hitCoord.y = PrevHit[1]; hitCoord.z = PrevHit[2];
        MapData cur = SampleMap(hitCoord);
        if(cur.IsNull || !cur.IsSolid) {
            PrevHit[3] = -1; return;
        };
        SetMap(PlayerInteraction.HandleRemoveSolid(cur, 1), hitCoord);
    }
    private static void OnTerrainAdd(float _){
        if(!FindNearest(out int3 hitCoord)) return;
        MapData cur = SampleMap(hitCoord);
        if(cur.IsNull || cur.SolidDensity == 0xFF) {
            PrevHit[3] = -1; return;
        }
        if(!FindNextSolidMat(out int nextSlot)) return;
        SetMap(HandleAddNextSolid(cur, 1, nextSlot), hitCoord);
    }


    private static bool FindNearest(out int3 hitCoord, bool OnlySolid = false){
        hitCoord = int3.zero;
        static float GetDistFromRay(Ray ray, Vector3 point) => Vector3.Cross(ray.direction, point - ray.origin).magnitude;
        if(!PlayerInteraction.RayTestSolid(PlayerHandler.data, out float3 hitPt)) return false;
        Ray ray = new Ray(PlayerHandler.data.position, PlayerHandler.camera.forward);
        int3 hitOrig = (int3)math.floor(hitPt);

        hitCoord = hitOrig;
        for(int i = 0; i < 8; i++){
            int3 hitCorner = hitOrig + new int3(i & 0x1, (i >> 1) & 0x1, (i >> 2) & 0x1);
            MapData cInfo = SampleMap(hitCorner);
            if(!cInfo.IsSolid && OnlySolid) continue;
            if(cInfo.SolidDensity == 0xFF) continue; //Ignore fully filled
            float curDist = GetDistFromRay(ray, (float3)hitCorner);
            float bestDist = GetDistFromRay(ray, (float3)hitCoord);
            if(curDist < bestDist) hitCoord = hitCorner;
        } return true;
    }

    private static bool FindNextSolidMat(out int slot){
        int start = InventoryController.SelectedIndex;
        var settings = Config.CURRENT.GamePlay.Inventory.value;
        for(slot = start + 1; slot != start; slot = (slot + 1) % settings.PrimarySlotCount){
            if(InventoryController.Primary.Info[slot] == null) continue;
            Authoring authoring = ItemInfo.Retrieve(InventoryController.Primary.Info[slot].Index);
            if(!MatInfo.Contains(authoring.MaterialName) || !authoring.IsSolid) continue;
            return true;
        } return false;

    }

    public static MapData HandleAddNextSolid(MapData pointInfo, float brushStrength, int slot){
        brushStrength *= PlayerInteraction.settings.terraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;

        int material = MatInfo.RetrieveIndex(ItemInfo.Retrieve(InventoryController.Primary.Info[slot].Index).MaterialName);
        int solidDensity = pointInfo.SolidDensity;
        if(solidDensity < IsoValue || pointInfo.material == material){
            //If adding solid density, override water
            int deltaDensity = PlayerInteraction.GetStaggeredDelta(solidDensity, brushStrength);
            deltaDensity = InventoryController.RemoveMaterial(deltaDensity, slot);

            solidDensity += deltaDensity;
            pointInfo.density = math.min(pointInfo.density + deltaDensity, 255);
            pointInfo.viscosity = math.min(pointInfo.viscosity + deltaDensity, 255);
            if(solidDensity >= IsoValue) pointInfo.material = material;
        }
        return pointInfo;
    }
}}