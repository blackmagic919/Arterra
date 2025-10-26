using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Entity;
using MapStorage;

[CreateAssetMenu(menuName = "Generation/Entity/SkyScavanger")]
public class SkyScavanger : Authoring
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
        public Movement.Flight flight;
        public Vitality.Decomposition decomposition;
        public Option<RCarnivore> recognition;
        public Option<Vitality.Stats> physicality;
        public RCarnivore Recognition => recognition;
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
        private Guid TaskTarget;
        [JsonProperty]
        private float TaskDuration;
        [JsonProperty]
        private uint TaskIndex;
        private AnimalController controller;
        private AnimalSetting settings;
        private static Action<Animal>[] TaskRegistry = new Action<Animal>[]{
            Idle,
            FindFlight,
            FollowFlight,
            FollowLanding,
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
            if (TaskIndex >= 9) return false;
            return settings.Recognition.CanMateWith(entity);
        }
        public void MateWith(Entity entity) {
            if (!CanMateWith(entity)) return;
            if (settings.Recognition.MateWithEntity(genetics, entity, ref random))
                vitality.Damage(genetics.Get(settings.Physicality.MateCost));
            TaskDuration = settings.Physicality.PregnacyLength;
            TaskIndex = 9;
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
            TaskDuration = settings.movement.AverageIdleTime * random.NextFloat(0f, 2f);
            TaskTarget = Guid.Empty;
            TaskIndex = 0;
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord) {
            settings = (AnimalSetting)setting;
            controller = new AnimalController(Controller, this);
            vitality.Deserialize(settings.Physicality, genetics);
            tCollider.OnHitGround = ProcessFallDamage;
            GCoord = this.GCoord;
        }


        public override void Update() {
            if (!active) return;
            //use gravity if not flying
            tCollider.Update(this);
            EntityManager.AddHandlerEvent(controller.Update);

            vitality.Update();
            TaskRegistry[(int)TaskIndex].Invoke(this);
            if (TaskIndex != 14 && vitality.IsDead) {
                TaskDuration = genetics.Get(settings.decomposition.DecompositionTime);
                TaskIndex = 14;
            } else if (TaskIndex <= 12) DetectPredator();

            TerrainInteractor.DetectMapInteraction(position,
            OnInSolid: (dens) => vitality.ProcessSuffocation(this, dens),
            OnInLiquid: (dens) => vitality.ProcessInLiquid(this, ref tCollider, dens),
            OnInGas: vitality.ProcessInGas);
        }

        //Always detect unless already running from predator
        private unsafe void DetectPredator() {
            if (!settings.Recognition.FindClosestPredator(this, genetics.Get(
                settings.Recognition.SightDistance), out Entity predator))
                return;

            int PathDist = settings.Recognition.FleeDistance;
            float3 rayDir = position - predator.position;
            byte* path = PathFinder.FindPathAlongRay(GCoord, ref rayDir, PathDist + 1, settings.flight.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
            TaskIndex = 13;
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

        private unsafe void RandomFly() {
            float3 flightDir = Normalize(RandomDirection() + math.up() * random.NextFloat(0, genetics.Get(settings.flight.FlyBiasWeight)));
            flightDir.y *= settings.flight.VerticalFreedom;
            byte* path = PathFinder.FindPathAlongRay(GCoord, ref flightDir, settings.movement.pathDistance + 1, settings.flight.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
        }

        private unsafe bool FindGround() {
            float3 flightDir = Normalize(RandomDirection() + math.down() * math.max(0, -TaskDuration / genetics.Get(settings.flight.AverageFlightTime)));
            int3 dP = (int3)(flightDir * settings.movement.pathDistance);

            //Use the ground profile
            byte* path = PathFinder.FindMatchAlongRay(GCoord, dP, settings.movement.pathDistance + 1, settings.flight.profile, settings.profile, EntityJob.cxt, out int pLen, out bool fGround);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
            return fGround;
        }

        private unsafe bool FindPreyLanding() {
            if (!settings.Recognition.FindPreferredPrey(this, genetics.Get(
                settings.Recognition.SightDistance), out Entity prey, CanEatEntity)
            ) {
                RandomFly();
                return false;
            }

            int PathDist = settings.movement.pathDistance;
            int3 destination = (int3)math.round(prey.origin) - GCoord;
            byte* path = PathFinder.FindMatchAlongRay(GCoord, destination, PathDist + 1, settings.flight.profile, settings.profile, EntityJob.cxt, out int pLen, out bool ReachedEnd);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
            return ReachedEnd;
        }

        //Task 0
        private static void Idle(Animal self) {
            self.tCollider.useGravity = true;
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if (self.TaskDuration <= 0) {
                self.TaskDuration = self.genetics.Get(self.settings.flight.AverageFlightTime) * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 1;
                return;
            }
            if (self.vitality.BeginHunting()) {
                self.TaskIndex = 4;
                return;
            }
            if (self.vitality.BeginMating()) {
                self.TaskIndex = 7;
                return;
            }
            //Rotate towards neutral
            ref Quaternion rotation = ref self.tCollider.transform.rotation;
            float3 lookRotation = new(rotation.eulerAngles.x, 0, rotation.eulerAngles.z);
            if (Vector3.Magnitude(lookRotation) > 1E-05f) rotation = Quaternion.RotateTowards(rotation, Quaternion.LookRotation(lookRotation), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);
        }

        //Task 1 -> Land
        private static unsafe void FindFlight(Animal self) {
            self.tCollider.useGravity = false;
            if (self.TaskDuration <= 0) {
                bool fGround = self.FindGround();
                if (fGround) self.TaskIndex = 3;
                else self.TaskIndex = 2;
                return;
            }
            if (self.vitality.BeginHunting()) {
                bool fGround = self.FindPreyLanding();
                if (fGround) self.TaskIndex = 3;
                else self.TaskIndex = 2;
                return;
            }
            if (self.vitality.BeginMating()) {
                self.TaskDuration = math.min(0, self.TaskDuration); //try to land to mate
                return;
            }
            self.RandomFly();
            self.TaskIndex = 2;
        }

        //Task 2 -> Fly 
        private static unsafe void FollowFlight(Animal self) {
            self.tCollider.useGravity = false;
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            Movement.FollowStaticPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);

            if (!self.pathFinder.hasPath) {
                self.TaskIndex = 1;
            }
        }

        //Task 3 -> Landing 
        private static unsafe void FollowLanding(Animal self) {
            self.tCollider.useGravity = false;
            Movement.FollowStaticPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);

            if (!self.pathFinder.hasPath) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 0; //Landed
            }
        }

        private static bool CanEatEntity(Entity entity) {
            if (entity is not IAttackable) return false;
            return (entity as IAttackable).IsDead;
        }

        //Task 4 - Find Prey
        private static unsafe void FindPrey(Animal self) {
            self.tCollider.useGravity = true;
            //Use mate threshold not hunt because the entity may lose the target while eating
            if (self.vitality.StopHunting() ||
                !self.settings.Recognition.FindPreferredPrey(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity prey, CanEatEntity)
            ) {
                self.RandomFly();
                self.TaskIndex = 2;
                return;
            }

            int PathDist = self.settings.movement.pathDistance;
            int3 destination = (int3)math.round(prey.origin) - self.GCoord;
            byte* path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = 5;

            //If it can't get to the prey and is currently at the closest position it can be
            if (math.all(self.pathFinder.destination == self.GCoord)
                && Recognition.GetColliderDist(prey, self)
                > self.genetics.Get(self.settings.Physicality.AttackDistance)
            ) {
                self.RandomFly();
                self.TaskIndex = 2;
            }
        }

        //Task 5 - Chase Prey
        private static unsafe void ChasePrey(Animal self) {
            self.tCollider.useGravity = true;
            if (!self.settings.Recognition.FindPreferredPrey(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity prey, CanEatEntity)
            ) {
                self.TaskIndex = 4;
                return;
            }
            Movement.FollowDynamicPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, prey.origin,
                self.genetics.Get(self.settings.movement.walkSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            float preyDist = Recognition.GetColliderDist(self, prey);
            if (preyDist < self.genetics.Get(self.settings.Physicality.AttackDistance)) {
                self.TaskIndex = 6;
                return;
            }
            if (!self.pathFinder.hasPath) {
                self.TaskIndex = 4;
                return;
            }
        }

        //Task 6 - EatFood
        private static void EatFood(Animal self) {
            self.tCollider.useGravity = true;
            self.TaskIndex = 4;
            if (!self.settings.Recognition.FindPreferredPrey(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity prey, CanEatEntity))
                return;
            float preyDist = Recognition.GetColliderDist(self, prey);
            if (preyDist > self.genetics.Get(self.settings.Physicality.AttackDistance)) return;
            if (prey is not IAttackable) return;
            self.TaskIndex = 6;

            float3 atkDir = math.normalize(prey.position - self.position); atkDir.y = 0;
            self.tCollider.transform.rotation = Quaternion.RotateTowards(self.tCollider.transform.rotation,
            Quaternion.LookRotation(atkDir), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);

            IAttackable target = (IAttackable)prey;
            if (!target.IsDead) { self.TaskIndex = 4; return; }
            EntityManager.AddHandlerEvent(() => {
                WorldConfig.Generation.Item.IItem item = target.Collect(self.settings.Physicality.ConsumptionRate);
                if (item != null && self.settings.Recognition.CanConsume(self.genetics, item, out float nutrition)) {
                    self.vitality.Heal(nutrition);
                }
                if (self.vitality.healthPercent >= 1) {
                    self.TaskIndex = 1;
                }
            });
        }

        private unsafe void RandomWalk() {
            if (pathFinder.hasPath) {
                Movement.FollowStaticPath(settings.profile, ref pathFinder, ref tCollider,
                    genetics.Get(settings.movement.walkSpeed), settings.movement.rotSpeed,
                    settings.movement.acceleration);
                return;
            }

            int PathDist = settings.movement.pathDistance;
            int3 dP = new(random.NextInt(-PathDist, PathDist), random.NextInt(-PathDist, PathDist), random.NextInt(-PathDist, PathDist));
            if (PathFinder.VerifyProfile(GCoord + dP, settings.profile, EntityJob.cxt)) {
                byte* path = PathFinder.FindPath(GCoord, dP, PathDist + 1, settings.profile, EntityJob.cxt, out int pLen);
                pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
            }
        }

        //Task 7
        private static unsafe void FindMate(Animal self) {
            self.tCollider.useGravity = true;
            if (self.vitality.StopMating()) {
                self.TaskIndex = 0;
                return;
            }
            if (!self.settings.Recognition.FindPreferredMate(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity mate)
            ) {
                self.RandomWalk();
                return;
            }
            int PathDist = self.settings.movement.pathDistance;
            int3 destination = (int3)mate.origin - self.GCoord;
            byte* path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = 8;
        }

        //Task 8
        private static unsafe void ChaseMate(Animal self) {//I feel you man
            self.tCollider.useGravity = true;
            if (!self.settings.Recognition.FindPreferredMate(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity mate)
            ) {
                self.TaskIndex = 7;
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
                self.TaskIndex = 7;
                return;
            }
        }


        //Task 9 (I will never get here)
        private static void Reproduce(Animal self) {
            self.tCollider.useGravity = true;
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if (self.TaskDuration > 0) return;
            self.TaskIndex = 1;
        }

        //Task 10
        private static unsafe void RunFromTarget(Animal self) {
            self.tCollider.useGravity = false;
            if (!EntityManager.TryGetEntity(self.TaskTarget, out Entity target))
                self.TaskTarget = Guid.Empty;
            else if (Recognition.GetColliderDist(self, target) > self.genetics.Get(self.settings.Recognition.SightDistance))
                self.TaskTarget = Guid.Empty;
            if (self.TaskTarget == Guid.Empty) {
                self.TaskIndex = 1;
                return;
            }

            if (!self.pathFinder.hasPath) {
                int PathDist = self.settings.Recognition.FleeDistance;
                float3 rayDir = self.position - target.position;
                byte* path = PathFinder.FindPathAlongRay(self.GCoord, ref rayDir, PathDist + 1, self.settings.flight.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            }
            Movement.FollowStaticPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
        }

        //Task 11
        private static unsafe void ChaseTarget(Animal self) {
            self.tCollider.useGravity = false;
            if (!EntityManager.TryGetEntity(self.TaskTarget, out Entity target))
                self.TaskTarget = Guid.Empty;
            else if (Recognition.GetColliderDist(self, target) > self.genetics.Get(self.settings.Recognition.SightDistance))
                self.TaskTarget = Guid.Empty;
            if (self.TaskTarget == Guid.Empty) {
                self.TaskIndex = 1;
                return;
            }

            if (!self.pathFinder.hasPath) {
                int PathDist = self.settings.movement.pathDistance;
                int3 destination = (int3)math.round(target.origin) - self.GCoord;
                byte* path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.flight.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            }
            Movement.FollowDynamicPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider, target.position,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            if (Recognition.GetColliderDist(self, target) < self.genetics.Get(self.settings.Physicality.AttackDistance)) {
                self.TaskIndex = 12;
                return;
            }
        }

        //Task 12
        private static void AttackTarget(Animal self) {
            self.tCollider.useGravity = false;
            if (!EntityManager.TryGetEntity(self.TaskTarget, out Entity tEntity))
                self.TaskTarget = Guid.Empty;
            else if (tEntity is not IAttackable)
                self.TaskTarget = Guid.Empty;
            if (self.TaskTarget == Guid.Empty) {
                self.TaskIndex = 1;
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
            if (target.IsDead) self.TaskIndex = 1;
            else self.vitality.Attack(tEntity, self);
        }


        //Task 13
        private static unsafe void RunFromPredator(Animal self) {
            self.tCollider.useGravity = false;
            Movement.FollowStaticPath(self.settings.flight.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            if (!self.pathFinder.hasPath) {
                self.TaskIndex = 1;
            }
        }

        //Task 14
        private static void Death(Animal self) {
            self.tCollider.useGravity = true;
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if (!self.IsDead) { //Bring back from the dead 
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
            Gizmos.color = Color.magenta;
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
            private Transform transform;
            private bool active = false;
            private int AnimatorTask;


            private static readonly string[] AnimationNames = new string[]{
                "IsIdling",  "IsFlying", "IsFlying",  "IsFlying", "IsWalking",
                "IsWalking", "IsEating",  "IsWalking", "IsWalking",  "IsCuddling",
                "IsFlying", "IsFlying", "IsAttacking", "IsFlying",  "IsDead"
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
                if (AnimationNames[AnimatorTask] == "IsFlying") {
                    if (entity.velocity.y >= 0) animator.SetBool("IsAscending", true);
                    else animator.SetBool("IsAscending", false);
                }

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


