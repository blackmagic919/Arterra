using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using Arterra.Configuration;
using Arterra.Configuration.Generation.Item;
using Arterra.Configuration.Generation.Entity;
using static Arterra.Core.Terrain.Readback.IVertFormat;
using Arterra.Core.Terrain.Readback;
using Arterra.Core.Storage;

[CreateAssetMenu(menuName = "Generation/Entity/Item")]
public class EItem : Arterra.Configuration.Generation.Entity.Authoring
{
    public Option<EItemSetting> _Setting;
    
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
    public class EItemEntity : Entity, IAttackable {
        [JsonProperty]
        private TerrainCollider tCollider;
        [JsonProperty]
        private Unity.Mathematics.Random random;
        [JsonProperty]
        private Registerable<IItem> item;
        [JsonProperty]
        private float decomposition;
        private EItemController controller;
        private EItemSetting settings;
        [JsonIgnore]
        public override ref TerrainCollider.Transform transform => ref tCollider.transform;
        [JsonIgnore]
        public int3 GCoord => (int3)math.floor(origin);
        [JsonIgnore]
        public bool IsDead => true;

        public void Interact(Entity targert) { }
        public IItem Collect(float amount) {
            if (item.Value == null) return null;
            IItem ret;
            ret = (IItem)item.Value.Clone();
            amount *= ret.UnitSize;
            int delta = Mathf.FloorToInt(amount) 
                + (random.NextFloat() < math.frac(amount) ? 1 : 0);
            ret.AmountRaw = math.min(delta, ret.AmountRaw);
            item.Value.AmountRaw -= ret.AmountRaw;
            if (item.Value.AmountRaw == 0) item.Value = null;

            return ret;
        }

        public void TakeDamage(float damage, float3 knockback, Entity attacker) {
            Indicators.DisplayDamageParticle(position, knockback);
            velocity += knockback;
        }

        public EItemEntity() { }
        public EItemEntity(IItem item, Quaternion rot = default) {
            this.item = new Registerable<IItem>(item);
            tCollider.transform.rotation = rot;
        }

        //This function shouldn't be used
        public override void Initialize(EntitySetting setting, GameObject Controller, float3 GCoord) {
            settings = (EItemSetting)setting;
            controller = new EItemController(Controller, this);
            random = new Unity.Mathematics.Random((uint)UnityEngine.Random.Range(0, int.MaxValue));
            tCollider = new TerrainCollider(this.settings.collider, GCoord);
            decomposition = settings.DecayTime;
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord) {
            settings = (EItemSetting)setting;
            controller = new EItemController(Controller, this);
            decomposition = math.min(settings.DecayTime, decomposition);
            tCollider.useGravity = true;
            GCoord = this.GCoord;
        }


        public override void Update() {
            if (!active) return;
            tCollider.useGravity = true;

            TerrainInteractor.DetectMapInteraction(position,
                OnInSolid: (dens) => eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InSolid, this, null, ref dens),
                OnInLiquid: (dens) => {
                    eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InLiquid, this, null, ref dens);
                    velocity += EntityJob.cxt.deltaTime * -EntityJob.cxt.gravity;
                    tCollider.useGravity = false;
                }, OnInGas: (dens) => eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InGas, this, null, ref dens));

            decomposition -= EntityJob.cxt.deltaTime;
            if (decomposition <= 0)
                item.Value = null;
            MergeNearbyEItems();

            if (item.Value == null || item.Value.AmountRaw == 0) {
                EntityManager.ReleaseEntity(info.entityId);
            }
            if (GetGroundDir(out float3 gDir)) {
                tCollider.transform.rotation = Quaternion.LookRotation(gDir, math.up());
                velocity *= 1 - settings.StickFriction;
            }
            tCollider.Update(this);
            EntityManager.AddHandlerEvent(controller.Update);
        }

        private void MergeNearbyEItems() {
            if (item.Value == null) return;
            if (item.Value.AmountRaw >= item.Value.StackLimit) return;

            void MergeWithEItem(EItemEntity neighbor) {
                if (item.Value == null || item.Value.AmountRaw == 0)
                    return; //Already merged by neighbor
                if (neighbor == null) return;
                if (neighbor.item.Value == null) return;
                int delta = math.min(item.Value.AmountRaw
                    + neighbor.item.Value.AmountRaw, item.Value.StackLimit)
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
                if (item.Value.AmountRaw + nItem.item.Value.AmountRaw > item.Value.StackLimit)
                    return;
                EntityManager.AddHandlerEvent(() => MergeWithEItem(nItem));
            });
        }

        private bool GetGroundDir( out float3 dir) => tCollider.SampleCollision(
            transform.position,
            new float3(settings.collider.size.x, -settings.GroundStickDist, settings.collider.size.z),
            EntityJob.cxt.mapContext, out dir
        );

        public override void Disable() {
            controller.Dispose();
        }
        
        private class EItemController
        {
            private EItemEntity entity;
            private GameObject gameObject;
            private Transform transform;
            private Indicators indicators;

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
                indicators = new Indicators(gameObject, entity);

                if(entity.item.Value == null) return; 
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
                TerrainCollider.Transform rTransform = entity.tCollider.transform;
                this.transform.SetPositionAndRotation(CPUMapManager.GSToWS(entity.position), rTransform.rotation);
                indicators.Update();
            }

            public void Dispose(){ 
                if(!active) return;
                active = false;

                indicators.Release();
                Destroy(gameObject);
            }
            ~EItemController(){
                Dispose();
            }
        }
    }
}


