using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Entity;
using MapStorage;
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

[CreateAssetMenu(menuName = "Generation/Entity/RidableSurfaceHerbivore")]
public class RidableSurfaceHerbivore : Authoring
{
    [UISetting(Ignore = true)][JsonIgnore]
    public Option<Animal> _Entity;
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
        public Option<RHerbivore> recognition;
        public Option<Vitality.Stats> physicality;
        public RHerbivore Recognition => recognition;
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
        private uint TaskIndex;
        [JsonProperty]
        private float TaskDuration;
        [JsonProperty]
        private Guid RiderTarget;

        private AnimalController controller;
        private AnimalSetting settings;
        private static Action<Animal>[] TaskRegistry = new Action<Animal>[]{
            Idle, //0
            RandomPath, //1
            FollowPath,//2
            FindMate, //3
            ChaseMate, //4
            Reproduce, //5
            FollowRider, //6
            FindPrey, //7
            ChasePrey, //8
            EatFood, //9
            RunFromTarget, //10
            ChaseTarget, //11
            AttackTarget, //12
            RunFromPredator, //13
            Death, //14
        };

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
            TaskTarget = attacker.info.entityId;
            if (TaskTarget == RiderTarget) Dismount(); //Don't hit your mount
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
        public void Interact(Entity caller) {
            if (IsDead) return; //You can't ride a dead animal
            if (caller == null) return;
            if (caller is not IRider) return;
            //Don't allow riding if the caller hit it
            if (caller.info.entityId == TaskTarget) return;
            IRider rider = (IRider)caller;
            if (RiderTarget != Guid.Empty) return;

            RiderTarget = caller.info.entityId;
            if (TaskIndex < 6) TaskIndex = 6;
            EntityManager.AddHandlerEvent(() => rider.OnMounted(this));
        }

        public WorldConfig.Generation.Item.IItem Collect(float amount) {
            if (!IsDead) return null; //You can't collect resources until the entity is dead
            var item = settings.decomposition.LootItem(genetics, amount, ref random);
            TaskDuration -= amount;
            return item;
        }

        //Not thread safe
        public bool CanMateWith(Entity entity) {
            if (vitality.StopMating()) return false;
            if (vitality.IsDead) return false;
            if (TaskIndex >= 8) return false;
            return settings.Recognition.CanMateWith(entity);
        }
        public void MateWith(Entity entity) {
            if (!CanMateWith(entity)) return;
            if (settings.Recognition.MateWithEntity(genetics, entity, ref random))
                vitality.Damage(genetics.Get(settings.Physicality.MateCost));
            TaskDuration = settings.Physicality.PregnacyLength;
            TaskIndex = 5;
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
            if (TaskIndex == 6) TaskIndex = 0;
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
            controller = new AnimalController(Controller, this);
            //The seed is the entity's memory address
            this.random = new Unity.Mathematics.Random((uint)GetHashCode());
            this.genetics ??= new Genetics(this.info.entityType, ref random);
            this.vitality = new Vitality(settings.Physicality, this.genetics);
            this.tCollider = new TerrainCollider(settings.collider, GCoord, ProcessFallDamage);
            pathFinder.hasPath = false;
            tCollider.transform.position = GCoord;

            //Start by Idling
            TaskDuration = settings.movement.AverageIdleTime * random.NextFloat(0f, 2f);
            TaskTarget = Guid.Empty;
            RiderTarget = Guid.Empty;
            TaskIndex = 0;
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
            OnInSolid: (dens) => vitality.ProcessSuffocation(this, dens),
            OnInLiquid: (dens) => vitality.ProcessInLiquid(this, ref tCollider, dens),
            OnInGas: vitality.ProcessInGas);

