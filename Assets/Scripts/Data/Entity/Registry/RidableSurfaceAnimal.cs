using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using Arterra.Configuration;
using Arterra.Configuration.Generation.Entity;
using Arterra.Configuration.Generation.Item;
using Arterra.Core.Storage;
using Arterra.Core.Events;
// Defining the contract between a rider and their mount
public interface IRider
{
    public void OnMounted(IRidable entity);
    public void OnDismounted(IRidable entity);
}

public interface IRidable {
    public Transform GetRiderRoot();
    public void WalkInDirection(float3 direction);
    public void Dismount();
}

[CreateAssetMenu(menuName = "Generation/Entity/RidableSurfaceAnimal")]
public class RidableSurfaceAnimal : Arterra.Configuration.Generation.Entity.Authoring
{
    public Option<AnimalSetting> _Setting;
    [JsonIgnore]
    public override Entity Entity { get => new Animal(); }
    [JsonIgnore]
    public override EntitySetting Setting { get => _Setting.value; set => _Setting.value = (AnimalSetting)value; }

    [Serializable]
    public class AnimalSetting : EntitySetting
    {
        public Movement movement;
        public Vitality.Decomposition decomposition;
        public Option<Recognition> recognition;
        public Option<Vitality.Stats> physicality;
        public Recognition Recognition => recognition;
        public Vitality.Stats Physicality => physicality;

        public override void Preset(uint entityType)
        {
            Recognition.Construct();

            movement.InitGenome(entityType);
            Physicality.InitGenome(entityType);
            Recognition.InitGenome(entityType);
            decomposition.InitGenome(entityType);

            base.Preset(entityType);
        }
    }

    //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
    //**If you release here the controller might still be accessing it
    public class Animal : Entity, IAttackable, IMateable, IRidable {
        [JsonProperty]
        private Genetics genetics;
        [JsonProperty]
        private Vitality vitality;
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
        [JsonProperty]
        private Guid RiderTarget;

        private AnimalController controller;
        private AnimalSetting settings;
        private static Action<Animal>[] TaskRegistry = new Action<Animal>[]{
            Idle, RandomPath, FollowPath, FollowRider, 
            FindPrey, ChasePreyEntity, Attack, 
            ChasePreyPlant, EatPlant,
            FindMate, ChaseMate, Reproduce, 
            RunFromTarget, ChaseTarget, AttackTarget,
            RunFromPredator, Death,
        };

        private enum AnimalTasks {
            Idle, RandomPath, FollowPath, FollowRider, 
            FindPrey, ChasePreyEntity, Attack, 
            ChasePreyPlant, EatPlant,
            FindMate, ChaseMate, Reproduce, 
            RunFromTarget, ChaseTarget, AttackTarget,
            RunFromPredator, Death
        }

