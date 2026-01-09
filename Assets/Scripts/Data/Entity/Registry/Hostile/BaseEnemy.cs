using System;
using System.Collections.Generic;
using System.Linq;
using Arterra.Core.Storage;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using Arterra.Configuration;
using Arterra.Configuration.Generation.Entity;

[CreateAssetMenu(menuName = "Generation/Entity/BaseEnemy")]
public class BaseEnemy : Authoring {
    public Option<BaseEnemySettings> _Setting;
    
    [JsonIgnore]
    public override Entity Entity { get => new Animal(); }
    [JsonIgnore]
    public override EntitySetting Setting { get => _Setting.value; set => _Setting.value = (BaseEnemySettings)value; }
    [Serializable]
    public class BaseEnemySettings : EntitySetting {
        public Genetics.GeneFeature EnemyCheckDist;
        public Genetics.GeneFeature EnemyCheckDelay;
        public Movement movement;
        public Option<MinimalRecognition> recognition;
        public Option<MediumVitality.Stats> physicality;
        public Vitality.Decomposition decomposition;
        public RangedAttack rangedAttack;
        public float ConsumptionRate;
        public MinimalRecognition Recognition => recognition;
        public MediumVitality.Stats Physicality => physicality;

        [UISetting(Ignore = true)][JsonIgnore]
        public Dictionary<int, uint> AnimToggle;
        [Serializable]
        public struct RangedAttack {
            public ProjectileLauncher.Stats Projectile;
            public Genetics.GeneFeature BlindDist;
            public void InitGenome(uint entityType) {
                Projectile.InitGenome(entityType);
                Genetics.AddGene(entityType, ref BlindDist);
            }
        }

        public override void Preset(uint entityType)
        {
            Recognition.Construct();

            movement.InitGenome(entityType);
            Physicality.InitGenome(entityType);
            Recognition.InitGenome(entityType);
            decomposition.InitGenome(entityType);
            rangedAttack.InitGenome(entityType);
            Genetics.AddGene(entityType, ref EnemyCheckDelay);
            Genetics.AddGene(entityType, ref EnemyCheckDist);

            base.Preset(entityType);
        }
    }

    public class Animal : Entity, IAttackable {
        [JsonProperty]
        private Genetics genetics;
        [JsonProperty]
        private MediumVitality vitality;
        [JsonProperty]
        private ProjectileLauncher launcher;
        [JsonProperty]
        private PathFinder.PathInfo pathFinder;
        [JsonProperty]
        private TerrainCollider tCollider;
        [JsonProperty]
        private Unity.Mathematics.Random random;
        [JsonProperty]
        private Guid TaskTarget;
        [JsonProperty]
        private AnimalTasks TaskIndex;
        [JsonProperty]
        private float TaskDuration;
        private static Action<Animal>[] TaskRegistry = new Action<Animal>[]{
            Idle, RandomPath,
            FindPrey, ChasePreyEntity, StepBackFromEntity, 
            Attack, ChasePreyPlant, EatPlant, 
            ChaseTarget, StepBackFromEntity, AttackTarget,
            RunFromTarget, RunFromPredator, Death
        };
        private enum AnimalTasks {
            Idle, RandomPath,
            FindPrey, ChasePreyEntity,  StepBackFromPrey,
            Attack, ChasePreyPlant, EatPlant, 
            ChaseTarget, StepBackFromTarget, AttackTarget,
            RunFromTarget, RunFromPredator, Death
        };

        private AnimalController controller;
        private BaseEnemySettings settings;
        [JsonIgnore]
        public override ref TerrainCollider.Transform transform => ref tCollider.transform;
        [JsonIgnore]
        public int3 GCoord => (int3)math.floor(origin);
        [JsonIgnore]
        public bool IsDead => vitality.IsDead;
        
