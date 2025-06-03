using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Item;
using WorldConfig.Generation.Entity;
using static TerrainGeneration.Readback.IVertFormat;
using TerrainGeneration.Readback;
using Unity.Services.Analytics;
[CreateAssetMenu(menuName = "Generation/Entity/Item")]
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
    }

    //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
    //**If you release here the controller might still be accessing it
    public class EItemEntity : Entity, IAttackable
    {  
        public TerrainColliderJob tCollider;
        public Unity.Mathematics.Random random;
        public Registerable<IItem> item;
        [JsonIgnore]
        private EItemController controller;
        [JsonIgnore]
        public EItemSetting settings;
        [JsonIgnore]
        public override float3 position {
            get => tCollider.transform.position + settings.collider.size / 2;
            set => tCollider.transform.position = value - settings.collider.size / 2;
        }
        [JsonIgnore]
        public override float3 origin {
            get => tCollider.transform.position;
            set => tCollider.transform.position = value;
        }
        [JsonIgnore]
        public int3 GCoord => (int3)math.floor(origin); 
        [JsonIgnore]
        public bool IsDead => true;
        public IItem Collect(float amount){
            if(item.Value == null) return null;
            IItem ret;
            if(!item.Value.IsStackable){
                ret = item.Value;
            } else {
                int delta = Mathf.FloorToInt(amount) + (random.NextFloat() < math.frac(amount) ? 1 : 0);
                ret = (IItem)item.Value.Clone();
                ret.AmountRaw = math.max(delta, ret.AmountRaw);
            }
            item.Value.AmountRaw -= ret.AmountRaw;
            if(item.Value.AmountRaw == 0) item.Value = null;

            return ret;
        }

        public void TakeDamage(float damage, float3 knockback, Entity attacker){
            Indicators.DisplayDamageParticle(position, knockback);
            tCollider.velocity += knockback;
        }

        public unsafe EItemEntity(){}
        public EItemEntity(TerrainColliderJob.Transform origin, IItem item){
            tCollider.transform = origin;
            tCollider.velocity = 0;
            
            this.item = new Registerable<IItem>(item);
            this.random = new Unity.Mathematics.Random((uint)GetHashCode());
        } 

        //This function shouldn't be used
        public override void Initialize(EntitySetting setting, GameObject Controller, int3 GCoord)
        {
            settings = (EItemSetting)setting;
            controller = new EItemController(Controller, this);
            tCollider.transform.position = GCoord;
            tCollider.useGravity = true;
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord)
        {
            settings = (EItemSetting)setting;
            controller = new EItemController(Controller, this);
            tCollider.useGravity = true;
            GCoord = this.GCoord;
        }


        public override void Update()
        {
            if(!active) return;

            tCollider.useGravity = true;
            Recognition.DetectMapInteraction(position, OnInSolid: null,
            OnInLiquid: (dens) => {
                tCollider.velocity += EntityJob.cxt.deltaTime * -EntityJob.cxt.gravity;
                tCollider.velocity.y *= 1 - settings.collider.friction;
                tCollider.useGravity = false;
            }, OnInGas: null);

            if(item.Value == null)  EntityManager.ReleaseEntity(info.entityId);
            if(tCollider.GetGroundDir(settings.GroundStickDist, settings.collider, EntityJob.cxt.mapContext, out float3 gDir)){
                tCollider.transform.rotation = Quaternion.LookRotation(gDir, math.up());
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
            this.transform.position = CPUMapManager.GSToWS(entity.position);

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
            TerrainColliderJob.Transform rTransform = entity.tCollider.transform;
            this.transform.SetPositionAndRotation(CPUMapManager.GSToWS(entity.position), rTransform.rotation);
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


