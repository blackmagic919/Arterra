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
using System.Collections.Generic;
using WorldConfig.Gameplay.Player;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
        //public int2 SpriteSampleSize;
        //public float AlphaClip;
        //public float ExtrudeHeight;
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
    public class Boat : Entity, IRidable, IAttackable
    {  
        public MinimalVitality vitality;
        public TerrainColliderJob tCollider;
        public Unity.Mathematics.Random random;

        private Guid RiderTarget = Guid.Empty;

        [JsonIgnore]
        private BoatController controller;
        [JsonIgnore]
        public BoatSetting settings;
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

        public unsafe Boat() { }
        public Boat(TerrainColliderJob.Transform origin){
            tCollider.transform = origin;
            tCollider.velocity = 0;
            
            this.random = new Unity.Mathematics.Random((uint)GetHashCode());
        } 

        //This function shouldn't be used
        public override void Initialize(EntitySetting setting, GameObject Controller, int3 GCoord)
        {
            settings = (BoatSetting)setting;
            controller = new BoatController(Controller, this);
            vitality = new MinimalVitality(settings.durability, ref random);
            tCollider.transform.position = GCoord;
            tCollider.useGravity = true;
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord)
        {
            settings = (BoatSetting)setting;
            controller = new BoatController(Controller, this);
            if(vitality == null) vitality = new MinimalVitality(settings.durability, ref random);
            else vitality.Deserialize(settings.durability);

            tCollider.useGravity = true;
            GCoord = this.GCoord;
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

        // IRidable implementation
        public Transform GetRiderRoot() {
            return controller.transform;
        }
        public void WalkInDirection(float3 aim) {
            aim = new(aim.x, 0, aim.z);
            if (Vector3.Magnitude(aim) <= 1E-05f) return;

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
        public void Dismount() { 
            if (RiderTarget == Guid.Empty) return;
            Entity target = EntityManager.GetEntity(RiderTarget);
            if (target == null || target is not IRider rider)
                return;

            EntityManager.AddHandlerEvent(() =>rider.OnDismounted(this));
            RiderTarget = Guid.Empty;
        }

        public void Interact(Entity caller) {
            if (caller == null) return;
            if (caller is not IRider rider) return;
            if (RiderTarget != Guid.Empty) return; //Already has a rider
            RiderTarget = caller.info.entityId;
            EntityManager.AddHandlerEvent(() => rider.OnMounted(this));

        }
        public WorldConfig.Generation.Item.IItem Collect(float collectRate) {
            return null; // Boats are not collectible
        }

        public void TakeDamage(float damage, float3 knockback, Entity attacker = null) {
            if (!vitality.Damage(damage)) return;
            Indicators.DisplayDamageParticle(position, knockback);
            tCollider.velocity += knockback;
        }

        public override void Disable() {
            controller.Dispose();
        }
    }

    public class BoatController
    {
        private Boat entity;
        private GameObject gameObject;
        internal Transform transform;

        private bool active = false;

        private MeshFilter meshFilter;

        public BoatController(GameObject GameObject, Entity Entity)
        {
            this.gameObject = Instantiate(GameObject);
            this.transform = gameObject.transform;
            this.entity = (Boat)Entity;
            this.active = true;

            float3 GCoord = new (entity.GCoord);
            this.transform.position = CPUMapManager.GSToWS(entity.position);

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
        ~BoatController(){
            Dispose();
        }

    }
}


