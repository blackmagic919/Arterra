using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Item;
using WorldConfig.Generation.Entity;
using MapStorage;

[CreateAssetMenu(menuName = "Generation/Entity/Boat")]
public class BoatEntity : WorldConfig.Generation.Entity.Authoring
{
    [UISetting(Ignore = true)][JsonIgnore]
    public Option<Boat> _Entity;
    public Option<BoatSetting> _Setting;
    public static Catalogue<WorldConfig.Generation.Item.Authoring> ItemRegistry => Config.CURRENT.Generation.Items;
    
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
        private TerrainColliderJob tCollider;
        [JsonProperty]
        private Guid RiderTarget = Guid.Empty;

        private BoatController controller;
        private BoatSetting settings;
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
        public bool IsDead => vitality.IsDead;

        public void Dismount() {
            if (RiderTarget == Guid.Empty) return;
            Entity target = EntityManager.GetEntity(RiderTarget);
            if (target == null || target is not IRider rider)
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
        public IItem Collect(float collectRate) {
            return null; // Boats are not collectible
        }

        public void TakeDamage(float damage, float3 knockback, Entity attacker = null) {
            if (!vitality.Damage(damage)) return;
            Indicators.DisplayDamageParticle(position, knockback);
            tCollider.velocity += knockback;
        }

        // IRidable implementation
        public Transform GetRiderRoot() => controller.RideRoot;
        public void WalkInDirection(float3 aim) {
            aim = new(aim.x, 0, aim.z);
            if (Vector3.Magnitude(aim) <= 1E-05f) return;
            this.controller.SetAimAngle(aim);

            if (math.length(tCollider.velocity) <= settings.MaxLandSpeed &&
                PlayerHandler.data.collider.SampleCollision(origin, new float3(
                settings.collider.size.x, -settings.groundStickDist, settings.collider.size.z), out _)
            ) {
                tCollider.velocity += settings.Acceleration * EntityJob.cxt.deltaTime * aim;
            } else {
                float3 basePos = position + (float3)(Vector3.down * settings.groundStickDist);
                TerrainInteractor.DetectMapInteraction(basePos, OnInSolid: null,
                    OnInLiquid: (dens) => {
                        if (math.length(tCollider.velocity) > settings.MaxWaterSpeed) return;
                        tCollider.velocity += settings.Acceleration * EntityJob.cxt.deltaTime * aim;
                    }, OnInGas: null
                );
            }
        }

        public unsafe Boat() { }
        public override void Initialize(EntitySetting setting, GameObject Controller, float3 GCoord) {
            settings = (BoatSetting)setting;
            controller = new BoatController(Controller, this);
            vitality = new MinimalVitality(settings.durability);
            tCollider.transform.position = GCoord;
            tCollider.useGravity = true;
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord) {
            settings = (BoatSetting)setting;
            controller = new BoatController(Controller, this);
            if (vitality == null) vitality = new MinimalVitality(settings.durability);
            else vitality.Deserialize(settings.durability);

            tCollider.useGravity = true;
            GCoord = this.GCoord;

            if (RiderTarget == Guid.Empty) return;
            Entity entity = EntityManager.GetEntity(RiderTarget);
            if (entity != null && entity is IRider rider)
            EntityManager.AddHandlerEvent(() => rider.OnMounted(this));
        }


        public override void Update() {
            if (!active) return;

            tCollider.useGravity = true;

            vitality.Update();
            TerrainInteractor.DetectMapInteraction(position, OnInSolid: null,
            OnInLiquid: (dens) => {
                tCollider.velocity += EntityJob.cxt.deltaTime * -EntityJob.cxt.gravity;
                tCollider.velocity.y *= 1 - settings.collider.friction;
                tCollider.useGravity = false;
            }, OnInGas: null);

            if (RiderTarget != Guid.Empty) {
                float3 aim = math.normalize(new float3(tCollider.velocity.x, 0, tCollider.velocity.z));
                if (Vector3.Magnitude(aim) > 1E-05f) {
                    tCollider.transform.rotation = Quaternion.RotateTowards(tCollider.transform.rotation,
                    Quaternion.LookRotation(aim), settings.rotSpeed * EntityJob.cxt.deltaTime);
                }
            }

            tCollider.Update(settings.collider, this);
            EntityManager.AddHandlerEvent(controller.Update);

            if (!IsDead) return;
            EntityManager.ReleaseEntity(info.entityId);
            var itemReg = Config.CURRENT.Generation.Items;
            if (!itemReg.Contains(settings.ItemDrop)) return;

            //Create boat item
            int index = itemReg.RetrieveIndex(settings.ItemDrop);
            IItem dropItem = itemReg.Retrieve(index).Item;
            dropItem.Create(index, 0xFF);

            InventoryController.DropItem(dropItem, position);
        }

        public override void Disable() {
            controller.Dispose();
        }
        
        private class BoatController
        {
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
                TerrainColliderJob.Transform rTransform = entity.tCollider.transform;
                this.transform.SetPositionAndRotation(CPUMapManager.GSToWS(entity.position), rTransform.rotation);

                float minSpeed = math.min(entity.settings.MaxLandSpeed, entity.settings.MaxWaterSpeed) * 0.5f;
                if (Vector2.SqrMagnitude(entity.tCollider.velocity.xz) < minSpeed * minSpeed) {
                    this.animator.SetBool("Paddle", false);
                    return;
                }

                this.animator.SetBool("Paddle", true);
                if (angle <= -15) this.animator.SetTrigger("Left");
                else if (angle >= 15) this.animator.SetTrigger("Right");
                else this.animator.SetTrigger("Forward");
            }

            public void SetAimAngle(float3 aim) { angle = Vector3.Angle(this.entity.tCollider.velocity, aim); }

            public void Dispose() {
                if (!active) return;
                active = false;

                Destroy(gameObject);
            }
            ~BoatController(){
                Dispose();
            }
        }
    }
}