        [JsonIgnore]
        public override ref TerrainCollider.Transform transform => ref tCollider.transform;
        [JsonIgnore]
        public int3 GCoord => (int3)math.floor(origin);
        [JsonIgnore]
        public bool IsDead => vitality.IsDead;
        [JsonIgnore]
        public Genetics Genetics {
            get => this.genetics;
            set => this.genetics = value;
        }
        public void TakeDamage(float damage, float3 knockback, Entity attacker) {
            if (!vitality.Damage(damage)) return;
            Indicators.DisplayDamageParticle(position, knockback);
            velocity += knockback;

            if (IsDead) return;
            if (attacker == null) return; //If environmental damage, we don't need to retaliate
            if (attacker.info.entityId == info.entityId) return;
            TaskTarget = attacker.info.entityId;
            if (TaskTarget == RiderTarget) Dismount(); //Don't hit your mount
            Recognition.Recognizable recog = settings.Recognition.Recognize(attacker);
            if (recog.IsPredator) TaskIndex = AnimalTasks.RunFromTarget; //if predator run away
            else if (recog.IsMate) TaskIndex = AnimalTasks.ChaseTarget; //if mate fight back
            else if (recog.IsPrey) TaskIndex = AnimalTasks.ChaseTarget; //if prey fight back
            else TaskIndex = settings.Recognition.FightAggressor ? AnimalTasks.ChaseTarget : AnimalTasks.RunFromTarget; //if unknown, depends
            if (TaskIndex == AnimalTasks.ChaseTarget && attacker is not IAttackable) TaskIndex = AnimalTasks.RunFromTarget;  //Don't try to attack a non-attackable entity
            pathFinder.hasPath = false;
        }
        public void ProcessFallDamage(float zVelDelta) {
            if (zVelDelta <= Vitality.FallDmgThresh) return;
            float damage = zVelDelta - Vitality.FallDmgThresh;
            damage = math.pow(damage, settings.Physicality.weight);
            EntityManager.AddHandlerEvent(() => TakeDamage(damage, 0, null));
        }
        public void Interact(Entity caller) {
            if (IsDead) return; //You can't ride a dead animal
            if (caller == null) return;
            if (caller is not IRider) return;
            //Don't allow riding if the caller hit it
            if (caller.info.entityId == TaskTarget) return;
            IRider rider = (IRider)caller;
            if (RiderTarget != Guid.Empty) return;

            RiderTarget = caller.info.entityId;
            if (TaskIndex < AnimalTasks.FollowRider) TaskIndex = AnimalTasks.FollowRider;
            EntityManager.AddHandlerEvent(() => rider.OnMounted(this));
        }

        public IItem Collect(Entity caller, float amount) {
            IItem item = null;
            if (IsDead) item = settings.decomposition.LootItem(genetics, amount, ref random);
            eventCtrl.RaiseEvent(GameEvent.Entity_Collect, this, caller, (item, amount));
            if (IsDead) TaskDuration -= amount;
            return item;
        }

        //Not thread safe
        public bool CanMateWith(Entity entity) {
            if (vitality.StopMating()) return false;
            if (vitality.IsDead) return false;
            if (TaskIndex >= AnimalTasks.Reproduce) return false;
            return settings.Recognition.CanMateWith(entity);
        }
        public void MateWith(Entity entity) {
            if (!CanMateWith(entity)) return;
            if (settings.Recognition.MateWithEntity(genetics, entity, ref random))
                vitality.Damage(genetics.Get(settings.Physicality.MateCost));
            TaskDuration = settings.Physicality.PregnacyLength;
            TaskIndex = AnimalTasks.Reproduce;
        }
        public Transform GetRiderRoot() => controller.RideRoot;
        public void Dismount() {
            if (RiderTarget == Guid.Empty) return;
            if (!EntityManager.TryGetEntity(RiderTarget, out Entity target) ||
                target is not IRider rider)
                return;

            EntityManager.AddHandlerEvent(() => rider.OnDismounted(this));
            RiderTarget = Guid.Empty;
            //Return to idling if following rider commands
            if (TaskIndex == AnimalTasks.FollowRider)
                TaskIndex = AnimalTasks.Idle;
        }

        public void WalkInDirection(float3 aim) {
            aim = new(aim.x, 0, aim.z);
            if (Vector3.Magnitude(aim) <= 1E-05f) return;
            if (math.length(velocity) > genetics.Get(settings.movement.runSpeed))
                return;

            velocity += settings.movement.acceleration * EntityJob.cxt.deltaTime * aim;
        }

