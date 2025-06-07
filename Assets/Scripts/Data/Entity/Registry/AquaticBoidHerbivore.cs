using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Entity;
using System.Threading.Tasks;

[CreateAssetMenu(menuName = "Generation/Entity/AquaticBoidHerbivore")]
public class AquaticBoidHerbivore : Authoring
{
    [UISetting(Ignore = true)][JsonIgnore]
    public Option<Animal> _Entity;
    public Option<AnimalSetting> _Setting;
    
    [JsonIgnore]
    public override Entity Entity { get => new Animal(); }
    [JsonIgnore]
    public override EntitySetting Setting { get => _Setting.value; set => _Setting.value = (AnimalSetting)value; }

    [Serializable]
    public class AnimalSetting : EntitySetting{
        public Movement movement;
        public Swim swim;
        public Vitality.Aquatic aquatic;
        public Vitality.Decomposition decomposition;
        public Option<Vitality.Stats> physicality;
        public Option<RHerbivore> recognition;
        public RHerbivore Recognition => recognition;
        public Vitality.Stats Physicality => physicality;
        [Serializable]
        public struct Swim{
            public float SwarmTime; //25 sec -> how often to mindlessly swarm
            public float SeperationWeight; //0.75
            public float AlignmentWeight; //0.5
            public float CohesionWeight; //0.25
            public int InfluenceDist; //24
            public int SwimDist; //5
            public int MaxSwarmSize; //8
        }

        public override void Preset(){ 
            uint pEnd = profile.bounds.x * profile.bounds.y * profile.bounds.z;
            aquatic.SurfaceProfile.profileStart = profile.profileStart + pEnd;
            Recognition.Construct();
            base.Preset();
        }
    }

    //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
    //**If you release here the controller might still be accessing it
    public class Animal : Entity, IMateable, IAttackable
    {  
        public Vitality vitality;
        public PathFinder.PathInfo pathFinder;
        public TerrainColliderJob tCollider;
        public Unity.Mathematics.Random random;
        public float3 flightDirection;
        public Guid TaskTarget;
        public float TaskDuration;
        public uint TaskIndex;
        [JsonIgnore]
        private AnimalController controller;
        [JsonIgnore]
        public AnimalSetting settings;
        [JsonIgnore]
        public static Action<Animal>[] TaskRegistry = new Action<Animal>[]{
            Idle,
            FollowFlight,
            FindPrey,
            ChasePrey,
            EatFood,
            FindMate,
            ChaseMate,
            Reproduce,
            SwimUp,
            Surface,
            RunFromTarget,
            ChaseTarget,
            AttackTarget,
            RunFromPredator,
            FlopOnGround,
            Death
        };
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
        public void TakeDamage(float damage, float3 knockback, Entity attacker){
            if(!vitality.Damage(damage)) return;
            Indicators.DisplayDamageParticle(position, knockback);
            tCollider.velocity += knockback;
            
            if(IsDead) return;
            if(attacker == null) return; //If environmental damage, we don't need to retaliate
            TaskTarget = attacker.info.entityId;
            Recognition.Recognizable recog = settings.Recognition.Recognize(attacker);
            if(recog.IsPredator) TaskIndex = 10u; //if predator run away
            else if(recog.IsMate) TaskIndex = 11u; //if mate fight back
            else if(recog.IsPrey) TaskIndex = 11u; //if prey fight back
            else TaskIndex = settings.Recognition.FightAggressor ? 11u : 10u; //if unknown, depends
            if(TaskIndex == 11 && attacker is not IAttackable) TaskIndex = 10u;  //Don't try to attack a non-attackable entity
            pathFinder.hasPath = false;
        }

        public void ProcessFallDamage(float zVelDelta){
            if(zVelDelta <= Vitality.FallDmgThresh) return;
            float damage = zVelDelta - Vitality.FallDmgThresh;    
            damage = math.pow(damage, settings.Physicality.weight);
            EntityManager.AddHandlerEvent(() => TakeDamage(damage, 0, null));
        }
        public WorldConfig.Generation.Item.IItem Collect(float amount){
            if(!IsDead) return null; //You can't collect resources until the entity is dead
            var item = settings.decomposition.LootItem(amount, ref random);
            TaskDuration -= settings.decomposition.DecompPerLoot * amount;
            return item;
        }

