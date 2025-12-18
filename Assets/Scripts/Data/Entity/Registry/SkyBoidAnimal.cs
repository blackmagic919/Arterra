using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Entity;
using MapStorage;

[CreateAssetMenu(menuName = "Generation/Entity/SkyBoidAnimal")]
public class SkyBoidAnimal : Authoring
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
        public Movement.BoidFlight flight;
        public Vitality.Decomposition decomposition;
        public Option<Vitality.Stats> physicality;
        public Option<Recognition> recognition;
        public Recognition Recognition => recognition;
        public Vitality.Stats Physicality => physicality;

        public override void Preset(uint entityType){ 
            uint pEnd = profile.bounds.x * profile.bounds.y * profile.bounds.z;
            flight.profile.profileStart = profile.profileStart + pEnd;
            Recognition.Construct();

            movement.InitGenome(entityType);
            Physicality.InitGenome(entityType);
            Recognition.InitGenome(entityType);
            flight.InitGenome(entityType);
            decomposition.InitGenome(entityType);

            base.Preset(entityType);
        }
    }

    //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
    //**If you release here the controller might still be accessing it
    public class Animal : Entity, IMateable, IAttackable, Movement.IBoid {
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
        private float3 flightDirection;
        [JsonProperty]
        private Guid TaskTarget;
        [JsonProperty]
        private float TaskDuration;
        [JsonProperty]
        private AnimalTasks TaskIndex;
        private AnimalController controller;
        private AnimalSetting settings;
        private static Action<Animal>[] TaskRegistry = new Action<Animal>[]{
            Idle, FollowFlight, FollowLanding,
            FindPreyEntity, ChasePreyEntity, AttackPrey,
            FindPreyPlant, ChasePreyPlant, EatPlant,
            FindMate, ChaseMate, Reproduce,
            RunFromTarget, ChaseTarget, AttackTarget,
            RunFromPredator, Death
        };

        private enum AnimalTasks {
            Idle, FollowFlight, FollowLanding,
            FindPreyEntity, ChasePreyEntity, AttackPrey,
            FindPreyPlant, ChasePreyPlant, EatPlant,
            FindMate, ChaseMate, Reproduce,
            RunFromTarget, ChaseTarget, AttackTarget,
            RunFromPredator, Death
        }
        [JsonIgnore]
        public override ref TerrainCollider.Transform transform => ref tCollider.transform;
        [JsonIgnore]
        public float3 MoveDirection{get => flightDirection; set => flightDirection = value; }
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

        public void ProcessFallDamage(float zVelDelta) {
            if (zVelDelta <= Vitality.FallDmgThresh) return;
            float damage = zVelDelta - Vitality.FallDmgThresh;
            damage = math.pow(damage, settings.Physicality.weight);
            EntityManager.AddHandlerEvent(() => TakeDamage(damage, 0, null));
        }
        public void Interact(Entity caller) { }
        public WorldConfig.Generation.Item.IItem Collect(float amount) {
            if (!IsDead) return null; //You can't collect resources until the entity is dead
            var item = settings.decomposition.LootItem(genetics, amount, ref random);
            TaskDuration -= amount;
            return item;
        }

        //Not thread safe
        public bool CanMateWith(Entity entity) {
            if (vitality.StopMating())
                return false;
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

        public bool HasPackTarget(out Guid target) {
            target = TaskTarget;
            if (TaskIndex == AnimalTasks.ChaseTarget || TaskIndex == AnimalTasks.AttackTarget)
                return true;
            if (TaskIndex == AnimalTasks.ChasePreyEntity || TaskIndex == AnimalTasks.AttackPrey)
                return true;
            return false;
        } 

        public void SetPackTarget(Guid target) {
            TaskIndex = AnimalTasks.ChaseTarget;
            TaskTarget = target;
        }

        public override void Initialize(EntitySetting setting, GameObject Controller, float3 GCoord) {
            settings = (AnimalSetting)setting;
            controller = new AnimalController(Controller, this);
            //The seed is the entity's memory address
            this.random = new Unity.Mathematics.Random((uint)GetHashCode());
            this.genetics ??= new Genetics(this.info.entityType, ref random);
            this.vitality = new Vitality(settings.Physicality, this.genetics);
            this.tCollider = new TerrainCollider(settings.collider, GCoord, ProcessFallDamage);
            pathFinder.hasPath = false;
            flightDirection = Movement.RandomDirection(ref random);
            TaskDuration = settings.movement.AverageIdleTime * random.NextFloat(0f, 2f);
            TaskTarget = Guid.Empty;
            TaskIndex = 0;
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord) {
            settings = (AnimalSetting)setting;
            controller = new AnimalController(Controller, this);
            vitality.Deserialize(settings.Physicality, genetics);
            tCollider.OnHitGround = ProcessFallDamage;
            random.state ^= (uint)GetHashCode();
            GCoord = this.GCoord;
        }

        public override void Update() {
            if (!active) return;
            //use gravity if not flying
            tCollider.Update(this);
            if (!tCollider.useGravity) velocity.y *= 1 - settings.collider.friction;
            EntityManager.AddHandlerEvent(controller.Update);

            vitality.Update();
            TaskRegistry[(int)TaskIndex].Invoke(this);
            if (TaskIndex != AnimalTasks.Death && vitality.IsDead) {
                TaskDuration = genetics.Get(settings.decomposition.DecompositionTime);
                flightDirection = 0;
                TaskIndex = AnimalTasks.Death;
            } else if (TaskIndex < AnimalTasks.RunFromPredator) DetectPredator();

            TerrainInteractor.DetectMapInteraction(position,
            OnInSolid: (dens) => vitality.ProcessSuffocation(this, dens),
            OnInLiquid: (dens) => vitality.ProcessInLiquid(this, ref tCollider, dens),
            OnInGas: vitality.ProcessInGas);
        }

        private void DetectPredator() {
            if (!settings.Recognition.FindClosestPredator(this,
                genetics.Get(settings.Recognition.SightDistance),
                out Entity predator))
                return;

            int PathDist = settings.Recognition.FleeDistance;
            flightDirection = position - predator.position;
            byte[] path = PathFinder.FindPathAlongRay(GCoord, ref flightDirection, PathDist + 1, settings.flight.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
            flightDirection = math.normalize(flightDirection);
            TaskIndex = AnimalTasks.RunFromPredator;
        }


        public void BoidFly() {
            if (vitality.BeginHunting()) {
                if (settings.Recognition.HasEntityPrey) {
                    TaskIndex = AnimalTasks.FindPreyEntity;
                    return;
                } else TaskDuration = math.min(0, TaskDuration);
            }
            if (vitality.BeginMating()) {
                TaskDuration = math.min(0, TaskDuration);
            }

            TaskIndex = AnimalTasks.FollowFlight;
            if (TaskDuration <= 0) {
                FindGround();
                return;
            }

            Movement.CalculateBoidDirection(this, genetics, settings.flight);
            byte[] path = PathFinder.FindPathAlongRay(GCoord, ref flightDirection, settings.flight.PathDist + 1, settings.flight.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
        }

        public void FindGround() {
            flightDirection = Movement.Normalize(flightDirection + math.down());
            int3 dP = (int3)(flightDirection * settings.flight.PathDist);

            byte[] path = PathFinder.FindMatchAlongRay(GCoord, dP, settings.flight.PathDist + 1, settings.flight.profile, settings.profile, EntityJob.cxt, out int pLen, out bool fGround);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
            if (fGround) TaskIndex = AnimalTasks.FollowLanding;
        }

        private void RandomWalk() {
            if (pathFinder.hasPath) {
                Movement.FollowStaticPath(settings.profile, ref pathFinder, ref tCollider,
                    genetics.Get(settings.movement.walkSpeed), settings.movement.rotSpeed,
                    settings.movement.acceleration);
                return;
            }

            int PathDist = settings.movement.pathDistance;
            int3 dP = new(random.NextInt(-PathDist, PathDist), random.NextInt(-PathDist, PathDist), random.NextInt(-PathDist, PathDist));
            if (PathFinder.VerifyProfile(GCoord + dP, settings.profile, EntityJob.cxt)) {
                byte[] path = PathFinder.FindPath(GCoord, dP, PathDist + 1, settings.profile, EntityJob.cxt, out int pLen);
                pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
            }
        }

        //Task 0
        private static void Idle(Animal self) {
            self.tCollider.useGravity = true;
            if (self.vitality.BeginMating()) {
                self.TaskIndex = AnimalTasks.FindPreyPlant;
                return;
            }
            if (self.vitality.BeginHunting()) {
                self.TaskIndex = AnimalTasks.FindMate;
                return;
            }

            if (self.TaskDuration <= 0) {
                self.TaskDuration = self.genetics.Get(self.settings.flight.AverageFlightTime) * self.random.NextFloat(0f, 2f);
                self.flightDirection = Movement.RandomDirection(ref self.random);
                self.BoidFly();
                return;
            } else self.TaskDuration -= EntityJob.cxt.deltaTime;

            //Rotate towards neutral
            ref Quaternion rotation = ref self.tCollider.transform.rotation;
            float3 lookRotation = new(rotation.eulerAngles.x, 0, rotation.eulerAngles.z);
            if (Vector3.Magnitude(lookRotation) > 1E-05f) rotation = Quaternion.RotateTowards(rotation, Quaternion.LookRotation(lookRotation), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);
        }

        //Task 1 -> Fly 
        private static void FollowFlight(Animal self) {
            self.tCollider.useGravity = false;
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            Movement.FollowStaticPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);

            if (!self.pathFinder.hasPath) {
                self.BoidFly();
            }
        }

        //Task 2 -> Landing 
        private static void FollowLanding(Animal self) {
            self.tCollider.useGravity = false;
            Movement.FollowStaticPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);

            if (!self.pathFinder.hasPath) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.flightDirection = 0;
                self.TaskIndex = AnimalTasks.Idle; //Landed
            }
        }

        private static void FindPreyEntity(Animal self) {
            self.tCollider.useGravity = false;
            //Use mate threshold not hunt because the entity may lose the target while eating
            if (self.vitality.StopHunting()) {
                self.BoidFly();
                return;
            }
            if (!self.settings.Recognition.FindPreferredPreyEntity(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity prey)) {
                //Go to ground to find plant to eat
                if (self.settings.Recognition.HasPlantPrey) self.FindGround(); 
                else self.BoidFly();
                return;
            }

            int PathDist = self.settings.movement.pathDistance;
            int3 destination = (int3)math.round(prey.origin) - self.GCoord;
            byte[] path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.flight.profile, EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = AnimalTasks.ChasePreyEntity;
            self.flightDirection = Movement.Normalize(self.pathFinder.destination - self.GCoord);

            //If it can't get to the prey and is currently at the closest position it can be
            if (math.all(self.pathFinder.destination == self.GCoord)
                && Recognition.GetColliderDist(prey, self) >
                self.genetics.Get(self.settings.Physicality.AttackDistance)
            ) {
                self.BoidFly();
            }
        }

        //Task 5 - Chase Prey
        private static void ChasePreyEntity(Animal self) {
            self.tCollider.useGravity = false;
            if (!self.settings.Recognition.FindPreferredPreyEntity(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity prey)
            ) {
                self.TaskIndex = AnimalTasks.FindPreyEntity;
                return;
            }
            Movement.FollowDynamicPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider, prey.origin,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            float preyDist = Recognition.GetColliderDist(self, prey);
            if (preyDist < self.genetics.Get(self.settings.Physicality.AttackDistance)) {
                self.TaskIndex = AnimalTasks.AttackPrey;
                return;
            }
            if (!self.pathFinder.hasPath) {
                self.TaskIndex = AnimalTasks.FindPreyEntity;
                return;
            }
        }

        //Task 6 - Attack
        private static void AttackPrey(Animal self) {
            self.tCollider.useGravity = false;
            self.TaskIndex = AnimalTasks.FindPreyEntity;
            if (!self.settings.Recognition.FindPreferredPreyEntity(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity prey))
                return;
            float preyDist = Recognition.GetColliderDist(self, prey);
            if (preyDist > self.genetics.Get(self.settings.Physicality.AttackDistance))
                return;
            if (prey is not IAttackable) return;
            self.TaskIndex = AnimalTasks.AttackPrey;

            float3 atkDir = math.normalize(prey.position - self.position); atkDir.y = 0;
            self.tCollider.transform.rotation = Quaternion.RotateTowards(self.tCollider.transform.rotation,
            Quaternion.LookRotation(atkDir), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);

            IAttackable target = (IAttackable)prey;
            if (target.IsDead) {
                EntityManager.AddHandlerEvent(() => {
                    WorldConfig.Generation.Item.IItem item = target.Collect(self.settings.Physicality.ConsumptionRate);
                    if (item != null && self.settings.Recognition.CanConsume(self.genetics, item, out float nutrition)) {
                        self.vitality.Heal(nutrition);
                    } if (self.vitality.healthPercent >= 1) {
                        self.BoidFly();
                    }
                });
            } else self.vitality.Attack(prey, self);
        }


        //Task 3
        private static void FindPreyPlant(Animal self) {
            self.tCollider.useGravity = true;
            if (self.vitality.StopHunting()) {
                self.TaskIndex = AnimalTasks.Idle;
                return;
            } 
            if (!self.settings.Recognition.FindPreferredPreyPlant((int3)math.round(self.position),
                self.genetics.GetInt(self.settings.Recognition.PlantFindDist), out int3 preyPos)
            ) {
                //If we can't find plant and find an entity we can eat
                if (self.settings.Recognition.FindPreferredPreyEntity(self, self.genetics.Get(
                    self.settings.Recognition.SightDistance), out Entity prey))
                    self.TaskIndex = AnimalTasks.FindPreyEntity;
                else self.RandomWalk();
                return;
            } 
            byte[] path = PathFinder.FindPathOrApproachTarget(self.GCoord, preyPos - self.GCoord,
                self.genetics.GetInt(self.settings.Recognition.PlantFindDist) + 1, self.settings.profile,
                EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = AnimalTasks.ChasePreyPlant;

            //If it can't get to the prey and is currently at the closest position it can be
            if (math.all(self.pathFinder.destination == self.GCoord)) {
                float dist = Recognition.GetColliderDist(self, preyPos);
                if (dist <= self.genetics.Get(self.settings.Physicality.AttackDistance)) {
                    self.TaskDuration = 1 / math.max(self.settings.Physicality.ConsumptionRate, 0.0001f);
                    self.TaskIndex = AnimalTasks.EatPlant;
                } else {
                    self.RandomWalk();
                }
            }
        }

        //Task 4
        private static void ChasePreyPlant(Animal self) {
            self.tCollider.useGravity = true;
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.walkSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration);
            if (self.pathFinder.hasPath) return;

            if (self.settings.Recognition.FindPreferredPreyPlant((int3)math.round(self.position), self.genetics.GetInt(
                self.settings.Recognition.PlantFindDist), out int3 preyPos) &&
                Recognition.GetColliderDist(self, preyPos) <= self.genetics.Get(self.settings.Physicality.AttackDistance)
            ) {
                self.TaskDuration = 1 / math.max(self.settings.Physicality.ConsumptionRate, 0.0001f);
                self.TaskIndex = AnimalTasks.EatPlant;
            } else self.TaskIndex = AnimalTasks.FindPreyPlant;
        }

        //Task 5
        private static void EatPlant(Animal self) {
            self.tCollider.useGravity = true;
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if (self.TaskDuration <= 0) {
                if (self.settings.Recognition.FindPreferredPreyPlant((int3)math.round(self.position),
                    self.genetics.GetInt(self.settings.Recognition.PlantFindDist), out int3 foodPos)
                ) {
                    WorldConfig.Generation.Item.IItem item = self.settings.Recognition.ConsumePlant(self, foodPos);
                    if (item != null && self.settings.Recognition.CanConsume(self.genetics, item, out float nutrition))
                        self.vitality.Heal(nutrition);
                } self.TaskIndex = AnimalTasks.FindPreyPlant;
            }
        }

        //Task 6
        private static void FindMate(Animal self) {
            self.tCollider.useGravity = true;
            if (self.vitality.StopMating()) {
                self.TaskIndex = AnimalTasks.Idle;
                return;
            }
            if (!self.settings.Recognition.FindPreferredMate(self, self.genetics.Get(self.settings.Recognition.SightDistance), out Entity mate)) {
                self.RandomWalk();
                return;
            }
            int PathDist = self.settings.movement.pathDistance;
            int3 destination = (int3)mate.origin - self.GCoord;
            byte[] path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = AnimalTasks.ChaseMate;
        }

        //Task 7
        private static void ChaseMate(Animal self) {//I feel you man
            self.tCollider.useGravity = true;
            if (!self.settings.Recognition.FindPreferredMate(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity mate)
            ) {
                self.TaskIndex = AnimalTasks.FindMate;
                return;
            }

            Movement.FollowDynamicPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, mate.origin,
                self.genetics.Get(self.settings.movement.walkSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration);
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

        //Task 8 (I will never get here)
        private static void Reproduce(Animal self) {
            self.tCollider.useGravity = true;
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if (self.TaskDuration > 0) return;
            self.BoidFly();
        }

        //Task 9
        private static void RunFromTarget(Animal self) {
            self.tCollider.useGravity = false;
            if (!EntityManager.TryGetEntity(self.TaskTarget, out Entity target))
                self.TaskTarget = Guid.Empty;
            else if (Recognition.GetColliderDist(self, target) > self.genetics.GetInt(self.settings.Recognition.SightDistance))
                self.TaskTarget = Guid.Empty;
            if (self.TaskTarget == Guid.Empty) {
                self.BoidFly();
                return;
            }

            if (!self.pathFinder.hasPath) {
                int PathDist = self.settings.Recognition.FleeDistance;
                float3 rayDir = self.position - target.position;
                byte[] path = PathFinder.FindPathAlongRay(self.GCoord, ref rayDir, PathDist + 1, self.settings.flight.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            }
            Movement.FollowStaticPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
        }

        //Task 10
        private static void ChaseTarget(Animal self) {
            self.tCollider.useGravity = false;
            if (!EntityManager.TryGetEntity(self.TaskTarget, out Entity target))
                self.TaskTarget = Guid.Empty;
            else if (Recognition.GetColliderDist(self, target) > self.genetics.GetInt(self.settings.Recognition.SightDistance))
                self.TaskTarget = Guid.Empty;
            if (self.TaskTarget == Guid.Empty) {
                self.BoidFly();
                return;
            }

            if (!self.pathFinder.hasPath) {
                int PathDist = self.settings.movement.pathDistance;
                int3 destination = (int3)math.round(target.origin) - self.GCoord;
                byte[] path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.flight.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            }
            Movement.FollowDynamicPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider, target.origin,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            if (Recognition.GetColliderDist(self, target) < self.genetics.Get(self.settings.Physicality.AttackDistance)) {
                self.TaskIndex = AnimalTasks.AttackTarget;
                return;
            }
        }

        //Task 11
        private static void AttackTarget(Animal self) {
            self.tCollider.useGravity = false;
            if (!EntityManager.TryGetEntity(self.TaskTarget, out Entity tEntity))
                self.TaskTarget = Guid.Empty;
            else if (tEntity is not IAttackable)
                self.TaskTarget = Guid.Empty;
            if (self.TaskTarget == Guid.Empty) {
                self.BoidFly();
                return;
            }
            float targetDist = Recognition.GetColliderDist(tEntity, self);
            if (targetDist > self.genetics.Get(self.settings.Physicality.AttackDistance)) {
                self.TaskIndex = AnimalTasks.ChaseTarget;
                return;
            }

            float3 atkDir = math.normalize(tEntity.position - self.position); atkDir.y = 0;
            self.tCollider.transform.rotation = Quaternion.RotateTowards(self.tCollider.transform.rotation,
            Quaternion.LookRotation(atkDir), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);

            IAttackable target = tEntity as IAttackable;
            if (target.IsDead) self.BoidFly();
            else self.vitality.Attack(tEntity, self);
        }


        //Task 12
        private static void RunFromPredator(Animal self) {
            self.tCollider.useGravity = false;
            Movement.FollowStaticPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            if (!self.pathFinder.hasPath) {
                self.BoidFly();
            }
        }

        //Task 13
        private static void Death(Animal self) {
            self.tCollider.useGravity = true;
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
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(CPUMapManager.GSToWS(position), settings.collider.size * 2);
            Gizmos.DrawLine(CPUMapManager.GSToWS(position), CPUMapManager.GSToWS(position + flightDirection));
        }

        private class AnimalController {
            private Animal entity;
            private Animator animator;
            private GameObject gameObject;
            private Transform transform;
            private bool active = false;
            private int AnimatorTask;
            private static readonly string[] AnimationNames = new string[]{
                "IsIdling",  "IsFlying", "IsFlying",  
                "IsFlying", "IsFlying", "IsAttacking",
                "IsWalking", "IsWalking", "IsEating",
                "IsWalking",  "IsWalking", "IsCuddling", 
                "IsFlying", "IsFlying", "IsAttacking",
                "IsFlying", "IsDead"
            };

            public AnimalController(GameObject GameObject, Animal entity) {
                this.entity = entity;
                this.gameObject = Instantiate(GameObject);
                this.transform = gameObject.transform;
                this.animator = gameObject.GetComponent<Animator>();
                this.active = true;
                this.AnimatorTask = 0;

                Indicators.SetupIndicators(gameObject);
                float3 GCoord = new(entity.GCoord);
                transform.position = CPUMapManager.GSToWS(entity.position);
            }

            public void Update() {
                if (!active) return;
                if (!entity.active) return;
                if (gameObject == null) return;
                TerrainCollider.Transform rTransform = entity.tCollider.transform;
                this.transform.SetPositionAndRotation(CPUMapManager.GSToWS(entity.position), rTransform.rotation);

#if UNITY_EDITOR
                if (UnityEditor.Selection.Contains(gameObject)) {
                    Debug.Log(entity.TaskIndex);
                }
#endif

                Indicators.UpdateIndicators(gameObject, entity.vitality, entity.pathFinder);
                if (AnimationNames[AnimatorTask] == "IsFlying") {
                    if (entity.velocity.y >= -1E-4f) animator.SetBool("IsAscending", true);
                    else animator.SetBool("IsAscending", false);
                } 
                if (AnimatorTask == (int)entity.TaskIndex) return;
                if (AnimationNames[AnimatorTask] != null) animator.SetBool(AnimationNames[AnimatorTask], false);
                AnimatorTask = (int)entity.TaskIndex;
                if (AnimationNames[AnimatorTask] != null) animator.SetBool(AnimationNames[AnimatorTask], true);
            }

            public void Dispose() {
                if (!active) return;
                active = false;
                entity = null;
                
                Destroy(gameObject);
            }

            ~AnimalController() {
                Dispose();
            }
        }
    }
}