        public override void Initialize(EntitySetting setting, GameObject Controller, float3 GCoord) {
            settings = (AnimalSetting)setting;
            //The seed is the entity's memory address
            this.random = new Unity.Mathematics.Random((uint)GetHashCode());
            this.genetics ??= new Genetics(this.info.entityType, ref random);
            this.vitality = new Vitality(settings.Physicality, this.genetics);
            this.tCollider = new TerrainCollider(settings.collider, GCoord, ProcessFallDamage);
            controller = new AnimalController(Controller, this);
            pathFinder.hasPath = false;
            
            tCollider.transform.position = GCoord;

            //Start by Idling
            TaskDuration = settings.movement.AverageIdleTime * random.NextFloat(0f, 2f);
            TaskTarget = Guid.Empty;
            RiderTarget = Guid.Empty;
            TaskIndex = AnimalTasks.Idle;
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord) {
            settings = (AnimalSetting)setting;
            controller = new AnimalController(Controller, this);
            vitality.Deserialize(settings.Physicality, genetics);
            tCollider.OnHitGround = ProcessFallDamage;
            random.state ^= (uint)GetHashCode();
            GCoord = this.GCoord;

            if (RiderTarget == Guid.Empty) return;
            if (EntityManager.TryGetEntity(RiderTarget, out Entity entity) 
                && entity is IRider rider)
                EntityManager.AddHandlerEvent(() => rider.OnMounted(this));
        }


        public override void Update() {
            if (!active) return;
            tCollider.Update(this);
            EntityManager.AddHandlerEvent(controller.Update);

            tCollider.useGravity = true;
            TerrainInteractor.DetectMapInteraction(position,
            OnInSolid: (dens) => vitality.ProcessInSolid(this, dens),
            OnInLiquid: (dens) => vitality.ProcessInLiquid(this, ref tCollider, dens),
            OnInGas: (dens) => vitality.ProcessInGas(this, dens));

            vitality.Update(this);
            TaskRegistry[(int)TaskIndex].Invoke(this);
            //Shared high priority states
            if (TaskIndex != AnimalTasks.Death && vitality.IsDead) {
                TaskDuration = genetics.Get(settings.decomposition.DecompositionTime);
                TaskIndex = AnimalTasks.Death;
                eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_Death, this, null);
            } else if (TaskIndex <= AnimalTasks.RunFromPredator) DetectPredator();
        }


        //Always detect unless already running from predator
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

        //Task 0
        private static void Idle(Animal self) {
            if (self.TaskDuration <= 0) {
                self.TaskIndex = AnimalTasks.RandomPath;
            } else self.TaskDuration -= EntityJob.cxt.deltaTime;

            if (self.vitality.BeginHunting())
                self.TaskIndex = AnimalTasks.FindPrey; //Hunt for food
            else if (self.RiderTarget != Guid.Empty)
                self.TaskIndex = AnimalTasks.FollowRider; //Follow rider
            else if (self.vitality.BeginMating())
                self.TaskIndex = AnimalTasks.FindMate;
        }

        // Task 1
        private static void RandomPath(Animal self) {
            int PathDist = self.settings.movement.pathDistance;
            int3 dP = new(self.random.NextInt(-PathDist, PathDist), self.random.NextInt(-PathDist, PathDist), self.random.NextInt(-PathDist, PathDist));
            if (PathFinder.VerifyProfile(self.GCoord + dP, self.settings.profile, EntityJob.cxt)) {
                byte[] path = PathFinder.FindPath(self.GCoord, dP, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
                self.TaskIndex = AnimalTasks.FollowPath;
            }
        }


        //Task 2
        private static void FollowPath(Animal self) {
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.walkSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration);
            if (self.pathFinder.hasPath) return;
            self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
            self.TaskIndex = AnimalTasks.Idle;
        }


        //Task 6
        private static void FollowRider(Animal self) {
            if (self.RiderTarget == Guid.Empty) {
                self.TaskIndex = 0;
                return;
            }

            if (math.length(self.velocity.xz) < 1E-05f) return;
            float3 aim = math.normalize(new float3(self.velocity.x, 0, self.velocity.z));
            self.tCollider.transform.rotation = Quaternion.RotateTowards(self.tCollider.transform.rotation,
            Quaternion.LookRotation(aim), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);
        }