        //Not thread safe
        public bool CanMateWith(Entity entity){
            if(vitality.healthPercent < settings.Physicality.MateThreshold) return false;
            if(vitality.IsDead) return false;
            if(TaskIndex >= 7) return false;
            return settings.Recognition.CanMateWith(entity);
        }

        public void MateWith(Entity entity){
            if(!CanMateWith(entity)) return;
            if(settings.Recognition.MateWithEntity(entity, ref random))
                vitality.Damage(settings.Physicality.MateCost);
            TaskDuration = settings.Physicality.PregnacyLength;
            TaskIndex = 7;
        }

        public override void Initialize(EntitySetting setting, GameObject Controller, int3 GCoord)
        {
            settings = (AnimalSetting)setting;
            controller = new AnimalController(Controller, this);
            //The seed is the entity's memory address
            this.random = new Unity.Mathematics.Random((uint)GetHashCode());
            this.vitality = new Vitality(settings.Physicality, ref random);
            this.tCollider = new TerrainColliderJob(GCoord, true, ProcessFallDamage);

            pathFinder.hasPath = false;
            flightDirection = RandomDirection();
            TaskDuration = settings.movement.AverageIdleTime * random.NextFloat(0f, 2f);
            TaskTarget = Guid.Empty;
            TaskIndex = 0;
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord)
        {
            settings = (AnimalSetting)setting;
            controller = new AnimalController(Controller, this);
            vitality.Deserialize(settings.Physicality);
            tCollider.OnHitGround = ProcessFallDamage;
            random.state ^= (uint)GetHashCode();
            GCoord = this.GCoord;
        }

        public override void Update()
        {
            if(!active) return;
            //use gravity if not flying
            tCollider.Update(EntityJob.cxt, settings.collider);
            if(!tCollider.useGravity) tCollider.velocity.y *= 1 - settings.collider.friction;
            EntityManager.AddHandlerEvent(controller.Update);

            Recognition.DetectMapInteraction(position, 
                OnInSolid: (dens) => vitality.ProcessSuffocation(this, dens),
                OnInLiquid: (dens) => vitality.ProcessInLiquidAquatic(this, ref tCollider, dens, settings.aquatic.DrownTime),
                OnInGas:(dens) => {
                    vitality.ProcessInGasAquatic(this, ref tCollider, dens);
                    if(TaskIndex < 14) TaskIndex = 14; //Flop on ground
            });

            vitality.Update();
            TaskRegistry[(int)TaskIndex].Invoke(this);
            if(TaskIndex != 15 && vitality.IsDead) {
                TaskDuration = settings.decomposition.DecompositionTime;
                flightDirection = 0;
                TaskIndex = 15;
            } else if(TaskIndex <= 7 && IsSurfacing()) TaskIndex = 8; 
            else if(TaskIndex <= 12)  DetectPredator();
        }

