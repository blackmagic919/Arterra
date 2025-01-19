using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
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
    public static Registry<WorldConfig.Generation.Item.Authoring> ItemRegistry => Config.CURRENT.Generation.Items;
    
    [JsonIgnore]
    public override EntityController Controller { get { return _Controller.value.GetComponent<EntityController>(); } }
    [JsonIgnore]
    public override Entity Entity { get => new EItemEntity(); }
    [JsonIgnore]
    public override IEntitySetting Setting { get => _Setting.value; set => _Setting.value = (EItemSetting)value; }
    [Serializable]
    public struct EItemSetting : IEntitySetting{
        public float GroundStickDist;
        public float StickFriction;
        public int2 SpriteSampleSize;
        public float AlphaClip;
        public float ExtrudeHeight;
        public TerrainColliderJob.Settings collider;
    }

    //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
    //**If you release here the controller might still be accessing it
    public class EItemEntity : Entity
    {  
        //This is the real-time position streamed by the controller
        public TerrainColliderJob tCollider;
        public Unity.Mathematics.Random random;
        public int3 GCoord; 
        public bool isPickedUp;
        public ItemInfo item;

        public static EItemSetting settings;
        
        public override void Preset(IEntitySetting setting){
            settings = (EItemSetting)setting;
        }
        public override void Unset(){ }

        //This function shouldn't be used
        public override void Initialize(int3 GCoord)
        {
            tCollider.transform.position = GCoord;
            isPickedUp = false;
        }

        public unsafe EItemEntity(TerrainColliderJob.Transform origin, IItem item){
            tCollider.transform = origin;
            tCollider.velocity = 0;
            GCoord = (int3)origin.position;
            
            isPickedUp = false;
            this.random = default;
            this.item = new ItemInfo(item);
        } 
        public unsafe EItemEntity(){}

        public override void Deserialize(out int3 GCoord)
        {
            GCoord = this.GCoord;
        }


        public override void Update()
        {
            if(!active) return;
            GCoord = (int3)tCollider.transform.position;

            if(tCollider.GetGroundDir(settings.GroundStickDist, settings.collider, EntityJob.cxt.mapContext, out float3 gDir)){
                tCollider.transform.rotation = Quaternion.LookRotation(gDir, math.up());
                tCollider.velocity.y = math.max(0, tCollider.velocity.y);
                tCollider.velocity *= 1 - settings.StickFriction;
            }
            tCollider.Update(EntityJob.cxt, settings.collider);
        }


        public override void Disable(){}

        public struct ItemInfo{
            public IItem item;
            public string type;

            [JsonIgnore]
            public IItem Slot{
                readonly get{   
                    string SerialType = type;
                    IItem slot = (IItem)item.Clone();
                    slot.Deserialize((int _) => SerialType);
                    return slot;
                } 
                set {
                    string SerialType = ""; 
                    value.Serialize((string name) => {
                        SerialType = name; 
                        return 0;
                    }); type = SerialType;
                    item = value;
                }
            }

            public ItemInfo(IItem slot){
                item = default; type = default;
                Slot = slot;
            }
        }
    }
}


