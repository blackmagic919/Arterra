using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Entity;
using MapStorage;

[CreateAssetMenu(menuName = "Generation/Entity/AquaticBoidHerbivore")]
public class AquaticBoidHerbivore : Authoring {
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
        public Option<RHerbivore> recognition;
        public RHerbivore Recognition => recognition;
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
    public class Animal : Entity, IMateable, IAttackable {
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
        private uint TaskIndex;
        private AnimalController controller;
        private AnimalSetting settings;
        private static Action<Animal>[] TaskRegistry = new Action<Animal>[]{
            Idle,
            FollowFlight,
            FindPrey,
            ChasePrey,
            EatFood,
            FindMate,
            ChaseMate,
            Reproduce,
            SwimUp,
            Surface,
            RunFromTarget,
            ChaseTarget,
            AttackTarget,
            RunFromPredator,
            FlopOnGround,
            Death
        };
        [JsonIgnore]
        public override ref TerrainCollider.Transform transform => ref tCollider.transform;
        [JsonIgnore]
        public Quaternion Facing => tCollider.transform.rotation;
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
            TaskTarget = attacker.info.entityId;
            Recognition.Recognizable recog = settings.Recognition.Recognize(attacker);
            if (recog.IsPredator) TaskIndex = 10u; //if predator run away
            else if (recog.IsMate) TaskIndex = 11u; //if mate fight back
            else if (recog.IsPrey) TaskIndex = 11u; //if prey fight back
            else TaskIndex = settings.Recognition.FightAggressor ? 11u : 10u; //if unknown, depends
            if (TaskIndex == 11 && attacker is not IAttackable) TaskIndex = 10u;  //Don't try to attack a non-attackable entity
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
            if (vitality.healthPercent < genetics.Get(settings.Physicality.MateThreshold)) return false;
            if (vitality.IsDead) return false;
            if (TaskIndex >= 7) return false;
            return settings.Recognition.CanMateWith(entity);
        }

