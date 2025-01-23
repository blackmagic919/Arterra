using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using System;
using Unity.Collections;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Entity;

[CreateAssetMenu(menuName = "Entity/Rabbit")]
public class Rabbit : Authoring
{
    [UISetting(Ignore = true)][JsonIgnore]
    public Option<RabbitEntity> _Entity;
    public Option<RabbitSetting> _Setting;

    [JsonIgnore]
    public override Entity Entity { get => new RabbitEntity(); }
    [JsonIgnore]
    public override IEntitySetting Setting { get => _Setting.value; set => _Setting.value = (RabbitSetting)value; }

    [Serializable]
    public struct RabbitSetting : IEntitySetting{
        public Movement movement;
        public TerrainColliderJob.Settings collider;

        [Serializable]
        public struct Movement{
            public float GroundStickDist; //0.05
            public float moveSpeed; //4
            public float acceleration; //50
            public float friction; //0.075
            public float rotSpeed;//180
            public int pathDistance;//31
            public float AverageIdleTime; //2.5
        }
    }

    [BurstCompile]
    //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
    //**If you release here the controller might still be accessing it
    public class RabbitEntity : Entity
    {  
        [JsonIgnore]
        private RabbitController controller;
        public int3 GCoord; 
        public uint TaskIndex;
        public float TaskDuration;
        public PathFinder.PathInfo pathFinder;
        public TerrainColliderJob tCollider;
        public Unity.Mathematics.Random random;

        public static Action<RabbitEntity>[] TaskRegistry = new Action<RabbitEntity>[]{
            Idle,
            GeneratePath,
            FollowPath
        };

        public static RabbitSetting settings;
        public override void Preset(IEntitySetting setting){
            settings = (RabbitSetting)setting;
        }
        public override void Unset(){ }

        public override void Initialize(GameObject Controller, int3 GCoord){
            controller = new RabbitController(Controller, this);
            //The seed is the entity's memory address
            this.random = new Unity.Mathematics.Random((uint)GetHashCode());
            this.GCoord = GCoord;
            pathFinder.hasPath = false;
            tCollider.transform.position = GCoord;
            //Start by Idling
            TaskDuration = settings.movement.AverageIdleTime * random.NextFloat(0f, 2f);
            TaskIndex = 0;
        }

        public override void Deserialize(GameObject Controller, out int3 GCoord)
        {
            controller = new RabbitController(Controller, this);
            GCoord = this.GCoord;
        }


        public override void Update()
        {
            if(!active) return;
            GCoord = (int3)tCollider.transform.position;
            TaskRegistry[(int)TaskIndex].Invoke(this);

            tCollider.Update(EntityJob.cxt, settings.collider);
            tCollider.velocity.xz *= 1 - settings.movement.friction;
            EntityManager.AddHandlerEvent(controller.Update);
        }

        //Task 0
        public static void Idle(RabbitEntity self){
            if(self .TaskDuration <= 0){
                self.TaskIndex = 1;
            }
            else self.TaskDuration -= EntityJob.cxt.deltaTime;
        }

