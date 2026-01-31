using System;
using System.Collections.Generic;
using System.Linq;
using Arterra.Core.Storage;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using Arterra.Configuration;
using Arterra.Data.Entity;
using Arterra.Core.Events;
using TerrainCollider = Arterra.GamePlay.Interaction.TerrainCollider;
using Arterra.GamePlay.Interaction;

[CreateAssetMenu(menuName = "Generation/Entity/EelEnemy")]
public class EelEnemy : Authoring {
    public Option<EelSettings> _Setting;
    
    [JsonIgnore]
    public override Entity Entity { get => new Animal(); }
    [JsonIgnore]
    public override EntitySetting Setting { get => _Setting.value; set => _Setting.value = (EelSettings)value; }
    [Serializable]
    public class EelSettings : EntitySetting {
        public Genetics.GeneFeature EnemyCheckDist;
        public Genetics.GeneFeature EnemyCheckDelay;
        public Movement movement;
        public Movement.Aquatic aquatic;
        public Option<MinimalRecognition> recognition;
        public Option<MediumVitality.Stats> physicality;
        public Vitality.Decomposition decomposition;
        public MultiAttack attack;
        public float ConsumptionRate;
        public MinimalRecognition Recognition => recognition;
        public MediumVitality.Stats Physicality => physicality;
        public ProceduralSegmentSettings Animation;
        public bool IndependentWalk = true;
        [Serializable]
        public struct HeadInfo {
            public float3 Offset;
            public TerrainCollider.Settings Collider;
        }

        [Serializable]
        public struct MultiAttack {
            public ProjectileLauncher.Stats Projectile;
            public Genetics.GeneFeature BlindDist;
            public void InitGenome(uint entityType) {
                Projectile.InitGenome(entityType);
                Genetics.AddGene(entityType, ref BlindDist);
            }
        }
        [Serializable]
        public class ProceduralSegmentSettings : ProceduralAnimated.PASettings {
            public override List<ProceduralAnimated.AppendageSettings> Appendages => Segments.value.Select(l => (ProceduralAnimated.AppendageSettings)l).ToList();
            public Option<List<SegmentSettings>> Segments;
            public Option<AnimationCurve> SlitherX;
            public Option<AnimationCurve> SlitherY;
            public float SlitherSpeed;
            public float MoveAccel;
        }

        [Serializable]
        public class SegmentSettings : ProceduralAnimated.AppendageSettings {
            public float MoveAccel;
            public float Phase;
            public float RBStrength = 1.0f;
        }

        public override void Preset(uint entityType)
        {
            uint pEnd = profile.bounds.x * profile.bounds.y * profile.bounds.z;
            aquatic.SurfaceProfile.profileStart = profile.profileStart + pEnd;
            Recognition.Construct();

            movement.InitGenome(entityType);
            aquatic.InitGenome(entityType);
            Physicality.InitGenome(entityType);
            Recognition.InitGenome(entityType);
            decomposition.InitGenome(entityType);
            attack.InitGenome(entityType);
            Genetics.AddGene(entityType, ref EnemyCheckDelay);
            Genetics.AddGene(entityType, ref EnemyCheckDist);
            Animation.Preset();

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
        private Unity.Mathematics.Random random;
        [JsonProperty]
        internal Tail Animate;
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
            SwimUp, Surface,
            ChaseTarget, StepBackFromEntity, AttackTarget,
            RunFromTarget, RunFromPredator, FlopOnGround, Death
        };
        private enum AnimalTasks {
            Idle, RandomPath,
            FindPrey, ChasePreyEntity,  StepBackFromPrey,
            Attack, ChasePreyPlant, EatPlant, 
            SwimUp, Surface,
            ChaseTarget, StepBackFromTarget, AttackTarget,
            RunFromTarget, RunFromPredator, FlopOnGround, Death
        };

        
        private AnimalController controller;
        private EelSettings settings;
        [JsonIgnore]
        public override ref TerrainCollider.Transform transform => ref Animate.Head.transform;
        [JsonIgnore]
        public int3 GCoord => (int3)math.floor(origin);
        [JsonIgnore]
        public bool IsDead => vitality.IsDead;
        
        public void Interact(Entity target) { }
        //ToDo: Finish implemenation here
        public Arterra.Data.Item.IItem Collect(Entity target, float amount) {
            Arterra.Data.Item.IItem item = null; 
            eventCtrl.RaiseEvent(GameEvent.Entity_Collect, this, target, (item, amount));
            return null;
        }
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
            Animate.Disable();
            controller.Dispose();
        }
        