        public void Interact(Entity target) { }
        public Arterra.Configuration.Generation.Item.IItem Collect(float amount) { return null; }
        public void TakeDamage(float damage, float3 knockback, Entity attacker) {
            if (!vitality.Damage(damage)) return;
            Indicators.DisplayDamageParticle(position, knockback);
            velocity += knockback;

            if (IsDead) return;
            if (attacker == null) return; //If environmental damage, we don't need to retaliate
            if (attacker.info.entityId == info.entityId) return;
            TaskTarget = attacker.info.entityId;
            Recognition.Recognizable recog = settings.Recognition.Recognize(attacker);
            if (recog.IsPredator) TaskIndex = AnimalTasks.RunFromTarget; //if predator run away
            else if (recog.IsMate) TaskIndex = AnimalTasks.ChaseTarget; //if mate fight back
            else if (recog.IsPrey) TaskIndex = AnimalTasks.ChaseTarget; //if prey fight back
            //if unknown, depends
            else TaskIndex = settings.Recognition.FightAggressor ? AnimalTasks.ChaseTarget : AnimalTasks.RunFromTarget; 
            //Don't try to attack a non-attackable entity
            if (TaskIndex == AnimalTasks.ChaseTarget && attacker is not IAttackable) TaskIndex = AnimalTasks.RunFromTarget;  
            pathFinder.hasPath = false;
        }

        private void ProcessFallDamage(float zVelDelta) {
            if (zVelDelta <= Vitality.FallDmgThresh) return;
            float damage = zVelDelta - Vitality.FallDmgThresh;
            damage = math.pow(damage, settings.Physicality.weight);
            EntityManager.AddHandlerEvent(() => TakeDamage(damage, 0, null));
        }

        private static bool TestNotDead(Entity e) {
            if (e is not IAttackable attackable)
                return false;
            return !attackable.IsDead;
        }

        public override void Disable(){
            controller.Dispose();
        }
        
        public override void Initialize(EntitySetting setting, GameObject Controller, float3 GCoord) {
            settings = (BaseEnemySettings)setting;

            this.tCollider = new TerrainCollider(settings.collider, GCoord, ProcessFallDamage);
            random = new Unity.Mathematics.Random((uint)UnityEngine.Random.Range(0, int.MaxValue));
            controller = new AnimalController(Controller, this);
            this.genetics ??= new Genetics(this.info.entityType, ref random);
            this.vitality = new MediumVitality(settings.Physicality, this.genetics);
            this.launcher = new ProjectileLauncher(settings.rangedAttack.Projectile, this.genetics);
            this.TaskDuration = genetics.Get(settings.EnemyCheckDelay);
            this.TaskIndex = AnimalTasks.Idle;
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord) {
            settings = (BaseEnemySettings)setting;
            
            controller = new AnimalController(Controller, this);
            vitality.Deserialize(settings.Physicality, genetics);
            launcher.Deserialize(settings.rangedAttack.Projectile, genetics);
            tCollider.OnHitGround = ProcessFallDamage;
            random.state ^= (uint)GetHashCode();
            GCoord = this.GCoord;
        }

        public override void Update() {
            if (!active) return;
            tCollider.Update(this);
            EntityManager.AddHandlerEvent(controller.Update);

            TerrainInteractor.DetectMapInteraction(position,
            OnInSolid: (dens) => vitality.ProcessInSolid(this, dens),
            OnInLiquid: (dens) => vitality.ProcessInLiquid(this, ref tCollider, dens),
            OnInGas: (dens) => vitality.ProcessInGas(this, dens));

            vitality.Update(this);
            launcher.Update(this);
            TaskRegistry[(int)TaskIndex].Invoke(this);
            //Shared high priority states
            if (TaskIndex != AnimalTasks.Death && vitality.IsDead) {
                TaskDuration = genetics.Get(settings.decomposition.DecompositionTime);
                TaskIndex = AnimalTasks.Death;
            } else if (TaskIndex < AnimalTasks.RunFromPredator) DetectPredator();
        }

