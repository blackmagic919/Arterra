using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Entity;

[CreateAssetMenu(menuName = "Entity/Bird")]
public class Sparrow : Authoring
{
    [UISetting(Ignore = true)][JsonIgnore]
    public Option<SparrowEntity> _Entity;
    public Option<SparrowSetting> _Setting;
    
    [JsonIgnore]
    public override Entity Entity { get => new SparrowEntity(); }
    [JsonIgnore]
    public override EntitySetting Setting { get => _Setting.value; set => _Setting.value = (SparrowSetting)value; }

    [Serializable]
    public class SparrowSetting : EntitySetting{
        public Movement movement;
        public Flight flight;
        public TerrainColliderJob.Settings collider;
        [Serializable]
        public struct Flight{
            public float AverageFlightTime; //120
            public int InfluenceDist; //24
            public float SeperationWeight; //0.75
            public float AlignmentWeight; //0.5
            public float CohesionWeight; //0.25
            public float FlyBiasWeight; //0.25
        }
    }

    //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
    //**If you release here the controller might still be accessing it
    public class SparrowEntity : Entity
    {  
        [JsonIgnore]
        private SparrowController controller;
        public int3 GCoord;
        public PathFinder.PathInfo pathFinder;
        public TerrainColliderJob tCollider;
        public Unity.Mathematics.Random random;
        public float3 flightDirection;
        public float TaskDuration;
        public uint TaskIndex;
        public SparrowSetting settings;
        public static Action<SparrowEntity>[] TaskRegistry = new Action<SparrowEntity>[]{
            Idle,
            FollowPath,
            FollowPath
        };
        public override float3 position {
            get => tCollider.transform.position;
            set => tCollider.transform.position = value;
        }

        public override void Initialize(EntitySetting setting, GameObject Controller, int3 GCoord)
        {
            settings = (SparrowSetting)setting;
            controller = new SparrowController(Controller, this);
            //The seed is the entity's memory address
            this.random = new Unity.Mathematics.Random((uint)GetHashCode());
            this.GCoord = GCoord;
            pathFinder.hasPath = false;
            tCollider.transform.position = GCoord;
            flightDirection = RandomDirection();
            TaskDuration = settings.movement.AverageIdleTime * random.NextFloat(0f, 2f);
            TaskIndex = 0;
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord)
        {
            settings = (SparrowSetting)setting;
            controller = new SparrowController(Controller, this);
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
        public static void Idle(SparrowEntity self){
            if(self.TaskDuration <= 0) {
                self.TaskDuration = self.settings.flight.AverageFlightTime * self.random.NextFloat(0f, 2f);
                self.flightDirection = self.RandomDirection();
                self.TaskIndex = 1;
                self.BoidFly();
            }
            else self.TaskDuration -= EntityJob.cxt.deltaTime;

            //Rotate towards neutral
            ref Quaternion rotation = ref self.tCollider.transform.rotation;
            float3 lookRotation = new (rotation.eulerAngles.x, 0, rotation.eulerAngles.z);
            if(math.any(lookRotation != 0)) rotation = Quaternion.RotateTowards(rotation, Quaternion.LookRotation(lookRotation), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);
        }


        public unsafe void BoidFly(){
            if(TaskDuration <= 0) {
                FindGround();
                return;
            }

            CalculateBoidDirection();
            byte* path = PathFinder.FindPathAlongRay(GCoord, ref flightDirection, settings.movement.pathDistance + 1, settings.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
        }
        public unsafe void FindGround(){
            flightDirection = Normalize(flightDirection + math.down()); 
            int3 dP = (int3)(flightDirection * settings.movement.pathDistance);

            byte* path = PathFinder.FindMatchAlongRay(GCoord, dP, settings.movement.pathDistance + 1, settings.profile, settings.profile, EntityJob.cxt, out int pLen, out bool fGround);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
            if(fGround) TaskIndex = 2;
        }

        public unsafe void CalculateBoidDirection(){
            BoidDMtrx boidDMtrx = new(){
                SeperationDir = float3.zero,
                AlignmentDir = float3.zero,
                CohesionDir = float3.zero,
                count = 0
            };

            unsafe void OnEntityFound(Entity nEntity)
            {
                if(nEntity == null) return;
                if(nEntity.info.entityType != info.entityType) return;
                SparrowEntity nBoid = (SparrowEntity)nEntity;
                float3 nBoidPos = nBoid.tCollider.transform.position;
                float3 boidPos = tCollider.transform.position;

                if(math.distance(boidPos, nBoidPos) < settings.movement.pathDistance) 
                    boidDMtrx.SeperationDir += boidPos - nBoidPos;
                boidDMtrx.AlignmentDir += nBoid.flightDirection;
                boidDMtrx.CohesionDir += nBoidPos;
                boidDMtrx.count++;
            }
            
            EntityManager.ESTree.Query(new EntityManager.STree.TreeNode.Bounds{
                Min = GCoord - new int3(settings.flight.InfluenceDist),
                Max = GCoord + new int3(settings.flight.InfluenceDist)
            }, OnEntityFound);

            if(boidDMtrx.count == 0) return;
            flightDirection = Normalize(flightDirection + 
                settings.flight.SeperationWeight * boidDMtrx.SeperationDir / boidDMtrx.count +
                settings.flight.AlignmentWeight * (boidDMtrx.AlignmentDir / boidDMtrx.count - flightDirection) +
                settings.flight.CohesionWeight * (boidDMtrx.CohesionDir / boidDMtrx.count - tCollider.transform.position) +
                math.up() * random.NextFloat(0, settings.flight.FlyBiasWeight));
        }


        //Task 1 -> Fly, 2 -> Land
        public static unsafe void FollowPath(SparrowEntity self){
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, self.settings.movement.walkSpeed, 
            self.settings.movement.rotSpeed, self.settings.movement.acceleration, AllowVerticalRotation: true);
            
            if(!self.pathFinder.hasPath) {
                if(self.TaskIndex == 1) {
                    self.BoidFly();
                } else {
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

        unsafe struct BoidDMtrx{
            public float3 SeperationDir;
            public float3 AlignmentDir;
            public float3 CohesionDir;
            public uint count;
        } 

        public override void OnDrawGizmos(){
            if(!active) return;
            Gizmos.color = Color.green; 
            Gizmos.DrawWireCube(CPUMapManager.GSToWS(tCollider.transform.position), settings.collider.size * 2);
            float3 location = tCollider.transform.position - settings.collider.offset;
            Gizmos.DrawLine(CPUMapManager.GSToWS(location), CPUMapManager.GSToWS(location + flightDirection));
        }
    }

    private class SparrowController {
        private SparrowEntity entity;
        private Animator animator;
        private GameObject gameObject;
        private Transform transform;
        private bool active = false;

        public SparrowController(GameObject GameObject, SparrowEntity entity){
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

        ~SparrowController(){
            Dispose();
        }
    }
}