        // Task 1
        public static unsafe void GeneratePath(RabbitEntity self){
            int PathDist = settings.movement.pathDistance;
            int3 dP = new (self.random.NextInt(-PathDist, PathDist), self.random.NextInt(-PathDist, PathDist), self.random.NextInt(-PathDist, PathDist));
            if(PathFinder.VerifyProfile(self.GCoord + dP, self.info.profile, EntityJob.cxt)) {
                byte* path = PathFinder.FindPath(self.GCoord, dP, PathDist + 1, self.info.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
                self.TaskIndex = 2;
            }
        }

        //Task 2
        public static unsafe void FollowPath(RabbitEntity self){
            ref PathFinder.PathInfo finder = ref self.pathFinder;
            ref TerrainColliderJob tCollider = ref self.tCollider;

            //Entity has fallen off path
            if(math.any(math.abs(tCollider.transform.position - finder.currentPos) > self.info.profile.bounds)) finder.hasPath = false;
            if(finder.currentInd == finder.path.Length) finder.hasPath = false;
            if(!finder.hasPath) {
                self.TaskIndex = 0;
                self.TaskDuration = settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                return;
            }

            byte dir = finder.path[finder.currentInd];
            int3 nextPos = finder.currentPos + new int3((dir / 9) - 1, (dir / 3 % 3) - 1, (dir % 3) - 1);
            if(!PathFinder.VerifyProfile(nextPos, self.info.profile, EntityJob.cxt)) finder.hasPath = false;

            if(math.all(math.abs(self.GCoord - nextPos) <= 1)){
                finder.currentPos = nextPos;
                finder.currentInd++;
            } else {
                float3 aim = math.normalize(nextPos - self.GCoord);
                Quaternion rot = tCollider.transform.rotation;
                if(math.any(aim.xz != 0)) rot = Quaternion.LookRotation(new Vector3(aim.x, 0, aim.z));
                tCollider.transform.rotation = Quaternion.RotateTowards(tCollider.transform.rotation, rot, settings.movement.rotSpeed * EntityJob.cxt.deltaTime);
                if(math.length(tCollider.velocity) < settings.movement.moveSpeed) 
                    tCollider.velocity += settings.movement.acceleration * EntityJob.cxt.deltaTime * aim;
            }
        }

        public override void Disable(){
            controller.Dispose();
        }

        public override void OnDrawGizmos(){
            if(!active) return;
            Gizmos.color = Color.red; 
            Gizmos.DrawWireCube(CPUMapManager.GSToWS(tCollider.transform.position), settings.collider.size * 2);
            PathFinder.PathInfo finder = pathFinder; //copy so we don't actual
            if(finder.hasPath){
                int ind = finder.currentInd;
                while(ind != finder.path.Length){
                    int dir = finder.path[ind];
                    int3 dest = finder.currentPos + new int3((dir / 9) - 1, (dir / 3 % 3) - 1, (dir % 3) - 1);
                    Gizmos.DrawLine(CPUMapManager.GSToWS(finder.currentPos - settings.collider.offset),
                                    CPUMapManager.GSToWS(dest - settings.collider.offset));
                    finder.currentPos = dest;
                    ind++;
                }
            }
        }
    }

    private class RabbitController{
        private RabbitEntity entity;
        private Animator animator;
        private GameObject gameObject;
        private Transform transform;
        private bool active = false;
        public RabbitController(GameObject GameObject, RabbitEntity entity){
            this.entity = entity;
            this.gameObject = Instantiate(GameObject);
            this.transform = gameObject.transform;
            this.animator = gameObject.GetComponent<Animator>();
            this.active = true;

            float3 GCoord = new (entity.GCoord);
            transform.position = CPUMapManager.GSToWS(GCoord - RabbitEntity.settings.collider.offset) + (float3)Vector3.up * 1;
        }

        public void Update(){
            if(!entity.active) return;
            if(gameObject == null) return;
            EntityManager.AssertEntityLocation(entity, entity.GCoord);    
            TerrainColliderJob.Transform rTransform = entity.tCollider.transform;
            rTransform.position = CPUMapManager.GSToWS(rTransform.position - RabbitEntity.settings.collider.offset);
            this.transform.SetPositionAndRotation(rTransform.position, rTransform.rotation);

            if(entity.TaskIndex == 2)  
                animator.SetBool("IsMoving", true);
            else {
                animator.SetBool("IsMoving", false);
                if(entity.TaskDuration > 2.0f) animator.SetBool("IsScratching", true);
                else animator.SetBool("IsScratching", false);
            }
        }

        public void Dispose(){
            if(!active) return;
            active = false;

            Destroy(gameObject);
        }

        ~RabbitController(){
            Dispose();
        }
    }
}


