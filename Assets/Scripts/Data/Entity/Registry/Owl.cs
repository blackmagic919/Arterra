using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Entity;

[CreateAssetMenu(menuName = "Entity/Owl")]
public class Owl : Authoring
{
    [UISetting(Ignore = true)][JsonIgnore]
    public Option<OwlEntity> _Entity;
    public Option<OwlSetting> _Setting;

    [JsonIgnore]
    public override Entity Entity { get => new OwlEntity(); }
    [JsonIgnore]
    public override EntitySetting Setting { get => _Setting.value; set => _Setting.value = (OwlSetting)value; }

    [Serializable]
    public class OwlSetting : EntitySetting{
        public Movement movement;
        public Flight flight;
        public TerrainColliderJob.Settings collider;
        [Serializable]
        public struct Flight{
            public float AverageFlightTime; //120
            public float FlyBiasWeight; //0.25

        }
    }

    //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
    //**If you release here the controller might still be accessing it
    public class OwlEntity : Entity
    {  
        //This is the real-time position streamed by the controller
        [JsonIgnore]
        private OwlController controller;
        public int3 GCoord;
        public PathFinder.PathInfo pathFinder;
        public TerrainColliderJob tCollider;
        public Unity.Mathematics.Random random;
        public float3 flightDirection;
        public float TaskDuration;
        public uint TaskIndex;
        public OwlSetting settings;
        public static Action<OwlEntity>[] TaskRegistry = new Action<OwlEntity>[]{
            Idle,
            FollowPath,
            FollowPath
        };
        public override float3 position {
            get => tCollider.transform.position;
            set => tCollider.transform.position = value;
        }

        public override void Initialize(EntitySetting setting, GameObject Controller, int3 GCoord){
            settings = (OwlSetting)setting;
            controller = new OwlController(Controller, this);
            //The seed is the entity's memory address
            this.random = new Unity.Mathematics.Random((uint)GetHashCode());

            this.GCoord = GCoord;
            pathFinder.hasPath = false;
            tCollider.transform.position = GCoord;
            flightDirection = RandomDirection();
            TaskDuration = settings.movement.AverageIdleTime * random.NextFloat(0f, 2f);
            TaskIndex = 0;
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord){
            settings = (OwlSetting)setting;
            controller = new OwlController(Controller, this);
            GCoord = this.GCoord;
        }


        public override void Update()
        {
            if(!active) return;
            GCoord = (int3)math.floor(tCollider.transform.position);
            TaskRegistry[(int)TaskIndex].Invoke(this);

            tCollider.Update(EntityJob.cxt, settings.collider);
            tCollider.velocity *= 1 - settings.movement.friction;
            EntityManager.AddHandlerEvent(controller.Update);
        }

        //Task 0
        public static void Idle(OwlEntity self){
            if(self.TaskDuration <= 0) {
                self.TaskDuration = self.settings.flight.AverageFlightTime * self.random.NextFloat(0f, 2f);
                self.flightDirection = self.RandomDirection();
                self.TaskIndex = 1;
                self.RandomFly();
            }
            else self.TaskDuration -= EntityJob.cxt.deltaTime;

            //Rotate towards neutral
            ref Quaternion rotation = ref self.tCollider.transform.rotation;
            float3 lookRotation = new (rotation.eulerAngles.x, 0, rotation.eulerAngles.z);
            if(math.length(lookRotation) != 0) rotation = Quaternion.RotateTowards(rotation, Quaternion.LookRotation(lookRotation), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);
        }


        public unsafe void RandomFly(){
            if(TaskDuration <= 0) {
                FindGround();
                return;
            }
            
            flightDirection = RandomDirection() + math.up() * random.NextFloat(0, settings.flight.FlyBiasWeight);
            byte* path = PathFinder.FindPathAlongRay(GCoord, ref flightDirection, settings.movement.pathDistance + 1, info.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
        }

        public unsafe void FindGround(){
            flightDirection = Normalize(flightDirection + math.down()); 
            int3 dP = (int3)(flightDirection * settings.movement.pathDistance);

            byte* path = PathFinder.FindClosestAlongPath(GCoord, dP, settings.movement.pathDistance + 1, info.profile, EntityJob.cxt, out int pLen, out bool fGround);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
            if(fGround) TaskIndex = 2;
        }


        //Task 1 -> Fly, 2 -> Land
        public static unsafe void FollowPath(OwlEntity self){
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            Movement.FollowStaticPath(self.info.profile, ref self.pathFinder, ref self.tCollider, 
            self.settings.movement.walkSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration);

            if(!self.pathFinder.hasPath) {
                if(self.TaskIndex == 1) 
                    self.RandomFly();
                else {
                    self.TaskIndex = 0;
                    self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                }
                return;
            }
        }

        private static float3 Normalize(float3 v){
            if(math.length(v) == 0) return math.forward();
            else return math.normalize(v);
            //This norm guarantees the vector will be on the edge of a cube
        }

        private float3 RandomDirection(){
            float3 normal = new (random.NextFloat(-1, 1), random.NextFloat(-1, 1), random.NextFloat(-1, 1));
            if(math.length(normal) == 0) return math.forward();
            else return Normalize(normal);
        }

        public override void Disable(){
            controller.Dispose();
        }

        public override void OnDrawGizmos(){
            if(!active) return;
            Gizmos.color = Color.green; 
            Gizmos.DrawWireCube(CPUMapManager.GSToWS(tCollider.transform.position), settings.collider.size * 2);
            float3 location = tCollider.transform.position - settings.collider.offset;
            Gizmos.DrawLine(CPUMapManager.GSToWS(location), CPUMapManager.GSToWS(location + flightDirection));
        }
    }

    private class OwlController {
        private OwlEntity entity;
        private Animator animator;
        private GameObject gameObject;
        private Transform transform;
        private bool active = false;

        public OwlController(GameObject GameObject, OwlEntity entity){
            this.entity = entity;
            this.gameObject = Instantiate(GameObject);
            this.transform = gameObject.transform;
            this.animator = gameObject.GetComponent<Animator>();
            this.active = true;

            float3 GCoord = new (entity.GCoord);
            transform.position = CPUMapManager.GSToWS(GCoord - entity.settings.collider.offset) + (float3)Vector3.up * 1;
        }

        public void Update(){
            if(!entity.active) return;
            if(gameObject == null) return;
            EntityManager.AssertEntityLocation(entity, entity.GCoord);    
            TerrainColliderJob.Transform rTransform = entity.tCollider.transform;
            rTransform.position = CPUMapManager.GSToWS(rTransform.position - entity.settings.collider.offset);
            this.transform.SetPositionAndRotation(rTransform.position, rTransform.rotation);

            if(entity.TaskIndex == 1){
                animator.SetBool("IsFlying", true);
                if(entity.tCollider.velocity.y >= 0) animator.SetBool("IsAscending", true);
                else animator.SetBool("IsAscending", false);
            }
            else{
                animator.SetBool("IsFlying", false);
                if(entity.TaskDuration > 2.0f) animator.SetBool("PlayIdle", true);
                else animator.SetBool("PlayIdle", false);
            }
        }

        public void Dispose(){
            if(!active) return;
            active = false;

            Destroy(gameObject);
        }

        ~OwlController(){
            Dispose();
        }
    }
}


