using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Item;
using WorldConfig.Generation.Entity;
using static TerrainGeneration.Readback.IVertFormat;
using TerrainGeneration.Readback;
[CreateAssetMenu(menuName = "Entity/Item")]
public class EItem : WorldConfig.Generation.Entity.Authoring
{
    [UISetting(Ignore = true)][JsonIgnore]
    public Option<EItemEntity> _Entity;
    public Option<EItemSetting> _Setting;
    public static Registry<WorldConfig.Generation.Item.Authoring> ItemRegistry => Config.CURRENT.Generation.Items;
    
    [JsonIgnore]
    public override Entity Entity { get => new EItemEntity(); }
    [JsonIgnore]
    public override EntitySetting Setting { get => _Setting.value; set => _Setting.value = (EItemSetting)value; }
    [Serializable]
    public class EItemSetting : EntitySetting{
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
        [JsonIgnore]
        private EItemController controller;
        public TerrainColliderJob tCollider;
        public Unity.Mathematics.Random random;
        public int3 GCoord; 
        public bool isPickedUp;
        public Registerable<IItem> item;
        public EItemSetting settings;
        public override float3 position {
            get => tCollider.transform.position;
            set => tCollider.transform.position = value;
        }

        public unsafe EItemEntity(){}
        public EItemEntity(TerrainColliderJob.Transform origin, IItem item){
            tCollider.transform = origin;
            tCollider.velocity = 0;
            GCoord = (int3)origin.position;
            
            isPickedUp = false;
            this.random = default;
            this.item = new Registerable<IItem>(item);
        } 

        //This function shouldn't be used
        public override void Initialize(EntitySetting setting, GameObject Controller, int3 GCoord)
        {
            settings = (EItemSetting)setting;
            controller = new EItemController(Controller, this);
            tCollider.transform.position = GCoord;
            isPickedUp = false;
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord)
        {
            settings = (EItemSetting)setting;
            controller = new EItemController(Controller, this);
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
            EntityManager.AddHandlerEvent(controller.Update);
        }

        public override void Disable(){
            controller.Dispose();
        }
    }

    public class EItemController
    {
        private EItemEntity entity;
        private GameObject gameObject;
        private Transform transform;

        private bool active = false;

        private MeshFilter meshFilter;

        public EItemController(GameObject GameObject, Entity Entity)
        {
            this.gameObject = Instantiate(GameObject);
            this.transform = gameObject.transform;
            this.entity = (EItemEntity)Entity;
            this.active = true;

            float3 GCoord = new (entity.GCoord);
            this.transform.position = CPUMapManager.GSToWS(GCoord - entity.settings.collider.offset) + (float3)Vector3.up;

            meshFilter = gameObject.GetComponent<MeshFilter>();
            SpriteExtruder.Extrude(new SpriteExtruder.ExtrudeSettings{
                ImageIndex = entity.item.Value.TexIndex,
                SampleSize = entity.settings.SpriteSampleSize,
                AlphaClip = entity.settings.AlphaClip,
                ExtrudeHeight = entity.settings.ExtrudeHeight,
            }, OnMeshRecieved);
        }

        private void OnMeshRecieved(ReadbackTask<SVert>.SharedMeshInfo meshInfo){
            if(active) meshFilter.sharedMesh = meshInfo.GenerateMesh(UnityEngine.Rendering.IndexFormat.UInt32);
            meshInfo.Release();
        }

        public void Update(){
            if(!entity.active) return;
            if(gameObject == null) return;
            EntityManager.AssertEntityLocation(entity, entity.GCoord);    
            TerrainColliderJob.Transform rTransform = entity.tCollider.transform;
            rTransform.position = CPUMapManager.GSToWS(rTransform.position - entity.settings.collider.offset);
            this.transform.SetPositionAndRotation(rTransform.position, rTransform.rotation);
        }

        public void Dispose(){ 
            if(!active) return;
            active = false;

            Destroy(gameObject);
        }
        ~EItemController(){
            Dispose();
        }

    }

}