        private void DetectPredator() {
            if (!settings.Recognition.FindClosestPredator(this, genetics.Get(
                settings.Recognition.SightDistance), out Entity predator))
                return;

            int PathDist = settings.Recognition.FleeDistance;
            float3 rayDir = position - predator.position;
            byte[] path = PathFinder.FindPathAlongRay(GCoord, ref rayDir, PathDist + 1, settings.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
            TaskIndex = AnimalTasks.RunFromPredator;
        }

        public static void Idle(Animal self) {
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if (self.TaskDuration > 0) return;

            if (self.settings.Recognition.FindPreferredPreyEntity(self,
                self.genetics.Get(self.settings.EnemyCheckDist),
                out Entity entity, TestNotDead)) {
                self.TaskIndex = AnimalTasks.FindPrey;
                return;
            } 

            self.TaskDuration = self.genetics.Get(self.settings.EnemyCheckDelay);
            self.TaskIndex = AnimalTasks.Idle;
        }

        public static void RandomPath(Animal self) {
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if (self.TaskDuration < 0) {
                self.TaskIndex = AnimalTasks.Idle;
                return;
            }

            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.walkSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration);
            if (self.pathFinder.hasPath) return;
            int PathDist = self.settings.movement.pathDistance;
            int3 dP = new(self.random.NextInt(-PathDist, PathDist), self.random.NextInt(-PathDist, PathDist), self.random.NextInt(-PathDist, PathDist));
            if (PathFinder.VerifyProfile(self.GCoord + dP, self.settings.profile, EntityJob.cxt)) {
                byte[] path = PathFinder.FindPath(self.GCoord, dP, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            }
        }

        //Task 3
        private static void FindPrey(Animal self) {
            float dist;
            AnimalTasks preyAction;
            if (self.settings.Recognition.FindPreferredPreyEntity(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity prey, TestNotDead)
            ) {
                int PathDist = self.settings.movement.pathDistance;
                int3 destination = (int3)math.round(prey.origin) - self.GCoord;
                dist = Recognition.GetColliderDist(self, prey);
                byte[] path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
                self.TaskIndex = AnimalTasks.ChasePreyEntity;
                preyAction = AnimalTasks.Attack;
            } else if(self.settings.Recognition.FindPreferredPreyPlant(
                self.GCoord, self.genetics.GetInt(
                self.settings.Recognition.PlantFindDist), out int3 preyPos)) {
                byte[] path = PathFinder.FindPathOrApproachTarget(self.GCoord, preyPos - self.GCoord, self.genetics.GetInt(
                    self.settings.Recognition.PlantFindDist) + 1, self.settings.profile,
                    EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
                dist = Recognition.GetColliderDist(self, prey);
                self.TaskIndex = AnimalTasks.ChasePreyPlant;
                preyAction = AnimalTasks.EatPlant;
            } else {
                self.TaskDuration = self.settings.movement.AverageIdleTime;
                self.TaskIndex = AnimalTasks.RandomPath;
                return;
            }

            //If it can't get to the prey and is currently at the closest position it can be
            if (math.all(self.pathFinder.destination == self.GCoord)) {
                if (dist <= self.genetics.Get(self.settings.Physicality.AttackDistance)) {
                    self.TaskDuration = 1 / math.max(self.settings.ConsumptionRate, 0.0001f);
                    self.TaskIndex = preyAction;
                } else {
                    self.TaskDuration = self.settings.movement.AverageIdleTime;
                    self.TaskIndex = AnimalTasks.RandomPath;
                }
            }
        }

        //Task 4
        private static void ChasePreyEntity(Animal self) {
            if (!self.settings.Recognition.FindPreferredPreyEntity(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity prey, TestNotDead)
            ) {
                self.TaskIndex = AnimalTasks.FindPrey;
                return;
            }
            Movement.FollowDynamicPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, prey.origin,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration);
            
            
            float preyDist = Recognition.GetColliderDist(self, prey);
            if (preyDist < self.genetics.Get(self.settings.Physicality.AttackDistance)) {
                if(preyDist > self.genetics.Get(self.settings.rangedAttack.BlindDist) ) {
                    self.TaskTarget = prey.info.entityId;
                    self.TaskIndex = AnimalTasks.Attack;
                } else {
                    self.TaskTarget = prey.info.entityId;
                    self.TaskIndex = AnimalTasks.StepBackFromPrey;
                }
            } else self.launcher.Fire(prey.position, self);
            
            if (!self.pathFinder.hasPath) {
                self.TaskIndex = AnimalTasks.FindPrey;
                return;
            }
        }

        //Task 4
        private static void ChasePreyPlant(Animal self) {
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.walkSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration);
            if (self.pathFinder.hasPath) return;

            if (self.settings.Recognition.FindPreferredPreyPlant(self.GCoord, self.genetics.GetInt(
                self.settings.Recognition.PlantFindDist), out int3 preyPos)
                && Recognition.GetColliderDist(self, preyPos)
                <= self.genetics.Get(self.settings.Physicality.AttackDistance)
            ) {
                self.TaskDuration = 1 / math.max(self.settings.ConsumptionRate, 0.0001f);
                self.TaskIndex = AnimalTasks.EatPlant;
            } else self.TaskIndex = AnimalTasks.FindPrey;
        }

