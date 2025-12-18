using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Entity;
using MapStorage;

[CreateAssetMenu(menuName = "Generation/Entity/AquaticBoidAnimal")]
public class AquaticBoidAnimal : Authoring {
    [UISetting(Ignore = true)]
    [JsonIgnore]
    public Option<Animal> _Entity;
    public Option<AnimalSetting> _Setting;

    [JsonIgnore]
    public override Entity Entity { get => new Animal(); }
    [JsonIgnore]
    public override EntitySetting Setting { get => _Setting.value; set => _Setting.value = (AnimalSetting)value; }

    [Serializable]
    public class AnimalSetting : EntitySetting {
        public Movement movement;
        public Movement.BoidFlight swim;
        public Movement.Aquatic aquatic;
        public Vitality.Decomposition decomposition;
        public Option<Vitality.Stats> physicality;
        public Option<Recognition> recognition;
        public Recognition Recognition => recognition;
        public Vitality.Stats Physicality => physicality;

        public override void Preset(uint entityType) {
            uint pEnd = profile.bounds.x * profile.bounds.y * profile.bounds.z;
            aquatic.SurfaceProfile.profileStart = profile.profileStart + pEnd;
            Recognition.Construct();

            movement.InitGenome(entityType);
            aquatic.InitGenome(entityType);
            Physicality.InitGenome(entityType);
            Recognition.InitGenome(entityType);
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
        private float3 swimDirection;
        [JsonProperty]
        private Guid TaskTarget;
        [JsonProperty]
        private float TaskDuration;
        [JsonProperty]
        private AnimalTasks TaskIndex;
        private AnimalController controller;
        private AnimalSetting settings;
        private static Action<Animal>[] TaskRegistry = new Action<Animal>[]{
            Idle, FollowFlight, 
            FindPrey, ChasePreyEntity, AttackPrey,
            ChasePreyPlant, EatPlant,
            FindMate, ChaseMate, Reproduce,
            SwimUp, Surface,
            RunFromTarget, ChaseTarget, AttackTarget,
            RunFromPredator, FlopOnGround, Death
        };
        private enum AnimalTasks {
            Idle, FollowFlight, 
            FindPrey, ChasePreyEntity, AttackPrey,
            ChasePreyPlant, EatPlant,
            FindMate, ChaseMate, Reproduce,
            SwimUp, Surface,
            RunFromTarget, ChaseTarget, AttackTarget,
            RunFromPredator, FlopOnGround, Death
        }
        [JsonIgnore]
        public override ref TerrainCollider.Transform transform => ref tCollider.transform;
        [JsonIgnore]
        public float3 MoveDirection{get => swimDirection; set => swimDirection = value; }
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

        public void Interact(Entity caller) { }
        public WorldConfig.Generation.Item.IItem Collect(float amount) {
            if (!IsDead) return null; //You can't collect resources until the entity is dead
            var item = settings.decomposition.LootItem(genetics, amount, ref random);
            TaskDuration -= amount;
            return item;
        }

        public void ProcessFallDamage(float zVelDelta) {
            if (zVelDelta <= Vitality.FallDmgThresh) return;
            float damage = zVelDelta - Vitality.FallDmgThresh;
            damage = math.pow(damage, settings.Physicality.weight);
            EntityManager.AddHandlerEvent(() => TakeDamage(damage, 0, null));
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
            swimDirection = Movement.RandomDirection(ref random);
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
            EntityManager.AddHandlerEvent(controller.Update);

            TerrainInteractor.DetectMapInteraction(position,
                OnInSolid: (dens) => vitality.ProcessSuffocation(this, dens),
                OnInLiquid: (dens) => vitality.ProcessInLiquidAquatic(this, ref tCollider, dens,
                    genetics.Get(settings.aquatic.DrownTime)),
                OnInGas: (dens) => {
                    vitality.ProcessInGasAquatic(this, ref tCollider, dens);
                    if (TaskIndex < AnimalTasks.FlopOnGround) TaskIndex = AnimalTasks.FlopOnGround; //Flop on ground
                });

            vitality.Update();
            TaskRegistry[(int)TaskIndex].Invoke(this);
            if (TaskIndex != AnimalTasks.Death && vitality.IsDead) {
                TaskDuration = genetics.Get(settings.decomposition.DecompositionTime);
                swimDirection = 0;
                TaskIndex = AnimalTasks.Death;
            } else if (TaskIndex < AnimalTasks.SwimUp && IsSurfacing()) TaskIndex = AnimalTasks.SwimUp;
            else if (TaskIndex < AnimalTasks.RunFromPredator) DetectPredator();
        }

        private void DetectPredator() {
            if (!settings.Recognition.FindClosestPredator(this,
                genetics.Get(settings.Recognition.SightDistance),
                out Entity predator))
                return;

            int PathDist = settings.Recognition.FleeDistance;
            swimDirection = position - predator.position;
            byte[] path = PathFinder.FindPathAlongRay(GCoord, ref swimDirection, PathDist + 1, settings.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
            swimDirection = Movement.Normalize(swimDirection);
            TaskIndex = AnimalTasks.RunFromPredator;
        }


        private void BoidFly() {
            if (vitality.BeginMating() || vitality.BeginHunting()) {
                TaskDuration = math.min(0, TaskDuration);
            }
            if (TaskDuration <= 0) {
                TaskDuration = settings.movement.AverageIdleTime * random.NextFloat(0f, 2f);
                TaskIndex = 0; //Go Idle
                return;
            }
            TaskIndex = AnimalTasks.FollowFlight;

            Movement.CalculateBoidDirection(this, genetics, settings.swim);
            byte[] path = PathFinder.FindPathAlongRay(GCoord, ref swimDirection, settings.swim.PathDist + 1, settings.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
        }

        private unsafe bool IsSurfacing() {
            if (vitality.breath > 0) return false; //In air
            if (genetics.Get(settings.aquatic.SurfaceThreshold) == 0) return false; //Doesn't drown
            if (-vitality.breath > genetics.Get(settings.aquatic.SurfaceThreshold)
                * genetics.Get(settings.aquatic.DrownTime))
                return false; //Still holding breath
            return true;
        }

        //Task 0
        private static void Idle(Animal self) {
            if (self.vitality.BeginHunting())
                self.TaskIndex = AnimalTasks.FindPrey;
            else if (self.vitality.BeginMating())
                self.TaskIndex = AnimalTasks.FindMate;
            if (self.TaskDuration <= 0) {
                self.swimDirection = Movement.RandomDirection(ref self.random);
                self.TaskDuration = self.genetics.Get(self.settings.swim.AverageFlightTime) * self.random.NextFloat(0f, 2f);
                self.BoidFly();
                return;
            } else self.TaskDuration -= EntityJob.cxt.deltaTime;

            //Rotate towards neutral
            ref Quaternion rotation = ref self.tCollider.transform.rotation;
            float3 lookRotation = new(rotation.eulerAngles.x, 0, rotation.eulerAngles.z);
            if (Vector3.Magnitude(lookRotation) > 1E-05f) rotation = Quaternion.RotateTowards(rotation, Quaternion.LookRotation(lookRotation), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);
        }

        //Task 1 -> Fly 
        private static unsafe void FollowFlight(Animal self) {
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.walkSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            if (!self.pathFinder.hasPath) {
                self.BoidFly();
            }
        }


        //Task 2
        private static void FindPrey(Animal self) {
            if (self.vitality.StopHunting()) {
                self.TaskDuration = self.genetics.Get(self.settings.swim.AverageFlightTime) * self.random.NextFloat(0f, 2f);
                self.BoidFly();
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

                self.swimDirection = Movement.Normalize(self.pathFinder.destination - self.GCoord);
                dist = Recognition.GetColliderDist(self, prey);
                self.TaskIndex = AnimalTasks.ChasePreyEntity;
                preyAction = AnimalTasks.AttackPrey;
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
                self.TaskDuration = self.genetics.Get(self.settings.swim.AverageFlightTime) * self.random.NextFloat(0f, 2f);
                self.BoidFly();
                return;
            }

            //If it can't get to the prey and is currently at the closest position it can be
            if (math.all(self.pathFinder.destination == self.GCoord)) {
                if (dist <= self.genetics.Get(self.settings.Physicality.AttackDistance)) {
                    self.TaskDuration = 1 / math.max(self.settings.Physicality.ConsumptionRate, 0.0001f);
                    self.TaskIndex = preyAction;
                } else {
                    self.TaskDuration = self.genetics.Get(self.settings.swim.AverageFlightTime) * self.random.NextFloat(0f, 2f);
                    self.BoidFly();
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
                self.settings.movement.acceleration, true);
            
            float preyDist = Recognition.GetColliderDist(self, prey);
            if (preyDist < self.genetics.Get(self.settings.Physicality.AttackDistance)) {
                self.TaskIndex = AnimalTasks.AttackPrey;
                return;
            }
            if (!self.pathFinder.hasPath) {
                self.TaskIndex = AnimalTasks.FindPrey;
                return;
            }
        }

        //Task 3
        private static void ChasePreyPlant(Animal self) {
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            if (self.pathFinder.hasPath) return;

            if (self.settings.Recognition.FindPreferredPreyPlant((int3)math.round(self.position),
                self.genetics.GetInt(self.settings.Recognition.PlantFindDist), out int3 preyPos) &&
                Recognition.GetColliderDist(self, preyPos) <= self.genetics.Get(self.settings.Physicality.AttackDistance)
            ) {
                self.TaskDuration = 1 / math.max(self.settings.Physicality.ConsumptionRate, 0.0001f);
                self.TaskIndex = AnimalTasks.EatPlant;
            } else self.TaskIndex = AnimalTasks.FindPrey;
        }

        //Task 5
        private static void AttackPrey(Animal self) {
            self.TaskIndex = AnimalTasks.FindPrey;
            if (!self.settings.Recognition.FindPreferredPreyEntity(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity prey)) return;
            float preyDist = Recognition.GetColliderDist(self, prey);
            if (preyDist > self.genetics.Get(self.settings.Physicality.AttackDistance)) return;
            if (prey is not IAttackable) return;
            self.TaskIndex = AnimalTasks.AttackPrey;

            float3 atkDir = math.normalize(prey.position - self.position); atkDir.y = 0;
            if (math.any(atkDir != 0)) self.tCollider.transform.rotation = Quaternion.RotateTowards(self.tCollider.transform.rotation,
            Quaternion.LookRotation(atkDir), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);

            IAttackable target = (IAttackable)prey;
            if (target.IsDead) {
                EntityManager.AddHandlerEvent(() => {
                    WorldConfig.Generation.Item.IItem item = target.Collect(self.settings.Physicality.ConsumptionRate);
                    if (item != null && self.settings.Recognition.CanConsume(self.genetics, item, out float nutrition)) {
                        self.vitality.Heal(nutrition);
                    }
                    if (self.vitality.healthPercent >= 1) {
                        self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                        self.TaskIndex = AnimalTasks.Idle;
                    }
                });
            } else self.vitality.Attack(prey, self);
        }

        //Task 4
        private static void EatPlant(Animal self) {
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if (self.TaskDuration <= 0) {
                if (self.settings.Recognition.FindPreferredPreyPlant((int3)math.round(self.position),
                self.genetics.GetInt(self.settings.Recognition.PlantFindDist), out int3 foodPos)) {
                    WorldConfig.Generation.Item.IItem item = self.settings.Recognition.ConsumePlant(self, foodPos);
                    if (item != null && self.settings.Recognition.CanConsume(self.genetics, item, out float nutrition))
                        self.vitality.Heal(nutrition);
                } self.TaskIndex = AnimalTasks.FindPrey;
            }
        }

        //Task 5
        private static void FindMate(Animal self) {
            if (self.vitality.StopMating() || 
              !self.settings.Recognition.FindPreferredMate(self, self.genetics.Get(self.settings.Recognition.SightDistance),
              out Entity mate)
            ) {
                self.TaskDuration = self.genetics.Get(self.settings.swim.AverageFlightTime) * self.random.NextFloat(0f, 2f);
                self.BoidFly();
                return;
            }
            int PathDist = self.settings.movement.pathDistance;
            int3 destination = (int3)mate.origin - self.GCoord;
            byte[] path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = AnimalTasks.ChaseMate;
        }

        //Task 6
        private static unsafe void ChaseMate(Animal self) {//I feel you man
            if (!self.settings.Recognition.FindPreferredMate(self,
                self.genetics.Get(self.settings.Recognition.SightDistance), out Entity mate)
            ) {
                self.TaskIndex = AnimalTasks.FindMate;
                return;
            }

            Movement.FollowDynamicPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, mate.origin,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
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

        //Task 7 (I will never get here)
        private static void Reproduce(Animal self) {
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if (self.TaskDuration > 0) return;
            self.TaskDuration = self.genetics.Get(self.settings.swim.AverageFlightTime) * self.random.NextFloat(0f, 2f);
            self.BoidFly();
        }

        //Task 8 swim up
        private static void SwimUp(Animal self) {
            float swimIntensity = self.genetics.Get(self.settings.aquatic.DrownTime)
                * self.genetics.Get(self.settings.aquatic.SurfaceThreshold) / math.min(-self.vitality.breath, -0.001f);

            float3 swimDir = Movement.Normalize(Movement.RandomDirection(ref self.random) + math.up() * math.max(0, swimIntensity));
            int PathDist = self.settings.movement.pathDistance;
            byte[] path = PathFinder.FindMatchAlongRay(self.GCoord, in swimDir, PathDist + 1, self.settings.profile,
            self.settings.aquatic.SurfaceProfile, EntityJob.cxt, out int pLen, out bool _);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = AnimalTasks.Surface;
        }

        //Task 9 follow swim up path
        private static void Surface(Animal self) {
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            if (!self.pathFinder.hasPath) {
                self.TaskIndex = AnimalTasks.SwimUp;
                return;
            }
            if (!self.IsSurfacing()) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = AnimalTasks.Idle; //Go back to swiming
            }
        }


        //Task 10
        private static void RunFromTarget(Animal self) {
            if (!EntityManager.TryGetEntity(self.TaskTarget, out Entity target))
                self.TaskTarget = Guid.Empty;
            else if (Recognition.GetColliderDist(self, target) > self.genetics.Get(self.settings.Recognition.SightDistance))
                self.TaskTarget = Guid.Empty;
            if (self.TaskTarget == Guid.Empty) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
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
                self.settings.movement.acceleration, true);
        }

        //Task 11
        private static void ChaseTarget(Animal self) {
            if (!EntityManager.TryGetEntity(self.TaskTarget, out Entity target))
                self.TaskTarget = Guid.Empty;
            else if (Recognition.GetColliderDist(self, target) > self.genetics.Get(self.settings.Recognition.SightDistance))
                self.TaskTarget = Guid.Empty;
            if (self.TaskTarget == Guid.Empty) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 0;
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
                self.settings.movement.acceleration, true);
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
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = AnimalTasks.Idle;
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
            if (target.IsDead) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = AnimalTasks.Idle;
            } else self.vitality.Attack(tEntity, self);
        }


        //Task 13
        private static unsafe void RunFromPredator(Animal self) {
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            if (!self.pathFinder.hasPath) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = AnimalTasks.Idle;
            }
        }

