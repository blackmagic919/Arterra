using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Entity;
using UnityEngine.Profiling;

[CreateAssetMenu(menuName = "Entity/SkyCarnivore")]
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
        public RCarnivore recognition;
        public Vitality.Stats physicality;
        public Vitality.Decomposition decomposition;
        public TerrainColliderJob.Settings collider;
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
            recognition.Construct();
            base.Preset();
        }
    }

    //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
    //**If you release here the controller might still be accessing it
    public class Animal : Entity, IMateable, IAttackable
    {  
        //This is the real-time position streamed by the controller
        [JsonIgnore]
        private AnimalController controller;
        public int3 GCoord;
        public Vitality vitality;
        public PathFinder.PathInfo pathFinder;
        public TerrainColliderJob tCollider;
        public Unity.Mathematics.Random random;
        public float TaskDuration;
        public uint TaskIndex;
        public AnimalSetting settings;
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
            RunFromPredator,
            Death
        };
        public override float3 position {
            get => tCollider.transform.position;
            set => tCollider.transform.position = value;
        }

        public bool IsDead => vitality.IsDead;
        public void TakeDamage(float damage, float3 knockback){
            if(!vitality.Damage(damage)) return;
            Indicators.DisplayPopupText(damage.ToString(), position);
            tCollider.velocity += knockback;
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
            if(TaskIndex >= 9) return false;
            return settings.recognition.CanMateWith(entity);
        }
        public void MateWith(Entity entity){
            if(!CanMateWith(entity)) return;
            if(settings.recognition.MateWithEntity(entity, ref random))
                vitality.Damage(settings.physicality.MateCost);
            TaskDuration = settings.physicality.PregnacyLength;
            TaskIndex = 9;
        }

        public override void Initialize(EntitySetting setting, GameObject Controller, int3 GCoord){
            settings = (AnimalSetting)setting;
            controller = new AnimalController(Controller, this);
            //The seed is the entity's memory address
            this.random = new Unity.Mathematics.Random((uint)GetHashCode());
            this.vitality = new Vitality(settings.physicality, ref random);

            this.GCoord = GCoord;
            pathFinder.hasPath = false;
            tCollider.transform.position = GCoord;
            TaskDuration = settings.movement.AverageIdleTime * random.NextFloat(0f, 2f);
            TaskIndex = 0;
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord){
            settings = (AnimalSetting)setting;
            controller = new AnimalController(Controller, this);
            vitality.Deserialize(settings.physicality);
            GCoord = this.GCoord;
        }


        public override void Update()
        {
            Profiler.BeginSample($"SkyCarnivore Update Task: {TaskIndex}, {info.entityId}");
            if(!active) return;
            GCoord = (int3)math.floor(tCollider.transform.position);
            TaskRegistry[(int)TaskIndex].Invoke(this);

            //use gravity if not flying
            bool useGGrav = TaskIndex == 0 || (TaskIndex >= 7 && TaskIndex <= 9) || TaskIndex == 11;
            tCollider.Update(EntityJob.cxt, settings.collider, useGGrav);
            tCollider.velocity *= 1 - settings.movement.friction;
            EntityManager.AddHandlerEvent(controller.Update);

            vitality.Update();
            if(TaskIndex != 11 && vitality.IsDead) {
                TaskDuration = settings.decomposition.DecompositionTime;
                TaskIndex = 11;
            } else if(TaskIndex <= 9)  DetectPredator();
            Profiler.EndSample();
        }

        //Always detect unless already running from predator
        private unsafe void DetectPredator(){
            if(!settings.recognition.FindClosestPredator(this, out Entity predator))
                return;

            int PathDist = settings.recognition.FleeDistance;
            float3 rayDir = GCoord - predator.position;
            byte* path = PathFinder.FindPathAlongRay(GCoord, ref rayDir, PathDist + 1, settings.flight.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
            TaskIndex = 10;
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
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if(self.TaskDuration <= 0) {
                self.TaskDuration = self.settings.flight.AverageFlightTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 1;
                return;
            } if(self.vitality.healthPercent > self.settings.physicality.MateThreshold){
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
            if(self.TaskDuration <= 0){
                bool fGround = self.FindGround();
                if(fGround) self.TaskIndex = 3;
                else self.TaskIndex = 2;
                return;
            }
            if (self.vitality.healthPercent > self.settings.physicality.MateThreshold){
                self.TaskDuration = math.min(0, self.TaskDuration); //try to land to mate
                return;
            }  
            if(self.vitality.healthPercent < self.settings.physicality.HuntThreshold){
                self.TaskIndex = 4;
                return;
            }
            self.RandomFly();
            self.TaskIndex = 2;
        }

        //Task 2 -> Fly 
        public static unsafe void FollowFlight(Animal self){
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            Movement.FollowStaticPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider, 
            self.settings.movement.runSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration, true);

            if(!self.pathFinder.hasPath) {
                self.TaskIndex = 1;
            }
        }

        //Task 3 -> Landing 
        public static unsafe void FollowLanding(Animal self){
            Movement.FollowStaticPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider, 
            self.settings.movement.runSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration, true);

            if(!self.pathFinder.hasPath) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 0; //Landed
            }
        }

        //Task 4 - Find Prey
        private static unsafe void FindPrey(Animal self){
            //Use mate threshold not hunt because the entity may lose the target while eating
            if(!self.settings.recognition.FindPreferredPrey(self, out Entity prey)){
                self.TaskIndex = 2;
                self.RandomFly();
                return;   
            }

            int PathDist = self.settings.movement.pathDistance;
            int3 destination = (int3)math.round(prey.position) - self.GCoord;
            byte* path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.flight.profile, EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = 5;

            //If it can't get to the prey and is currently at the closest position it can be
            if(math.all(self.pathFinder.destination == self.GCoord) && math.distance(prey.position, self.position) > self.settings.physicality.AttackDistance){
                self.TaskIndex = 2;
                self.RandomFly();
            } 
        }

        //Task 5 - Chase Prey
        private static unsafe void ChasePrey(Animal self){
            if(!self.settings.recognition.FindPreferredPrey(self, out Entity prey)){
                self.TaskIndex = 4;
                return;
            }
            Movement.FollowDynamicPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider, prey.position,
            self.settings.movement.runSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration, true);
            float preyDist = math.distance(self.tCollider.transform.position, prey.position);
            if(preyDist < self.settings.physicality.AttackDistance) {
                self.TaskIndex = 6;
                return;
            } if(!self.pathFinder.hasPath) {
                self.TaskIndex = 4;
                return;
            }
        }

        //Task 6 - Attack
        private static void Attack(Animal self){
            self.TaskIndex = 4;
            if(!self.settings.recognition.FindPreferredPrey(self, out Entity prey)) return;
            float preyDist = math.distance(self.tCollider.transform.position, prey.position);
            if(preyDist > self.settings.physicality.AttackDistance) return;
            if(prey is not IAttackable) return;
            self.TaskIndex = 6;

            float3 atkDir = math.normalize(prey.position - self.tCollider.transform.position); atkDir.y = 0;
            self.tCollider.transform.rotation = Quaternion.RotateTowards(self.tCollider.transform.rotation, 
            Quaternion.LookRotation(atkDir), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);

            IAttackable target = (IAttackable)prey;
            if(target.IsDead) {
                EntityManager.AddHandlerEvent(() => {
                WorldConfig.Generation.Item.IItem item = target.Collect(self.settings.physicality.ConsumptionRate);
                if(item != null && self.settings.recognition.CanConsume(item, out float nutrition)){
                    self.vitality.Heal(nutrition);  
                } if(self.vitality.healthPercent >= 1){
                    self.TaskIndex = 1;
                }}); 
            } else self.vitality.Attack(prey, self.tCollider.transform.position);
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
            if(self.vitality.healthPercent < self.settings.physicality.MateThreshold){
                self.TaskIndex = 0;
                return;
            }
            if(!self.settings.recognition.FindPreferredMate(self, out Entity mate)){
                self.RandomWalk();
                return;   
            }
            int PathDist = self.settings.movement.pathDistance;
            int3 destination = (int3)mate.position - self.GCoord;
            byte* path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = 8;
        }

        //Task 8
        private static unsafe void ChaseMate(Animal self){//I feel you man
            if(!self.settings.recognition.FindPreferredMate(self, out Entity mate)){
                self.TaskIndex = 7;
                return;
            }

            Movement.FollowDynamicPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, mate.position,
            self.settings.movement.walkSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration);
            float mateDist = math.distance(self.tCollider.transform.position, mate.position);
            if(mateDist < self.settings.physicality.AttackDistance) {
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
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if(self.TaskDuration > 0) return;
            self.TaskIndex = 1;
        }


        //Task 10
        private static unsafe void RunFromPredator(Animal self){
            Movement.FollowStaticPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider, self.settings.movement.runSpeed, 
            self.settings.movement.rotSpeed, self.settings.movement.acceleration, true);
            if(!self.pathFinder.hasPath) {
                self.TaskIndex = 1;
            }
        }

        //Task 11
        private static void Death(Animal self){
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
            Gizmos.DrawWireCube(CPUMapManager.GSToWS(tCollider.transform.position), settings.collider.size * 2);
            PathFinder.PathInfo finder = pathFinder; //copy so we don't modify the original
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
            "IsFlying", "IsDead"
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
            transform.position = CPUMapManager.GSToWS(GCoord - entity.settings.collider.offset) + (float3)Vector3.up * 1;
        }

        public void Update(){
            if(!entity.active) return;
            if(gameObject == null) return;
            EntityManager.AssertEntityLocation(entity, entity.GCoord);    
            TerrainColliderJob.Transform rTransform = entity.tCollider.transform;
            rTransform.position = CPUMapManager.GSToWS(rTransform.position - entity.settings.collider.offset);
            this.transform.SetPositionAndRotation(rTransform.position, rTransform.rotation);
            if(UnityEditor.Selection.Contains(gameObject)) {
                Debug.Log(entity.TaskIndex);
            }
            
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