        public void MateWith(Entity entity) {
            if (!CanMateWith(entity)) return;
            if (settings.Recognition.MateWithEntity(genetics, entity, ref random))
                vitality.Damage(genetics.Get(settings.Physicality.MateCost));
            TaskDuration = settings.Physicality.PregnacyLength;
            TaskIndex = 7;
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
            flightDirection = RandomDirection();
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
                    if (TaskIndex < 14) TaskIndex = 14; //Flop on ground
                });

            vitality.Update();
            TaskRegistry[(int)TaskIndex].Invoke(this);
            if (TaskIndex != 15 && vitality.IsDead) {
                TaskDuration = genetics.Get(settings.decomposition.DecompositionTime);
                flightDirection = 0;
                TaskIndex = 15;
            } else if (TaskIndex <= 7 && IsSurfacing()) TaskIndex = 8;
            else if (TaskIndex <= 12) DetectPredator();
        }

        private unsafe void DetectPredator() {
            if (!settings.Recognition.FindClosestPredator(this,
                genetics.Get(settings.Recognition.SightDistance),
                out Entity predator))
                return;

            int PathDist = settings.Recognition.FleeDistance;
            flightDirection = position - predator.position;
            byte* path = PathFinder.FindPathAlongRay(GCoord, ref flightDirection, PathDist + 1, settings.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
            flightDirection = math.normalize(flightDirection);
            TaskIndex = 13;
        }


        private unsafe void BoidFly() {
            if (TaskDuration <= 0) {
                TaskDuration = settings.movement.AverageIdleTime * random.NextFloat(0f, 2f);
                TaskIndex = 0; //Go Idle
                return;
            }
            if (vitality.healthPercent > genetics.Get(settings.Physicality.MateThreshold) ||
                vitality.healthPercent < genetics.Get(settings.Physicality.HuntThreshold)
            ) {
                TaskDuration = math.min(0, TaskDuration); //try to land to mate
            }
            TaskIndex = 1;

            CalculateBoidDirection();
            byte* path = PathFinder.FindPathAlongRay(GCoord, ref flightDirection, settings.swim.PathDist + 1, settings.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
        }

        private unsafe void CalculateBoidDirection() {
            BoidDMtrx boidDMtrx = new() {
                SeperationDir = float3.zero,
                AlignmentDir = float3.zero,
                CohesionDir = float3.zero,
                count = 0
            };

            unsafe void OnEntityFound(Entity nEntity) {
                if (nEntity == null) return;
                if (nEntity.info.entityType != info.entityType) return;
                Animal nBoid = (Animal)nEntity;
                float3 nBoidPos = nBoid.tCollider.transform.position;
                float3 boidPos = tCollider.transform.position;

                if (math.all(nBoid.flightDirection == 0)) return;
                if (math.distance(boidPos, nBoidPos) < settings.swim.PathDist)
                    boidDMtrx.SeperationDir += boidPos - nBoidPos;
                boidDMtrx.AlignmentDir += nBoid.flightDirection;
                boidDMtrx.CohesionDir += nBoidPos;
                boidDMtrx.count++;
            }

            EntityManager.ESTree.Query(new((float3)GCoord,
                2 * new float3(settings.swim.InfluenceDist)),
            OnEntityFound);

            if (boidDMtrx.count == 0) return;
            float3 influenceDir;
            if (boidDMtrx.count > settings.swim.MaxSwarmSize) //the sign of seperation is flipped for this case
                influenceDir = genetics.Get(settings.swim.SeperationWeight) * boidDMtrx.SeperationDir / boidDMtrx.count -
                genetics.Get(settings.swim.CohesionWeight) * (boidDMtrx.CohesionDir / boidDMtrx.count - position);
            else influenceDir = genetics.Get(settings.swim.SeperationWeight) * boidDMtrx.SeperationDir / boidDMtrx.count +
                genetics.Get(settings.swim.AlignmentWeight) * (boidDMtrx.AlignmentDir / boidDMtrx.count - flightDirection) +
                genetics.Get(settings.swim.CohesionWeight) * (boidDMtrx.CohesionDir / boidDMtrx.count - position);

            flightDirection = Normalize(flightDirection + influenceDir);
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
            if (self.vitality.healthPercent < self.genetics.Get(self.settings.Physicality.HuntThreshold))
                self.TaskIndex = 2;
            else if (self.vitality.healthPercent > self.genetics.Get(self.settings.Physicality.MateThreshold))
                self.TaskIndex = 5;
            if (self.TaskDuration <= 0) {
                self.flightDirection = self.RandomDirection();
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
                self.TaskDuration = self.genetics.Get(self.settings.swim.AverageFlightTime) * self.random.NextFloat(0f, 2f);
                self.BoidFly();
            }
        }


        //Task 2
        private static unsafe void FindPrey(Animal self) {
            if (!self.settings.Recognition.FindPreferredPrey((int3)math.round(self.position),
                self.genetics.GetInt(self.settings.Recognition.PlantFindDist), out int3 preyPos)
            ) {
                self.TaskDuration = self.genetics.Get(self.settings.swim.AverageFlightTime) * self.random.NextFloat(0f, 2f);
                self.BoidFly();
                return;
            }
            byte* path = PathFinder.FindPathOrApproachTarget(self.GCoord, preyPos - self.GCoord,
                self.genetics.GetInt(self.settings.Recognition.PlantFindDist) + 1, self.settings.profile,
                EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = 3;

            //If it can't get to the prey and is currently at the closest position it can be
            if (math.all(self.pathFinder.destination == self.GCoord)) {
                float dist = Recognition.GetColliderDist(self, preyPos);
                if (dist <= self.genetics.Get(self.settings.Physicality.AttackDistance)) {
                    self.TaskDuration = 1 / math.max(self.settings.Physicality.ConsumptionRate, 0.0001f);
                    self.TaskIndex = 4;
                } else {
                    self.TaskDuration = self.genetics.Get(self.settings.swim.AverageFlightTime) * self.random.NextFloat(0f, 2f);
                    self.BoidFly();
                }
            }
        }

        //Task 3
        private static unsafe void ChasePrey(Animal self) {
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            if (self.pathFinder.hasPath) return;

            if (self.settings.Recognition.FindPreferredPrey((int3)math.round(self.position),
                self.genetics.GetInt(self.settings.Recognition.PlantFindDist), out int3 preyPos) &&
                Recognition.GetColliderDist(self, preyPos) <= self.genetics.Get(self.settings.Physicality.AttackDistance)
            ) {
                self.TaskDuration = 1 / math.max(self.settings.Physicality.ConsumptionRate, 0.0001f);
                self.TaskIndex = 4;
            } else self.TaskIndex = 2;
        }

        //Task 4
        private static unsafe void EatFood(Animal self) {
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if (self.TaskDuration <= 0) {
                if (self.settings.Recognition.FindPreferredPrey((int3)math.round(self.position),
                self.genetics.GetInt(self.settings.Recognition.PlantFindDist), out int3 foodPos)) {
                    WorldConfig.Generation.Item.IItem item = self.settings.Recognition.ConsumeFood(self, foodPos);
                    if (item != null && self.settings.Recognition.CanConsume(self.genetics, item, out float nutrition))
                        self.vitality.Heal(nutrition);
                    self.TaskDuration = self.genetics.Get(self.settings.swim.AverageFlightTime) * self.random.NextFloat(0f, 2f);
                    self.BoidFly();
                } else self.TaskIndex = 2;
            }
        }

        //Task 5
        private static unsafe void FindMate(Animal self) {
            if (self.vitality.healthPercent < self.genetics.Get(self.settings.Physicality.MateThreshold) ||
              !self.settings.Recognition.FindPreferredMate(self, self.genetics.Get(self.settings.Recognition.SightDistance),
              out Entity mate)
            ) {
                self.TaskDuration = self.genetics.Get(self.settings.swim.AverageFlightTime) * self.random.NextFloat(0f, 2f);
                self.BoidFly();
                return;
            }
            int PathDist = self.settings.movement.pathDistance;
            int3 destination = (int3)mate.origin - self.GCoord;
            byte* path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = 6;
        }

        //Task 6
        private static unsafe void ChaseMate(Animal self) {//I feel you man
            if (!self.settings.Recognition.FindPreferredMate(self,
                self.genetics.Get(self.settings.Recognition.SightDistance), out Entity mate)
            ) {
                self.TaskIndex = 5;
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
                self.TaskIndex = 5;
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
        private static unsafe void SwimUp(Animal self) {
            float swimIntensity = self.genetics.Get(self.settings.aquatic.DrownTime)
                * self.genetics.Get(self.settings.aquatic.SurfaceThreshold) / math.min(-self.vitality.breath, -0.001f);

            float3 swimDir = Normalize(self.RandomDirection() + math.up() * math.max(0, swimIntensity));
            int PathDist = self.settings.movement.pathDistance;
            byte* path = PathFinder.FindMatchAlongRay(self.GCoord, in swimDir, PathDist + 1, self.settings.profile,
            self.settings.aquatic.SurfaceProfile, EntityJob.cxt, out int pLen, out bool _);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = 9;
        }

        //Task 9 follow swim up path
        private static unsafe void Surface(Animal self) {
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            if (!self.pathFinder.hasPath) {
                self.TaskIndex = 8;
                return;
            }
            if (!self.IsSurfacing()) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 0; //Go back to swiming
            }
        }


        //Task 10
        private static unsafe void RunFromTarget(Animal self) {
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
                int PathDist = self.settings.Recognition.FleeDistance;
                float3 rayDir = self.position - target.position;
                byte* path = PathFinder.FindPathAlongRay(self.GCoord, ref rayDir, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            }
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
        }

        //Task 11
        private static unsafe void ChaseTarget(Animal self) {
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
                byte* path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            }
            Movement.FollowDynamicPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, target.origin,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            if (Recognition.GetColliderDist(self, target) < self.genetics.Get(self.settings.Physicality.AttackDistance)) {
                self.TaskIndex = 12;
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
                self.TaskIndex = 0;
                return;
            }
            float targetDist = Recognition.GetColliderDist(tEntity, self);
            if (targetDist > self.genetics.Get(self.settings.Physicality.AttackDistance)) {
                self.TaskIndex = 11;
                return;
            }

            float3 atkDir = math.normalize(tEntity.position - self.position); atkDir.y = 0;
            self.tCollider.transform.rotation = Quaternion.RotateTowards(self.tCollider.transform.rotation,
            Quaternion.LookRotation(atkDir), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);

            IAttackable target = tEntity as IAttackable;
            if (target.IsDead) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 0;
            } else self.vitality.Attack(tEntity, self);
        }


        //Task 13
        private static unsafe void RunFromPredator(Animal self) {
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            if (!self.pathFinder.hasPath) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 0;
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

        unsafe struct BoidDMtrx {
            public float3 SeperationDir;
            public float3 AlignmentDir;
            public float3 CohesionDir;
            public uint count;
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
                "IsIdling",  "IsWalking", null, "IsRunning", "IsEating", null,
                "IsRunning", "IsCuddling", "IsRunning", null, "IsRunning", "IsRunning",
                "IsAttacking", "IsRunning", "IsFlopping", "IsDead"
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
                if (AnimatorTask == entity.TaskIndex) return;
                if (AnimationNames[AnimatorTask] != null) animator.SetBool(AnimationNames[AnimatorTask], false);
                AnimatorTask = (int)entity.TaskIndex;
                if (AnimationNames[AnimatorTask] != null) animator.SetBool(AnimationNames[AnimatorTask], true);
            }

            public void Dispose() {
                if (!active) return;
                active = false;

                Destroy(gameObject);
            }

            ~AnimalController() {
                Dispose();
            }
        }
    }
}