        //Task 3
        private static void FindPrey(Animal self) {
            if (self.vitality.StopHunting()) {
                self.TaskIndex = AnimalTasks.RandomPath;
                return;
            } 
            float dist;
            AnimalTasks preyAction;
            if (self.settings.Recognition.FindPreferredPreyEntity(self,
                self.genetics.Get(self.settings.Recognition.SightDistance), out Entity prey)
            ) {
                int PathDist = self.settings.movement.pathDistance;
                int3 destination = (int3)math.round(prey.origin) - self.GCoord;
                byte[] path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
                dist = Recognition.GetColliderDist(self, prey);
                self.TaskIndex = AnimalTasks.ChasePreyEntity;
                preyAction = AnimalTasks.Attack;
            } else if(self.settings.Recognition.FindPreferredPreyPlant(
                (int3)math.round(self.position), self.genetics.GetInt(
                self.settings.Recognition.PlantFindDist), out int3 preyPos)) {
                byte[] path = PathFinder.FindPathOrApproachTarget(self.GCoord, preyPos - self.GCoord, self.genetics.GetInt(
                    self.settings.Recognition.PlantFindDist) + 1, self.settings.profile,
                    EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
                dist = Recognition.GetColliderDist(self, prey);
                self.TaskIndex = AnimalTasks.ChasePreyPlant;
                preyAction = AnimalTasks.EatPlant;
            } else {
                self.TaskIndex = AnimalTasks.RandomPath;
                return;
            }

            //If it can't get to the prey and is currently at the closest position it can be
            if (math.all(self.pathFinder.destination == self.GCoord)) {
                if (dist <= self.genetics.Get(self.settings.Physicality.AttackDistance)) {
                    self.TaskDuration = 1 / math.max(self.settings.Physicality.ConsumptionRate, 0.0001f);
                    self.TaskIndex = preyAction;
                } else {
                    self.TaskIndex = AnimalTasks.RandomPath;
                }
            }
        }

        //Task 4
        private static void ChasePreyEntity(Animal self) {
            if (!self.settings.Recognition.FindPreferredPreyEntity(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity prey)
            ) {
                self.TaskIndex = AnimalTasks.FindPrey;
                return;
            }
            Movement.FollowDynamicPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, prey.origin,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration);
            
            float preyDist = Recognition.GetColliderDist(self, prey);
            if (preyDist < self.genetics.Get(self.settings.Physicality.AttackDistance)) {
                self.TaskIndex = AnimalTasks.Attack;
                return;
            }
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

            if (self.settings.Recognition.FindPreferredPreyPlant((int3)math.round(self.position), self.genetics.GetInt(
                self.settings.Recognition.PlantFindDist), out int3 preyPos)
                && Recognition.GetColliderDist(self, preyPos)
                <= self.genetics.Get(self.settings.Physicality.AttackDistance)
            ) {
                self.TaskDuration = 1 / math.max(self.settings.Physicality.ConsumptionRate, 0.0001f);
                self.TaskIndex = AnimalTasks.EatPlant;
            } else self.TaskIndex = AnimalTasks.FindPrey;
        }

        //Task 5
        private static void Attack(Animal self) {
            self.TaskIndex = AnimalTasks.FindPrey;
            if (!self.settings.Recognition.FindPreferredPreyEntity(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity prey)) return;
            float preyDist = Recognition.GetColliderDist(self, prey);
            if (preyDist > self.genetics.Get(self.settings.Physicality.AttackDistance)) return;
            if (prey is not IAttackable) return;
            self.TaskIndex = AnimalTasks.Attack;

            float3 atkDir = math.normalize(prey.position - self.position); atkDir.y = 0;
            if (math.any(atkDir != 0)) self.tCollider.transform.rotation = Quaternion.RotateTowards(self.tCollider.transform.rotation,
            Quaternion.LookRotation(atkDir), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);

            IAttackable target = (IAttackable)prey;
            if (target.IsDead) {
                EntityManager.AddHandlerEvent(() => {
                    IItem item = target.Collect(self, self.settings.Physicality.ConsumptionRate);
                    if (item != null && self.settings.Recognition.CanConsume(self.genetics, item, out float nutrition)) {
                        self.vitality.Heal(nutrition);
                    }
                    if (self.vitality.healthPercent >= 1) {
                        self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                        self.TaskIndex = AnimalTasks.Idle;
                    }
                });
            } else self.vitality.Attack(prey);
        }

