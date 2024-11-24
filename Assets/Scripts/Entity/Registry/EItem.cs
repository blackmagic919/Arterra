using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Burst;
using System;

[CreateAssetMenu(menuName = "Entity/Item")]
public class EItem : EntityAuthoring
{
    [UISetting(Ignore = true)]
    public Option<GameObject> _Controller;
    [UISetting(Ignore = true)]
    public Option<EItemEntity> _Entity;
    public Option<EItemSetting> _Setting;
    public Option<List<ProfileE> > _Profile;
     public Option<Entity.Info.ProfileInfo> _Info;
    public override EntityController Controller { get { return _Controller.value.GetComponent<EntityController>(); } }
    public override IEntity Entity { get => _Entity.value; set => _Entity.value = (EItemEntity)value; }
    public override IEntitySetting Setting { get => _Setting.value; set => _Setting.value = (EItemSetting)value; }
    public override Entity.Info.ProfileInfo Info { get => _Info.value; set => _Info.value = value; }
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
        public int3 GCoord; 
        public InventoryController.Inventory.Slot item;
        public TerrainColliderJob tCollider;
        public Unity.Mathematics.Random random;
        public bool isPickedUp;

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

            item = new InventoryController.Inventory.Slot();
            tCollider.transform.position = GCoord;
            isPickedUp = false;

            Marshal.StructureToPtr(this, entity.obj, false);
            IntPtr nEntity = Marshal.AllocHGlobal(Marshal.SizeOf(entity));
            Marshal.StructureToPtr(entity, nEntity, false);
            return nEntity;
        }

        public EItemEntity(TerrainColliderJob.Transform origin, InventoryController.Inventory.Slot item){
            this.item = item; this.random = default;
            tCollider.transform = origin;
            tCollider.velocity = 0;
            GCoord = (int3)origin.position;
            isPickedUp = false;
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

    }
}