        //Task 5
        private static void EatPlant(Animal self) {
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if (self.TaskDuration <= 0) {
                if (self.settings.Recognition.FindPreferredPreyPlant(self.GCoord, self.genetics.GetInt(
                    self.settings.Recognition.PlantFindDist), out int3 foodPos)
                ) {
                    self.settings.Recognition.ConsumePlant(self, foodPos);
                } self.TaskIndex = AnimalTasks.FindPrey;
            }
        }

        public static void Attack(Animal self) {
            self.TaskIndex = AnimalTasks.FindPrey;
            if (!EntityManager.TryGetEntity(self.TaskTarget, out Entity prey)) {
                self.TaskTarget = Guid.Empty;
                return;
            }

            float preyDist = Recognition.GetColliderDist(self, prey);
            if (preyDist > self.genetics.Get(self.settings.Physicality.AttackDistance) || 
                preyDist < self.genetics.Get(self.settings.rangedAttack.BlindDist)) {
                self.TaskTarget = Guid.Empty;
                return;
            }

            if (prey is not IAttackable target || target.IsDead) {
                self.TaskIndex = AnimalTasks.Idle;
                return;
            }

            float3 atkDir = math.normalize(prey.position - self.position); atkDir.y = 0;
            if (math.any(atkDir != 0)) self.tCollider.transform.rotation = Quaternion.RotateTowards(self.tCollider.transform.rotation,
            Quaternion.LookRotation(atkDir), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);
            self.vitality.Attack(prey);
            self.TaskIndex = AnimalTasks.Attack;
        }

        //Task 9
        private static void RunFromTarget(Animal self) {
            if (!EntityManager.TryGetEntity(self.TaskTarget, out Entity target))
                self.TaskTarget = Guid.Empty;
            else if (Recognition.GetColliderDist(self, target)
                > self.genetics.Get(self.settings.Recognition.SightDistance))
                self.TaskTarget = Guid.Empty;
            if (self.TaskTarget == Guid.Empty) {
                self.TaskIndex = 0;
                return;
            }

            if (!self.pathFinder.hasPath) {
                int PathDist = self.settings.Recognition.FleeDistance;
                float3 rayDir = self.GCoord - target.position;
                byte[] path = PathFinder.FindPathAlongRay(self.GCoord, ref rayDir, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            }
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration);
        }

        //Task 10
        private static void ChaseTarget(Animal self) {
            if (!EntityManager.TryGetEntity(self.TaskTarget, out Entity target))
                self.TaskTarget = Guid.Empty;
            else if (Recognition.GetColliderDist(self, target)
                > self.genetics.Get(self.settings.Recognition.SightDistance))
                self.TaskTarget = Guid.Empty;
            if (self.TaskTarget == Guid.Empty) {
                self.TaskIndex = AnimalTasks.Idle;
                return;
            }


            if (!self.pathFinder.hasPath) {
                int PathDist = self.settings.movement.pathDistance;
                int3 destination = (int3)math.round(target.origin) - self.GCoord;
                byte[] path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            }
            
            Movement.FollowDynamicPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, target.origin,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration);
            float targetDist = Recognition.GetColliderDist(self, target);