        //Task 14
        private static void FlopOnGround(Animal self) {
            if (self.vitality.breath < 0) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 0; //Idle
                return;
            }

            if (self.tCollider.SampleCollision(self.origin, new float3(self.settings.collider.size.x,
            -self.settings.aquatic.JumpStickDistance, self.settings.collider.size.z), EntityJob.cxt.mapContext, out _)) {
                self.velocity.y += self.settings.aquatic.JumpStrength;
            }
        }

        //Task 15
        private static void Death(Animal self) {
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if (!self.IsDead) { //Bring back from the dead 
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 0;
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
            Gizmos.DrawLine(CPUMapManager.GSToWS(position), CPUMapManager.GSToWS(position + swimDirection));
        }

        private class AnimalController {
            private Animal entity;
            private Animator animator;
            private GameObject gameObject;
            private Transform transform;
            private bool active = false;
            private int AnimatorTask;
            private static readonly string[] AnimationNames = new string[]{
                "IsIdling",  "IsWalking",
                null, "IsRunning", "IsAttacking", 
                "IsRunning", "IsEating", 
                null, "IsRunning", "IsCuddling",
                "IsRunning", null,
                "IsRunning", "IsRunning", "IsAttacking",
                "IsRunning", "IsFlopping", "IsDead"
            };

            public AnimalController(GameObject GameObject, Animal entity) {
                this.entity = entity;
                this.gameObject = Instantiate(GameObject);
                this.transform = gameObject.transform;
                this.animator = gameObject.GetComponent<Animator>();
                this.active = true;
                this.AnimatorTask = 0;

                Indicators.SetupIndicators(gameObject);
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


