using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Burst;
using System;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Item;
using WorldConfig.Generation.Entity;
[CreateAssetMenu(menuName = "Entity/Item")]
public class EItem : WorldConfig.Generation.Entity.Authoring
{
    [UISetting(Ignore = true)][JsonIgnore]
    public Option<GameObject> _Controller;
    [UISetting(Ignore = true)][JsonIgnore]
    public Option<EItemEntity> _Entity;
    public Option<EItemSetting> _Setting;
    public Option<List<ProfileE> > _Profile;
    public Option<Entity.ProfileInfo> _Info;
    public static Registry<WorldConfig.Generation.Item.Authoring> ItemRegistry => Config.CURRENT.Generation.Items;
    
    [JsonIgnore]
    public override EntityController Controller { get { return _Controller.value.GetComponent<EntityController>(); } }
    [JsonIgnore]
    public override IEntity Entity { get => _Entity.value; set => _Entity.value = (EItemEntity)value; }
    [JsonIgnore]
    public override IEntitySetting Setting { get => _Setting.value; set => _Setting.value = (EItemSetting)value; }
    [JsonIgnore]
    public override Entity.ProfileInfo Info { get => _Info.value; set => _Info.value = value; }
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

        public unsafe EItemEntity(TerrainColliderJob.Transform origin, IItem item){
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
            public IItem Slot{
                readonly get{   
                    string SerialType = Type; //first part of type is the actual type
                    string tType = SerialType.Split("::")[0];
                    if(!ItemRegistry.Contains(tType)) return null;
                    Type itemType = ItemRegistry.Retrieve(tType).Item.GetType();
                    IItem nSlot = JsonConvert.DeserializeObject(new string((char*)info), itemType) as IItem;
                    nSlot.Deserialize((int _) => SerialType);
                    return nSlot;
                } 
                set {
                    string SerialType = ""; 
                    value.Serialize((string name) => {
                        SerialType = name; 
                        return 0;
                    }); Type = SerialType; 

                    if(info != IntPtr.Zero) Marshal.FreeHGlobal(info);
                    info = (IntPtr)StrToUnmanaged(JsonConvert.SerializeObject(value));

                }
            }
            public string Type{
                readonly get => new((char*)type);
                set {
                    if(type != IntPtr.Zero) Marshal.FreeHGlobal(type);
                    type = (IntPtr)StrToUnmanaged(value);
                }
            }

            //Json Serialization of Slot
            [JsonProperty("Slot")]
            private IItem SerialSlot{
                readonly get{
                    //Json-Deserialization calls this, but we don't have enough info to call getter
                    //without having called setter first, so we ignore it(if info is NULL)
                    if(info == IntPtr.Zero) return null; 
                    Type itemType = ItemRegistry.Retrieve(Type.Split("::")[0]).Item.GetType(); 
                    return JsonConvert.DeserializeObject(new string((char*)info), itemType) as IItem;
                } set {
                    string SerialType = Type; //first part of type is the actual type
                    value.Deserialize((int _) => SerialType);
                    if(info != IntPtr.Zero) Marshal.FreeHGlobal(info);
                    info = (IntPtr)StrToUnmanaged(JsonConvert.SerializeObject(value));
                }
            }

            private readonly unsafe char* StrToUnmanaged(string str){
                char* nType = (char*)Marshal.AllocHGlobal((str.Length+1) * sizeof(char));
                for(int i = 0; i < str.Length; i++){ nType[i] = str[i];}
                nType[str.Length] = '\0';
                return nType;
            }


            public ItemInfo(IItem slot){
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


