using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Entity;
using UnityEngine.Profiling;

[CreateAssetMenu(menuName = "Generation/Entity/SkyCarnivore")]
public class SkyCarnivore : Authoring
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
        public Flight flight;
        public Vitality.Decomposition decomposition;
        public Option<RCarnivore> recognition;
        public Option<Vitality.Stats> physicality;
        public RCarnivore Recognition => recognition;
        public Vitality.Stats Physicality => physicality;
        [Serializable]
        public struct Flight{
            //Starts after the profile of the ground entity
            public ProfileInfo profile;
            public float AverageFlightTime; //120
            public float FlyBiasWeight; //0.25\
            [Range(0, 1)]
            public float VerticalFreedom;
        }
        public override void Preset(){ 
            uint pEnd = profile.bounds.x * profile.bounds.y * profile.bounds.z;
            flight.profile.profileStart = profile.profileStart + pEnd;
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
            FindFlight,
            FollowFlight,
            FollowLanding,
            FindPrey,
            ChasePrey,
            Attack,
            FindMate,
            ChaseMate,
            Reproduce,
            RunFromTarget,
            ChaseTarget,
            AttackTarget,
            RunFromPredator,
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
            if(TaskIndex >= 9) return false;
            return settings.Recognition.CanMateWith(entity);
        }
        public void MateWith(Entity entity){
            if(!CanMateWith(entity)) return;
            if(settings.Recognition.MateWithEntity(entity, ref random))
                vitality.Damage(settings.Physicality.MateCost);
            TaskDuration = settings.Physicality.PregnacyLength;
            TaskIndex = 9;
        }

        public override void Initialize(EntitySetting setting, GameObject Controller, int3 GCoord){
            settings = (AnimalSetting)setting;
            controller = new AnimalController(Controller, this);
            //The seed is the entity's memory address
            this.random = new Unity.Mathematics.Random((uint)GetHashCode());
            this.vitality = new Vitality(settings.Physicality, ref random);
            this.tCollider = new TerrainColliderJob(GCoord, true, ProcessFallDamage);

            pathFinder.hasPath = false;
            TaskDuration = settings.movement.AverageIdleTime * random.NextFloat(0f, 2f);
            TaskTarget = Guid.Empty;
            TaskIndex = 0;
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord){
            settings = (AnimalSetting)setting;
            controller = new AnimalController(Controller, this);
            vitality.Deserialize(settings.Physicality);
            tCollider.OnHitGround = ProcessFallDamage;
            GCoord = this.GCoord;
        }


        public override void Update()
        {
            if (!active) return;
            Profiler.BeginSample($"Sampling task {TaskIndex}");
            //use gravity if not flying
            tCollider.Update(EntityJob.cxt, settings.collider);
            if (!tCollider.useGravity) tCollider.velocity.y *= 1 - settings.collider.friction;
            EntityManager.AddHandlerEvent(controller.Update);

            vitality.Update();
            TaskRegistry[(int)TaskIndex].Invoke(this);
            if (TaskIndex != 14 && vitality.IsDead)
            {
                TaskDuration = settings.decomposition.DecompositionTime;
                TaskIndex = 14;
            }
            else if (TaskIndex <= 12) DetectPredator();
            Profiler.EndSample();
            
            Recognition.DetectMapInteraction(position, 
            OnInSolid: (dens) => vitality.ProcessSuffocation(this, dens),
            OnInLiquid: (dens) => vitality.ProcessInLiquid(this, ref tCollider, dens),
            OnInGas: vitality.ProcessInGas);
        }

        //Always detect unless already running from predator
        private unsafe void DetectPredator(){
            if(!settings.Recognition.FindClosestPredator(this, out Entity predator))
                return;

            int PathDist = settings.Recognition.FleeDistance;
            float3 rayDir = position - predator.position;
            byte* path = PathFinder.FindPathAlongRay(GCoord, ref rayDir, PathDist + 1, settings.flight.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
            TaskIndex = 13;
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

        public unsafe void RandomFly(){
            float3 flightDir = Normalize(RandomDirection() + math.up() * random.NextFloat(0, settings.flight.FlyBiasWeight));
            flightDir.y *= settings.flight.VerticalFreedom;
            byte* path = PathFinder.FindPathAlongRay(GCoord, ref flightDir, settings.movement.pathDistance + 1, settings.flight.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
        }

        public unsafe bool FindGround(){
            float3 flightDir = Normalize(RandomDirection() + math.down() * math.max(0, -TaskDuration / settings.flight.AverageFlightTime)); 
            int3 dP = (int3)(flightDir * settings.movement.pathDistance);

            //Use the ground profile
            byte* path = PathFinder.FindMatchAlongRay(GCoord, dP, settings.movement.pathDistance + 1, settings.flight.profile, settings.profile, EntityJob.cxt, out int pLen, out bool fGround);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
            return fGround;
        }

        //Task 0
        public static void Idle(Animal self){
            self.tCollider.useGravity = true;
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if(self.TaskDuration <= 0) {
                self.TaskDuration = self.settings.flight.AverageFlightTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 1;
                return;
            } if(self.vitality.healthPercent > self.settings.Physicality.MateThreshold){
                self.TaskIndex = 7;
                return;
            }
            //Rotate towards neutral
            ref Quaternion rotation = ref self.tCollider.transform.rotation;
            float3 lookRotation = new (rotation.eulerAngles.x, 0, rotation.eulerAngles.z);
            if(math.length(lookRotation) != 0) rotation = Quaternion.RotateTowards(rotation, Quaternion.LookRotation(lookRotation), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);
        }

        //Task 1 -> Land
        public static unsafe void FindFlight(Animal self){
            self.tCollider.useGravity = false;
            if(self.TaskDuration <= 0){
                bool fGround = self.FindGround();
                if(fGround) self.TaskIndex = 3;
                else self.TaskIndex = 2;
                return;
            }
            if (self.vitality.healthPercent > self.settings.Physicality.MateThreshold){
                self.TaskDuration = math.min(0, self.TaskDuration); //try to land to mate
                return;
            }  
            if(self.vitality.healthPercent < self.settings.Physicality.HuntThreshold){
                self.TaskIndex = 4;
                return;
            }
            self.RandomFly();
            self.TaskIndex = 2;
        }

        //Task 2 -> Fly 
        public static unsafe void FollowFlight(Animal self){
            self.tCollider.useGravity = false;
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            Movement.FollowStaticPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider, 
            self.settings.movement.runSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration, true);

            if(!self.pathFinder.hasPath) {
                self.TaskIndex = 1;
            }
        }

        //Task 3 -> Landing 
        public static unsafe void FollowLanding(Animal self){
            self.tCollider.useGravity = false;
            Movement.FollowStaticPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider, 
            self.settings.movement.runSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration, true);

            if(!self.pathFinder.hasPath) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 0; //Landed
            }
        }

        //Task 4 - Find Prey
        private static unsafe void FindPrey(Animal self){
            self.tCollider.useGravity = false;
            //Use mate threshold not hunt because the entity may lose the target while eating
            if(!self.settings.Recognition.FindPreferredPrey(self, out Entity prey)){
                self.TaskIndex = 2;
                self.RandomFly();
                return;   
            }

            int PathDist = self.settings.movement.pathDistance;
            int3 destination = (int3)math.round(prey.origin) - self.GCoord;
            byte* path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.flight.profile, EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = 5;

            //If it can't get to the prey and is currently at the closest position it can be
            if(math.all(self.pathFinder.destination == self.GCoord) && Recognition.GetColliderDist(prey, self) > self.settings.Physicality.AttackDistance){
                self.TaskIndex = 2;
                self.RandomFly();
            } 
        }

        //Task 5 - Chase Prey
        private static unsafe void ChasePrey(Animal self){
            self.tCollider.useGravity = false;
            if(!self.settings.Recognition.FindPreferredPrey(self, out Entity prey)){
                self.TaskIndex = 4;
                return;
            }
            Movement.FollowDynamicPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider, prey.origin,
            self.settings.movement.runSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration, true);
            float preyDist = Recognition.GetColliderDist(self, prey);
            if(preyDist < self.settings.Physicality.AttackDistance) {
                self.TaskIndex = 6;
                return;
            } if(!self.pathFinder.hasPath) {
                self.TaskIndex = 4;
                return;
            }
        }

        //Task 6 - Attack
        private static void Attack(Animal self){
            self.tCollider.useGravity = false;
            self.TaskIndex = 4;
            if(!self.settings.Recognition.FindPreferredPrey(self, out Entity prey)) return;
            float preyDist = Recognition.GetColliderDist(self, prey);
            if(preyDist > self.settings.Physicality.AttackDistance) return;
            if(prey is not IAttackable) return;
            self.TaskIndex = 6;

            float3 atkDir = math.normalize(prey.position - self.position); atkDir.y = 0;
            self.tCollider.transform.rotation = Quaternion.RotateTowards(self.tCollider.transform.rotation, 
            Quaternion.LookRotation(atkDir), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);

            IAttackable target = (IAttackable)prey;
            if(target.IsDead) {
                EntityManager.AddHandlerEvent(() => {
                WorldConfig.Generation.Item.IItem item = target.Collect(self.settings.Physicality.ConsumptionRate);
                if(item != null && self.settings.Recognition.CanConsume(item, out float nutrition)){
                    self.vitality.Heal(nutrition);  
                } if(self.vitality.healthPercent >= 1){
                    self.TaskIndex = 1;
                }}); 
            } else self.vitality.Attack(prey, self);
        }

        private unsafe void RandomWalk(){
            if(pathFinder.hasPath){
                Movement.FollowStaticPath(settings.profile, ref pathFinder, ref tCollider, 
                settings.movement.walkSpeed, settings.movement.rotSpeed, settings.movement.acceleration);
                return;
            }

            int PathDist = settings.movement.pathDistance;
            int3 dP = new (random.NextInt(-PathDist, PathDist), random.NextInt(-PathDist, PathDist), random.NextInt(-PathDist, PathDist));
            if(PathFinder.VerifyProfile(GCoord + dP, settings.profile, EntityJob.cxt)) {
                byte* path = PathFinder.FindPath(GCoord, dP, PathDist + 1, settings.profile, EntityJob.cxt, out int pLen);
                pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
            }
        }

        //Task 7
        private static unsafe void FindMate(Animal self){
            self.tCollider.useGravity = true;
            if(self.vitality.healthPercent < self.settings.Physicality.MateThreshold){
                self.TaskIndex = 0;
                return;
            }
            if(!self.settings.Recognition.FindPreferredMate(self, out Entity mate)){
                self.RandomWalk();
                return;   
            }
            int PathDist = self.settings.movement.pathDistance;
            int3 destination = (int3)mate.origin - self.GCoord;
            byte* path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = 8;
        }

        //Task 8
        private static unsafe void ChaseMate(Animal self){//I feel you man
            self.tCollider.useGravity = true;
            if(!self.settings.Recognition.FindPreferredMate(self, out Entity mate)){
                self.TaskIndex = 7;
                return;
            }

            Movement.FollowDynamicPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, mate.origin,
            self.settings.movement.walkSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration);
            float mateDist = Recognition.GetColliderDist(self, mate);
            if(mateDist < self.settings.Physicality.AttackDistance) {
                EntityManager.AddHandlerEvent(() => (mate as IMateable).MateWith(self));
                self.MateWith(mate);
                return;
            } if(!self.pathFinder.hasPath) {
                self.TaskIndex = 7;
                return;
            }
        }


        //Task 9 (I will never get here)
        private static void Reproduce(Animal self){
            self.tCollider.useGravity = true;
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if(self.TaskDuration > 0) return;
            self.TaskIndex = 1;
        }

        //Task 10
        private static unsafe void RunFromTarget(Animal self){
            self.tCollider.useGravity = false;
            Entity target = EntityManager.GetEntity(self.TaskTarget);
            if(target == null) self.TaskTarget = Guid.Empty;
            else if(Recognition.GetColliderDist(self, target) > self.settings.Recognition.SightDistance)
                self.TaskTarget = Guid.Empty;
            if(self.TaskTarget == Guid.Empty) {
                self.TaskIndex = 1;
                return;
            }

            if(!self.pathFinder.hasPath) {
                int PathDist = self.settings.Recognition.FleeDistance;
                float3 rayDir = self.position - target.position;
                byte* path = PathFinder.FindPathAlongRay(self.GCoord, ref rayDir, PathDist + 1, self.settings.flight.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            } 
            Movement.FollowStaticPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider, 
            self.settings.movement.runSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration, true);
        }
        
        //Task 11
        private static unsafe void ChaseTarget(Animal self){
            self.tCollider.useGravity = false;
            Entity target = EntityManager.GetEntity(self.TaskTarget);
            if(target == null) 
                self.TaskTarget = Guid.Empty;
            else if(Recognition.GetColliderDist(self, target) > self.settings.Recognition.SightDistance)
                self.TaskTarget = Guid.Empty;
            if(self.TaskTarget == Guid.Empty) {
                self.TaskIndex = 1;
                return;
            }

            if(!self.pathFinder.hasPath) {
                int PathDist = self.settings.movement.pathDistance;
                int3 destination = (int3)math.round(target.origin) - self.GCoord;
                byte* path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.flight.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            } 
            Movement.FollowDynamicPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider, target.position,
            self.settings.movement.runSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration, true);
            if(Recognition.GetColliderDist(self, target) < self.settings.Physicality.AttackDistance) {
                self.TaskIndex = 12;
                return;
            }
        }

        //Task 12
        private static void AttackTarget(Animal self){
            self.tCollider.useGravity = false;
            Entity tEntity = EntityManager.GetEntity(self.TaskTarget);
            if(tEntity == null) 
                self.TaskTarget = Guid.Empty;
            else if(tEntity is not IAttackable)
                self.TaskTarget = Guid.Empty;
            if(self.TaskTarget == Guid.Empty) {
                self.TaskIndex = 1;
                return;
            }
            float targetDist = Recognition.GetColliderDist(tEntity, self);
            if(targetDist > self.settings.Physicality.AttackDistance) {
                self.TaskIndex = 11;
                return;
            }

            float3 atkDir = math.normalize(tEntity.position - self.position); atkDir.y = 0;
            self.tCollider.transform.rotation = Quaternion.RotateTowards(self.tCollider.transform.rotation, 
            Quaternion.LookRotation(atkDir), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);

            IAttackable target = tEntity as IAttackable;
            if(target.IsDead) self.TaskIndex = 1;
            else self.vitality.Attack(tEntity, self);
        }


        //Task 13
        private static unsafe void RunFromPredator(Animal self){
            self.tCollider.useGravity = false;
            Movement.FollowStaticPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider, self.settings.movement.runSpeed, 
            self.settings.movement.rotSpeed, self.settings.movement.acceleration, true);
            if(!self.pathFinder.hasPath) {
                self.TaskIndex = 1;
            }
        }

        //Task 14
        private static void Death(Animal self){
            self.tCollider.useGravity = true;
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if(!self.IsDead){ //Bring back from the dead 
                self.TaskIndex = 0;
                return;
            }
            //Kill the entity
            if(self.TaskDuration <= 0) EntityManager.ReleaseEntity(self.info.entityId);
        }
        

        public override void Disable(){
            controller.Dispose();
        }

        public override void OnDrawGizmos(){
            if(!active) return;
            Gizmos.color = Color.magenta; 
            Gizmos.DrawWireCube(CPUMapManager.GSToWS(position), settings.collider.size * 2);
            PathFinder.PathInfo finder = pathFinder; //copy so we don't modify the original
            if(finder.hasPath){
                int ind = finder.currentInd;
                while(ind != finder.path.Length){
                    int dir = finder.path[ind];
                    int3 dest = finder.currentPos + new int3((dir / 9) - 1, (dir / 3 % 3) - 1, (dir % 3) - 1);
                    Gizmos.DrawLine(CPUMapManager.GSToWS(finder.currentPos), 
                                    CPUMapManager.GSToWS(dest));
                    finder.currentPos = dest;
                    ind++;
                }
            }
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
            "IsIdling",  "IsFlying", "IsFlying",  "IsFlying", "IsFlying", 
            "IsFlying", "IsAttacking",  "IsWalking", "IsWalking",  "IsCuddling", 
            "IsFlying", "IsFlying", "IsAttacking", "IsFlying",  "IsDead"
        };

        public AnimalController(GameObject GameObject, Animal entity){
            this.entity = entity;
            this.gameObject = Instantiate(GameObject);
            this.transform = gameObject.transform;
            this.animator = gameObject.GetComponent<Animator>();
            this.active = true;
            this.AnimatorTask = 0;

            Indicators.SetupIndicators(gameObject);
            transform.position = CPUMapManager.GSToWS(entity.position);
        }

        public void Update(){
            if(!entity.active) return;
            if(gameObject == null) return;
            TerrainColliderJob.Transform rTransform = entity.tCollider.transform;
            this.transform.SetPositionAndRotation(CPUMapManager.GSToWS(entity.position), rTransform.rotation);
// #if UNITY_EDITOR
//            if(UnityEditor.Selection.Contains(gameObject)) {
//                Debug.Log(entity.TaskIndex);
//            }
// #endif
            
            Indicators.UpdateIndicators(gameObject, entity.vitality, entity.pathFinder);
            if(AnimatorTask == entity.TaskIndex) return;
            if(AnimationNames[AnimatorTask] != null) animator.SetBool(AnimationNames[AnimatorTask], false);
            AnimatorTask = (int)entity.TaskIndex;
            if(AnimationNames[AnimatorTask] != null) animator.SetBool(AnimationNames[AnimatorTask], true);
            if(AnimationNames[AnimatorTask] == "IsFlying"){
                if(entity.tCollider.velocity.y >= 0) animator.SetBool("IsAscending", true);
                else animator.SetBool("IsAscending", false);
            }

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