            if (targetDist < self.genetics.Get(self.settings.Physicality.AttackDistance)){
                if(targetDist > self.genetics.Get(self.settings.rangedAttack.BlindDist) ) {
                    self.TaskIndex = AnimalTasks.AttackTarget;
                } else self.TaskIndex = AnimalTasks.StepBackFromTarget;
            } else self.launcher.Fire(target.position, self);
        }

        //Task 9
        private static void StepBackFromEntity(Animal self) {
            if (!EntityManager.TryGetEntity(self.TaskTarget, out Entity target) || 
                Recognition.GetColliderDist(self, target) > self.genetics.Get(self.settings.rangedAttack.BlindDist)
            ) {
                if (self.TaskIndex == AnimalTasks.StepBackFromPrey){
                    self.TaskTarget = Guid.Empty;
                    self.TaskIndex = AnimalTasks.FindPrey;
                } else self.TaskIndex = AnimalTasks.ChaseTarget;
                return;
            }

            if (!self.pathFinder.hasPath) {
                if (Recognition.GetColliderDist(self, target) > self.genetics.Get(self.settings.rangedAttack.BlindDist)) {
                    if (self.TaskIndex == AnimalTasks.StepBackFromPrey){
                        self.TaskTarget = Guid.Empty;
                        self.TaskIndex = AnimalTasks.FindPrey;
                    } else self.TaskIndex = AnimalTasks.ChaseTarget;
                    return;
                }
                
                int PathDist = self.settings.Recognition.FleeDistance;
                float3 rayDir = self.GCoord - target.position;
                byte[] path = PathFinder.FindPathAlongRay(self.GCoord, ref rayDir, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            }
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration);
                
        }

        private static void AttackTarget(Animal self) {
            if (!EntityManager.TryGetEntity(self.TaskTarget, out Entity tEntity))
                self.TaskTarget = Guid.Empty;
            else if (tEntity is not IAttackable)
                self.TaskTarget = Guid.Empty;
            if (self.TaskTarget == Guid.Empty) {
                self.TaskIndex = AnimalTasks.Idle;
                return;
            }
            float targetDist = Recognition.GetColliderDist(tEntity, self);
            if (targetDist > self.genetics.Get(self.settings.Physicality.AttackDistance) || 
                targetDist < self.genetics.Get(self.settings.rangedAttack.BlindDist)) {
                self.TaskIndex = AnimalTasks.ChaseTarget;
                return;
            }

            float3 atkDir = math.normalize(tEntity.position - self.position); atkDir.y = 0;
            if (math.any(atkDir != 0)) self.tCollider.transform.rotation = Quaternion.RotateTowards(self.tCollider.transform.rotation,
            Quaternion.LookRotation(atkDir), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);

            IAttackable target = tEntity as IAttackable;
            if (target.IsDead) self.TaskIndex = AnimalTasks.Idle;
            else self.vitality.Attack(tEntity);
        }

        

