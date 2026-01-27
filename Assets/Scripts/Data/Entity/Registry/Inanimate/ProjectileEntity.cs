using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using Arterra.Configuration;
using Arterra.Configuration.Generation.Item;
using Arterra.Configuration.Generation.Entity;
using Arterra.Core.Storage;
using FMOD.Studio;
using Arterra.Core.Events;

[CreateAssetMenu(menuName = "Generation/Entity/Projectile")]
public class Projectile : Arterra.Configuration.Generation.Entity.Authoring
{
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
        public float weight = 0.1f;
        public GroundIntrc terrainInteration;
        public EntityIntrc entityInteraction;
        public AudioEvents FlybySound;
        public AudioEvents HitSound;
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
        private ProjectileController controller;
        private ProjectileSetting settings;
        [JsonProperty]
        private float decomposition;
        [JsonIgnore]
        public override ref TerrainCollider.Transform transform => ref tCollider.transform;
        public override float weight => settings.weight;
        [JsonIgnore]
        public int3 GCoord => (int3)math.floor(origin);
        [JsonIgnore]
        public bool IsDead => true;
        private bool HasCollided;

        public Guid ParentId;
        public void Interact(Entity target) { }
        public IItem Collect(Entity target, float amount) {
            var itemReg = Config.CURRENT.Generation.Items;
            IItem item  = null;
            if (itemReg.Contains(settings.ItemDrop)) {
                int index = itemReg.RetrieveIndex(settings.ItemDrop);
                item = itemReg.Retrieve(index).Item;
                item.Create(index, item.UnitSize);
                EntityManager.ReleaseEntity(info.entityId);
            }

            eventCtrl.RaiseEvent(GameEvent.Entity_Collect, this, target, (item, amount));
            return item;
        }

        public void TakeDamage(float damage, float3 knockback, Entity attacker) {
            Indicators.DisplayDamageParticle(position, knockback);
            velocity += knockback;
        }

        public void SetDecomposition(float time) => decomposition = time;
        public void ResetDecomposition() => SetDecomposition(settings.DecayTime);

        //This function shouldn't be used
        public override void Initialize(EntitySetting setting, GameObject Controller, float3 GCoord) {
            settings = (ProjectileSetting)setting;
            tCollider = new TerrainCollider(this.settings.collider, GCoord);
            controller = new ProjectileController(Controller, this);
            decomposition = settings.DecayTime;
            ParentId = info.entityId;
            HasCollided = false;
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

            TerrainInteractor.DetectMapInteraction(position,
                OnInSolid: (dens) => eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InSolid, this, null, dens),
                OnInLiquid: (dens) => {
                    eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InLiquid, this, null, dens);
                    velocity += EntityJob.cxt.deltaTime * -EntityJob.cxt.gravity;
                    tCollider.useGravity = false;
                }, OnInGas: (dens) => eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InGas, this, null, dens));

            decomposition -= EntityJob.cxt.deltaTime;
            if (decomposition <= 0) {
                EntityManager.ReleaseEntity(info.entityId);
                return;
            }
            if (CheckTerrainRayCollision() || CheckEntityRayCollision(position, velocity)) {
                if (!HasCollided) this.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_ProjectileHit, this, null);
                HasCollided = true;
            } else if (math.length(velocity) > 1) HasCollided = false;
            tCollider.Update(this, 0);
            EntityManager.AddHandlerEvent(controller.Update);
        }

        private bool CheckTerrainRayCollision() {
            if (!tCollider.SampleCollision(tCollider.transform.position, tCollider.transform.size * 1.05f, EntityJob.cxt.mapContext, out float3 gDir)) return false;
            switch (settings.terrainInteration) {
                case ProjectileSetting.GroundIntrc.Stick:
                    tCollider.transform.rotation = Quaternion.LookRotation(-gDir, math.up());
                    velocity = 0;
                    break;
                case ProjectileSetting.GroundIntrc.Destroy:
                    EntityManager.ReleaseEntity(info.entityId);
                    break;
                case ProjectileSetting.GroundIntrc.Bounce:
                    float3 dir = math.normalize(gDir);
                    float3 reflect = math.dot(velocity, dir) * dir;
                    velocity = velocity - 2 * (1 - TerrainCollider.BaseFriction) * reflect;
                    break;
                case ProjectileSetting.GroundIntrc.Slide:
                    break;
            } return true;
        }

        private bool CheckEntityRayCollision(float3 startGS, float3 pVel) {
            float3 endGS = startGS + pVel * EntityJob.cxt.deltaTime;
            if (!EntityManager.ESTree.FindClosestAlongRay(startGS, endGS, info.entityId, out Entity hitEntity, out _))
                return false;
            if (hitEntity is not IAttackable atkEntity) return false;

            float speed = math.length(pVel);
            float damage = speed * settings.DamageMultiplier;
            float3 knockback = velocity * settings.KnockbackMultiplier;
            if (!EntityManager.TryGetEntity(ParentId, out Entity attacker))
                EntityManager.TryGetEntity(info.entityId, out attacker);
            if (speed > settings.MinDamagingSpeed)
                MediumVitality.RealAttack(this, hitEntity, damage, knockback);
            switch (settings.entityInteraction) {
                case ProjectileSetting.EntityIntrc.Ricochet:
                    float3 dir = startGS - hitEntity.position;
                    float3 reflect = math.dot(velocity, dir) * dir;
                    velocity = velocity - 2 * (1 - TerrainCollider.BaseFriction) * reflect;
                    break;
                case ProjectileSetting.EntityIntrc.Destroy:
                    EntityManager.ReleaseEntity(info.entityId);
                    break;
                case ProjectileSetting.EntityIntrc.Penetrate:
                    break;
            } return true;
        }


        public override void Disable() {
            controller.Dispose();
        }
        
        private class ProjectileController
        {
            private ProjectileEntity entity;
            private GameObject gameObject;
            private Transform transform;
            private EventInstance instance;
            private Indicators indicators;

            private bool active = false;


            public ProjectileController(GameObject GameObject, Entity Entity)
            {
                this.gameObject = Instantiate(GameObject);
                this.transform = gameObject.transform;
                this.entity = (ProjectileEntity)Entity;
                this.active = true;

                this.indicators = new Indicators(gameObject, entity);
                this.transform.position = CPUMapManager.GSToWS(entity.position);
                entity.eventCtrl.AddEventHandler(Arterra.Core.Events.GameEvent.Entity_ProjectileHit, PlayHitEffects);
                instance = AudioManager.CreateEventAttached(entity.settings.FlybySound, gameObject);
            }

            public void Update(){
                if(!entity.active) return;
                if(gameObject == null) return;
                TerrainCollider.Transform rTransform = entity.tCollider.transform;
                this.transform.SetPositionAndRotation(CPUMapManager.GSToWS(entity.position), rTransform.rotation);

                this.indicators.Update();
                if (entity.HasCollided) return;
                float speed = math.length(entity.velocity);
                speed = 1.0f - math.exp(-0.1f * speed);
                instance.setParameterByName("Speed", speed);
            }

            public void Dispose(){ 
                if(!active) return;
                active = false;

                instance.stop(STOP_MODE.ALLOWFADEOUT);
                indicators.Release();
                Destroy(gameObject);
            }

            private void PlayHitEffects(object source, object target, object _) => 
                EntityManager.AddHandlerEvent(() => AudioManager.CreateEvent(entity.settings.HitSound, (source as Entity).position));

            ~ProjectileController(){
                Dispose();
            }
        }
    }
}


