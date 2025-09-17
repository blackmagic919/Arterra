using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Item;
using WorldConfig.Generation.Entity;
using static TerrainGeneration.Readback.IVertFormat;
using TerrainGeneration.Readback;
using MapStorage;

[CreateAssetMenu(menuName = "Generation/Entity/Item")]
public class EItem : WorldConfig.Generation.Entity.Authoring
{
    [UISetting(Ignore = true)][JsonIgnore]
    public Option<EItemEntity> _Entity;
    public Option<EItemSetting> _Setting;
    public static Catalogue<WorldConfig.Generation.Item.Authoring> ItemRegistry => Config.CURRENT.Generation.Items;
    
    [JsonIgnore]
    public override Entity Entity { get => new EItemEntity(); }
    [JsonIgnore]
    public override EntitySetting Setting { get => _Setting.value; set => _Setting.value = (EItemSetting)value; }
    [Serializable]
    public class EItemSetting : EntitySetting {
        public float GroundStickDist;
        public float StickFriction;
        public int2 SpriteSampleSize;
        public float AlphaClip;
        public float ExtrudeHeight;
        public float MergeRadius;
        public float DecayTime;
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
        public float decomposition;

        public void Interact(Entity targert) { }
        public IItem Collect(float amount) {
            if (item.Value == null) return null;
            IItem ret;
            if (!item.Value.IsStackable) {
                ret = (IItem)item.Value.Clone();
            } else {
                int delta = Mathf.FloorToInt(amount) + (random.NextFloat() < math.frac(amount) ? 1 : 0);
                ret = (IItem)item.Value.Clone();
                ret.AmountRaw = math.max(delta, ret.AmountRaw);
            }
            item.Value.AmountRaw -= ret.AmountRaw;
            if (item.Value.AmountRaw == 0) item.Value = null;

            return ret;
        }

        public void TakeDamage(float damage, float3 knockback, Entity attacker){
            Indicators.DisplayDamageParticle(position, knockback);
            tCollider.velocity += knockback;
        }

        public unsafe EItemEntity(){}
        public EItemEntity(IItem item, Quaternion rot = default) {
            this.item = new Registerable<IItem>(item);
            tCollider.transform.rotation = rot;
        }

        //This function shouldn't be used
        public override void Initialize(EntitySetting setting, GameObject Controller, float3 GCoord) {
            settings = (EItemSetting)setting;
            controller = new EItemController(Controller, this);
            random = new Unity.Mathematics.Random((uint)UnityEngine.Random.Range(0, int.MaxValue));
            decomposition = settings.DecayTime;
            tCollider.transform.position = GCoord;
            tCollider.useGravity = true;
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord)
        {
            settings = (EItemSetting)setting;
            controller = new EItemController(Controller, this);
            decomposition = math.min(settings.DecayTime, decomposition);
            tCollider.useGravity = true;
            GCoord = this.GCoord;
        }


        public override void Update()
        {
            if(!active) return;
            tCollider.useGravity = true;

            TerrainInteractor.DetectMapInteraction(position, OnInSolid: null,
            OnInLiquid: (dens) => {
                tCollider.velocity += EntityJob.cxt.deltaTime * -EntityJob.cxt.gravity;
                tCollider.velocity.y *= 1 - settings.collider.friction;
                tCollider.useGravity = false;
            }, OnInGas: null);

            decomposition -= EntityJob.cxt.deltaTime;
            if (decomposition <= 0)
                item.Value = null;
            MergeNearbyEItems();

            if (item.Value == null || item.Value.AmountRaw == 0){
                EntityManager.ReleaseEntity(info.entityId);
            }
            if (tCollider.GetGroundDir(settings.GroundStickDist, settings.collider, EntityJob.cxt.mapContext, out float3 gDir)) {
                    tCollider.transform.rotation = Quaternion.LookRotation(gDir, math.up());
                    tCollider.velocity *= 1 - settings.StickFriction;
                }
            tCollider.Update(settings.collider, this);
            EntityManager.AddHandlerEvent(controller.Update);
        }

        private void MergeNearbyEItems() {
            if (item.Value == null) return;
            if (!item.Value.IsStackable) return;
            if (item.Value.AmountRaw >= IItem.MaxAmountRaw) return;

            void MergeWithEItem(EItemEntity neighbor) {
                if (item.Value == null || item.Value.AmountRaw == 0)
                    return; //Already merged by neighbor
                if (neighbor == null) return;
                if (neighbor.item.Value == null) return;
                if (neighbor.item.Value.Index != item.Value.Index)
                    return;
                int delta = math.min(item.Value.AmountRaw
                    + neighbor.item.Value.AmountRaw, IItem.MaxAmountRaw)
                    - item.Value.AmountRaw;
                item.Value.AmountRaw += delta;
                neighbor.item.Value.AmountRaw -= delta;
                if (neighbor.item.Value.AmountRaw == 0)
                    neighbor.item.Value = null;
            }

            Bounds bounds = new Bounds(position, new float3(settings.MergeRadius * 2));
            EntityManager.ESTree.Query(bounds, (Entity nEntity) => {
                if (nEntity == null) return;
                if (nEntity.info.entityId == info.entityId) return;
                if (nEntity.info.entityType != info.entityType) return;

                EItemEntity nItem = nEntity as EItemEntity;
                if (nItem.item.Value == null) return;
                if (nItem.item.Value.Index != item.Value.Index)
                    return;
                EntityManager.AddHandlerEvent(() => MergeWithEItem(nItem));
            });
        }
        

        public override void Disable() {
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


