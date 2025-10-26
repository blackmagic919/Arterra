using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Entity;
using MapStorage;

[CreateAssetMenu(menuName = "Generation/Entity/AquaticCarnivore")]
public class AquaticCarnivore : Authoring
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
        public Vitality.Decomposition decomposition;
        public Movement.Aquatic aquatic;
        public Option<Vitality.Stats> physicality;
        public Option<RCarnivore> recognition;
        public RCarnivore Recognition => recognition;
        public Vitality.Stats Physicality => physicality;

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

            base.Preset(entityType);
        }
    }

    //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
    //**If you release here the controller might still be accessing it
    public class Animal : Entity, IAttackable, IMateable {
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
        private uint TaskIndex;
        [JsonProperty]
        private float TaskDuration;
        private AnimalController controller;
        private AnimalSetting settings;
        private static Action<Animal>[] TaskRegistry = new Action<Animal>[]{
            Idle,
            RandomPath,
            FollowPath,
            FindPrey,
            ChasePrey,
            Attack,
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
            Death,
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
            if (recog.IsPredator) TaskIndex = 11u; //if predator run away
            else if (recog.IsMate) TaskIndex = 12u; //if mate fight back
            else if (recog.IsPrey) TaskIndex = 12u; //if prey fight back
            else TaskIndex = settings.Recognition.FightAggressor ? 12u : 11u; //if unknown, depends
            if (TaskIndex == 12 && attacker is not IAttackable) TaskIndex = 11u;  //Don't try to attack a non-attackable entity
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
            if (vitality.healthPercent < genetics.Get(settings.Physicality.MateThreshold))
                return false;
            if (vitality.IsDead) return false;
            if (TaskIndex >= 8) return false;
            return settings.Recognition.CanMateWith(entity);
        }
        public void MateWith(Entity entity) {
            if (!CanMateWith(entity)) return;
            if (settings.Recognition.MateWithEntity(genetics, entity, ref random))
                vitality.Damage(genetics.Get(settings.Physicality.MateCost));
            TaskDuration = settings.Physicality.PregnacyLength;
            TaskIndex = 8;
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

            //Start by Idling   
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
            tCollider.Update(this);
            EntityManager.AddHandlerEvent(controller.Update);

            TerrainInteractor.DetectMapInteraction(position,
            OnInSolid: (dens) => vitality.ProcessSuffocation(this, dens),
            OnInLiquid: (dens) => vitality.ProcessInLiquidAquatic(this, ref tCollider, dens,
                genetics.Get(settings.aquatic.DrownTime)),
            OnInGas: (dens) => {
                vitality.ProcessInGasAquatic(this, ref tCollider, dens);
                if (TaskIndex < 15) TaskIndex = 15; //Flop on ground
            });

            vitality.Update();
            TaskRegistry[(int)TaskIndex].Invoke(this);
            //Shared high priority states
            if (TaskIndex != 16 && vitality.IsDead) {
                TaskDuration = genetics.Get(settings.decomposition.DecompositionTime);
                TaskIndex = 16;
            } else if (TaskIndex <= 8 && IsSurfacing()) TaskIndex = 9;
            else if (TaskIndex <= 13) DetectPredator();
        }

        //Always detect unless already running from predator
        private unsafe void DetectPredator() {
            if (!settings.Recognition.FindClosestPredator(this,
                genetics.Get(settings.Recognition.SightDistance), out Entity predator
            )) { return; }

            int PathDist = settings.Recognition.FleeDistance;
            float3 rayDir = position - predator.position;
            byte* path = PathFinder.FindPathAlongRay(GCoord, ref rayDir, PathDist + 1, settings.profile, EntityJob.cxt, out int pLen);
            pathFinder = new PathFinder.PathInfo(GCoord, path, pLen);
            TaskIndex = 14;
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
            if (self.TaskDuration <= 0) {
                self.TaskIndex = 1;
            } else self.TaskDuration -= EntityJob.cxt.deltaTime;
            if (self.vitality.healthPercent < self.genetics.Get(self.settings.Physicality.HuntThreshold))
                self.TaskIndex = 3;
            else if (self.vitality.healthPercent > self.genetics.Get(self.settings.Physicality.MateThreshold))
                self.TaskIndex = 6;
        }

        // Task 1
        private static unsafe void RandomPath(Animal self) {
            int PathDist = self.settings.movement.pathDistance;
            int3 dP = new(self.random.NextInt(-PathDist, PathDist), self.random.NextInt(-PathDist, PathDist), self.random.NextInt(-PathDist, PathDist));
            if (PathFinder.VerifyProfile(self.GCoord + dP, self.settings.profile, EntityJob.cxt)) {
                byte* path = PathFinder.FindPath(self.GCoord, dP, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
                self.TaskIndex = 2;
            }
        }


        //Task 2
        private static unsafe void FollowPath(Animal self) {
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.walkSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            if (self.pathFinder.hasPath) return;
            self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
            self.TaskIndex = 0;
        }

        //Task 3
        private static unsafe void FindPrey(Animal self) {
            //Use mate threshold not hunt because the entity may lose the target while eating
            if (self.vitality.healthPercent > self.genetics.Get(self.settings.Physicality.MateThreshold) ||
                !self.settings.Recognition.FindPreferredPrey(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity prey)
            ) {
                self.TaskIndex = 1;
                return;
            }

            int PathDist = self.settings.movement.pathDistance;
            int3 destination = (int3)math.round(prey.origin) - self.GCoord;
            byte* path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = 4;

            //If it can't get to the prey and is currently at the closest position it can be
            if (math.all(self.pathFinder.destination == self.GCoord) &&
                Recognition.GetColliderDist(prey, self) >
                self.Genetics.Get(self.settings.Physicality.AttackDistance)
            )
                self.TaskIndex = 1;
        }

        //Task 4
        private static unsafe void ChasePrey(Animal self) {
            if (!self.settings.Recognition.FindPreferredPrey(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity prey)
            ) {
                self.TaskIndex = 3;
                return;
            }
            Movement.FollowDynamicPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, prey.origin,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            float preyDist = Recognition.GetColliderDist(self, prey);
            if (preyDist < self.Genetics.Get(self.settings.Physicality.AttackDistance)) {
                self.TaskIndex = 5;
                return;
            }
            if (!self.pathFinder.hasPath) {
                self.TaskIndex = 3;
                return;
            }
        }

        //Task 5
        private static void Attack(Animal self) {
            self.TaskIndex = 3;
            if (!self.settings.Recognition.FindPreferredPrey(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity prey))
                return;
            float preyDist = Recognition.GetColliderDist(self, prey);
            if (preyDist > self.Genetics.Get(self.settings.Physicality.AttackDistance)) return;
            if (prey is not IAttackable) return;
            self.TaskIndex = 5;

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
                        self.TaskIndex = 0;
                    }
                });
            } else self.vitality.Attack(prey, self);
        }
        //Task 6
        private static unsafe void FindMate(Animal self) {
            if (self.vitality.healthPercent < self.genetics.Get(self.settings.Physicality.MateThreshold)
                || !self.settings.Recognition.FindPreferredMate(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity mate)
            ) {
                self.TaskIndex = 1;
                return;
            }
            int PathDist = self.settings.movement.pathDistance;
            int3 destination = (int3)math.round(mate.origin) - self.GCoord;
            byte* path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = 7;
        }

        //Task 7 
        private static unsafe void ChaseMate(Animal self) {//I feel you man
            if (!self.settings.Recognition.FindPreferredMate(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity mate)
            ) {
                self.TaskIndex = 6;
                return;
            }

            Movement.FollowDynamicPath(self.settings.profile, ref self.pathFinder, ref self.tCollider, mate.origin,
                self.genetics.Get(self.settings.movement.walkSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            float mateDist = Recognition.GetColliderDist(self, mate);
            if (mateDist < self.Genetics.Get(self.settings.Physicality.AttackDistance)) {
                EntityManager.AddHandlerEvent(() => (mate as IMateable).MateWith(self));
                self.MateWith(mate);
                return;
            }
            if (!self.pathFinder.hasPath) {
                self.TaskIndex = 6;
                return;
            }
        }

        //Task 8 (I will never get here)
        private static void Reproduce(Animal self) {
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if (self.TaskDuration > 0) return;
            self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
            self.TaskIndex = 0;
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

        //Task 9 swim up
        private static unsafe void SwimUp(Animal self) {
            float swimIntensity = self.genetics.Get(self.settings.aquatic.DrownTime)
                * self.genetics.Get(self.settings.aquatic.SurfaceThreshold) / math.min(-self.vitality.breath, -0.001f);

            float3 swimDir = Normalize(self.RandomDirection() + math.up() * math.max(0, swimIntensity));
            byte* path = PathFinder.FindMatchAlongRay(self.GCoord, in swimDir,
                self.settings.movement.pathDistance + 1, self.settings.profile,
                self.settings.aquatic.SurfaceProfile, EntityJob.cxt, out int pLen, out bool _);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = 10;
        }

        //Task 10 follow swim up path
        private static unsafe void Surface(Animal self) {
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.walkSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            if (!self.pathFinder.hasPath) {
                self.TaskIndex = 9;
                return;
            }
            if (!self.IsSurfacing()) {
                self.TaskIndex = 1; //Go back to swiming
            }
        }

        //Task 11
        private static unsafe void RunFromTarget(Animal self) {
            if (!EntityManager.TryGetEntity(self.TaskTarget, out Entity target))
                self.TaskTarget = Guid.Empty;
            else if (Recognition.GetColliderDist(self, target) > self.genetics.Get(self.settings.Recognition.SightDistance))
                self.TaskTarget = Guid.Empty;
            if (self.TaskTarget == Guid.Empty) {
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

        //Task 12
        private static unsafe void ChaseTarget(Animal self) {
            if (!EntityManager.TryGetEntity(self.TaskTarget, out Entity target))
                self.TaskTarget = Guid.Empty;
            else if (Recognition.GetColliderDist(self, target) > self.genetics.Get(self.settings.Recognition.SightDistance))
                self.TaskTarget = Guid.Empty;
            if (self.TaskTarget == Guid.Empty) {
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
            if (Recognition.GetColliderDist(self, target) < self.Genetics.Get(self.settings.Physicality.AttackDistance)) {
                self.TaskIndex = 13;
                return;
            }
        }

        //Task 13
        private static void AttackTarget(Animal self) {
            if (!EntityManager.TryGetEntity(self.TaskTarget, out Entity tEntity))
                self.TaskTarget = Guid.Empty;
            else if (tEntity is not IAttackable)
                self.TaskTarget = Guid.Empty;
            if (self.TaskTarget == Guid.Empty) {
                self.TaskIndex = 0;
                return;
            }

            float targetDist = Recognition.GetColliderDist(tEntity, self);
            if (targetDist > self.Genetics.Get(self.settings.Physicality.AttackDistance)) {
                self.TaskIndex = 12;
                return;
            }

            float3 atkDir = math.normalize(tEntity.position - self.position); atkDir.y = 0;
            if (math.any(atkDir != 0)) self.tCollider.transform.rotation = Quaternion.RotateTowards(self.tCollider.transform.rotation,
            Quaternion.LookRotation(atkDir), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);

            IAttackable target = tEntity as IAttackable;
            if (target.IsDead) self.TaskIndex = 0;
            else self.vitality.Attack(tEntity, self);
        }


        //Task 14
        private static unsafe void RunFromPredator(Animal self) {
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration, true);
            if (!self.pathFinder.hasPath) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 0;
                return;
            }
        }

        //Task 15
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

        //Task 16
        private static void Death(Animal self) {
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
            private Transform transform;
            private bool active = false;
            private int AnimatorTask;
            private static readonly string[] AnimationNames = new string[]{
                "IsIdling",  null, "IsWalking",  null, "IsRunning", 
                "IsAttacking",  null, "IsWalking", "IsCuddling",  null,
                "IsWalking", "IsRunning", "IsRunning", "IsAttacking", "IsRunning", 
                "IsFlopping", "IsDead"
            };

            public AnimalController(GameObject GameObject, Animal entity){
                this.entity = entity;
                this.gameObject = GameObject.Instantiate(GameObject);
                this.transform = gameObject.transform;
                this.animator = gameObject.GetComponent<Animator>();
                this.AnimatorTask = 0;
                this.active = true;

                Indicators.SetupIndicators(gameObject);
                transform.position = CPUMapManager.GSToWS(entity.position);
            }

            public void Update(){
                if(!entity.active) return;
                if(gameObject == null) return;
                TerrainCollider.Transform rTransform = entity.tCollider.transform;
                this.transform.SetPositionAndRotation(CPUMapManager.GSToWS(entity.position), rTransform.rotation);

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
}