        public override void Initialize(EntitySetting setting, GameObject Controller, float3 GCoord) {
            settings = (EelSettings)setting;
            
            Animate = new Tail();
            Animate.Initialize<TailSegment>(this, settings.Animation, GCoord, settings.collider, ProcessFallDamage);

            random = new Unity.Mathematics.Random((uint)UnityEngine.Random.Range(0, int.MaxValue));
            this.genetics ??= new Genetics(this.info.entityType, ref random);
            this.vitality = new MediumVitality(settings.Physicality, this.genetics);
            this.launcher = new ProjectileLauncher(settings.attack.Projectile, this.genetics);
            controller = new AnimalController(Controller, this);
            this.TaskDuration = genetics.Get(settings.EnemyCheckDelay);
            this.TaskIndex = AnimalTasks.Idle;
        }//

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord) {
            settings = (EelSettings)setting;
            Animate.Deserialize<TailSegment>(this, settings.Animation, ProcessFallDamage);
            
            controller = new AnimalController(Controller, this);
            vitality.Deserialize(settings.Physicality, genetics);
            launcher.Deserialize(settings.attack.Projectile, genetics);
            random.state ^= (uint)GetHashCode();
            GCoord = this.GCoord;
        }

        public override void Update() {//
            if (!active) return;
            Animate.Update();
            EntityManager.AddHandlerEvent(controller.Update);

            TerrainInteractor.DetectMapInteraction(position,
            OnInSolid: (dens) => vitality.ProcessInSolid(this, dens),
            OnInLiquid: (dens) => vitality.ProcessInLiquidAquatic(this, ref Animate.Body, dens, genetics.Get(settings.aquatic.DrownTime)),
            OnInGas: (dens) => {
                vitality.ProcessInGasAquatic(this, ref Animate.Body, dens);
                if (TaskIndex < AnimalTasks.FlopOnGround) TaskIndex = AnimalTasks.FlopOnGround;
            });

            vitality.Update(this);
            launcher.Update(this);
            TaskRegistry[(int)TaskIndex].Invoke(this);
            //Shared high priority states
            if (TaskIndex != AnimalTasks.Death && vitality.IsDead) {
                TaskDuration = genetics.Get(settings.decomposition.DecompositionTime);
                TaskIndex = AnimalTasks.Death;
            } else if (TaskIndex <= AnimalTasks.SwimUp && IsSurfacing()) TaskIndex = AnimalTasks.SwimUp;
            else if (TaskIndex < AnimalTasks.RunFromPredator) DetectPredator();
        }


        private void DetectPredator() {
            if (!settings.Recognition.FindClosestPredator(this, genetics.Get(
                settings.Recognition.SightDistance), out Entity predator))
                return;

            int PathDist = settings.Recognition.FleeDistance;
            float3 rayDir = Animate.BodyPosition - predator.position;
            byte[] path = PathFinder.FindPathAlongRay(Animate.BodyGCoord, ref rayDir, PathDist + 1, settings.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(Animate.BodyGCoord, path, pLen);
            TaskIndex = AnimalTasks.RunFromPredator;
        }

        private bool DetectPrey() {
            if (settings.Recognition.FindPreferredPreyEntity(this,
                genetics.Get(settings.EnemyCheckDist),
                out Entity entity, TestNotDead)) {
                TaskIndex = AnimalTasks.FindPrey;
                return true;
            } return false;
        }
        
        private bool IsSurfacing() {
            if (vitality.breath > 0) return false; //In air
            if (genetics.Get(settings.aquatic.SurfaceThreshold) == 0) return false; //Doesn't drown
            if (-vitality.breath > genetics.Get(settings.aquatic.SurfaceThreshold)
                * genetics.Get(settings.aquatic.DrownTime))
                return false; //Still holding breath
            return true;
        }

        public static void Idle(Animal self) {
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if (self.TaskDuration > 0) return;
            if (self.DetectPrey()) return;

            if (self.settings.IndependentWalk) {
                self.TaskDuration = self.settings.movement.AverageIdleTime;
                self.TaskIndex = AnimalTasks.RandomPath;
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

            self.DetectPrey();
            if (self.pathFinder.hasPath) {
                Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.Animate.Body,
                self.genetics.Get(self.settings.movement.walkSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
                return;
            };
            
            int PathDist = self.settings.movement.pathDistance;
            int3 dP = new(self.random.NextInt(-PathDist, PathDist), self.random.NextInt(-PathDist, PathDist), self.random.NextInt(-PathDist, PathDist));
            if (PathFinder.VerifyProfile(self.Animate.BodyGCoord + dP, self.settings.profile, EntityJob.cxt)) {
                byte[] path = PathFinder.FindPath(self.Animate.BodyGCoord, dP, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.Animate.BodyGCoord, path, pLen);
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
                int3 destination = (int3)math.round(prey.origin) - self.Animate.BodyGCoord;
                dist = Recognition.GetColliderDist(self, prey);
                byte[] path = PathFinder.FindPathOrApproachTarget(self.Animate.BodyGCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.Animate.BodyGCoord, path, pLen);
                self.TaskIndex = AnimalTasks.ChasePreyEntity;
                preyAction = AnimalTasks.Attack;
            } else if(self.settings.Recognition.FindPreferredPreyPlant(
                self.Animate.BodyGCoord, self.genetics.GetInt(
                self.settings.Recognition.PlantFindDist), out int3 preyPos)) {
                byte[] path = PathFinder.FindPathOrApproachTarget(self.Animate.BodyGCoord, preyPos - self.Animate.BodyGCoord, self.genetics.GetInt(
                    self.settings.Recognition.PlantFindDist) + 1, self.settings.profile,
                    EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.Animate.BodyGCoord, path, pLen);
                dist = Recognition.GetColliderDist(self, prey);
                self.TaskIndex = AnimalTasks.ChasePreyPlant;
                preyAction = AnimalTasks.EatPlant;
            } else {
                self.TaskDuration = self.settings.movement.AverageIdleTime;
                self.TaskIndex = AnimalTasks.RandomPath;
                return;
            }

            //If it can't get to the prey and is currently at the closest position it can be
            if (math.all(self.pathFinder.destination == self.Animate.BodyGCoord)) {
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
            Movement.FollowDynamicPath(self.settings.profile, ref self.pathFinder, ref self.Animate.Body, prey.origin,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            
            
            float preyDist = Recognition.GetColliderDist(self, prey);
            if (preyDist < self.genetics.Get(self.settings.Physicality.AttackDistance)) {
                if(preyDist > self.genetics.Get(self.settings.attack.BlindDist) ) {
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
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.Animate.Body,
                self.genetics.Get(self.settings.movement.walkSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            if (self.pathFinder.hasPath) return;

            if (self.settings.Recognition.FindPreferredPreyPlant(self.Animate.BodyGCoord, self.genetics.GetInt(
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
                if (self.settings.Recognition.FindPreferredPreyPlant(self.Animate.BodyGCoord, self.genetics.GetInt(
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
                preyDist < self.genetics.Get(self.settings.attack.BlindDist)) {
                self.TaskTarget = Guid.Empty;
                return;
            }

            if (prey is not IAttackable target || target.IsDead) {
                self.TaskIndex = AnimalTasks.Idle;
                return;
            }

            float3 atkDir = math.normalize(prey.position - self.position); atkDir.y = 0;
            if (math.any(atkDir != 0)) self.Animate.Body.transform.rotation = Quaternion.RotateTowards(self.Animate.Body.transform.rotation,
            Quaternion.LookRotation(atkDir), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);
            self.vitality.Attack(prey);
            self.TaskIndex = AnimalTasks.Attack;
        }

        private static float3 Normalize(float3 v) {
            if (math.length(v) == 0) return math.forward();
            else return math.normalize(v);
            //This norm guarantees the vector will be on the edge of a cube
        }

        private float3 RandomDirection() {
            float3 normal = new(random.NextFloat(-1, 1), random.NextFloat(-1, 1), random.NextFloat(-1, 1));
            if (math.length(normal) == 0) return math.forward();
            else return Normalize(normal);
        }

        private static void SwimUp(Animal self) {
            float swimIntensity = self.genetics.Get(self.settings.aquatic.DrownTime)
                * self.genetics.Get(self.settings.aquatic.SurfaceThreshold) / math.min(-self.vitality.breath, -0.001f);

            float3 swimDir = Normalize(self.RandomDirection() + math.up() * math.max(0, swimIntensity));
            byte[] path = PathFinder.FindMatchAlongRay(self.GCoord, in swimDir,
                self.settings.movement.pathDistance + 1, self.settings.profile,
                self.settings.aquatic.SurfaceProfile, EntityJob.cxt, out int pLen, out bool _);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = AnimalTasks.Surface;
        }

        //Task 10 follow swim up path
        private static void Surface(Animal self) {
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.Animate.Body,
                self.genetics.Get(self.settings.movement.walkSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            if (!self.pathFinder.hasPath) {
                self.TaskIndex = AnimalTasks.SwimUp;
                return;
            }
            if (!self.IsSurfacing()) {
                self.TaskIndex = AnimalTasks.RandomPath; //Go back to swiming
            }
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
                float3 rayDir = self.Animate.BodyGCoord - target.position;
                byte[] path = PathFinder.FindPathAlongRay(self.Animate.BodyGCoord, ref rayDir, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.Animate.BodyGCoord, path, pLen);
            }
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.Animate.Body,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
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
                int3 destination = (int3)math.round(target.origin) - self.Animate.BodyGCoord;
                byte[] path = PathFinder.FindPathOrApproachTarget(self.Animate.BodyGCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.Animate.BodyGCoord, path, pLen);
            }

            Movement.FollowDynamicPath(self.settings.profile, ref self.pathFinder, ref self.Animate.Body, target.origin,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            float targetDist = Recognition.GetColliderDist(self, target);


            if (targetDist < self.genetics.Get(self.settings.Physicality.AttackDistance)){
                if(targetDist > self.genetics.Get(self.settings.attack.BlindDist) ) {
                    self.TaskIndex = AnimalTasks.AttackTarget;
                } else self.TaskIndex = AnimalTasks.StepBackFromTarget;
            } else self.launcher.Fire(target.position, self);
        }

        //Task 9
        private static void StepBackFromEntity(Animal self) {
            if (!EntityManager.TryGetEntity(self.TaskTarget, out Entity target) || 
                Recognition.GetColliderDist(self, target) > self.genetics.Get(self.settings.attack.BlindDist)
            ) {
                if (self.TaskIndex == AnimalTasks.StepBackFromPrey){
                    self.TaskTarget = Guid.Empty;
                    self.TaskIndex = AnimalTasks.FindPrey;
                } else self.TaskIndex = AnimalTasks.ChaseTarget;
                return;
            }

            if (!self.pathFinder.hasPath) {
                if (Recognition.GetColliderDist(self, target) > self.genetics.Get(self.settings.attack.BlindDist)) {
                    if (self.TaskIndex == AnimalTasks.StepBackFromPrey){
                        self.TaskTarget = Guid.Empty;
                        self.TaskIndex = AnimalTasks.FindPrey;
                    } else self.TaskIndex = AnimalTasks.ChaseTarget;
                    return;
                }
                
                int PathDist = self.settings.Recognition.FleeDistance;
                float3 rayDir = self.Animate.BodyGCoord - target.position;
                byte[] path = PathFinder.FindPathAlongRay(self.Animate.BodyGCoord, ref rayDir, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.Animate.BodyGCoord, path, pLen);
            }
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.Animate.Body,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
                
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
                targetDist < self.genetics.Get(self.settings.attack.BlindDist)) {
                self.TaskIndex = AnimalTasks.ChaseTarget;
                return;
            }

            float3 atkDir = math.normalize(tEntity.position - self.position); atkDir.y = 0;
            if (math.any(atkDir != 0)) self.Animate.Body.transform.rotation = Quaternion.RotateTowards(self.Animate.Body.transform.rotation,
            Quaternion.LookRotation(atkDir), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);

            IAttackable target = tEntity as IAttackable;
            if (target.IsDead) self.TaskIndex = AnimalTasks.Idle;
            else self.vitality.Attack(tEntity);
        }

        

        private static void RunFromPredator(Animal self) {
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.Animate.Body,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            if (!self.pathFinder.hasPath) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = AnimalTasks.Idle;
                return;
            }
        }

        private static void FlopOnGround(Animal self) {
            if (self.vitality.breath < 0) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 0; //Idle
                return;
            }

            if (self.Animate.Head.SampleCollision(self.origin, new float3(self.settings.collider.size.x,
            -self.settings.aquatic.JumpStickDistance, self.settings.collider.size.z), EntityJob.cxt.mapContext, out _)) {
                self.velocity.y += self.settings.aquatic.JumpStrength;
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
            //Gizmos.DrawWireCube(CPUMapManager.GSToWS(position), settings.Animation.Head.value.Collider.size * 2)
            Gizmos.DrawWireCube(CPUMapManager.GSToWS(Animate.BodyPosition), settings.collider.size* 2);
            foreach(TailSegment l in Animate.appendages) {
                float3 center = CPUMapManager.GSToWS(
                    l.tCollider.transform.position + l.tCollider.transform.size/2);
                Gizmos.DrawLine(center, center + math.mul(l.collider.transform.rotation, l.settings.RestOffset));
                Gizmos.DrawWireCube(center, l.tCollider.transform.size * 2);
            }

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

        internal class Tail : ProceduralAnimated {
            public float SlitherProgress;
            private EelSettings.ProceduralSegmentSettings sSetting;
            public override void Initialize<T>(Entity entity, PASettings settings, float3 GCoord, TerrainCollider.Settings RootCollider, Action<float> ProcessFallDamage) {
                base.Initialize<T>(entity, settings, GCoord, RootCollider, ProcessFallDamage);
                this.sSetting = settings as EelSettings.ProceduralSegmentSettings;
                SlitherProgress = 0;
                AttachSegments();
            }

            public override void Deserialize<T>(Entity entity, PASettings settings, Action<float> ProcessFallDamage) {
                this.sSetting = settings as EelSettings.ProceduralSegmentSettings;
                base.Deserialize<T>(entity, settings, ProcessFallDamage);
                AttachSegments();   
            }

            private void AttachSegments() {
                //Attach segments together
                TerrainCollider prev = Head;
                for(int i = 0; i < appendages.Length - 1; i++){
                    (appendages[i] as TailSegment).AttachSegment(prev, appendages[i+1] as TailSegment);
                    prev = appendages[i].collider;
                } (appendages[^1] as TailSegment).AttachSegment(prev, default, false);
            }

            public override void Update() {
                SlitherProgress += EntityJob.cxt.deltaTime * sSetting.SlitherSpeed;
                SlitherProgress = math.frac(SlitherProgress);

                float3 desiredHead = BodyPosition + settings.Head.value.Offset;
                Head.transform.velocity += EntityJob.cxt.deltaTime * (desiredHead - HeadPosition) * sSetting.MoveAccel;
                float3 origin = appendages[0].desiredBody;
                ApplyRubberBand(Head, origin, settings.Head.value.RubberbandStrength);
                origin = HeadPosition - settings.Head.value.Offset;
                ApplyRubberBand(Body, origin, settings.RubberbandStrength);

                foreach(Appendage l in appendages) {
                    if (selfAtk.IsDead) l.Update();
                    else l.UpdateMovement();
                };

                Head.transform.rotation = Body.transform.rotation;
                Head.useGravity = false; //Head doesn't use gravity
                Head.Update(self);
                Body.Update();
            }

            private static void ApplyRubberBand(TerrainCollider collider, float3 origin, float strength) {
                float3 center = collider.transform.position + collider.transform.size/2;
                float3 dir = math.normalizesafe(origin - center);
                collider.transform.velocity += strength * EntityJob.cxt.deltaTime * dir; 
            }
        }

        private class TailSegment : ProceduralAnimated.Appendage {
            [JsonIgnore]
            private Animal Animal;
            [JsonIgnore]
            public EelSettings.SegmentSettings settings;
            [JsonProperty]
            private Guid LegId;
            [JsonProperty]
            public TerrainCollider tCollider;
            private bool active;

            //Ignored
            private TerrainCollider PrevSegment;
            private TailSegment NextSegment;
            private bool HasNext;

            private float3 PrevCenter => PrevSegment.transform.position + PrevSegment.transform.size / 2;
            private float3 NextCenter => NextSegment.collider.transform.position + NextSegment.collider.transform.size / 2;
            [JsonIgnore]
            public override TerrainCollider collider => tCollider;
            public override float3 desiredBody => center - math.mul(PrevSegment.transform.rotation, settings.RestOffset);
            private float3 center => tCollider.transform.position + tCollider.transform.size / 2;

            [JsonConstructor]
            public TailSegment() {} //Newtonsoft path
            public override void Initialize(Entity animal, ProceduralAnimated.AppendageSettings settings) {
                this.settings = settings as EelSettings.SegmentSettings;
                this.Animal = animal as Animal;
                this.tCollider = new TerrainCollider(settings.collider, animal.position);
                this.LegId = Guid.NewGuid();
                active = false;
            }

            public override void Deserialize(Entity animal, ProceduralAnimated.AppendageSettings settings) {
                this.settings = settings as EelSettings.SegmentSettings; 
                this.Animal = animal as Animal;
                if (!EntityManager.EntityIndex.TryAdd(LegId, animal))
                    throw new Exception("Failed to register tail segment for entity");
                active = true;
                
                this.tCollider.useGravity = false;
                Bounds bounds = new Bounds(center, settings.collider.size);
                EntityManager.ESTree.Insert(bounds, LegId);
            }

            public void AttachSegment(TerrainCollider PrevSegment, TailSegment NextSegment, bool HasNext = true) {
                this.PrevSegment = PrevSegment;
                this.NextSegment = NextSegment;
                this.HasNext = HasNext;
                if (active) return;
                
                //Finish setup
                this.tCollider.transform.position = PrevCenter + settings.RestOffset;
                this.tCollider.transform.rotation = Animal.transform.rotation;
                Deserialize(Animal, settings);
                active = true;
            }

            public override void Update() {
                EntityManager.AddHandlerEvent(() => {
                    Bounds bounds = new Bounds(center, settings.collider.size);
                    EntityManager.ESTree.AssertEntityLocation(LegId, bounds);  
                });
                if(HasNext) {
                    ApplyRubberBand(
                        NextCenter - math.mul(tCollider.transform.rotation, NextSegment.settings.RestOffset),
                        NextSegment.settings.RBStrength
                    );
                }

                float3 origin = PrevCenter + math.mul(PrevSegment.transform.rotation, settings.RestOffset);
                ApplyRubberBand(origin, settings.RBStrength);

                this.tCollider.Update(Animal);
            }

            private void ApplyRubberBand(float3 origin, float strength) {
                float dist = math.distance(origin, center);
                if (dist > tCollider.transform.size.y) { //Rubber banding
                    float3 dir = math.normalizesafe(origin - center);
                    strength *= math.distance(origin, center);
                    tCollider.transform.velocity += strength * EntityJob.cxt.deltaTime * dir; 
                }
            }

            public override void UpdateMovement() {
                Update();
                this.tCollider.transform.velocity *= 0.75f;

                float3 aim;
                float3 origin = PrevCenter + math.mul(PrevSegment.transform.rotation, settings.RestOffset);
                float t = math.frac(Animal.Animate.SlitherProgress + settings.Phase);
                float3 xOff = math.mul(PrevSegment.transform.rotation, Animal.settings.Animation.SlitherX.value.Evaluate(t) * Vector3.up);
                float3 yOff = math.mul(PrevSegment.transform.rotation, Animal.settings.Animation.SlitherY.value.Evaluate(t) * Vector3.right);
                aim = (origin - center) + xOff + yOff; 

                aim = math.normalizesafe(aim);
                if (Quaternion.Angle(PrevSegment.transform.rotation, tCollider.transform.rotation) > 15) 
                    tCollider.transform.rotation = Quaternion.Slerp(tCollider.transform.rotation, PrevSegment.transform.rotation, EntityJob.cxt.deltaTime);   
            
                tCollider.transform.velocity += settings.MoveAccel * EntityJob.cxt.deltaTime * aim;
            }

            public override void Disable() {
                if (!active) return;
                active = false;

                EntityManager.EntityIndex.Remove(LegId);
                EntityManager.ESTree.Delete(LegId);
            }
        }

        public class AnimalController : ProceduralAnimated.AnimalController<MultiParentConstraint>{
            private Animal entity;
            private Indicators indicators;
            private bool active = false;
            private int AnimatorTask;
            private bool PlayingAttack = false;

            private static readonly string[] AnimationNames = new string[]{
                null,  "IsRunning", 
                null, "IsRunning", "IsRunning",
                "IsAttacking", "IsRunning", "IsAttacking",
                "IsRunning", null,
                "IsRunning", "IsRunning", "IsAttacking",
                "IsRunning", "IsRunning", null, "IsDead"
            };

            private enum AnimalTasks {
                Idle, RandomPath,
                FindPrey, ChasePreyEntity,  StepBackFromPrey,
                Attack, ChasePreyPlant, EatPlant, 
                SwimUp, Surface,
                ChaseTarget, StepBackFromTarget, AttackTarget,
                RunFromTarget, RunFromPredator, FlopOnGround, Death
            };

            public AnimalController(GameObject controller, Animal entity) : base(
                controller,
                entity.Animate.appendages,
                entity.settings.Animation
            ) {
                this.AnimatorTask = 0;
                this.active = true;
                this.entity = entity;

                indicators = new Indicators(gameObject, entity);
            }

            public override void Update() {
                if (!active) return;
                if (!entity.active) return;
                if (gameObject == null) return;
                this.transform.SetPositionAndRotation(CPUMapManager.GSToWS(entity.position), entity.Animate.Head.transform.rotation);
                base.Update();
                
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