        private unsafe void DetectPredator(){
            if(!settings.Recognition.FindClosestPredator(this, out Entity predator))
                return;

            int PathDist = settings.Recognition.FleeDistance;
            flightDirection = position - predator.position;
            byte* path = PathFinder.FindPathAlongRay(GCoord, ref flightDirection, PathDist + 1, settings.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
            flightDirection = math.normalize(flightDirection);
            TaskIndex = 13;
        }


        public unsafe void BoidFly(){
            if(TaskDuration <= 0){
                TaskDuration = settings.movement.AverageIdleTime * random.NextFloat(0f, 2f);
                TaskIndex = 0; //Go Idle
                return;
            } if (vitality.healthPercent > settings.Physicality.MateThreshold || 
                vitality.healthPercent < settings.Physicality.HuntThreshold){
                TaskDuration = math.min(0, TaskDuration); //try to land to mate
            }  TaskIndex = 1;

            CalculateBoidDirection();
            byte* path = PathFinder.FindPathAlongRay(GCoord, ref flightDirection, settings.swim.SwimDist + 1, settings.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
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
                Animal nBoid = (Animal)nEntity;
                float3 nBoidPos = nBoid.tCollider.transform.position;
                float3 boidPos = tCollider.transform.position;

                if(math.all(nBoid.flightDirection == 0)) return;
                if(math.distance(boidPos, nBoidPos) < settings.swim.SwimDist) 
                    boidDMtrx.SeperationDir += boidPos - nBoidPos;
                boidDMtrx.AlignmentDir += nBoid.flightDirection;
                boidDMtrx.CohesionDir += nBoidPos;
                boidDMtrx.count++;
            }
            
            EntityManager.ESTree.Query(new ((float3)GCoord,
                2 * new float3(settings.swim.InfluenceDist)), 
            OnEntityFound);

            if(boidDMtrx.count == 0) return;
            float3 influenceDir;
            if(boidDMtrx.count > settings.swim.MaxSwarmSize) //the sign of seperation is flipped for this case
                influenceDir = settings.swim.SeperationWeight * boidDMtrx.SeperationDir / boidDMtrx.count - 
                settings.swim.CohesionWeight * (boidDMtrx.CohesionDir / boidDMtrx.count - position);
            else influenceDir = settings.swim.SeperationWeight * boidDMtrx.SeperationDir / boidDMtrx.count +
                settings.swim.AlignmentWeight * (boidDMtrx.AlignmentDir / boidDMtrx.count - flightDirection) +
                settings.swim.CohesionWeight * (boidDMtrx.CohesionDir / boidDMtrx.count - position);
            
            flightDirection = Normalize(flightDirection + influenceDir);
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

        private unsafe bool IsSurfacing(){
            if(vitality.breath > 0) return false; //In air
            if(settings.aquatic.SurfaceThreshold == 0) return false; //Doesn't drown
            if(-vitality.breath > settings.aquatic.SurfaceThreshold * settings.aquatic.DrownTime) 
                return false; //Still holding breath
            return true;
        }

        //Task 0
        public static void Idle(Animal self){
            if(self.vitality.healthPercent < self.settings.Physicality.HuntThreshold) 
                self.TaskIndex = 2;
            else if(self.vitality.healthPercent > self.settings.Physicality.MateThreshold) 
                self.TaskIndex = 5;
            if(self.TaskDuration <= 0) {
                self.flightDirection = self.RandomDirection();
                self.TaskDuration = self.settings.swim.SwarmTime * self.random.NextFloat(0f, 2f);
                self.BoidFly();
                return;
            } else self.TaskDuration -= EntityJob.cxt.deltaTime;

            //Rotate towards neutral
            ref Quaternion rotation = ref self.tCollider.transform.rotation;
            float3 lookRotation = new (rotation.eulerAngles.x, 0, rotation.eulerAngles.z);
            if(math.any(lookRotation != 0)) rotation = Quaternion.RotateTowards(rotation, Quaternion.LookRotation(lookRotation), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);
        }
        
        //Task 1 -> Fly 
        public static unsafe void FollowFlight(Animal self){
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, 
            self.settings.movement.walkSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration, true);

            if(!self.pathFinder.hasPath) {
                self.TaskDuration = self.settings.swim.SwarmTime * self.random.NextFloat(0f, 2f);
                self.BoidFly();
            }
        }


        //Task 2
        private static unsafe void FindPrey(Animal self){
            if(!self.settings.Recognition.FindPreferredPrey((int3)math.round(self.position), out int3 preyPos)){
                self.TaskDuration = self.settings.swim.SwarmTime * self.random.NextFloat(0f, 2f);
                self.BoidFly();
                return;
            }
            byte* path = PathFinder.FindPathOrApproachTarget(self.GCoord, preyPos - self.GCoord, self.settings.Recognition.PlantFindDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = 3;

            //If it can't get to the prey and is currently at the closest position it can be
            if(math.all(self.pathFinder.destination == self.GCoord)){
                float dist = math.distance(preyPos, self.position);
                if(dist <= self.settings.Physicality.AttackDistance){
                    self.TaskDuration = 1 / math.max(self.settings.Physicality.ConsumptionRate, 0.0001f);
                    self.TaskIndex = 4;
                } else {
                    self.TaskDuration = self.settings.swim.SwarmTime * self.random.NextFloat(0f, 2f);
                    self.BoidFly();
                }
            } 
        }

        //Task 3
        private static unsafe void ChasePrey(Animal self){
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, 
            self.settings.movement.runSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration, true);
            if(self.pathFinder.hasPath) return;

            if(self.settings.Recognition.FindPreferredPrey((int3)math.round(self.position), out int3 preyPos) && 
            math.distance(preyPos, self.position) <= self.settings.Physicality.AttackDistance){
                self.TaskDuration = 1 / math.max(self.settings.Physicality.ConsumptionRate, 0.0001f);
                self.TaskIndex = 4;
            } else self.TaskIndex = 2;
        }

        //Task 4
        private static unsafe void EatFood(Animal self){
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if(self.TaskDuration <= 0){
                if(self.settings.Recognition.FindPreferredPrey((int3)math.round(self.position), out int3 foodPos)){
                    WorldConfig.Generation.Item.IItem item = self.settings.Recognition.ConsumeFood(foodPos);
                    if(item != null && self.settings.Recognition.CanConsume(item, out float nutrition))
                        self.vitality.Heal(nutrition);  
                    self.TaskDuration = self.settings.swim.SwarmTime * self.random.NextFloat(0f, 2f);
                    self.BoidFly();
                } else self.TaskIndex = 2;
            }
        }

        //Task 5
        private static unsafe void FindMate(Animal self){
            if(self.vitality.healthPercent < self.settings.Physicality.MateThreshold ||
              !self.settings.Recognition.FindPreferredMate(self, out Entity mate)){
                self.TaskDuration = self.settings.swim.SwarmTime * self.random.NextFloat(0f, 2f);
                self.BoidFly();
                return;
            }
            int PathDist = self.settings.movement.pathDistance;
            int3 destination = (int3)mate.origin - self.GCoord;
            byte* path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = 6;
        }

        //Task 6
        private static unsafe void ChaseMate(Animal self){//I feel you man
            if(!self.settings.Recognition.FindPreferredMate(self, out Entity mate)){
                self.TaskIndex = 5;
                return;
            }

            Movement.FollowDynamicPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, mate.origin,
            self.settings.movement.runSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration, true);
            float mateDist = math.distance(self.position, mate.position);
            if(mateDist < self.settings.Physicality.AttackDistance) {
                EntityManager.AddHandlerEvent(() => (mate as IMateable).MateWith(self));
                self.MateWith(mate);
                return;
            } if(!self.pathFinder.hasPath) {
                self.TaskIndex = 5;
                return;
            }
        }

