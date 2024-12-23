using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Burst;
using System;
using Newtonsoft.Json;
using static InventoryController;
using System.Runtime.Serialization;
using Unity.Services.Analytics;

[CreateAssetMenu(menuName = "Entity/Item")]
public class EItem : EntityAuthoring
{
    [UISetting(Ignore = true)][JsonIgnore]
    public Option<GameObject> _Controller;
    [UISetting(Ignore = true)][JsonIgnore]
    public Option<EItemEntity> _Entity;
    public Option<EItemSetting> _Setting;
    public Option<List<ProfileE> > _Profile;
    public Option<Entity.Info.ProfileInfo> _Info;
    
    [JsonIgnore]
    public override EntityController Controller { get { return _Controller.value.GetComponent<EntityController>(); } }
    [JsonIgnore]
    public override IEntity Entity { get => _Entity.value; set => _Entity.value = (EItemEntity)value; }
    [JsonIgnore]
    public override IEntitySetting Setting { get => _Setting.value; set => _Setting.value = (EItemSetting)value; }
    [JsonIgnore]
    public override Entity.Info.ProfileInfo Info { get => _Info.value; set => _Info.value = value; }
    [JsonIgnore]
    public override ProfileE[] Profile { get => _Profile.value.ToArray(); set => _Profile.value = value.ToList(); }
    [Serializable]
    public struct EItemSetting : IEntitySetting{
        public float GroundStickDist;
        public float StickFriction;
        public int2 SpriteSampleSize;
        public float AlphaClip;
        public float ExtrudeHeight;
        public TerrainColliderJob.Settings collider;
    }

    [BurstCompile]
    //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
    //**If you release here the controller might still be accessing it
    public struct EItemEntity : IEntity
    {  
        //This is the real-time position streamed by the controller
        public TerrainColliderJob tCollider;
        public Unity.Mathematics.Random random;
        public int3 GCoord; 
        public bool isPickedUp;
        public ItemInfo item;

        public static readonly SharedStatic<EItemSetting> _settings = SharedStatic<EItemSetting>.GetOrCreate<EItemEntity, EItemSetting>();
        public static EItemSetting settings{get => _settings.Data; set => _settings.Data = value;}
        
        public unsafe readonly void Preset(IEntitySetting setting){
            settings = (EItemSetting)setting;
        }
        public unsafe readonly void Unset(){ 
            //not applicable
        }

        //This function shouldn't be used
        public unsafe IntPtr Initialize(ref Entity entity, int3 GCoord)
        {
            entity._Update = BurstCompiler.CompileFunctionPointer<IEntity.UpdateDelegate>(Update);
            entity._Disable = BurstCompiler.CompileFunctionPointer<IEntity.DisableDelegate>(Disable);
            entity.obj = Marshal.AllocHGlobal(Marshal.SizeOf(this));

            tCollider.transform.position = GCoord;
            isPickedUp = false;

            Marshal.StructureToPtr(this, entity.obj, false);
            IntPtr nEntity = Marshal.AllocHGlobal(Marshal.SizeOf(entity));
            Marshal.StructureToPtr(entity, nEntity, false);
            return nEntity;
        }

        public unsafe EItemEntity(TerrainColliderJob.Transform origin, ISlot item){
            tCollider.transform = origin;
            tCollider.velocity = 0;
            GCoord = (int3)origin.position;
            
            isPickedUp = false;
            this.random = default;
            this.item = new ItemInfo(item);

        }

        public unsafe IntPtr Deserialize(ref Entity entity, out int3 GCoord)
        {
            entity._Update = BurstCompiler.CompileFunctionPointer<IEntity.UpdateDelegate>(Update);
            entity._Disable = BurstCompiler.CompileFunctionPointer<IEntity.DisableDelegate>(Disable);
            entity.obj = Marshal.AllocHGlobal(Marshal.SizeOf(this));
            
            GCoord = this.GCoord;

            Marshal.StructureToPtr(this, entity.obj, false);
            IntPtr nEntity = Marshal.AllocHGlobal(Marshal.SizeOf(entity));
            Marshal.StructureToPtr(entity, nEntity, false);
            return nEntity;
        }


        [BurstCompile]
        public unsafe static void Update(Entity* entity, EntityJob.Context* context)
        {
            if(!entity->active) return;
            EItemEntity* item = (EItemEntity*)entity->obj;
            item->GCoord = (int3)item->tCollider.transform.position;

            if(item->tCollider.GetGroundDir(settings.GroundStickDist, settings.collider, context->mapContext, out float3 gDir)){
                item->tCollider.transform.rotation = Quaternion.LookRotation(gDir, math.up());
                item->tCollider.velocity.y = math.max(0, item->tCollider.velocity.y);
                item->tCollider.velocity *= 1 - settings.StickFriction;
            }
            item->tCollider.Update(*context, settings.collider);
        }


        [BurstCompile]
        public unsafe static void Disable(Entity* entity){
            entity->active = false;
        }

        public unsafe struct ItemInfo{
            [JsonIgnore]
            public IntPtr info;
            [JsonIgnore]
            public IntPtr type;

            [JsonIgnore]
            public ISlot Slot{
                readonly get{   
                    ISlot nSlot = (ISlot)Marshal.PtrToStructure(info, typeof(MaterialItem));
                    string type = Type; nSlot.Deserialize((int _) => type);
                    return nSlot;
                } 
                set {
                    string tType = ""; 
                    value.Serialize((string name) => {
                        tType = name; 
                        return 0;
                    }); Type = tType; 

                    if(info != IntPtr.Zero) Marshal.FreeHGlobal(info);
                    info = Marshal.AllocHGlobal(Marshal.SizeOf(value));
                    Marshal.StructureToPtr(value, info, false);
                }
            }
            public string Type{
                readonly get => new((char*)type);
                set {
                    if(type != IntPtr.Zero) Marshal.FreeHGlobal(type);
                    char* nType = (char*)Marshal.AllocHGlobal((value.Length+1) * sizeof(char));
                    for(int i = 0; i < value.Length; i++){ nType[i] = value[i];}
                    nType[value.Length] = '\0'; 
                    type = (IntPtr)nType;
                }
            }

            //Json Serialization of Slot
            [JsonProperty("Slot")]
            private ISlot SerialSlot{
                readonly get{
                    if(info == IntPtr.Zero) return null;
                    return (ISlot)Marshal.PtrToStructure(info, typeof(MaterialItem));
                } set {
                    string type = Type;
                    value.Deserialize((int _) => type);
                    if(info != IntPtr.Zero) Marshal.FreeHGlobal(info);
                    info = Marshal.AllocHGlobal(Marshal.SizeOf(value));
                    Marshal.StructureToPtr(value, info, false);
                }
            }


            public ItemInfo(ISlot slot){
                info = IntPtr.Zero; type = IntPtr.Zero;
                Slot = slot;
            }

            public void Dispose(){
                if(info != IntPtr.Zero) Marshal.FreeHGlobal(info);
                if(type != IntPtr.Zero) Marshal.FreeHGlobal((IntPtr)type);
                info = IntPtr.Zero; type = IntPtr.Zero;
            }
        }
    }
}


