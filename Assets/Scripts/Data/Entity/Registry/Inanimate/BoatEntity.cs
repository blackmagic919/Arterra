using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using Arterra.Configuration;
using Arterra.Data.Item;
using Arterra.Data.Entity;

using Arterra.Core.Storage;
using Arterra.GamePlay;
using Arterra.Core.Events;
using TerrainCollider = Arterra.GamePlay.Interaction.TerrainCollider;
using Arterra.GamePlay.Interaction;
using Arterra.Editor;
using Arterra.GamePlay.UI;
using Arterra.Data.Entity.Behavior;
using System.Collections.Generic;
using Arterra.Configuration.Gameplay;

[CreateAssetMenu(menuName = "Generation/Entity/Boat")]
public class BoatEntity : Arterra.Data.Entity.Authoring {
    public Option<BoatSetting> _Setting;
    public static Catalogue<Arterra.Data.Item.Authoring> ItemRegistry => Config.CURRENT.Generation.Items;

    [JsonIgnore]
    public override Entity Entity { get => new Boat(); }
    [JsonIgnore]
    public override EntitySetting Setting { get => _Setting.value; set => _Setting.value = (BoatSetting)value; }
    [Serializable]
    public class BoatSetting : EntitySetting {
        public float rotSpeed = 180;
        public float MaxWaterSpeed;
        public float MaxLandSpeed;
        public float Acceleration;
        public float groundStickDist = 0.15f;
        public MinimalVitality.Stats durability;
        [RegistryReference("Items")]
        public string ItemDrop;
    }

    //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
    //**If you release here the controller might still be accessing it
    public class Boat : Entity, IRidable, IVitality {
        private const string BaseGravityStateName = "BoatEntity::Base";
        private const string LiquidGravityStateName = "BoatEntity::Liquid";

        [JsonProperty]
        private MinimalVitality vitality;
        [JsonProperty]
        private TerrainCollider tCollider;
        [JsonProperty]
        private Guid RiderTarget = Guid.Empty;
        private HashSet<Guid> IgnoredEntities;
        private BoatController controller;
        private BoatSetting settings;
        [JsonIgnore]
        public override ref TerrainCollider.Transform transform => ref tCollider.transform;
        [JsonIgnore]
        public int3 GCoord => (int3)math.floor(origin);
        [JsonIgnore]
        public bool IsDead => vitality.IsDead;

        private void Dismounted(object source, object target, object context) {
            if (target is not Guid riderId || riderId != RiderTarget) return;
            IgnoredEntities = null;
            RiderTarget = Guid.Empty;
        }

        private void Interact(object source, object target, object context) {
            if (target is not Guid callerId) return;
            if (!EntityManager.TryGetEntity(callerId, out Entity caller)) return;
            if (RiderTarget != Guid.Empty) return; //Already has a rider
            RiderTarget = caller.info.entityId;
            IgnoredEntities = new HashSet<Guid>{ RiderTarget };
            caller.eventCtrl.EnqueueEvent(GameEvent.Action_Mount, caller.info.entityId, info.entityId);
        }
        private void Collect(object receiver, object source, object context) {
            if (context is not float amount) return;
            eventCtrl.RaiseEvent(GameEvent.Entity_Collect, info.entityId, receiver, amount);
        }

        private void TakeDamage(object source, object attacker, object context) {
            if (context is not (float damage, float3 knockback)) return;
            if (!vitality.Damage(damage)) return;
            Indicators.DisplayDamageParticle(position, knockback);
            velocity += knockback;
        }

        // IRidable implementation
        public Transform GetRiderRoot() => controller.RideRoot;
        public void WalkInDirection(float3 aim) {
            aim = new(aim.x, 0, aim.z);
            if (Vector3.Magnitude(aim) <= 1E-05f) return;
            this.controller.SetAimAngle(aim);

            if (math.length(velocity) <= settings.MaxLandSpeed &&
                TerrainCollider.SampleCollision(origin, new float3(
                settings.collider.size.x, -settings.groundStickDist, settings.collider.size.z))
            ) {
                velocity += settings.Acceleration * EntityJob.cxt.deltaTime * aim;
            } else {
                float3 basePos = position + (float3)(Vector3.down * settings.groundStickDist);
                TerrainInteractor.DetectMapInteraction(basePos, OnInSolid: null,
                    OnInLiquid: (dens) => {
                        if (math.length(velocity) > settings.MaxWaterSpeed) return;
                        velocity += settings.Acceleration * EntityJob.cxt.deltaTime * aim;
                    }, OnInGas: null
                );
            }
        }

        private void RegisterEvents() {
            eventCtrl.Register(this);
            eventCtrl.AddEventHandler(GameEvent.Entity_Interact, Interact);
            eventCtrl.AddEventHandler(GameEvent.Apply_Collect, Collect);
            eventCtrl.AddEventHandler(GameEvent.Apply_Damage, TakeDamage);
            eventCtrl.AddEventHandler(GameEvent.Action_Dismounted, Dismounted);
        }