        //Task 5
        private static void EatPlant(Animal self) {
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if (self.TaskDuration <= 0) {
                if (self.settings.Recognition.FindPreferredPreyPlant((int3)math.round(self.position), self.genetics.GetInt(
                    self.settings.Recognition.PlantFindDist), out int3 foodPos)
                ) {
                    IItem item = self.settings.Recognition.ConsumePlant(self, foodPos);
                    if (item != null && self.settings.Recognition.CanConsume(self.genetics, item, out float nutrition))
                        self.vitality.Heal(nutrition);
                } self.TaskIndex = AnimalTasks.FindPrey;
            }
        }

        //Task 3
        private static void FindMate(Animal self) {
            if (self.vitality.StopMating()
                || !self.settings.Recognition.FindPreferredMate(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity mate)
            ) {
                self.TaskIndex = AnimalTasks.RandomPath;
                return;
            }
            int PathDist = self.settings.movement.pathDistance;
            int3 destination = (int3)math.round(mate.origin) - self.GCoord;
            byte[] path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = AnimalTasks.ChaseMate;
        }

        //Task 4
        private static void ChaseMate(Animal self) {//I feel you man
            if (!self.settings.Recognition.FindPreferredMate(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity mate)
            ) {
                self.TaskIndex = AnimalTasks.FindMate;
                return;
            }

            Movement.FollowDynamicPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, mate.origin,
                self.Genetics.GetInt(self.settings.movement.walkSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration
            );

            float mateDist = Recognition.GetColliderDist(self, mate);
            if (mateDist < self.genetics.Get(self.settings.Physicality.AttackDistance)) {
                EntityManager.AddHandlerEvent(() => (mate as IMateable).MateWith(self));
                self.MateWith(mate);
                return;
            }
            if (!self.pathFinder.hasPath) {
                self.TaskIndex = AnimalTasks.FindMate;
                return;
            }
        }

        //Task 5 (I will never get here)
        private static void Reproduce(Animal self) {
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if (self.TaskDuration > 0) return;
            self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
            self.TaskIndex = AnimalTasks.Idle;
        }

        //Task 10
        private static void RunFromTarget(Animal self) {
            if (!EntityManager.TryGetEntity(self.TaskTarget, out Entity target))
                self.TaskTarget = Guid.Empty;
            else if (Recognition.GetColliderDist(self, target) > self.genetics.Get(self.settings.Recognition.SightDistance))
                self.TaskTarget = Guid.Empty;
            if (self.TaskTarget == Guid.Empty) {
                self.TaskIndex = AnimalTasks.Idle;
                return;
            }

            if (!self.pathFinder.hasPath) {
                int PathDist = self.settings.Recognition.FleeDistance;
                float3 rayDir = self.position - target.position;
                byte[] path = PathFinder.FindPathAlongRay(self.GCoord, ref rayDir, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            }
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration);
        }

        //Task 11
        private static void ChaseTarget(Animal self) {
            if (!EntityManager.TryGetEntity(self.TaskTarget, out Entity target))
                self.TaskTarget = Guid.Empty;
            else if (Recognition.GetColliderDist(self, target) > self.genetics.Get(self.settings.Recognition.SightDistance))
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
            if (Recognition.GetColliderDist(self, target) < self.genetics.Get(self.settings.Physicality.AttackDistance)) {
                self.TaskIndex = AnimalTasks.AttackTarget;
                return;
            }
        }

