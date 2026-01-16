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
using Arterra.Core.Events;

[CreateAssetMenu(menuName = "Generation/Entity/MultiPedal")]
public class MultiPedal : Authoring {
    public Option<MultiPedalSettings> _Setting;
    
    [JsonIgnore]
    public override Entity Entity { get => new Animal(); }
    [JsonIgnore]
    public override EntitySetting Setting { get => _Setting.value; set => _Setting.value = (MultiPedalSettings)value; }
    [Serializable]
    public class MultiPedalSettings : EntitySetting {
        public Genetics.GeneFeature EnemyCheckDist;
        public Genetics.GeneFeature EnemyCheckDelay;
        public Movement movement;
        public Option<MinimalRecognition> recognition;
        public Option<MediumVitality.Stats> physicality;
        public Vitality.Decomposition decomposition;
        public MultiAttack attack;
        public float ConsumptionRate;
        public MinimalRecognition Recognition => recognition;
        public MediumVitality.Stats Physicality => physicality;
        public TorsoInfo Torso;
        public Option<List<LegSettings>> Legs;

        [UISetting(Ignore = true)]
        public List<AnimOverride> AnimControl;
        [UISetting(Ignore = true)][JsonIgnore]
        public Dictionary<int, uint> AnimToggle;
        [Serializable]
        public struct AnimOverride {
            public string AnimName;
            public uint LegControlBitmap;
        }

        [Serializable]
        public struct TorsoInfo {
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
        public struct LegSettings {
            public TerrainCollider.Settings collider;
            //The offset relative to the center of the body collider
            // of the center of the leg collider
            public float3 RestOffset;
            [UISetting(Ignore = true)]
            public string IKObjectPath;
            public float StepDistThreshold;
            public float LegRaiseHeight;
            public float MoveAccel;
        }