        //Task 7 (I will never get here)
        private static void Reproduce(Animal self){
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if(self.TaskDuration > 0) return;
            self.TaskDuration = self.settings.swim.SwarmTime * self.random.NextFloat(0f, 2f);
            self.BoidFly();
        }

        //Task 8 swim up
        private static unsafe void SwimUp(Animal self){
            float swimIntensity = self.settings.aquatic.DrownTime * self.settings.aquatic.SurfaceThreshold / math.min(-self.vitality.breath, -0.001f);
            float3 swimDir = Normalize(self.RandomDirection() + math.up() * math.max(0, swimIntensity)); 
            byte* path = PathFinder.FindMatchAlongRay(self.GCoord, in swimDir, self.settings.movement.pathDistance + 1, self.settings.profile, 
            self.settings.aquatic.SurfaceProfile, EntityJob.cxt, out int pLen, out bool _);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = 9;
        }

        //Task 9 follow swim up path
        private static unsafe void Surface(Animal self){
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, 
            self.settings.movement.runSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration, true);
            if(!self.pathFinder.hasPath) {
                self.TaskIndex = 8;
                return;
            } if(!self.IsSurfacing()){
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 0; //Go back to swiming
            }
        }


        //Task 10
        private static unsafe void RunFromTarget(Animal self){
            Entity target = EntityManager.GetEntity(self.TaskTarget);
            if(target == null) self.TaskTarget = Guid.Empty;
            else if(math.distance(self.position, target.position) > self.settings.Recognition.SightDistance)
                self.TaskTarget = Guid.Empty;
            if(self.TaskTarget == Guid.Empty) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 0;
                return;
            }

