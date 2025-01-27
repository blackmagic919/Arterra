using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Entity;

[CreateAssetMenu(menuName = "Entity/Camel")]
public class Camel : Authoring
{
    [UISetting(Ignore = true)][JsonIgnore]
    public Option<CamelEntity> _Entity;
    public Option<CamelSetting> _Setting;
    
    [JsonIgnore]
    public override Entity Entity { get => new CamelEntity(); }
    [JsonIgnore]
    public override EntitySetting Setting { get => _Setting.value; set => _Setting.value = (CamelSetting)value; }

    [Serializable]
    public class CamelSetting : EntitySetting{
        public Movement movement;
        public IdleBehvaior idle;
        public TerrainColliderJob.Settings collider;
        [Serializable]
        public struct IdleBehvaior{
            public float AverageRestTime; //32
            public float RestProbability; //0.2
        }
    }

    //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
    //**If you release here the controller might still be accessing it
    public class CamelEntity : Entity
    { 
        [JsonIgnore]
        private CamelController controller;
        public int3 GCoord; 
        public uint TaskIndex;
        public float TaskDuration;
        public PathFinder.PathInfo pathFinder;
        public TerrainColliderJob tCollider;
        public Unity.Mathematics.Random random;
        public CamelSetting settings;
        public static Action<CamelEntity>[] TaskRegistry = new Action<CamelEntity>[]{
            Idle,
            Idle,
            GeneratePath,
            FollowPath
        };
        public override float3 position {
            get => tCollider.transform.position;
            set => tCollider.transform.position = value;
        }

        public override void Initialize(EntitySetting setting, GameObject Controller, int3 GCoord)
        {
            settings = (CamelSetting)setting;
            controller = new CamelController(Controller, this);
            //The seed is the entity's memory address
            this.random = new Unity.Mathematics.Random((uint)GetHashCode());
            this.GCoord = GCoord;
            pathFinder.hasPath = false;
            tCollider.transform.position = GCoord;

            //Start by Idling
            TaskDuration = settings.movement.AverageIdleTime * random.NextFloat(0f, 2f);
            TaskIndex = 0;
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord)
        {
            settings = (CamelSetting)setting;
            controller = new CamelController(Controller, this);
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
        public static void Idle(CamelEntity self){
            if(self.TaskDuration <= 0){
                self.TaskIndex = 2;
            }
            else self.TaskDuration -= EntityJob.cxt.deltaTime;
        }
        

        // Task 2
        public static unsafe void GeneratePath(CamelEntity self){
            int PathDist = self.settings.movement.pathDistance;
            int3 dP = new (self.random.NextInt(-PathDist, PathDist), self.random.NextInt(-PathDist, PathDist), self.random.NextInt(-PathDist, PathDist));
            if(PathFinder.VerifyProfile(self.GCoord + dP, self.info.profile, EntityJob.cxt)) {
                byte* path = PathFinder.FindPath(self.GCoord, dP, PathDist + 1, self.info.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
                self.TaskIndex = 3;
            }
        }


        //Task 3
        public static unsafe void FollowPath(CamelEntity self){
            Movement.FollowStaticPath(self.info.profile, ref self.pathFinder, ref self.tCollider, 
            self.settings.movement.walkSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration);
            if(!self.pathFinder.hasPath) {
                bool IsResting = self.random.NextFloat() >= self.settings.idle.RestProbability;
                self.TaskIndex = IsResting ? 1u : 0u;
                self.TaskDuration = IsResting ? self.settings.idle.AverageRestTime : self.settings.movement.AverageIdleTime;
                self.TaskDuration *= self.random.NextFloat(0f, 2f);
                return;
            }
        }

        public override void Disable(){
            controller.Dispose();
        }

        public override void OnDrawGizmos(){
            if(!active) return;
            Gizmos.color = Color.red; 
            Gizmos.DrawWireCube(CPUMapManager.GSToWS(tCollider.transform.position), settings.collider.size * 2);
            PathFinder.PathInfo finder = pathFinder; //Copy so we don't modify the original
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

    public class CamelController
    {
        private CamelEntity entity;
        private GameObject gameObject;
        private Transform transform;
        private Animator animator;
        private bool active = false;

        public CamelController(GameObject GameObject, CamelEntity Entity)
        {
            this.entity = Entity;
            this.gameObject = Instantiate(GameObject);
            this.transform = gameObject.transform;
            this.animator = gameObject.GetComponent<Animator>();
            this.active = true;
            

            float3 GCoord = new (entity.GCoord);
            this.transform.position = CPUMapManager.GSToWS(GCoord - entity.settings.collider.offset) + (float3)Vector3.up * 1;
        }

        public void Update(){
            if(!entity.active) return;
            if(gameObject == null) return;
            EntityManager.AssertEntityLocation(entity, entity.GCoord);    
            TerrainColliderJob.Transform rTransform = entity.tCollider.transform;
            rTransform.position = CPUMapManager.GSToWS(rTransform.position - entity.settings.collider.offset);
            this.transform.SetPositionAndRotation(rTransform.position, rTransform.rotation);

            if(entity.TaskIndex == 3) 
                animator.SetBool("IsWalking", true);
            else animator.SetBool("IsWalking", false);
            if(entity.TaskIndex == 1)
                animator.SetBool("IsResting", true);
            else animator.SetBool("IsResting", false);
            if(entity.TaskIndex == 0 && entity.TaskDuration > 2.0f){
                animator.SetBool("PlayIdle", true);
            } else animator.SetBool("PlayIdle", false);
        }

        public void Dispose(){ 
            if(!active) return;
            active = false;

            Destroy(gameObject);
        }
    }
}