        public override void Preset(uint entityType)
        {
            Recognition.Construct();

            movement.InitGenome(entityType);
            Physicality.InitGenome(entityType);
            Recognition.InitGenome(entityType);
            decomposition.InitGenome(entityType);
            attack.InitGenome(entityType);
            Genetics.AddGene(entityType, ref EnemyCheckDelay);
            Genetics.AddGene(entityType, ref EnemyCheckDist);

            AnimToggle = new Dictionary<int, uint>();
            foreach(AnimOverride anim in AnimControl) {
                int hash = Animator.StringToHash(anim.AnimName);
                AnimToggle.TryAdd(hash, anim.LegControlBitmap);
            }

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
        private TerrainCollider Torso;
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

        [JsonProperty]
        private TerrainCollider Body;
        [JsonIgnore]
        private float3 BodyPosition {
            get => Body.transform.position + Body.transform.size / 2;
            set => Body.transform.position = value - Body.transform.size / 2;
        }
        [JsonIgnore]
        private float3 BodyOrigin {
            get => Body.transform.position;
            set => Body.transform.position = value;
        }
        [JsonIgnore]
        public int3 BodyGCoord => (int3)math.floor(BodyOrigin);
        [JsonProperty]
        private Leg[] Legs; 
        private AnimalController controller;
        private MultiPedalSettings settings;
        [JsonIgnore]
        public override ref TerrainCollider.Transform transform => ref Torso.transform;
        [JsonIgnore]
        public int3 GCoord => (int3)math.floor(origin);
        [JsonIgnore]
        public bool IsDead => vitality.IsDead;
        
        public void Interact(Entity target) { }
        //ToDo: Finish implemenation here
        public Arterra.Configuration.Generation.Item.IItem Collect(Entity target, float amount) {
            Arterra.Configuration.Generation.Item.IItem item = null; 
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
            foreach(Leg l in Legs) l.Release();
            controller.Dispose();
        }
        
        public override void Initialize(EntitySetting setting, GameObject Controller, float3 GCoord) {
            settings = (MultiPedalSettings)setting;

            this.Body = new TerrainCollider(this.settings.collider, GCoord);
            this.Torso = new TerrainCollider(settings.Torso.Collider, GCoord + settings.Torso.Offset, ProcessFallDamage);
            Legs = new Leg[settings.Legs.value.Count];
            for(int i = 0; i < settings.Legs.value.Count; i++) {
                Legs[i] = new Leg(this, settings.Legs.value[i]);
                Legs[i].Initialize(this, settings.Legs.value[i]);
            }

            random = new Unity.Mathematics.Random((uint)UnityEngine.Random.Range(0, int.MaxValue));
            controller = new AnimalController(Controller, this);
            this.genetics ??= new Genetics(this.info.entityType, ref random);
            this.vitality = new MediumVitality(settings.Physicality, this.genetics);
            this.launcher = new ProjectileLauncher(settings.attack.Projectile, this.genetics);
            this.TaskDuration = genetics.Get(settings.EnemyCheckDelay);
            this.TaskIndex = AnimalTasks.Idle;
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord) {
            settings = (MultiPedalSettings)setting;

            Legs ??= new Leg[settings.Legs.value.Count];
            for(int i = 0; i < settings.Legs.value.Count; i++){
                Legs[i] ??= new Leg(this, settings.Legs.value[i]);
                Legs[i].Initialize(this, settings.Legs.value[i]);
            }
            
            controller = new AnimalController(Controller, this);
            vitality.Deserialize(settings.Physicality, genetics);
            launcher.Deserialize(settings.attack.Projectile, genetics);
            Torso.OnHitGround = ProcessFallDamage;
            random.state ^= (uint)GetHashCode();
            GCoord = this.GCoord;
        }

        public override void Update() {
            if (!active) return;
            UpdateColliders();
            EntityManager.AddHandlerEvent(controller.Update);

            TerrainInteractor.DetectMapInteraction(position,
            OnInSolid: (dens) => vitality.ProcessInSolid(this, dens),
            OnInLiquid: (dens) => vitality.ProcessInLiquid(this, ref Torso, dens),
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

        private void UpdateColliders() {
            foreach(Leg l in Legs) {
                if (IsDead) l.Update();
                else l.UpdateMovement();

                float dist = math.distance(l.desiredBody, BodyPosition);
                //Apply weaker rubber banding to invisible body
                if (dist > l.tCollider.transform.size.y) { 
                    dist -= l.tCollider.transform.size.y;
                    float3 dir = math.normalizesafe(l.desiredBody - BodyPosition);
                    float strength = math.pow(dist, 1.0f);
                    Body.transform.velocity += strength * EntityJob.cxt.deltaTime * dir;
                }
                
                float3 desiredTorso = l.desiredBody + settings.Torso.Offset;
                Torso.transform.velocity += EntityJob.cxt.deltaTime * 10 * (desiredTorso - position);
            };

            Torso.transform.rotation = Body.transform.rotation;
            Torso.useGravity = false;
            Body.useGravity = true;
            Torso.Update(this);
            Body.Update();
        }

        private void DetectPredator() {
            if (!settings.Recognition.FindClosestPredator(this, genetics.Get(
                settings.Recognition.SightDistance), out Entity predator))
                return;

            int PathDist = settings.Recognition.FleeDistance;
            float3 rayDir = BodyPosition - predator.position;
            byte[] path = PathFinder.FindPathAlongRay(BodyGCoord, ref rayDir, PathDist + 1, settings.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(BodyGCoord, path, pLen);
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

            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.Body,
                self.genetics.Get(self.settings.movement.walkSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration);
            if (self.pathFinder.hasPath) return;
            int PathDist = self.settings.movement.pathDistance;
            int3 dP = new(self.random.NextInt(-PathDist, PathDist), self.random.NextInt(-PathDist, PathDist), self.random.NextInt(-PathDist, PathDist));
            if (PathFinder.VerifyProfile(self.BodyGCoord + dP, self.settings.profile, EntityJob.cxt)) {
                byte[] path = PathFinder.FindPath(self.BodyGCoord, dP, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.BodyGCoord, path, pLen);
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
                int3 destination = (int3)math.round(prey.origin) - self.BodyGCoord;
                dist = Recognition.GetColliderDist(self, prey);
                byte[] path = PathFinder.FindPathOrApproachTarget(self.BodyGCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.BodyGCoord, path, pLen);
                self.TaskIndex = AnimalTasks.ChasePreyEntity;
                preyAction = AnimalTasks.Attack;
            } else if(self.settings.Recognition.FindPreferredPreyPlant(
                self.BodyGCoord, self.genetics.GetInt(
                self.settings.Recognition.PlantFindDist), out int3 preyPos)) {
                byte[] path = PathFinder.FindPathOrApproachTarget(self.BodyGCoord, preyPos - self.BodyGCoord, self.genetics.GetInt(
                    self.settings.Recognition.PlantFindDist) + 1, self.settings.profile,
                    EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.BodyGCoord, path, pLen);
                dist = Recognition.GetColliderDist(self, prey);
                self.TaskIndex = AnimalTasks.ChasePreyPlant;
                preyAction = AnimalTasks.EatPlant;
            } else {
                self.TaskDuration = self.settings.movement.AverageIdleTime;
                self.TaskIndex = AnimalTasks.RandomPath;
                return;
            }

            //If it can't get to the prey and is currently at the closest position it can be
            if (math.all(self.pathFinder.destination == self.BodyGCoord)) {
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
            Movement.FollowDynamicPath(self.settings.profile, ref self.pathFinder, ref self.Body, prey.origin,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration);
            
            
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
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.Body,
                self.genetics.Get(self.settings.movement.walkSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration);
            if (self.pathFinder.hasPath) return;

            if (self.settings.Recognition.FindPreferredPreyPlant(self.BodyGCoord, self.genetics.GetInt(
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
                if (self.settings.Recognition.FindPreferredPreyPlant(self.BodyGCoord, self.genetics.GetInt(
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
            if (math.any(atkDir != 0)) self.Body.transform.rotation = Quaternion.RotateTowards(self.Body.transform.rotation,
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
                float3 rayDir = self.BodyGCoord - target.position;
                byte[] path = PathFinder.FindPathAlongRay(self.BodyGCoord, ref rayDir, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.BodyGCoord, path, pLen);
            }
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.Body,
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
                int3 destination = (int3)math.round(target.origin) - self.BodyGCoord;
                byte[] path = PathFinder.FindPathOrApproachTarget(self.BodyGCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.BodyGCoord, path, pLen);
            }

            Movement.FollowDynamicPath(self.settings.profile, ref self.pathFinder, ref self.Body, target.origin,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration);
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
                float3 rayDir = self.BodyGCoord - target.position;
                byte[] path = PathFinder.FindPathAlongRay(self.BodyGCoord, ref rayDir, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.BodyGCoord, path, pLen);
            }
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.Body,
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
                targetDist < self.genetics.Get(self.settings.attack.BlindDist)) {
                self.TaskIndex = AnimalTasks.ChaseTarget;
                return;
            }

            float3 atkDir = math.normalize(tEntity.position - self.position); atkDir.y = 0;
            if (math.any(atkDir != 0)) self.Body.transform.rotation = Quaternion.RotateTowards(self.Body.transform.rotation,
            Quaternion.LookRotation(atkDir), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);

            IAttackable target = tEntity as IAttackable;
            if (target.IsDead) self.TaskIndex = AnimalTasks.Idle;
            else self.vitality.Attack(tEntity);
        }

        

        private static void RunFromPredator(Animal self) {
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.Body,
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
            Gizmos.DrawWireCube(CPUMapManager.GSToWS(position), settings.Torso.Collider.size * 2);
            foreach(Leg l in Legs) {
                if (l.State == Leg.StepState.Stand) Gizmos.color = Color.green;
                else if (l.State == Leg.StepState.Raise) Gizmos.color = Color.red;
                else Gizmos.color = Color.blue;
                Gizmos.DrawWireCube(CPUMapManager.GSToWS(
                    l.tCollider.transform.position + l.tCollider.transform.size/2),
                    l.tCollider.transform.size * 2);
                Gizmos.DrawSphere(CPUMapManager.GSToWS(l.TargetPosition), 0.25f);
            }
        }

        private class Leg {
            [JsonIgnore]
            private Animal Animal;
            [JsonIgnore]
            private MultiPedalSettings.LegSettings settings;
            [JsonProperty]
            private Guid LegId;
            [JsonProperty]
            public float3 TargetPosition;
            [JsonProperty]
            public TerrainCollider tCollider;
            public StepState State;
            private bool active;
            private float3 restPos => tCollider.transform.position + new float3(tCollider.transform.size.x, 0, tCollider.transform.size.z) / 2;
            public float3 desiredBody => restPos - settings.RestOffset;
            public enum StepState {
                Stand,
                Raise,
                Lower
            }
            public Leg() {} //Newtonsoft path
            public Leg(Animal animal, MultiPedalSettings.LegSettings settings) {
                this.settings = settings;
                this.Animal = animal;
                this.tCollider = new TerrainCollider(settings.collider, Animal.BodyPosition + settings.RestOffset);
                this.tCollider.useGravity = true;
                this.LegId = Guid.NewGuid();
                active = false;
                State = StepState.Stand;
            }

            public void Initialize(Animal animal, MultiPedalSettings.LegSettings settings) {
                this.settings = settings; 
                this.Animal = animal;
                if (!EntityManager.EntityIndex.TryAdd(LegId, animal))
                    throw new Exception("Failed to register leg for entity");
                active = true;
                
                Bounds bounds = new Bounds(Animal.BodyPosition + settings.RestOffset, settings.collider.size);
                EntityManager.ESTree.Insert(bounds, LegId);
            }

            public void Update() {
                EntityManager.AddHandlerEvent(() => {
                    Bounds bounds = new Bounds(Animal.BodyPosition + settings.RestOffset, settings.collider.size);
                    EntityManager.ESTree.AssertEntityLocation(LegId, bounds);  
                });
                float3 origin = Animal.BodyPosition  + math.mul(Animal.transform.rotation, settings.RestOffset);
                float dist = math.distance(origin, restPos);
                if (dist > tCollider.transform.size.y) { //Rubber banding
                    dist -= tCollider.transform.size.y;
                    float3 dir = math.normalizesafe(origin - restPos);
                    float strength = math.pow(math.distance(origin, restPos), 3.0f);
                    tCollider.transform.velocity += strength * EntityJob.cxt.deltaTime * dir; 
                }

                this.tCollider.Update(Animal);
            }

            public void UpdateMovement() {
                //Update leg in spatial tree--this is not done automatically because Leg is not an entity
                EntityManager.AddHandlerEvent(() => {
                    Bounds bounds = new Bounds(Animal.BodyPosition + settings.RestOffset, settings.collider.size);
                    EntityManager.ESTree.AssertEntityLocation(LegId, bounds);  
                });

                this.tCollider.useGravity = State != StepState.Raise;
                this.tCollider.Update(Animal);
                this.tCollider.transform.velocity *= 0.75f;

                float3 origin = Animal.BodyPosition  + math.mul(Animal.transform.rotation, settings.RestOffset);
                float dist = math.distance(origin, restPos);
                if (dist > tCollider.transform.size.y) {
                    dist -= tCollider.transform.size.y;
                    float3 dir = math.normalizesafe(origin - restPos);
                    float strength = math.pow(math.distance(origin, restPos), 3.0f);
                    tCollider.transform.velocity += strength * EntityJob.cxt.deltaTime * dir;
                    State = StepState.Stand;
                }
                
                if (State == StepState.Stand){
                    if (CPUMapManager.RayCastTerrain(origin, Vector3.down, 2*settings.collider.size.y, 
                        CPUMapManager.RayTestSolid, out float3 hit)) {
                        TargetPosition = hit;
                    } else TargetPosition = origin + (float3)(Vector3.down * settings.collider.size.y * 2);
                    if(math.distance(TargetPosition, restPos) > settings.StepDistThreshold) State = StepState.Raise;
                } 
                if ( State == StepState.Lower) {
                    if (tCollider.SampleCollision(tCollider.transform.position + (float3)(0.05f * Vector3.down),
                        tCollider.transform.size, EntityJob.cxt.mapContext, out float3 gDir))
                        State = StepState.Stand;
                    if (math.distance(TargetPosition, restPos) < 0.05f)
                        State = StepState.Stand;
                }  if (State == StepState.Stand) return;

                float3 aim = float3.zero;
                if (State == StepState.Raise) { //Otherwise go above the target
                    if (restPos.y > TargetPosition.y + settings.LegRaiseHeight)
                        State = StepState.Lower;
                    float3 target = TargetPosition + (float3)(Vector3.up * settings.LegRaiseHeight);
                    aim = target - restPos;
                } if (State == StepState.Lower) aim = TargetPosition - restPos;

                aim = math.normalizesafe(aim);
                tCollider.transform.velocity += settings.MoveAccel * EntityJob.cxt.deltaTime * aim;
            }

            public void Release() {
                if (!active) return;
                active = false;

                EntityManager.EntityIndex.Remove(LegId);
                EntityManager.ESTree.Delete(LegId);
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
            private uint LastOverrideState;
            private bool PlayingAttack = false;
            private LegController[] legs;

            private static readonly string[] AnimationNames = new string[]{
                "IsIdling",  null, 
                null, null, null,
                null, null, null,
                null, null, null,
                null, null, "IsDead"
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
                this.LastOverrideState = 0;
                this.active = true;

                indicators = new Indicators(gameObject, entity);
                transform.position = CPUMapManager.GSToWS(entity.position);
                legs = new LegController[entity.Legs.Count()];
                for(int i = 0; i < entity.Legs.Count(); i++) {
                    legs[i] = new LegController(entity.Legs[i],
                        gameObject.transform,
                        entity.settings.Legs.value[i].IKObjectPath
                    );
                }
            }

            public void Update() {
                if (!active) return;
                if (!entity.active) return;
                if (!entity.active) return;
                if (gameObject == null) return;
                this.transform.SetPositionAndRotation(CPUMapManager.GSToWS(entity.position), entity.Torso.transform.rotation);

                foreach(LegController c in legs) c.Update();
                int hash = animator.GetCurrentAnimatorStateInfo(0).shortNameHash; uint bitmap = 0;
                entity.settings.AnimToggle.TryGetValue(hash, out bitmap);
                if (bitmap != LastOverrideState) {
                    LastOverrideState = bitmap;
                    for(int i = 0; i < legs.Count(); i++) {
                        legs[i].SetActive(((bitmap >> i) & 0x1) != 0);
                    }
                }

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

            private struct LegController {
                public Leg Leg;
                private Transform Controller;
                private TwoBoneIKConstraint contraint;
                private bool active;
                public LegController(Leg Leg, Transform root, string path) {
                    this.Leg = Leg;
                    this.active = false;
                    this.Controller = root.Find(path);
                    this.contraint = Controller.transform.parent.GetComponent<TwoBoneIKConstraint>();
                }
                public void Update() {
                    if (!active) return;
                    float3 bottom = Leg.tCollider.transform.position;
                    bottom.xz += Leg.tCollider.transform.size.xz / 2;
                    Controller.position = CPUMapManager.GSToWS(bottom);
                }

                public void SetActive(bool enabled) {
                    if (active != enabled) {
                        active = enabled;
                        contraint.weight = active ? 1 : 0;
                    } if (!active) return;
                }
            }
        }
    }
}