            vitality.Update();
            TaskRegistry[(int)TaskIndex].Invoke(this);
            //Shared high priority states
            if (TaskIndex != 14 && vitality.IsDead) {
                TaskDuration = genetics.Get(settings.decomposition.DecompositionTime);
                TaskIndex = 14;
            } else if (TaskIndex <= 12) DetectPredator();
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
            TaskIndex = 12;
        }

        //Task 0
        private static void Idle(Animal self) {
            if (self.TaskDuration <= 0) {
                self.TaskIndex = 1;
            } else self.TaskDuration -= EntityJob.cxt.deltaTime;

            if (self.vitality.BeginHunting())
                self.TaskIndex = 7; //Hunt for food
            else if (self.RiderTarget != Guid.Empty)
                self.TaskIndex = 6; //Follow rider
            else if (self.vitality.BeginMating())
                self.TaskIndex = 3;
        }

        // Task 1
        private static void RandomPath(Animal self) {
            int PathDist = self.settings.movement.pathDistance;
            int3 dP = new(self.random.NextInt(-PathDist, PathDist), self.random.NextInt(-PathDist, PathDist), self.random.NextInt(-PathDist, PathDist));
            if (PathFinder.VerifyProfile(self.GCoord + dP, self.settings.profile, EntityJob.cxt)) {
                byte[] path = PathFinder.FindPath(self.GCoord, dP, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
                self.TaskIndex = 2;
            }
        }


        //Task 2
        private static void FollowPath(Animal self) {
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.walkSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration);
            if (self.pathFinder.hasPath) return;
            self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
            self.TaskIndex = 0;
        }

        //Task 3
        private static void FindMate(Animal self) {
            if (self.vitality.StopMating()
                || !self.settings.Recognition.FindPreferredMate(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity mate)
            ) {
                self.TaskIndex = 1;
                return;
            }
            int PathDist = self.settings.movement.pathDistance;
            int3 destination = (int3)math.round(mate.origin) - self.GCoord;
            byte[] path = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = 4;
        }

        //Task 4
        private static unsafe void ChaseMate(Animal self) {//I feel you man
            if (!self.settings.Recognition.FindPreferredMate(self, self.genetics.Get(
                self.settings.Recognition.SightDistance), out Entity mate)
            ) {
                self.TaskIndex = 3;
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
                self.TaskIndex = 3;
                return;
            }
        }

        //Task 5 (I will never get here)
        private static void Reproduce(Animal self) {
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if (self.TaskDuration > 0) return;
            self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
            self.TaskIndex = 0;
        }


        //Task 6
        private static unsafe void FollowRider(Animal self) {
            if (self.RiderTarget == Guid.Empty) {
                self.TaskIndex = 0;
                return;
            }

            if (math.length(self.velocity.xz) < 1E-05f) return;
            float3 aim = math.normalize(new float3(self.velocity.x, 0, self.velocity.z));
            self.tCollider.transform.rotation = Quaternion.RotateTowards(self.tCollider.transform.rotation,
            Quaternion.LookRotation(aim), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);
        }

        //Task 7
        private static void FindPrey(Animal self) {
            if (self.vitality.StopHunting() ||
                !self.settings.Recognition.FindPreferredPrey((int3)math.round(self.position),
                self.genetics.GetInt(self.settings.Recognition.PlantFindDist), out int3 preyPos)
            ) {
                self.TaskIndex = 1;
                return;
            }
            byte[] path = PathFinder.FindPathOrApproachTarget(self.GCoord, preyPos - self.GCoord,
                self.genetics.GetInt(self.settings.Recognition.PlantFindDist) + 1, self.settings.profile,
                EntityJob.cxt, out int pLen);
            self.pathFinder = new PathFinder.PathInfo(self.GCoord, path, pLen);
            self.TaskIndex = 8;

            //If it can't get to the prey and is currently at the closest position it can be
            if (math.all(self.pathFinder.destination == self.GCoord)) {
                float dist = Recognition.GetColliderDist(self, preyPos);
                if (dist <= self.genetics.Get(self.settings.Physicality.AttackDistance)) {
                    self.TaskDuration = 1 / math.max(self.settings.Physicality.ConsumptionRate, 0.0001f);
                    self.TaskIndex = 9;
                } else {
                    self.TaskIndex = 1;
                }
            }
        }

        //Task 8
        private static unsafe void ChasePrey(Animal self) {
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.walkSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration);
            if (self.pathFinder.hasPath) return;

            if (self.settings.Recognition.FindPreferredPrey((int3)math.round(self.position),
                self.genetics.GetInt(self.settings.Recognition.PlantFindDist), out int3 preyPos) &&
                Recognition.GetColliderDist(self, preyPos) <= self.genetics.Get(self.settings.Physicality.AttackDistance)
            ) {
                self.TaskDuration = 1 / math.max(self.settings.Physicality.ConsumptionRate, 0.0001f);
                self.TaskIndex = 9;
            } else self.TaskIndex = 7;
        }

        //Task 9
        private static unsafe void EatFood(Animal self) {
            self.TaskDuration -= EntityJob.cxt.deltaTime;
            if (self.TaskDuration <= 0) {
                if (self.settings.Recognition.FindPreferredPrey((int3)math.round(self.position),
                    self.genetics.GetInt(self.settings.Recognition.PlantFindDist), out int3 foodPos)
                ) {
                    WorldConfig.Generation.Item.IItem item = self.settings.Recognition.ConsumeFood(self, foodPos);
                    if (item != null && self.settings.Recognition.CanConsume(self.genetics, item, out float nutrition))
                        self.vitality.Heal(nutrition);
                } self.TaskIndex = 7;
            }
        }

        //Task 10
        private static void RunFromTarget(Animal self) {
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
                self.settings.movement.acceleration);
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
                self.TaskIndex = 0;
                return;
            }
            float targetDist = Recognition.GetColliderDist(tEntity, self);
            if (targetDist > self.genetics.Get(self.settings.Physicality.AttackDistance)) {
                self.TaskIndex = 11;
                return;
            }

            float3 atkDir = math.normalize(tEntity.position - self.position); atkDir.y = 0;
            if (math.any(atkDir != 0)) self.tCollider.transform.rotation = Quaternion.RotateTowards(self.tCollider.transform.rotation,
            Quaternion.LookRotation(atkDir), self.settings.movement.rotSpeed * EntityJob.cxt.deltaTime);

            IAttackable target = tEntity as IAttackable;
            if (target.IsDead) self.TaskIndex = 0;
            else self.vitality.Attack(tEntity, self);
        }

        //Task 13
        private static unsafe void RunFromPredator(Animal self) {
            Movement.FollowStaticPath(self.settings.profile, ref self.pathFinder, ref self.tCollider,
                self.genetics.Get(self.settings.movement.runSpeed), self.settings.movement.rotSpeed,
                self.settings.movement.acceleration);
            if (!self.pathFinder.hasPath) {
                self.TaskDuration = self.settings.movement.AverageIdleTime * self.random.NextFloat(0f, 2f);
                self.TaskIndex = 0;
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
            internal Transform RideRoot;
            private Transform transform;
            private bool active = false;
            private int AnimatorTask;
            private static readonly string[] AnimationNames = new string[]{
                "IsIdling",  null, "IsWalking", null, "IsWalking", "IsCuddling",  
                null, null, "IsWalking", "IsEating", "IsRunning", "IsRunning", 
                "IsAttacking", "IsRunning", "IsDead"
            };

            public AnimalController(GameObject GameObject, Animal entity){
                this.entity = entity;
                this.gameObject = GameObject.Instantiate(GameObject);
                this.transform = gameObject.transform;
                this.animator = gameObject.GetComponent<Animator>();
                this.RideRoot = gameObject.transform.Find("Armature").Find("root").Find("base");
                this.AnimatorTask = 0;
                this.active = true;

                Indicators.SetupIndicators(gameObject);
                transform.position = CPUMapManager.GSToWS(entity.position);
            }

            public void Update() {
                if (!entity.active) return;
                if (gameObject == null) return;
                this.transform.SetPositionAndRotation(CPUMapManager.GSToWS(entity.position), entity.tCollider.transform.rotation);
#if UNITY_EDITOR
                if (UnityEditor.Selection.Contains(gameObject)) Debug.Log(entity.TaskIndex);
#endif

                Indicators.UpdateIndicators(gameObject, entity.vitality, entity.pathFinder);
                if (AnimatorTask == 6) UpdateRidingAnimations();
                if (AnimatorTask == entity.TaskIndex) return;
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

                GameObject.Destroy(gameObject);
            }

            ~AnimalController(){
                Dispose();
            }
        }
    }
}


