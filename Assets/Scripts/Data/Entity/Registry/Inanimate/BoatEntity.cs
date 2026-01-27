using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using Arterra.Configuration;
using Arterra.Configuration.Generation.Item;
using Arterra.Configuration.Generation.Entity;

using Arterra.Core.Storage;
using Arterra.Core.Player;
using Arterra.Core.Events;

[CreateAssetMenu(menuName = "Generation/Entity/Boat")]
public class BoatEntity : Arterra.Configuration.Generation.Entity.Authoring {
    public Option<BoatSetting> _Setting;
    public static Catalogue<Arterra.Configuration.Generation.Item.Authoring> ItemRegistry => Config.CURRENT.Generation.Items;

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
    public class Boat : Entity, IRidable, IAttackable {
        [JsonProperty]
        private MinimalVitality vitality;
        [JsonProperty]
        private TerrainCollider tCollider;
        [JsonProperty]
        private Guid RiderTarget = Guid.Empty;
        private BoatController controller;
        private BoatSetting settings;
        [JsonIgnore]
        public override ref TerrainCollider.Transform transform => ref tCollider.transform;
        [JsonIgnore]
        public int3 GCoord => (int3)math.floor(origin);
        [JsonIgnore]
        public bool IsDead => vitality.IsDead;

        public void Dismount() {
            if (RiderTarget == Guid.Empty) return;
            if (!EntityManager.TryGetEntity(RiderTarget, out Entity target) ||
                target is not IRider rider)
                return;

            EntityManager.AddHandlerEvent(() => rider.OnDismounted(this));
            RiderTarget = Guid.Empty;
        }

        public void Interact(Entity caller) {
            if (caller == null) return;
            if (caller is not IRider rider) return;
            if (RiderTarget != Guid.Empty) return; //Already has a rider
            RiderTarget = caller.info.entityId;
            EntityManager.AddHandlerEvent(() => rider.OnMounted(this));

        }
        public IItem Collect(Entity caller, float amount) {
            IItem item = null;
            eventCtrl.RaiseEvent(GameEvent.Entity_Collect, this, caller, (item, amount));
            return item; // Boats are not collectible
        }

        public void TakeDamage(float damage, float3 knockback, Entity attacker = null) {
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
                PlayerHandler.data.collider.SampleCollision(origin, new float3(
                settings.collider.size.x, -settings.groundStickDist, settings.collider.size.z), out _)
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

        public Boat() { }
        public override void Initialize(EntitySetting setting, GameObject Controller, float3 GCoord) {
            settings = (BoatSetting)setting;
            vitality = new MinimalVitality(settings.durability);
            tCollider = new TerrainCollider(this.settings.collider, GCoord);
            controller = new BoatController(Controller, this);
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord) {
            settings = (BoatSetting)setting;
            controller = new BoatController(Controller, this);
            if (vitality == null) vitality = new MinimalVitality(settings.durability);
            else vitality.Deserialize(settings.durability);

            tCollider.useGravity = true;
            GCoord = this.GCoord;

            if (RiderTarget == Guid.Empty) return;
            if (EntityManager.TryGetEntity(RiderTarget, out Entity entity)
                && entity is IRider rider)
                EntityManager.AddHandlerEvent(() => rider.OnMounted(this));
        }


        public override void Update() {
            if (!active) return;

            tCollider.useGravity = true;

            vitality.Update(this);
            TerrainInteractor.DetectMapInteraction(position,
                OnInSolid: (dens) => eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InSolid, this, null, dens),
                OnInLiquid: (dens) => {
                    eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InLiquid, this, null, dens);
                    velocity += EntityJob.cxt.deltaTime * -EntityJob.cxt.gravity;
                    tCollider.useGravity = false;
                }, OnInGas: (dens) => eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InGas, this, null, dens));

            if (RiderTarget != Guid.Empty) {
                float3 aim = math.normalize(new float3(velocity.x, 0, velocity.z));
                if (Vector3.Magnitude(aim) > 1E-05f) {
                    tCollider.transform.rotation = Quaternion.RotateTowards(tCollider.transform.rotation,
                    Quaternion.LookRotation(aim), settings.rotSpeed * EntityJob.cxt.deltaTime);
                }
            }

            tCollider.Update(this);
            EntityManager.AddHandlerEvent(controller.Update);

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
            controller.Dispose();
        }

        private class BoatController {
            private Boat entity;
            private Animator animator;
            private GameObject gameObject;
            internal Transform transform;
            internal Transform RideRoot;
            private Indicators indicators;

            private bool active = false;
            private float angle;

            public BoatController(GameObject GameObject, Entity Entity) {
                this.gameObject = Instantiate(GameObject);
                this.transform = gameObject.transform;
                this.animator = gameObject.GetComponent<Animator>();
                this.entity = (Boat)Entity;
                this.active = true;
                this.angle = 0;

                this.indicators = new Indicators(gameObject, entity);
                this.transform.position = CPUMapManager.GSToWS(entity.position);
                this.RideRoot = gameObject.transform.Find("Armature").Find("root").Find("base");

            }

            public void Update() {
                if (!entity.active) return;
                if (gameObject == null) return;
                TerrainCollider.Transform rTransform = entity.tCollider.transform;
                this.transform.SetPositionAndRotation(CPUMapManager.GSToWS(entity.position), rTransform.rotation);
                indicators.Update();

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

                indicators.Release();
                Destroy(gameObject);
            }
            ~BoatController() {
                Dispose();
            }
        }
    }
}