        private static void RunFromPredator(Animal self) {
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration);
            if (!self.pathFinder.hasPath) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = AnimalTasks.Idle;
                return;
            }
        }

        //Task 12
        private static void Death(Animal self) {
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if (!self.IsDead) { //Bring back from the dead 
                self.TaskIndex = AnimalTasks.Idle;
                return;
            }
            //Kill the entity
            if (self.TaskDuration <= 0) EntityManager.ReleaseEntity(self.info.entityId);
        }

        public override void OnDrawGizmos() {
            if (!active) return;
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(CPUMapManager.GSToWS(position), tCollider.transform.size * 2);
            PathFinder.PathInfo finder = pathFinder; //copy so we don't modify the original
            if (finder.hasPath) {
                int ind = finder.currentInd;
                while (ind != finder.path.Length) {
                    int dir = finder.path[ind];
                    int3 dest = finder.currentPos + new int3((dir / 9) - 1, (dir / 3 % 3) - 1, (dir % 3) - 1);
                    Gizmos.DrawLine(CPUMapManager.GSToWS(finder.currentPos),
                                    CPUMapManager.GSToWS(dest));
                    finder.currentPos = dest;
                    ind++;
                }
            }
        }

        public class AnimalController {
            private Animal entity;
            private Animator animator;
            private GameObject gameObject;
            private GameObject root;
            private Transform transform;
            private Indicators indicators;
            private bool active = false;
            private int AnimatorTask;
            private bool PlayingAttack = false;

            private static Action<Animal>[] TaskRegistry = new Action<Animal>[]{
            Idle, RandomPath,
            FindPrey, ChasePreyEntity, StepBackFromEntity, 
            Attack, ChasePreyPlant, EatPlant, 
            ChaseTarget, StepBackFromEntity, AttackTarget,
            RunFromTarget, RunFromPredator, Death
        };

            private static readonly string[] AnimationNames = new string[]{
                "IsIdling",  null, 
                null, "IsRunning", "IsRunning",
                null, "IsWalking", "IsEating",
                "IsRunning", "IsRunning", null,
                "IsRunning", "IsRunning", "IsDead"
            };

            private enum AnimalTasks {
            Idle, RandomPath,
            FindPrey, ChasePreyEntity,  StepBackFromPrey,
            Attack, ChasePreyPlant, EatPlant, 
            ChaseTarget, StepBackFromTarget, AttackTarget,
            RunFromTarget, RunFromPredator, Death
        };

            public AnimalController(GameObject controller, Animal entity) {
                this.entity = entity;
                this.root = GameObject.Instantiate(controller);
                this.transform = root.transform;
                this.gameObject = transform.GetChild(0).gameObject;
                this.animator = gameObject.transform.GetComponent<Animator>();
                this.AnimatorTask = 0;
                this.active = true;

                indicators = new Indicators(gameObject, entity);
                transform.position = CPUMapManager.GSToWS(entity.position);
                if (AnimationNames[AnimatorTask] != null) animator.SetBool(AnimationNames[AnimatorTask], true);
            }

            public void Update() {
                if (!active) return;
                if (!entity.active) return;
                if (!entity.active) return;
                if (gameObject == null) return;
                this.transform.SetPositionAndRotation(CPUMapManager.GSToWS(entity.position), entity.transform.rotation);

#if UNITY_EDITOR
                if (UnityEditor.Selection.Contains(root)) {
                    Debug.Log(entity.TaskIndex);
                }
#endif

                indicators.Update();
                //Handle trigger based actions
                PlayAttacks();

                if (AnimatorTask == (int)entity.TaskIndex) return;
                if (AnimationNames[AnimatorTask] != null) animator.SetBool(AnimationNames[AnimatorTask], false);
                AnimatorTask = (int)entity.TaskIndex;
                if (AnimationNames[AnimatorTask] != null) animator.SetBool(AnimationNames[AnimatorTask], true);
            }

            private void PlayAttacks() {
                animator.SetBool("IsShooting", entity.launcher.ShotInProgress);

                if (!entity.vitality.AttackInProgress) PlayingAttack = false;
                if (AnimatorTask != (int)AnimalTasks.AttackTarget && AnimatorTask != (int)AnimalTasks.Attack) return;
                if (!PlayingAttack && entity.vitality.AttackInProgress) {
                    if (UnityEngine.Random.Range(0, 2) == 0) animator.SetTrigger("AttackL");
                    else animator.SetTrigger("AttackR");   
                    PlayingAttack = true;
                }

            }

            public void Dispose() {
                if (!active) return;
                active = false;
                entity = null;

                indicators.Release();
                Destroy(root);
            }

            ~AnimalController() {
                Dispose();
            }
        }
    }
}