using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Entity;

[CreateAssetMenu(menuName = "Generation/Entity/SkyBoidHerbivore")]
public class SkyBoidHerbivore : Authoring
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
        public RHerbivore recognition;
        public Vitality.Stats physicality;
        public Vitality.Decomposition decomposition;
        [Serializable]
        public struct Flight{
            public ProfileInfo profile;
            public float AverageFlightTime; //120
            public float SeperationWeight; //0.75
            public float AlignmentWeight; //0.5
            public float CohesionWeight; //0.25
            public int InfluenceDist; //24
            public int PathDist; //5
            public int MaxSwarmSize; //8
        }

        public override void Preset(){ 
            uint pEnd = profile.bounds.x * profile.bounds.y * profile.bounds.z;
            flight.profile.profileStart = profile.profileStart + pEnd;
            recognition.Construct();
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
            FollowLanding,
            FindPrey,
            ChasePrey,
            EatFood,
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
            Recognition.Recognizable recog = settings.recognition.Recognize(attacker);
            if(recog.IsPredator) TaskIndex = 9u; //if predator run away
            else if(recog.IsMate) TaskIndex = 10u; //if mate fight back
            else if(recog.IsPrey) TaskIndex = 10u; //if prey fight back
            else TaskIndex = settings.recognition.FightAggressor ? 10u : 9u; //if unknown, depends
            if(TaskIndex == 10 && attacker is not IAttackable) TaskIndex = 9u;  //Don't try to attack a non-attackable entity
            pathFinder.hasPath = false;
        }

        public void ProcessFallDamage(float zVelDelta){
            if(zVelDelta <= Vitality.FallDmgThresh) return;
            float damage = zVelDelta - Vitality.FallDmgThresh;    
            damage = math.pow(damage, settings.physicality.weight);
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
            if(vitality.healthPercent < settings.physicality.MateThreshold) return false;
            if(vitality.IsDead) return false;
            if(TaskIndex >= 8) return false;
            return settings.recognition.CanMateWith(entity);
        }

        public void MateWith(Entity entity){
            if(!CanMateWith(entity)) return;
            if(settings.recognition.MateWithEntity(entity, ref random))
                vitality.Damage(settings.physicality.MateCost);
            TaskDuration = settings.physicality.PregnacyLength;
            TaskIndex = 8;
        }

        public override void Initialize(EntitySetting setting, GameObject Controller, int3 GCoord)
        {
            settings = (AnimalSetting)setting;
            controller = new AnimalController(Controller, this);
            //The seed is the entity's memory address
            this.random = new Unity.Mathematics.Random((uint)GetHashCode());
            this.vitality = new Vitality(settings.physicality, ref random);
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
            vitality.Deserialize(settings.physicality);
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
            OnInLiquid: (dens) => vitality.ProcessInLiquid(this, ref tCollider, dens),
            OnInGas: vitality.ProcessInGas);

            vitality.Update();
            TaskRegistry[(int)TaskIndex].Invoke(this);
            if(TaskIndex != 13 && vitality.IsDead) {
                TaskDuration = settings.decomposition.DecompositionTime;
                flightDirection = 0;
                TaskIndex = 13;
            } else if(TaskIndex <= 11)  DetectPredator();
        }

        private unsafe void DetectPredator(){
            if(!settings.recognition.FindClosestPredator(this, out Entity predator))
                return;

            int PathDist = settings.recognition.FleeDistance;
            flightDirection = position - predator.position;
            byte* path = PathFinder.FindPathAlongRay(GCoord, ref flightDirection, PathDist + 1, settings.flight.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
            flightDirection = math.normalize(flightDirection);
            TaskIndex = 12;
        }


        public unsafe void BoidFly(){
            if (vitality.healthPercent > settings.physicality.MateThreshold || 
                vitality.healthPercent < settings.physicality.HuntThreshold){
                TaskDuration = math.min(0, TaskDuration); //try to land to mate
            }  
            TaskIndex = 1;
            if(TaskDuration <= 0){
                FindGround();
                return;
            }

            CalculateBoidDirection();
            byte* path = PathFinder.FindPathAlongRay(GCoord, ref flightDirection, settings.flight.PathDist + 1, settings.flight.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
        }

        public unsafe void FindGround(){
            flightDirection = Normalize(flightDirection + math.down()); 
            int3 dP = (int3)(flightDirection * settings.flight.PathDist);

            byte* path = PathFinder.FindMatchAlongRay(GCoord, dP, settings.flight.PathDist + 1, settings.flight.profile, settings.profile, EntityJob.cxt, out int pLen, out bool fGround);
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
                Animal nBoid = (Animal)nEntity;
                float3 nBoidPos = nBoid.tCollider.transform.position;
                float3 boidPos = tCollider.transform.position;

                if(math.all(nBoid.flightDirection == 0)) return;
                if(math.distance(boidPos, nBoidPos) < settings.flight.PathDist) 
                    boidDMtrx.SeperationDir += boidPos - nBoidPos;
                boidDMtrx.AlignmentDir += nBoid.flightDirection;
                boidDMtrx.CohesionDir += nBoidPos;
                boidDMtrx.count++;
            }
            
            EntityManager.ESTree.Query(new ((float3)GCoord,
                2 * new float3(settings.flight.InfluenceDist)), 
            OnEntityFound);

            if(boidDMtrx.count == 0) return;
            float3 influenceDir;
            if(boidDMtrx.count > settings.flight.MaxSwarmSize) //the sign of seperation is flipped for this case
                influenceDir = settings.flight.SeperationWeight * boidDMtrx.SeperationDir / boidDMtrx.count - 
                settings.flight.CohesionWeight * (boidDMtrx.CohesionDir / boidDMtrx.count - position);
            else influenceDir = settings.flight.SeperationWeight * boidDMtrx.SeperationDir / boidDMtrx.count +
                settings.flight.AlignmentWeight * (boidDMtrx.AlignmentDir / boidDMtrx.count - flightDirection) +
                settings.flight.CohesionWeight * (boidDMtrx.CohesionDir / boidDMtrx.count - position);
            
            flightDirection = Normalize(flightDirection + influenceDir);
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

        //Task 0
        public static void Idle(Animal self){
            self.tCollider.useGravity = true;
            if (self.vitality.healthPercent > self.settings.physicality.MateThreshold){
                self.TaskIndex = 6;
                return;
            }  if(self.vitality.healthPercent < self.settings.physicality.HuntThreshold){
                self.TaskIndex = 3;
                return;
            }

            if(self.TaskDuration <= 0) {
                self.TaskDuration = self.settings.flight.AverageFlightTime * self.random.NextFloat(0f, 2f);
                self.flightDirection = self.RandomDirection();
                self.BoidFly();
                return;
            }
            else self.TaskDuration -= EntityJob.cxt.deltaTime;

            //Rotate towards neutral
            ref Quaternion rotation = ref self.tCollider.transform.rotation;
            float3 lookRotation = new (rotation.eulerAngles.x, 0, rotation.eulerAngles.z);
            if(math.any(lookRotation != 0)) rotation = Quaternion.RotateTowards(rotation, Quaternion.LookRotation(lookRotation), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);
        }
        
        //Task 1 -> Fly 
        public static unsafe void FollowFlight(Animal self){
            self.tCollider.useGravity = false;
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            Movement.FollowStaticPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider, 
            self.settings.movement.runSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration, true);

            if(!self.pathFinder.hasPath) {
                self.BoidFly();
            }
        }

        //Task 2 -> Landing 
        public static unsafe void FollowLanding(Animal self){
            self.tCollider.useGravity = false;
            Movement.FollowStaticPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider, 
            self.settings.movement.runSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration, true);

            if(!self.pathFinder.hasPath) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.flightDirection = 0;
                self.TaskIndex = 0; //Landed
            }
        }


        //Task 3
        private static unsafe void FindPrey(Animal self){
            self.tCollider.useGravity = true;
            if(!self.settings.recognition.FindPreferredPrey((int3)math.round(self.position), out int3 preyPos)){
                self.RandomWalk();
                return;
            }
            byte* path = PathFinder.FindPathOrApproachTarget(self.GCoord, preyPos - self.GCoord, self.settings.recognition.PlantFindDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = 4;

            //If it can't get to the prey and is currently at the closest position it can be
            if(math.all(self.pathFinder.destination == self.GCoord)){
                float dist = math.distance(preyPos, self.position);
                if(dist <= self.settings.physicality.AttackDistance){
                    self.TaskDuration = 1 / math.max(self.settings.physicality.ConsumptionRate, 0.0001f);
                    self.TaskIndex = 5;
                } else {
                    self.BoidFly();
                }
            } 
        }

        //Task 4
        private static unsafe void ChasePrey(Animal self){
            self.tCollider.useGravity = true;
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, 
            self.settings.movement.walkSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration);
            if(self.pathFinder.hasPath) return;

            if(self.settings.recognition.FindPreferredPrey((int3)math.round(self.position), out int3 preyPos) && 
            math.distance(preyPos, self.position) <= self.settings.physicality.AttackDistance){
                self.TaskDuration = 1 / math.max(self.settings.physicality.ConsumptionRate, 0.0001f);
                self.TaskIndex = 5;
            } else self.TaskIndex = 3;
        }

        //Task 5
        private static unsafe void EatFood(Animal self){
            self.tCollider.useGravity = true;
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if(self.TaskDuration <= 0){
                if(self.settings.recognition.FindPreferredPrey((int3)math.round(self.position), out int3 foodPos)){
                    WorldConfig.Generation.Item.IItem item = self.settings.recognition.ConsumeFood(foodPos);
                    if(item != null && self.settings.recognition.CanConsume(item, out float nutrition))
                        self.vitality.Heal(nutrition);  
                    self.TaskIndex = 0;
                } else self.TaskIndex = 3;
            }
        }

        //Task 6
        private static unsafe void FindMate(Animal self){
            self.tCollider.useGravity = true;
            if(self.vitality.healthPercent < self.settings.physicality.MateThreshold){
                self.TaskIndex = 0;
                return;
            }
            if(!self.settings.recognition.FindPreferredMate(self, out Entity mate)){
                self.RandomWalk();
                return;   
            }
            int PathDist = self.settings.movement.pathDistance;
            int3 destination = (int3)mate.origin - self.GCoord;
            byte* path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = 7;
        }

        //Task 7
        private static unsafe void ChaseMate(Animal self){//I feel you man
            self.tCollider.useGravity = true;
            if(!self.settings.recognition.FindPreferredMate(self, out Entity mate)){
                self.TaskIndex = 6;
                return;
            }

            Movement.FollowDynamicPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, mate.origin,
            self.settings.movement.walkSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration);
            float mateDist = math.distance(self.position, mate.position);
            if(mateDist < self.settings.physicality.AttackDistance) {
                EntityManager.AddHandlerEvent(() => (mate as IMateable).MateWith(self));
                self.MateWith(mate);
                return;
            } if(!self.pathFinder.hasPath) {
                self.TaskIndex = 6;
                return;
            }
        }

        //Task 8 (I will never get here)
        private static void Reproduce(Animal self){
            self.tCollider.useGravity = true;
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if(self.TaskDuration > 0) return;
            self.BoidFly();
        }

        //Task 9
        private static unsafe void RunFromTarget(Animal self){
            self.tCollider.useGravity = false;
            Entity target = EntityManager.GetEntity(self.TaskTarget);
            if(target == null) self.TaskTarget = Guid.Empty;
            else if(math.distance(self.position, target.position) > self.settings.recognition.SightDistance)
                self.TaskTarget = Guid.Empty;
            if(self.TaskTarget == Guid.Empty) {
                self.BoidFly();
                return;
            }

            if(!self.pathFinder.hasPath) {
                int PathDist = self.settings.recognition.FleeDistance;
                float3 rayDir = self.position - target.position;
                byte* path = PathFinder.FindPathAlongRay(self.GCoord, ref rayDir, PathDist + 1, self.settings.flight.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            } 
            Movement.FollowStaticPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider, 
            self.settings.movement.runSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration, true);
        }
        
        //Task 10
        private static unsafe void ChaseTarget(Animal self){
            self.tCollider.useGravity = false;
            Entity target = EntityManager.GetEntity(self.TaskTarget);
            if(target == null) 
                self.TaskTarget = Guid.Empty;
            else if(math.distance(self.position, target.position) > self.settings.recognition.SightDistance)
                self.TaskTarget = Guid.Empty;
            if(self.TaskTarget == Guid.Empty) {
                self.BoidFly();
                return;
            }

            if(!self.pathFinder.hasPath) {
                int PathDist = self.settings.movement.pathDistance;
                int3 destination = (int3)math.round(target.origin) - self.GCoord;
                byte* path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.flight.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            } 
            Movement.FollowDynamicPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider, target.origin,
            self.settings.movement.runSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration, true);
            if(math.distance(self.position, target.position) < self.settings.physicality.AttackDistance) {
                self.TaskIndex = 11;
                return;
            }
        }

        //Task 11
        private static void AttackTarget(Animal self){
            self.tCollider.useGravity = false;
            Entity tEntity = EntityManager.GetEntity(self.TaskTarget);
            if(tEntity == null) 
                self.TaskTarget = Guid.Empty;
            else if(tEntity is not IAttackable)
                self.TaskTarget = Guid.Empty;
            if(self.TaskTarget == Guid.Empty) {
                self.BoidFly();
                return;
            }
            float targetDist = math.distance(tEntity.position, self.position);
            if(targetDist > self.settings.physicality.AttackDistance) {
                self.TaskIndex = 10;
                return;
            }

            float3 atkDir = math.normalize(tEntity.position - self.position); atkDir.y = 0;
            self.tCollider.transform.rotation = Quaternion.RotateTowards(self.tCollider.transform.rotation, 
            Quaternion.LookRotation(atkDir), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);

            IAttackable target = tEntity as IAttackable;
            if(target.IsDead) self.BoidFly();
            else self.vitality.Attack(tEntity, self);
        }


        //Task 12
        private static unsafe void RunFromPredator(Animal self){
            self.tCollider.useGravity = false;
            Movement.FollowStaticPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider, self.settings.movement.runSpeed, 
            self.settings.movement.rotSpeed, self.settings.movement.acceleration, true);
            if(!self.pathFinder.hasPath) {
                self.BoidFly();
            }
        }

        //Task 13
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
            "IsIdling",  "IsFlying", "IsFlying",  "IsWalking", "IsWalking", 
            "IsEating", "IsWalking",  "IsWalking", "IsCuddling",  "IsFlying", 
            "IsFlying", "IsAttacking", "IsFlying", "IsDead"
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
                Debug.Log(AnimationNames[entity.TaskIndex]);
            }
#endif

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


