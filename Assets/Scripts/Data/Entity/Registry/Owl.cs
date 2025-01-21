using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
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
    public override IEntitySetting Setting { get => _Setting.value; set => _Setting.value = (OwlSetting)value; }

    [Serializable]
    public struct OwlSetting : IEntitySetting{
        public Movement movement;
        public Flight flight;
        public TerrainColliderJob.Settings collider;
        [Serializable]
        public struct Movement{
            public float moveSpeed; //15
            public float acceleration; //50
            public float friction; //0.075
            public float rotSpeed;//180
        }
        [Serializable]
        public struct Flight{
            public float AverageIdleTime; //2.5
            public float AverageFlightTime; //120
            public int flightDistance; //4
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
        public static Action<OwlEntity>[] TaskRegistry = new Action<OwlEntity>[]{
            Idle,
            FollowPath,
            FollowPath
        };
        public static OwlSetting settings;
        public override void Preset(IEntitySetting setting){
            settings = (OwlSetting)setting;
        }
        public override void Unset(){ }

        public override void Initialize(GameObject Controller, int3 GCoord){
            controller = new OwlController(Controller, this);
            //The seed is the entity's memory address
            this.random = new Unity.Mathematics.Random((uint)GetHashCode());

            this.GCoord = GCoord;
            pathFinder.hasPath = false;
            tCollider.transform.position = GCoord;
            flightDirection = RandomDirection();
            TaskDuration = settings.flight.AverageIdleTime * random.NextFloat(0f, 2f);
            TaskIndex = 0;
        }

        public override void Deserialize(GameObject Controller, out int3 GCoord){
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
                self.TaskDuration = settings.flight.AverageFlightTime * self.random.NextFloat(0f, 2f);
                self.flightDirection = self.RandomDirection();
                self.TaskIndex = 1;
                self.RandomFly();
            }
            else self.TaskDuration -= EntityJob.cxt.deltaTime;

            //Rotate towards neutral
            ref Quaternion rotation = ref self.tCollider.transform.rotation;
            float3 lookRotation = new (rotation.eulerAngles.x, 0, rotation.eulerAngles.z);
            if(math.length(lookRotation) != 0) rotation = Quaternion.RotateTowards(rotation, Quaternion.LookRotation(lookRotation), settings.movement.rotSpeed * EntityJob.cxt.deltaTime);
        }


        public unsafe void RandomFly(){
            if(TaskDuration <= 0) {
                FindGround();
                return;
            }
            
            flightDirection = RandomDirection() + math.up() * random.NextFloat(0, settings.flight.FlyBiasWeight);
            byte* path = PathFinder.FindPathAlongRay(GCoord, ref flightDirection, settings.flight.flightDistance + 1, info.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
        }

        public unsafe void FindGround(){
            flightDirection = Normalize(flightDirection + math.down()); 
            int3 dP = (int3)(flightDirection * settings.flight.flightDistance);

            byte* path = PathFinder.FindClosestAlongPath(GCoord, dP, settings.flight.flightDistance + 1, info.profile, EntityJob.cxt, out int pLen, out bool fGround);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
            if(fGround) TaskIndex = 2;
        }


        //Task 1 -> Fly, 2 -> Land
        public static unsafe void FollowPath(OwlEntity self){
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            ref PathFinder.PathInfo finder = ref self.pathFinder;
            ref TerrainColliderJob tCollider = ref self.tCollider;
            if(math.any(math.abs(tCollider.transform.position - finder.currentPos) > self.info.profile.bounds)) finder.hasPath = false;
            if(finder.currentInd == finder.path.Length) finder.hasPath = false;
            if(!finder.hasPath) {
                if(self.TaskIndex == 1) 
                    self.RandomFly();
                else {
                    self.TaskIndex = 0;
                    self.TaskDuration = settings.flight.AverageIdleTime * self.random.NextFloat(0f, 2f);
                }
                return;
            }


            byte dir = finder.path[finder.currentInd];
            int3 nextPos = finder.currentPos + new int3((dir / 9) - 1, (dir / 3 % 3) - 1, (dir % 3) - 1);
            if(!PathFinder.VerifyProfile(nextPos, self.info.profile, EntityJob.cxt)) finder.hasPath = false;
            
            if(math.all(self.GCoord == nextPos)){
                finder.currentPos = nextPos;
                finder.currentInd++;
            } else {
                float3 aim = Normalize(nextPos - self.GCoord);
                tCollider.transform.rotation = Quaternion.RotateTowards(tCollider.transform.rotation, 
                                               Quaternion.LookRotation(aim), settings.movement.rotSpeed * EntityJob.cxt.deltaTime);
                if(math.length(tCollider.velocity) < settings.movement.moveSpeed) 
                    tCollider.velocity += settings.movement.acceleration * EntityJob.cxt.deltaTime * aim;
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
            Gizmos.DrawWireCube(CPUDensityManager.GSToWS(tCollider.transform.position), settings.collider.size * 2);
            float3 location = tCollider.transform.position - settings.collider.offset;
            Gizmos.DrawLine(CPUDensityManager.GSToWS(location), CPUDensityManager.GSToWS(location + flightDirection));
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
            transform.position = CPUDensityManager.GSToWS(GCoord - OwlEntity.settings.collider.offset) + (float3)Vector3.up * 1;
        }

        public void Update(){
            if(!entity.active) return;
            if(gameObject == null) return;
            EntityManager.AssertEntityLocation(entity, entity.GCoord);    
            TerrainColliderJob.Transform rTransform = entity.tCollider.transform;
            rTransform.position = CPUDensityManager.GSToWS(rTransform.position - OwlEntity.settings.collider.offset);
            this.transform.SetPositionAndRotation(rTransform.position, rTransform.rotation);

            if(entity.TaskIndex == 1){
                animator.SetBool("IsFlying", true);
                if(entity.tCollider.velocity.y >= 0) animator.SetBool("IsAscending", true);
                else animator.SetBool("IsAscending", false);
            }
            else{
                animator.SetBool("IsFlying", false);
                if(entity.TaskDuration > 2.0f) animator.SetBool("IsHopping", true);
                else animator.SetBool("IsHopping", false);
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


