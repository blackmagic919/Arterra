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
        public float GroundStickDist;
        public float StickFriction;
        //public int2 SpriteSampleSize;
        //public float AlphaClip;
        //public float ExtrudeHeight;
        public float MaxSpeed;

        public float Acceleration;
    }

    //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
    //**If you release here the controller might still be accessing it
    public class Boat : Entity, IRidable, IAttackable
    {  
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

        public unsafe Boat(){}
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
            tCollider.transform.position = GCoord;
            tCollider.useGravity = true;
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord)
        {
            settings = (BoatSetting)setting;
            controller = new BoatController(Controller, this);
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

            if(tCollider.GetGroundDir(settings.GroundStickDist, settings.collider, EntityJob.cxt.mapContext, out float3 gDir)){
                tCollider.transform.rotation = Quaternion.LookRotation(gDir, math.up());
                tCollider.velocity *= 1 - settings.StickFriction;
            }
            tCollider.Update(settings.collider, this);
            EntityManager.AddHandlerEvent(controller.Update);
        }

        // IRidable implementation
        public Transform GetRiderRoot() {
            return controller.transform;
        }
        public void WalkInDirection(float3 aim) {
            aim = new(aim.x, 0, aim.z);
            Debug.Log($"Walking in direction: {aim}");
            if (Vector3.Magnitude(aim) <= 1E-05f) return;
            if (math.length(tCollider.velocity) > settings.MaxSpeed) return;

            tCollider.velocity += settings.Acceleration *EntityJob.cxt.deltaTime * aim;           
        }
        public void Dismount() { 
            if (RiderTarget == Guid.Empty) return;
            Entity target = EntityManager.GetEntity(RiderTarget);
            if (target == null || target is not IRider rider)
                return;

            EntityManager.AddHandlerEvent(() =>rider.OnDismounted(this));
            RiderTarget = Guid.Empty;
        }

        // IAttackable implementation
        public bool IsDead => false;

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
        ~BoatController(){
            Dispose();
        }

    }
}


