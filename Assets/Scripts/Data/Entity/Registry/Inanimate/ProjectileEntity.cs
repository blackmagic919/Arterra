using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Item;
using WorldConfig.Generation.Entity;
using MapStorage;

[CreateAssetMenu(menuName = "Generation/Entity/Projectile")]
public class Projectile : WorldConfig.Generation.Entity.Authoring
{
    [UISetting(Ignore = true)][JsonIgnore]
    public Option<ProjectileEntity> _Entity;
    public Option<ProjectileSetting> _Setting;
    
    [JsonIgnore]
    public override Entity Entity { get => new ProjectileEntity(); }
    [JsonIgnore]
    public override EntitySetting Setting { get => _Setting.value; set => _Setting.value = (ProjectileSetting)value; }
    [Serializable]
    public class ProjectileSetting : EntitySetting {
        public float DecayTime;
        public float MinDamagingSpeed;
        public float DamageMultiplier;
        public float KnockbackMultiplier;
        public GroundIntrc terrainInteration;
        public EntityIntrc entityInteraction;
        [RegistryReference("Items")]
        public string ItemDrop;
        public enum GroundIntrc {
            Stick,
            Destroy,
            Slide,
            Bounce,
        }
        public enum EntityIntrc {
            Destroy,
            Penetrate,
            Ricochet,
        }
    }

    //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
    //**If you release here the controller might still be accessing it
    public class ProjectileEntity : Entity, IAttackable {
        [JsonProperty]
        private TerrainCollider tCollider;
        [JsonProperty]
        private Unity.Mathematics.Random random;
        private ProjectileController controller;
        private ProjectileSetting settings;
        [JsonProperty]
        private float decomposition;
        [JsonIgnore]
        public override ref TerrainCollider.Transform transform => ref tCollider.transform;
        [JsonIgnore]
        public int3 GCoord => (int3)math.floor(origin);
        [JsonIgnore]
        public bool IsDead => true;

        public Guid ParentId;
        public void Interact(Entity target) { }
        public IItem Collect(float amount) {
            var itemReg = Config.CURRENT.Generation.Items;
            if (!itemReg.Contains(settings.ItemDrop)) return null;
            int index = itemReg.RetrieveIndex(settings.ItemDrop);
            IItem dropItem = itemReg.Retrieve(index).Item;
            dropItem.Create(index, dropItem.UnitSize);
            EntityManager.ReleaseEntity(info.entityId);
            return dropItem;
        }

        public void TakeDamage(float damage, float3 knockback, Entity attacker) {
            Indicators.DisplayDamageParticle(position, knockback);
            velocity += knockback;
        }

        //This function shouldn't be used
        public override void Initialize(EntitySetting setting, GameObject Controller, float3 GCoord) {
            settings = (ProjectileSetting)setting;
            controller = new ProjectileController(Controller, this);
            random = new Unity.Mathematics.Random((uint)UnityEngine.Random.Range(0, int.MaxValue));
            tCollider = new TerrainCollider(this.settings.collider, GCoord);
            decomposition = settings.DecayTime;
            ParentId = info.entityId;
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord) {
            settings = (ProjectileSetting)setting;
            controller = new ProjectileController(Controller, this);
            decomposition = math.min(settings.DecayTime, decomposition);
            tCollider.useGravity = true;
            GCoord = this.GCoord;
        }


        public override void Update() {
            if (!active) return;
            tCollider.useGravity = true;

            TerrainInteractor.DetectMapInteraction(position, OnInSolid: null,
            OnInLiquid: (dens) => {
                velocity += EntityJob.cxt.deltaTime * -EntityJob.cxt.gravity;
                velocity.y *= 1 - settings.collider.friction;
                tCollider.useGravity = false;
            }, OnInGas: null);

            decomposition -= EntityJob.cxt.deltaTime;
            if (decomposition <= 0) {
                EntityManager.ReleaseEntity(info.entityId);
                return;
            }
            if (tCollider.SampleCollision(tCollider.transform.position, tCollider.transform.size * 1.05f, EntityJob.cxt.mapContext, out float3 gDir)) {
                switch (settings.terrainInteration) {
                    case ProjectileSetting.GroundIntrc.Stick:
                        tCollider.transform.rotation = Quaternion.LookRotation(-gDir, math.up());
                        velocity = 0;
                        break;
                    case ProjectileSetting.GroundIntrc.Destroy:
                        EntityManager.ReleaseEntity(info.entityId);
                        return;
                    case ProjectileSetting.GroundIntrc.Bounce:
                        float3 dir = math.normalize(gDir);
                        float3 reflect = math.dot(velocity, dir) * dir;
                        velocity = velocity - 2 * (1 - settings.collider.friction) * reflect;
                        break;
                    case ProjectileSetting.GroundIntrc.Slide:
                        break;
                }
            } else CheckEntityRayCollision(position, velocity);
            tCollider.Update(this);
            EntityManager.AddHandlerEvent(controller.Update);
        }

        private void CheckEntityRayCollision(float3 startGS, float3 pVel) {
            float speed = math.length(pVel);
            if (speed <= settings.MinDamagingSpeed) return;

            float3 endGS = startGS + pVel;
            if (!EntityManager.ESTree.FindClosestAlongRay(startGS, endGS, info.entityId, out Entity hitEntity)) return;
            if (hitEntity is not IAttackable atkEntity) return;
            float damage = speed * settings.DamageMultiplier;
            float3 knockback = velocity * settings.KnockbackMultiplier;
            if (!EntityManager.TryGetEntity(ParentId, out Entity attacker))
                EntityManager.TryGetEntity(info.entityId, out attacker);
            EntityManager.AddHandlerEvent(() => atkEntity.TakeDamage(damage, knockback, attacker));
            switch (settings.entityInteraction) {
                case ProjectileSetting.EntityIntrc.Ricochet:
                    float3 dir = startGS - hitEntity.position;
                    float3 reflect = math.dot(velocity, dir) * dir;
                    velocity = velocity - 2 * (1 - settings.collider.friction) * reflect;
                    break;
                case ProjectileSetting.EntityIntrc.Destroy:
                    EntityManager.ReleaseEntity(info.entityId);
                    break;
                case ProjectileSetting.EntityIntrc.Penetrate:
                    break;
            }
        }


        public override void Disable() {
            controller.Dispose();
        }
        
        private class ProjectileController
        {
            private ProjectileEntity entity;
            private GameObject gameObject;
            private Transform transform;

            private bool active = false;


            public ProjectileController(GameObject GameObject, Entity Entity)
            {
                this.gameObject = Instantiate(GameObject);
                this.transform = gameObject.transform;
                this.entity = (ProjectileEntity)Entity;
                this.active = true;

                this.transform.position = CPUMapManager.GSToWS(entity.position);
            }

            public void Update(){
                if(!entity.active) return;
                if(gameObject == null) return;
                TerrainCollider.Transform rTransform = entity.tCollider.transform;
                this.transform.SetPositionAndRotation(CPUMapManager.GSToWS(entity.position), rTransform.rotation);
            }

            public void Dispose(){ 
                if(!active) return;
                active = false;

                Destroy(gameObject);
            }
            ~ProjectileController(){
                Dispose();
            }
        }
    }
}


