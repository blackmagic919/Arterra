using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Entity;
using System.Threading.Tasks;
using UnityEditor;

[CreateAssetMenu(menuName = "Entity/SurfaceHerbivore")]
public class SurfaceHerbivore : Authoring
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
        public RHerbivore recognition;
        public Vitality.Stats physicality;
        public Vitality.Decomposition decomposition;

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
        public Guid TaskTarget;
        public uint TaskIndex;
        public float TaskDuration;
        public static Action<Animal>[] TaskRegistry = new Action<Animal>[]{
            Idle,
            RandomPath,
            FollowPath,
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
            Death,
        };
        public override float3 position {
            get => tCollider.transform.position;
            set => tCollider.transform.position = value;
        }

        public bool IsDead => vitality.IsDead;
        public void TakeDamage(float damage, float3 knockback, Entity attacker){
            if(!vitality.Damage(damage)) return;
            Indicators.DisplayPopupText(position, knockback);
            tCollider.velocity += knockback;

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
            this.GCoord = GCoord;
            pathFinder.hasPath = false;
            tCollider.transform.position = GCoord;

            //Start by Idling
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
            GCoord = (int3)tCollider.transform.position;
            tCollider.Update(EntityJob.cxt, settings.collider);
            EntityManager.AddHandlerEvent(controller.Update);
            
            vitality.Update();
            TaskRegistry[(int)TaskIndex].Invoke(this);
            //Shared high priority states
            if(TaskIndex != 13 && vitality.IsDead) {
                TaskDuration = settings.decomposition.DecompositionTime;
                TaskIndex = 13;
            } else if(TaskIndex <= 11)  DetectPredator();
        }

        //Always detect unless already running from predator
        private unsafe void DetectPredator(){
            if(!settings.recognition.FindClosestPredator(this, out Entity predator))
                return;

            int PathDist = settings.recognition.FleeDistance;
            float3 rayDir = GCoord - predator.position;
            byte* path = PathFinder.FindPathAlongRay(GCoord, ref rayDir, PathDist + 1, settings.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
            TaskIndex = 12;
        }

        //Task 0
        private static void Idle(Animal self){
            if(self.TaskDuration <= 0){
                self.TaskIndex = 1;
            } else self.TaskDuration -= EntityJob.cxt.deltaTime;
            
            if(self.vitality.healthPercent < self.settings.physicality.HuntThreshold) 
                self.TaskIndex = 3;
            if(self.vitality.healthPercent > self.settings.physicality.MateThreshold) 
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
            if(!self.settings.recognition.FindPreferredPrey((int3)math.round(self.position + self.settings.collider.offset), out int3 preyPos)){
                self.TaskIndex = 1;
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
                    self.TaskIndex = 1;
                }
            } 
        }

        //Task 4
        private static unsafe void ChasePrey(Animal self){
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, 
            self.settings.movement.walkSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration);
            if(self.pathFinder.hasPath) return;

            if(self.settings.recognition.FindPreferredPrey((int3)math.round(self.position - self.settings.collider.offset), out int3 preyPos) && 
            math.distance(preyPos, self.position) <= self.settings.physicality.AttackDistance){
                self.TaskDuration = 1 / math.max(self.settings.physicality.ConsumptionRate, 0.0001f);
                self.TaskIndex = 5;
            } else self.TaskIndex = 3;
        }

        //Task 5
        private static unsafe void EatFood(Animal self){
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if(self.TaskDuration <= 0){
                if(self.settings.recognition.FindPreferredPrey((int3)math.round(self.position - self.settings.collider.offset), out int3 foodPos)){
                    WorldConfig.Generation.Item.IItem item = self.settings.recognition.ConsumeFood(foodPos);
                    if(item != null && self.settings.recognition.CanConsume(item, out float nutrition))
                        self.vitality.Heal(nutrition);  
                    self.TaskIndex = 0;
                } else self.TaskIndex = 3;
            }
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
        private static unsafe void RunFromTarget(Animal self){
            Entity target = EntityManager.GetEntity(self.TaskTarget);
            if(target == null) self.TaskTarget = Guid.Empty;
            else if(math.distance(self.position, target.position) > self.settings.recognition.SightDistance)
                self.TaskTarget = Guid.Empty;
            if(self.TaskTarget == Guid.Empty) {
                self.TaskIndex = 0;
                return;
            }

            if(!self.pathFinder.hasPath) {
                int PathDist = self.settings.recognition.FleeDistance;
                float3 rayDir = self.GCoord - target.position;
                byte* path = PathFinder.FindPathAlongRay(self.GCoord, ref rayDir, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            } 
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, 
            self.settings.movement.runSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration);
        }
        
        //Task 10
        private static unsafe void ChaseTarget(Animal self){
            Entity target = EntityManager.GetEntity(self.TaskTarget);
            if(target == null) 
                self.TaskTarget = Guid.Empty;
            else if(math.distance(self.position, target.position) > self.settings.recognition.SightDistance)
                self.TaskTarget = Guid.Empty;
            if(self.TaskTarget == Guid.Empty) {
                self.TaskIndex = 0;
                return;
            }

            if(!self.pathFinder.hasPath) {
                int PathDist = self.settings.movement.pathDistance;
                int3 destination = (int3)math.round(target.position) - self.GCoord;
                byte* path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            } 
            Movement.FollowDynamicPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, target.position,
            self.settings.movement.runSpeed, self.settings.movement.rotSpeed, self.settings.movement.acceleration);
            if(math.distance(self.tCollider.transform.position, target.position) < self.settings.physicality.AttackDistance) {
                self.TaskIndex = 11;
                return;
            }
        }

        //Task 11
        private static void AttackTarget(Animal self){
            Entity tEntity = EntityManager.GetEntity(self.TaskTarget);
            if(tEntity == null) 
                self.TaskTarget = Guid.Empty;
            else if(tEntity is not IAttackable)
                self.TaskTarget = Guid.Empty;
            if(self.TaskTarget == Guid.Empty) {
                self.TaskIndex = 0;
                return;
            }
            float targetDist = math.distance(tEntity.position, self.position);
            if(targetDist > self.settings.physicality.AttackDistance) {
                self.TaskIndex = 10;
                return;
            }

            float3 atkDir = math.normalize(tEntity.position - self.tCollider.transform.position); atkDir.y = 0;
            if(math.any(atkDir != 0)) self.tCollider.transform.rotation = Quaternion.RotateTowards(self.tCollider.transform.rotation, 
            Quaternion.LookRotation(atkDir), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);

            IAttackable target = tEntity as IAttackable;
            if(target.IsDead) self.TaskIndex = 0;
            else self.vitality.Attack(tEntity, self);
        }

        //Task 12
        private static unsafe void RunFromPredator(Animal self){
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, self.settings.movement.runSpeed, 
            self.settings.movement.rotSpeed, self.settings.movement.acceleration);
            if(!self.pathFinder.hasPath) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 0;
                return;
            }
        }

        //Task 13
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
            "IsIdling",  null, "IsWalking", null, "IsRunning", "IsEating",  
            null, "IsWalking", "IsCuddling", "IsRunning", "IsRunning", 
            "IsAttacking", "IsRunning", "IsDead"
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
            TerrainColliderJob.Transform rTransform = entity.tCollider.transform;
            rTransform.position = CPUMapManager.GSToWS(rTransform.position - entity.settings.collider.offset);
            this.transform.SetPositionAndRotation(rTransform.position, rTransform.rotation);
#if UNITY_EDITOR
            if(UnityEditor.Selection.Contains(gameObject)) Debug.Log(entity.TaskIndex);
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

            GameObject.Destroy(gameObject);
        }

        ~AnimalController(){
            Dispose();
        }
    }
}