        public Boat() { }
        public override void Initialize(EntitySetting setting, GameObject Controller, float3 GCoord) {
            settings = (BoatSetting)setting;
            vitality = new MinimalVitality(settings.durability);
            tCollider = new TerrainCollider(this.settings.collider, GCoord);
            controller = new BoatController(Controller, this);
            RegisterEvents();
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord) {
            settings = (BoatSetting)setting;
            controller = new BoatController(Controller, this);
            RegisterEvents();
            if (vitality == null) vitality = new MinimalVitality(settings.durability);
            else vitality.Deserialize(settings.durability);

            tCollider.SetGravityState(BaseGravityStateName, TerrainCollider.GravityPriority.Default, true);
            GCoord = this.GCoord;

            if (RiderTarget == Guid.Empty) return;
            IgnoredEntities = new HashSet<Guid>{RiderTarget};
            if (EntityManager.TryGetEntity(RiderTarget, out Entity entity))
                entity.eventCtrl.EnqueueEvent(GameEvent.Action_Mount, entity.info.entityId, info.entityId);
        }

        public override void UpdateJobSync() => controller.Update();
        public override void Update() {
            if (!active) return;

            tCollider.SetGravityState(BaseGravityStateName, TerrainCollider.GravityPriority.Default, true);

            vitality.Update(this);
            bool inLiquid = false;
            TerrainInteractor.DetectMapInteraction(position,
                OnInSolid: (dens) => eventCtrl.RaiseEvent(GameEvent.Entity_InSolid, info.entityId, null, dens),
                OnInLiquid: (dens) => {
                    inLiquid = true;
                    eventCtrl.RaiseEvent(GameEvent.Entity_InLiquid, info.entityId, null, dens);
                    velocity += EntityJob.cxt.deltaTime * -EntityJob.cxt.gravity;
                }, OnInGas: (dens) => eventCtrl.RaiseEvent(GameEvent.Entity_InGas, info.entityId, null, dens));

            if (inLiquid) tCollider.SetGravityState(LiquidGravityStateName, TerrainCollider.GravityPriority.Environment, false);
            else tCollider.RemoveGravityState(LiquidGravityStateName);

            if (RiderTarget != Guid.Empty) {
                float3 aim = math.normalize(new float3(velocity.x, 0, velocity.z));
                if (Vector3.Magnitude(aim) > 1E-05f) {
                    tCollider.transform.rotation = Quaternion.RotateTowards(tCollider.transform.rotation,
                    Quaternion.LookRotation(aim), settings.rotSpeed * EntityJob.cxt.deltaTime);
                }
            }

            tCollider.Update(this);
            tCollider.EntityCollisionUpdate(this, IgnoredEntities);

            if (!IsDead) return;
            EntityManager.ReleaseEntity(info.entityId);
            var itemReg = Config.CURRENT.Generation.Items;
            if (!itemReg.Contains(settings.ItemDrop)) return;

            //Create boat item
            int index = itemReg.RetrieveIndex(settings.ItemDrop);
            IItem dropItem = itemReg.Retrieve(index).Item;
            dropItem.Create(index, dropItem.UnitSize);

            InventoryController.DropItem(dropItem, position);
        }

        public override void Disable() {
            eventCtrl.Dispose();
            controller.Dispose();
        }

        private class BoatController {
            private Boat entity;
            private Animator animator;
            private GameObject gameObject;
            internal Transform transform;
            internal Transform RideRoot;

            private bool active = false;
            private float angle;

            public BoatController(GameObject GameObject, Entity Entity) {
                this.gameObject = Instantiate(GameObject);
                this.transform = gameObject.transform;
                this.animator = gameObject.GetComponent<Animator>();
                this.entity = (Boat)Entity;
                this.active = true;
                this.angle = 0;

                this.transform.position = CPUMapManager.GSToWS(entity.position);
                this.RideRoot = gameObject.transform.Find("Armature").Find("root").Find("base");

            }

            public void Update() {
                if (!entity.active) return;
                if (gameObject == null) return;
                TerrainCollider.Transform rTransform = entity.tCollider.transform;
                this.transform.SetPositionAndRotation(CPUMapManager.GSToWS(entity.position), rTransform.rotation);

                float minSpeed = math.min(entity.settings.MaxLandSpeed, entity.settings.MaxWaterSpeed) * 0.5f;
                if (Vector2.SqrMagnitude(entity.velocity.xz) < minSpeed * minSpeed) {
                    this.animator.SetBool("Paddle", false);
                    return;
                }

                this.animator.SetBool("Paddle", true);
                this.animator.SetFloat("Direction", Mathf.InverseLerp(-90, 90, angle));
            }

            public void SetAimAngle(float3 aim) { angle = Vector3.Angle(this.entity.velocity, aim); }

            public void Dispose() {
                if (!active) return;
                active = false;

                Destroy(gameObject);
            }
            ~BoatController() {
                Dispose();
            }
        }
    }
}