        //Task 12
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
            if (targetDist > self.genetics.Get(self.settings.Physicality.AttackDistance)) {
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

        //Task 13
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

        //Task 14
        private static void Death(Animal self) {
            if (self.RiderTarget != Guid.Empty) {
                self.Dismount();
            }

            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if (!self.IsDead) { //Bring back from the dead 
                self.TaskIndex = AnimalTasks.Idle;
                return;
            }
            //Kill the entity
            if (self.TaskDuration <= 0) EntityManager.ReleaseEntity(self.info.entityId);
        }

        public override void Disable() {
            controller.Dispose();
        }

        public override void OnDrawGizmos() {
            if (!active) return;
            Gizmos.color = info.entityType % 2 == 0 ? Color.red : Color.blue;
            Gizmos.DrawWireCube(CPUMapManager.GSToWS(position), settings.collider.size * 2);
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
        
        private class AnimalController {
            private Animal entity;
            private Animator animator;
            private GameObject gameObject;
            internal Transform RideRoot;
            private Transform transform;
            private Indicators indicators;
            private bool active = false;
            private int AnimatorTask;
            private enum AnimalTasks {
                Idle, RandomPath, FollowPath, FollowRider, 
                FindPrey, ChasePreyEntity, Attack, 
                ChasePreyPlant, EatPlant,
                FindMate, ChaseMate, Reproduce, 
                RunFromTarget, ChaseTarget, AttackTarget,
                RunFromPredator, Death
            }
            private static readonly string[] AnimationNames = new string[]{
                "IsIdling",  null, "IsWalking", "IsWalking", 
                null, "IsRunning", "IsAttacking",
                "IsWalking", "IsEating",
                null, "IsWalking", "IsCuddling",  
                "IsRunning", "IsRunning", "IsAttacking",
                "IsRunning", "IsDead"
            };

            public AnimalController(GameObject GameObject, Animal entity){
                this.entity = entity;
                this.gameObject = GameObject.Instantiate(GameObject);
                this.transform = gameObject.transform;
                this.animator = gameObject.GetComponent<Animator>();
                this.RideRoot = gameObject.transform.Find("Armature").Find("root").Find("base");
                this.AnimatorTask = 0;
                this.active = true;

                indicators = new Indicators(gameObject, entity);
                transform.position = CPUMapManager.GSToWS(entity.position);
            }

            public void Update() {
                if (!active) return;
                if (entity == null) return;
                if (entity == null || !entity.active) return;
                if (gameObject == null) return;
                this.transform.SetPositionAndRotation(CPUMapManager.GSToWS(entity.position), entity.tCollider.transform.rotation);
#if UNITY_EDITOR
                if (UnityEditor.Selection.Contains(gameObject)) Debug.Log(entity.TaskIndex);
#endif

                indicators.Update();
                if (AnimatorTask == 6) UpdateRidingAnimations();
                if (AnimatorTask == (int)entity.TaskIndex) return;
                if (AnimatorTask == 6) DisableRidingAnimations();

                if (AnimationNames[AnimatorTask] != null) animator.SetBool(AnimationNames[AnimatorTask], false);
                AnimatorTask = (int)entity.TaskIndex;
                if (AnimationNames[AnimatorTask] != null) animator.SetBool(AnimationNames[AnimatorTask], true);

                void UpdateRidingAnimations() {
                    if (math.length(entity.velocity) <= 1E-05f) {
                        animator.SetBool("IsIdling", true);
                    } else animator.SetBool("IsIdling", false);

                    if (math.length(entity.velocity) > 1E-05F
                        && math.length(entity.velocity)
                        <= entity.genetics.Get(entity.settings.movement.walkSpeed)
                    ) {
                        animator.SetBool("IsWalking", true);
                    } else animator.SetBool("IsWalking", false);

                    if (math.length(entity.velocity)
                        > entity.genetics.Get(entity.settings.movement.walkSpeed)
                    ) {
                        animator.SetBool("IsRunning", true);
                    } else animator.SetBool("IsRunning", false);
                }
                
                void DisableRidingAnimations() {
                    animator.SetBool("IsIdling", false);
                    animator.SetBool("IsWalking", false);
                    animator.SetBool("IsRunning", false);
                }
            }

            public void Dispose(){
                if(!active) return;
                active = false;
                entity = null;
                
                indicators.Release();
                GameObject.Destroy(gameObject);
            }

            ~AnimalController(){
                Dispose();
            }
        }
    }
}


