using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Entity;
using System.Threading.Tasks;
using UnityEngine.Profiling;

[CreateAssetMenu(menuName = "Entity/SurfaceCarnivore")]
public class SurfaceCarnivore : Authoring
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
        public RCarnivore recognition;
        public Vitality.Stats physicality;
        public Vitality.Decomposition decomposition;
        public TerrainColliderJob.Settings collider;

        public override void Preset(){
            recognition.Construct();
            base.Preset();
        }
    }

    //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
    //**If you release here the controller might still be accessing it
    public class Animal : Entity, IAttackable, IMateable
    {  
        [JsonIgnore]
        private AnimalController controller;
        public int3 GCoord; 
        public Vitality vitality;
        public PathFinder.PathInfo pathFinder;
        public TerrainColliderJob tCollider;
        public Unity.Mathematics.Random random;
        public AnimalSetting settings;
        public uint TaskIndex;
        public float TaskDuration;
        public static Action<Animal>[] TaskRegistry = new Action<Animal>[]{
            Idle,
            RandomPath,
            FollowPath,
            FindPrey,
            ChasePrey,
            Attack,
            FindMate,
            ChaseMate,
            Reproduce,
            RunFromPredator,
            Death,
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
            this.GCoord = GCoord;
            pathFinder.hasPath = false;
            tCollider.transform.position = GCoord;

            //Start by Idling
            TaskDuration = settings.movement.AverageIdleTime * random.NextFloat(0f, 2f);
            TaskIndex = 0;
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord)
        {
            settings = (AnimalSetting)setting;
            controller = new AnimalController(Controller, this);
            vitality.Deserialize(settings.physicality);
            random.state ^= (uint)GetHashCode();
            GCoord = this.GCoord;
        }


        public override void Update()
        {
            if(!active) return;
            GCoord = (int3)tCollider.transform.position;
            tCollider.Update(EntityJob.cxt, settings.collider);
            tCollider.velocity.xz *= 1 - settings.movement.friction;
            EntityManager.AddHandlerEvent(controller.Update);
            
            vitality.Update();
            TaskRegistry[(int)TaskIndex].Invoke(this);
            //Shared high priority states
            if(TaskIndex != 10 && vitality.IsDead) {
                TaskDuration = settings.decomposition.DecompositionTime;
                TaskIndex = 10;
            } else if(TaskIndex <= 8)  DetectPredator();
        }

        //Always detect unless already running from predator
        private unsafe void DetectPredator(){
            if(!settings.recognition.FindClosestPredator(this, out Entity predator))
                return;

            int PathDist = settings.recognition.FleeDistance;
            float3 rayDir = GCoord - predator.position;
            byte* path = PathFinder.FindPathAlongRay(GCoord, ref rayDir, PathDist + 1, settings.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
            TaskIndex = 9;
        }

        //Task 0
        private static void Idle(Animal self){
            if(self.TaskDuration <= 0){
                self.TaskIndex = 1;
            } else self.TaskDuration -= EntityJob.cxt.deltaTime;
            if(self.vitality.healthPercent < self.settings.physicality.HuntThreshold) 
                self.TaskIndex = 3;
            else if(self.vitality.healthPercent > self.settings.physicality.MateThreshold) 
                self.TaskIndex = 6;
        }

        // Task 1
        private static unsafe void RandomPath(Animal self){
            int PathDist = self.settings.movement.pathDistance;
            int3 dP = new (self.random.NextInt(-PathDist, PathDist), self.random.NextInt(-PathDist, PathDist), self.random.NextInt(-PathDist, PathDist));
            if(PathFinder.VerifyProfile(self.GCoord + dP, self.settings.profile, EntityJob.cxt)) {
                byte* path = PathFinder.FindPath(self.GCoord, dP, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
                self.TaskIndex = 2;
            }
        }


        //Task 2
        private static unsafe void FollowPath(Animal self){
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, 
            self.settings.movement.walkSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration);
            if(self.pathFinder.hasPath) return;
            self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
            self.TaskIndex = 0;
        }

        //Task 3
        private static unsafe void FindPrey(Animal self){
            //Use mate threshold not hunt because the entity may lose the target while eating
            if(self.vitality.healthPercent > self.settings.physicality.MateThreshold ||
            !self.settings.recognition.FindPreferredPrey(self, out Entity prey)){
                self.TaskIndex = 1;
                return;   
            }

            int PathDist = self.settings.movement.pathDistance;
            int3 destination = (int3)math.round(prey.position) - self.GCoord;
            byte* path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = 4;

            //If it can't get to the prey and is currently at the closest position it can be
            if(math.all(self.pathFinder.destination == self.GCoord) && math.distance(prey.position, self.position) > self.settings.physicality.AttackDistance) 
                self.TaskIndex = 1;
        }

        //Task 4
        private static unsafe void ChasePrey(Animal self){
            if(!self.settings.recognition.FindPreferredPrey(self, out Entity prey)){
                self.TaskIndex = 3;
                return;
            }
            Movement.FollowDynamicPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, prey.position,
            self.settings.movement.runSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration);
            float preyDist = math.distance(self.tCollider.transform.position, prey.position);
            if(preyDist < self.settings.physicality.AttackDistance) {
                self.TaskIndex = 5;
                return;
            } if(!self.pathFinder.hasPath) {
                self.TaskIndex = 3;
                return;
            }
        }

        //Task 5
        private static void Attack(Animal self){
            self.TaskIndex = 3;
            if(!self.settings.recognition.FindPreferredPrey(self, out Entity prey)) return;
            float preyDist = math.distance(self.tCollider.transform.position, prey.position);
            if(preyDist > self.settings.physicality.AttackDistance) return;
            if(prey is not IAttackable) return;
            self.TaskIndex = 5;

            float3 atkDir = math.normalize(prey.position - self.tCollider.transform.position); atkDir.y = 0;
            if(math.any(atkDir != 0)) self.tCollider.transform.rotation = Quaternion.RotateTowards(self.tCollider.transform.rotation, 
            Quaternion.LookRotation(atkDir), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);

            IAttackable target = (IAttackable)prey;
            if(target.IsDead) {
                EntityManager.AddHandlerEvent(() => {
                WorldConfig.Generation.Item.IItem item = target.Collect(self.settings.physicality.ConsumptionRate);
                if(item != null && self.settings.recognition.CanConsume(item, out float nutrition)){
                    self.vitality.Heal(nutrition);  
                } if(self.vitality.healthPercent >= 1){
                    self.TaskIndex = 0;
                }}); 
            } else self.vitality.Attack(prey, self.tCollider.transform.position);
        }
        //Task 6
        private static unsafe void FindMate(Animal self){
            if(self.vitality.healthPercent < self.settings.physicality.MateThreshold 
            || !self.settings.recognition.FindPreferredMate(self, out Entity mate)){
                self.TaskIndex = 1;
                return;   
            }
            int PathDist = self.settings.movement.pathDistance;
            int3 destination = (int3)math.round(mate.position) - self.GCoord;
            byte* path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = 7;
        }

        //Task 7 
        private static unsafe void ChaseMate(Animal self){//I feel you man
            if(!self.settings.recognition.FindPreferredMate(self, out Entity mate)){
                self.TaskIndex = 6;
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
                self.TaskIndex = 6;
                return;
            }
        }

        //Task 8 (I will never get here)
        private static void Reproduce(Animal self){
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if(self.TaskDuration > 0) return;
            self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
            self.TaskIndex = 0;
        }
        

        //Task 9
        private static unsafe void RunFromPredator(Animal self){
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, self.settings.movement.runSpeed, 
            self.settings.movement.rotSpeed, self.settings.movement.acceleration);
            if(!self.pathFinder.hasPath) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 0;
                return;
            }
        }

        //Task 10
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
            Gizmos.color = info.entityType % 2 == 0 ? Color.red : Color.blue; 
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
            "IsIdling",  null, "IsWalking",  null, "IsRunning", 
            "IsAttacking",  null, "IsWalking", "IsCuddling", "IsRunning", 
            "IsDead"
        };

        public AnimalController(GameObject GameObject, Animal entity){
            this.entity = entity;
            this.gameObject = GameObject.Instantiate(GameObject);
            this.transform = gameObject.transform;
            this.animator = gameObject.GetComponent<Animator>();
            this.AnimatorTask = 0;
            this.active = true;

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

            Indicators.UpdateIndicators(gameObject, entity.vitality, entity.pathFinder);
            if(AnimatorTask == entity.TaskIndex) return;
            if(AnimationNames[AnimatorTask] != null) animator.SetBool(AnimationNames[AnimatorTask], false);
            AnimatorTask = (int)entity.TaskIndex;
            if(AnimationNames[AnimatorTask] != null) animator.SetBool(AnimationNames[AnimatorTask], true);
        }

        public void Dispose(){
            if(!active) return;
            active = false;

            GameObject.Destroy(gameObject);
        }

        ~AnimalController(){
            Dispose();
        }
    }
}