            if(!self.pathFinder.hasPath) {
                int PathDist = self.settings.Recognition.FleeDistance;
                float3 rayDir = self.position - target.position;
                byte* path = PathFinder.FindPathAlongRay(self.GCoord, ref rayDir, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            } 
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, 
            self.settings.movement.runSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration, true);
        }
        
        //Task 11
        private static unsafe void ChaseTarget(Animal self){
            Entity target = EntityManager.GetEntity(self.TaskTarget);
            if(target == null) 
                self.TaskTarget = Guid.Empty;
            else if(math.distance(self.position, target.position) > self.settings.Recognition.SightDistance)
                self.TaskTarget = Guid.Empty;
            if(self.TaskTarget == Guid.Empty) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 0;
                return;
            }

            if(!self.pathFinder.hasPath) {
                int PathDist = self.settings.movement.pathDistance;
                int3 destination = (int3)math.round(target.origin) - self.GCoord;
                byte* path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            } 
            Movement.FollowDynamicPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, target.origin,
            self.settings.movement.runSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration, true);
            if(math.distance(self.position, target.position) < self.settings.Physicality.AttackDistance) {
                self.TaskIndex = 12;
                return;
            }
        }

        //Task 12
        private static void AttackTarget(Animal self){
            Entity tEntity = EntityManager.GetEntity(self.TaskTarget);
            if(tEntity == null) 
                self.TaskTarget = Guid.Empty;
            else if(tEntity is not IAttackable)
                self.TaskTarget = Guid.Empty;
            if(self.TaskTarget == Guid.Empty) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 0;
                return;
            }
            float targetDist = math.distance(tEntity.position, self.position);
            if(targetDist > self.settings.Physicality.AttackDistance) {
                self.TaskIndex = 11;
                return;
            }

            float3 atkDir = math.normalize(tEntity.position - self.position); atkDir.y = 0;
            self.tCollider.transform.rotation = Quaternion.RotateTowards(self.tCollider.transform.rotation, 
            Quaternion.LookRotation(atkDir), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);

            IAttackable target = tEntity as IAttackable;
            if(target.IsDead) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 0;
            } else self.vitality.Attack(tEntity, self);
        }


        //Task 13
        private static unsafe void RunFromPredator(Animal self){
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, self.settings.movement.runSpeed, 
            self.settings.movement.rotSpeed, self.settings.movement.acceleration, true);
            if(!self.pathFinder.hasPath) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 0;
            }
        }

        //Task 14
        private static void FlopOnGround(Animal self){
            if(self.vitality.breath < 0) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 0; //Idle
                return;
            }

            if(self.tCollider.SampleCollision(self.origin,  new float3(self.settings.collider.size.x, 
            -self.settings.aquatic.JumpStickDistance, self.settings.collider.size.z), EntityJob.cxt.mapContext, out _)) {
                self.tCollider.velocity.y += self.settings.aquatic.JumpStrength;
            }
        }

        //Task 15
        private static void Death(Animal self){
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if(!self.IsDead){ //Bring back from the dead 
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 0;
                return;
            }
            //Kill the entity
            if(self.TaskDuration <= 0) EntityManager.ReleaseEntity(self.info.entityId);
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
            Gizmos.DrawWireCube(CPUMapManager.GSToWS(position), settings.collider.size * 2);
            Gizmos.DrawLine(CPUMapManager.GSToWS(position), CPUMapManager.GSToWS(position + flightDirection));
        }
    }

    private class AnimalController {
        private Animal entity;
        private Animator animator;
        private GameObject gameObject;
        private Transform transform;
        private bool active = false;
        private int AnimatorTask;
        private static readonly string[] AnimationNames = new string[]{
            "IsIdling",  "IsWalking", null, "IsRunning", "IsEating", null,
            "IsRunning", "IsCuddling", "IsRunning", null, "IsRunning", "IsRunning",
            "IsAttacking", "IsRunning", "IsFlopping", "IsDead"
        };

        public AnimalController(GameObject GameObject, Animal entity){
            this.entity = entity;
            this.gameObject = Instantiate(GameObject);
            this.transform = gameObject.transform;
            this.animator = gameObject.GetComponent<Animator>();
            this.active = true;
            this.AnimatorTask = 0;

            Indicators.SetupIndicators(gameObject);
            float3 GCoord = new (entity.GCoord);
            transform.position = CPUMapManager.GSToWS(entity.position);
        }

        public void Update(){
            if(!entity.active) return;
            if(gameObject == null) return;
            TerrainColliderJob.Transform rTransform = entity.tCollider.transform;
            this.transform.SetPositionAndRotation(CPUMapManager.GSToWS(entity.position), rTransform.rotation);
 #if UNITY_EDITOR
            if(UnityEditor.Selection.Contains(gameObject)) {
                Debug.Log(entity.TaskIndex);
            }
 #endif

            Indicators.UpdateIndicators(gameObject, entity.vitality, entity.pathFinder);
            if(AnimatorTask == entity.TaskIndex) return;
            if(AnimationNames[AnimatorTask] != null) animator.SetBool(AnimationNames[AnimatorTask], false);
            AnimatorTask = (int)entity.TaskIndex;
            if(AnimationNames[AnimatorTask] != null) animator.SetBool(AnimationNames[AnimatorTask], true);
        }

        public void Dispose(){
            if(!active) return;
            active = false;

            Destroy(gameObject);
        }

        ~AnimalController(){
            Dispose();
        }
    }
}


