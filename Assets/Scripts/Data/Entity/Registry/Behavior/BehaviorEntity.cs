using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using Arterra.Configuration;
using Arterra.Utils;
using TerrainCollider = Arterra.GamePlay.Interaction.TerrainCollider;
using System.Collections.Generic;
using System.Linq;
using Arterra.Core.Storage;
using UnityEngine.Profiling;

namespace Arterra.Data.Entity.Behavior {
    public interface IBehaviorSetting : ICloneable {
        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            
        }
        public void Unset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            
        }

        public void OnValidate(BehaviorEntity.AnimalSetting setting) {}
    }

    public interface IBehavior {
        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord){}
        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){}
        public void Update(BehaviorEntity.Animal self){}
        public void Disable(BehaviorEntity.Animal self){}
        public void OnDrawGizmos(BehaviorEntity.Animal self) {}
    }

    public interface ISpeciesBehavior : IBehavior {
        public void AddBehaviorDependencies(Dictionary<Behaviors, int> Behaviors){}
        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> Settings){}
    }

    

    public interface ITempBehavior : IBehavior {
        public bool CanApply(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting) => true;
        //Create is called before CanApply is called with it
        public ITempBehavior Create(BehaviorEntity.Animal self = null);
    }


    public static class BehaviorTypes {
        public const int Base = 0;
        public const int Creature = 1000;
        public const int Hostile = 5000;
        public const int StateMachine = 10000;

        public const int Effects = 50000;
    }

    public enum Behaviors {
        None = -1,
        Animator = BehaviorTypes.Base + 0,
        MapInteraction = BehaviorTypes.Base + 1,
        Indicators = BehaviorTypes.Base + 3,
        Rideable = BehaviorTypes.Base + 4,
        Collider = BehaviorTypes.Base + 5,
        Modifiers = BehaviorTypes.Base + 6,
        PlayerRoot = BehaviorTypes.Base + 100,
        PlayerMovement = BehaviorTypes.Base + 101,
        PlayerCamera = BehaviorTypes.Base + 102,
        PlayerEffects = BehaviorTypes.Base + 103,
        PlayerInteraction = BehaviorTypes.Base + 104,
        PlayerInventories = BehaviorTypes.Base + 105,
        PlayerBaseLogicHandler = BehaviorTypes.Base + 106,

        StateMachine = BehaviorTypes.Creature + 0,
        Attack = BehaviorTypes.Creature + 1,
        Genetics = BehaviorTypes.Creature + 2,
        Pathfinding = BehaviorTypes.Creature + 3,
        Reproduction = BehaviorTypes.Creature + 4,
        Vitality = BehaviorTypes.Creature + 5,
        Feedable = BehaviorTypes.Creature + 6,
        Relations = BehaviorTypes.Creature + 7,
        DefendFriend = BehaviorTypes.Creature + 8,
        FlapWing = BehaviorTypes.Creature + 9,
        Effector = BehaviorTypes.Creature + 10,

        ChaseEnemy = BehaviorTypes.Hostile + 0,
        LeadHead = BehaviorTypes.Hostile + 1,
        MultiAttack = BehaviorTypes.Hostile + 2,
        ProjectileFire = BehaviorTypes.Hostile + 3,
        MultiPedal = BehaviorTypes.Hostile + 4,
        SnakeTail = BehaviorTypes.Hostile + 5,
        Tentacles = BehaviorTypes.Hostile + 6,

        IdleState = BehaviorTypes.StateMachine + 0,
        RandomWalkState = BehaviorTypes.StateMachine + 10,
        BoidFollowState = BehaviorTypes.StateMachine + 11,
        RandomFlyBehavior = BehaviorTypes.StateMachine + 12,
        ChaseFriendsState = BehaviorTypes.StateMachine + 15,
        LandOnGround = BehaviorTypes.StateMachine + 16,
        SwimToSurface = BehaviorTypes.StateMachine + 17,
        ChaseMateState = BehaviorTypes.StateMachine + 20,
        RidedState = BehaviorTypes.StateMachine + 35,
        ChasePlantState = BehaviorTypes.StateMachine + 40,
        ChasePreyState = BehaviorTypes.StateMachine + 50,
        ChaseEnemyState = BehaviorTypes.StateMachine + 51,
        ConsumeMaterialState = BehaviorTypes.StateMachine + 60,
        ConsumeEntityState = BehaviorTypes.StateMachine + 70,
        AttackState = BehaviorTypes.StateMachine + 80,
        ChaseAttackerState = BehaviorTypes.StateMachine + 90,
        StepBackFromEntity = BehaviorTypes.StateMachine + 95,
        RunFromPredator = BehaviorTypes.StateMachine + 100,
        RunFromAttacker = BehaviorTypes.StateMachine + 110,
        BurrowUnderground = BehaviorTypes.StateMachine + 114,
        FlopOnGround = BehaviorTypes.StateMachine + 115,
        DeathState = BehaviorTypes.StateMachine + 120,
        
        Poison = BehaviorTypes.Effects + 0,
        Bleeding = BehaviorTypes.Effects + 1,
    }

    // つぎはぎの生物, stitchwork animal
    [CreateAssetMenu(menuName = "Generation/Entity/BehaviorEntity")]
    public class BehaviorEntity : Authoring
    {
        public Option<AnimalSetting> _Setting;

        [JsonIgnore]
        public override Entity Entity { get => new Animal(); }
        [JsonIgnore]
        public override EntitySetting Setting { get => _Setting.value; set => _Setting.value = (AnimalSetting)value; }

        public static Dictionary<Behaviors, Func<IBehavior>> BehaviorTemplates = new Dictionary<Behaviors, Func<IBehavior>> {
            { Behaviors.Animator, () => new AnimatedBehavior() },
            { Behaviors.MapInteraction, () => new MapInteractBehavior() },
            { Behaviors.Indicators, () => new InidcatorsBehavior() },
            { Behaviors.Rideable, () => new RidableBehavior() },
            { Behaviors.Collider, () => new ColliderUpdateBehavior() },
            { Behaviors.Modifiers, () => new Modifier() },

            { Behaviors.PlayerRoot, () => new PlayerBehavior() },
            { Behaviors.PlayerMovement, () => new PlayerMovementBehavior() },
            { Behaviors.PlayerCamera, () => new PlayerCameraBehavior() },
            { Behaviors.PlayerEffects, () => new PlayerEffectsBehavior() },
            { Behaviors.PlayerInteraction, () => new PlayerInteractionBehavior() },
            { Behaviors.PlayerInventories, () => new PlayerInventoriesBehavior() },
            { Behaviors.PlayerBaseLogicHandler, () => new PlayerBaseLogicHandler() },

            { Behaviors.StateMachine, () => new StateMachineManagerBehavior()},
            { Behaviors.Attack, () => new AttackBehavior()},
            { Behaviors.Genetics, () => new Genetics()},
            { Behaviors.Pathfinding, () => new PathFinderBehavior()},
            { Behaviors.Reproduction, () => new ReproductionBehavior()},
            { Behaviors.Vitality, () => new VitalityBehavior()},
            { Behaviors.Feedable, () => new FeedableBehavior()},
            { Behaviors.Relations, () => new RelationsBehavior()},
            { Behaviors.DefendFriend, () => new DefendFriendBehavior()},
            { Behaviors.FlapWing, () => new FlapWingsBehavior()},
            { Behaviors.Effector, () => new EffectorBehavior()},

            { Behaviors.ChaseEnemy, () => new ChaseEnemyBehavior()},
            { Behaviors.LeadHead, () => new LeadHeadBehavior()},
            { Behaviors.MultiAttack, () => new MultiAttackBehavior()},
            { Behaviors.ProjectileFire, () => new ProjectileFireBehavior()},
            { Behaviors.MultiPedal, () => new MultiPedalBehavior()},
            { Behaviors.SnakeTail, () => new SnakeTailBehavior()},
            { Behaviors.Tentacles, () => new TentacleBehavior()},

            { Behaviors.IdleState, () => new IdleStateBehavior()},
            { Behaviors.RandomWalkState, () => new RandomWalkBehavior()},
            { Behaviors.RandomFlyBehavior, () => new RandomFlyBehavior()},
            { Behaviors.BoidFollowState, () => new BoidFollowBehavior()},
            { Behaviors.ChaseFriendsState, () => new ChaseFriendsBehavior()},
            { Behaviors.ChaseEnemyState, () => new ChaseEnemyBehavior()},
            { Behaviors.LandOnGround, () => new LandOnGroundBehavior()},
            { Behaviors.SwimToSurface, () => new SwimToSurfaceBehavior()},
            { Behaviors.ChaseMateState, () => new ChaseMateBehavior()},
            { Behaviors.RidedState, () => new RidableStateBehavior()},
            { Behaviors.ChasePlantState, () => new ChasePlantBehavior()},
            { Behaviors.ChasePreyState, () => new ChasePreyBehavior()},
            { Behaviors.ConsumeMaterialState, () => new ConsumeMaterialBehavior()},
            { Behaviors.ConsumeEntityState, () => new ConsumeEntityBehavior()},
            { Behaviors.AttackState, () => new AttackTargetBehavior()},
            { Behaviors.ChaseAttackerState, () => new ChaseAttackerBehavior()},
            { Behaviors.StepBackFromEntity, () => new StepBackBehavior()},
            { Behaviors.RunFromPredator, () => new RunFromPredatorBehavior()},
            { Behaviors.RunFromAttacker, () => new RunFromAttackerBehavior()},
            { Behaviors.BurrowUnderground, () => new BurrowInGroundBehavior()},
            { Behaviors.FlopOnGround, () => new FlopOnLandBehavior()},
            { Behaviors.DeathState, () => new DeathBehavior()},

            { Behaviors.Poison, () => new PoisonEffect()},
            { Behaviors.Bleeding, () => new BleedingEffect()},
        };

        public enum UpdateContext { Job, Fixed, Main, Late, JobSync }

        public override void OnValidate() {
            base.OnValidate();
        }

        [Serializable]
        public class AnimalSetting : EntitySetting, ISerializationCallbackReceiver{
            public Option<List<Behaviors> > BehaviorList;
            [TypeNameElementList("value",
                typeof(Movement),
                typeof(MMove),
                typeof(FleeBehaviorSettings),
                typeof(HuntBehaviorSettings),
                typeof(FindPlantBehaviorSettings),
                typeof(PhysicalitySetting),
                typeof(AttackStats),
                typeof(AnimatedSettings),
                typeof(GeneticsSettings),
                typeof(DefendFriendSetting),
                typeof(FeedableBehaviorSettings),
                typeof(MapInteractorSettings),
                typeof(RelationsBehaviorSettings),
                typeof(MateRecognition),
                typeof(RideableSettings),
                typeof(StateMachineManagerSettings),
                typeof(ConsumeBehaviorSettings),
                typeof(AttackTargetSettings),
                typeof(BoidFollowSetting),
                typeof(ChaseAttackerSettings),
                typeof(ChaseFriendsSetting),
                typeof(ChaseMateStateSettings),
                typeof(ChasePlantSettings),
                typeof(ChasePreySettings),
                typeof(ConsumeEntitySettings),
                typeof(ConsumeMaterialSettings),
                typeof(DeathSettings),
                typeof(IdleStateSettings),
                typeof(RandomWalkStateSettings),
                typeof(RandomFlyStateSettings),
                typeof(RideableStateSettings),
                typeof(RunFromAttackerSettings),
                typeof(RunFromPredatorSettings),
                typeof(PlayerMovementSettings),
                typeof(PlayerCameraSettings),
                typeof(PlayerEffectsSettings),
                typeof(PlayerInteractionSettings),
                typeof(PlayerInventorySettings)
            )]
            public Option<List<ReferenceOption<IBehaviorSetting>> > Settings;

            [JsonIgnore][UISetting(Ignore = true)]
            public Dictionary <Type, object> DynamicTypes;

            public override void Preset(uint entityType) {
                DynamicTypes = new Dictionary<Type, object>();
                foreach(ReferenceOption<IBehaviorSetting> setting in Settings.value) {
                    setting.value.Preset(entityType, this);
                    Register(setting.value.GetType(), setting.value);
                }

                base.Preset(entityType);
            }

            public void OnBeforeSerialize() {}

            public void OnAfterDeserialize() => OnValidate();

            [SerializeField, HideInInspector] private int _prevBehaviorCount = 0;
            //Verify dependencies and apply 
            public void OnValidate() {
                Dictionary<Behaviors, int> BehaviorHeirarchy = new ();
                Dictionary<Type, IBehaviorSetting> SettingsHeirarchy = new ();
                BehaviorHeirarchy.Add(Behaviors.Collider, 0);

                //Get full sorted behavior dependency heirarchy
                if (this.BehaviorList.value == null) return;
                //Make sure new elements are always added as None and don't get Deduped
                if (this.BehaviorList.value.Count > _prevBehaviorCount) {
                    HashSet<Behaviors> existingBehaviors = new HashSet<Behaviors>();
                    for (int i = 0; i < _prevBehaviorCount; i++) existingBehaviors.Add(this.BehaviorList.value[i]);
                    for(; _prevBehaviorCount < this.BehaviorList.value.Count; _prevBehaviorCount++)
                        if(existingBehaviors.Contains(this.BehaviorList.value[_prevBehaviorCount]))
                            this.BehaviorList.value[_prevBehaviorCount] = Behaviors.None - _prevBehaviorCount;
                } else _prevBehaviorCount = this.BehaviorList.value.Count;
                
                foreach(Behaviors name in this.BehaviorList.value) {
                    if (name <= Behaviors.None) BehaviorHeirarchy.Add(name, BehaviorHeirarchy.Count);
                    if (!BehaviorTemplates.TryGetValue(name, out var getBehavior))
                        continue;
                    IBehavior behavior = getBehavior.Invoke();
                    if (behavior is ISpeciesBehavior species)
                        species.AddBehaviorDependencies(BehaviorHeirarchy);
                    BehaviorHeirarchy.TryAdd(name, BehaviorHeirarchy.Count);
                }

                var SortingList = BehaviorHeirarchy.ToList();
                SortingList.Sort((a, b) => a.Value.CompareTo(b.Value));
                var BehaviorList = SortingList.Select(a => a.Key).ToList();
                this.BehaviorList.value = BehaviorList;

                //Get all required settings
                foreach(Behaviors name in BehaviorList) {
                    if (!BehaviorTemplates.TryGetValue(name, out var getBehavior))
                        continue;
                    IBehavior behavior = getBehavior.Invoke();
                    if (behavior is ISpeciesBehavior species)
                        species.AddSettingsDependencies(SettingsHeirarchy);
                }

                //Merge Settings
                foreach (var setting in Settings.value) {
                    SettingsHeirarchy[setting.value.GetType()] = setting.value;
                }

                Settings.value = SettingsHeirarchy.Values
                    .Select(a => new ReferenceOption<IBehaviorSetting>{value = a})
                    .ToList();
                
                foreach (var setting in Settings.value) {
                    setting.value.OnValidate(this);
                }
            }

            public void Register<TInterface>(TInterface instance) => DynamicTypes.TryAdd(typeof(TInterface), instance);

            public void Register(Type type, IBehaviorSetting instance) => DynamicTypes.TryAdd(type, instance);

            public bool Is<TInstance>(out TInstance instance) {
                if (DynamicTypes == null) {instance = default; return false;}
                if (DynamicTypes.TryGetValue(typeof(TInstance), out object value)) {
                    instance = (TInstance) value;
                } else if (this is TInstance i1){
                    instance = i1;
                } else {
                    instance = default;
                    return false;
                } return true;
            } 
        }

        //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
        //**If you release here the controller might still be accessing it
        public class Animal : Entity{
            [JsonIgnore] public AnimalSetting settings;
            [JsonIgnore] public Dictionary <Type, object> DynamicTypes;
            [JsonIgnore] public AnimalController controller;
            public Unity.Mathematics.Random random;
            public List<IBehavior> Behaviors;

            private IMultiCollider colliderInfo;
            [JsonIgnore] public TerrainCollider Collider => colliderInfo.Collider;
            [JsonIgnore] public TerrainCollider PathCollider => colliderInfo.PathCollider;
            [JsonIgnore] public Quaternion Rotation {
                get => colliderInfo.Rotation;
                set => colliderInfo.Rotation = value;
            }

            [JsonIgnore] public override ref TerrainCollider.Transform transform => ref Collider.transform; 
            [JsonIgnore] public override float3 head => colliderInfo.HeadPosition;
            [JsonIgnore] public override Quaternion Facing => Rotation;
            [JsonIgnore] public int3 PathCoord => (int3)math.floor(PathCollider.transform.position);
            public UpdateContext context = UpdateContext.Job;
            [JsonIgnore] public float DeltaTime {
                get {
                    if (context == UpdateContext.Job || context == UpdateContext.JobSync)
                        return EntityJob.cxt.deltaTime;
                    else if(context == UpdateContext.Fixed)
                        return Time.fixedDeltaTime;
                    else return Time.deltaTime;
                }
            }
            [JsonIgnore] public float ExactDeltaTime {
                get {
                    if (context == UpdateContext.Job || context == UpdateContext.JobSync)
                        return EntityJob.cxt.totDeltaTime;
                    else if(context == UpdateContext.Fixed)
                        return Time.fixedDeltaTime;
                    else return Time.deltaTime;
                }
            }


            public void Register<TInterface>(TInterface instance) {
                if (typeof(TInterface) == typeof(IMultiCollider))
                    colliderInfo = instance as IMultiCollider;
                DynamicTypes[typeof(TInterface)] = instance;
            }
            public void Register(Type type, IBehavior instance) {
                if (type == typeof(IMultiCollider))
                    colliderInfo = instance as IMultiCollider;
                DynamicTypes[type] = instance;
            }

            public void Unregister(Type type) {
                if (DynamicTypes.ContainsKey(type))
                    DynamicTypes.Remove(type);
            }

            public override bool Is<TInstance>(out TInstance instance) {
                if (DynamicTypes == null) {instance = default; return false;}
                if (DynamicTypes.TryGetValue(typeof(TInstance), out object value)) {
                    instance = (TInstance) value;
                } else if (this is TInstance i1){
                    instance = i1;
                } else {
                    instance = default;
                    return false;
                } return true;
            } 

            public override void Initialize(EntitySetting setting, GameObject Controller, float3 GCoord) {
                settings = (AnimalSetting)setting;
                this.random = new Unity.Mathematics.Random((uint)GetHashCode());
                this.controller = new AnimalController(Controller, this);
                
                Behaviors = new List<IBehavior>();
                DynamicTypes = new Dictionary<Type, object>();
                foreach(Behaviors name in settings.BehaviorList.value) {
                    if (!BehaviorTemplates.TryGetValue(name, out var getBehavior))
                        continue;
                    _addBehavior(getBehavior.Invoke());
                }
                foreach(IBehavior behavior in Behaviors) {
                    behavior.Initialize(this, settings, GCoord);
                }
                //Clear constructor
                this.Constructor = null;
                controller.Initialize(transform);
            }

            public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord) {
                settings = (AnimalSetting)setting;
                DynamicTypes = new Dictionary<Type, object>();
                this.controller = new AnimalController(Controller, this);
                GCoord = default;

                foreach(IBehavior behavior in Behaviors)
                    Register(behavior.GetType(), behavior);

                foreach(IBehavior behavior in Behaviors) 
                    behavior.Deserialize(this, settings, ref GCoord);

                controller.Initialize(transform);
            }

            public void Update(UpdateContext ctx = UpdateContext.Main) {
                this.context = ctx;
                UpdateBehaviors();
            }

            public override void Update() {
                if (context == UpdateContext.Job || context == UpdateContext.JobSync)
                    context = UpdateContext.Job;
                else return;
                UpdateBehaviors();
            }

            public override void UpdateJobSync() {
                if (context == UpdateContext.Job || context == UpdateContext.JobSync)
                    context = UpdateContext.JobSync;
                else return;
                UpdateBehaviors();
            }

            public void UpdateBehaviors() {
                if (!active || !controller.active) return;
                if (controller.gameObject == null) return;
                if (context != UpdateContext.Job) controller.Update();

                foreach(IBehavior behavior in Behaviors) {
                    Profiler.BeginSample(behavior.GetType().Name);
                    behavior.Update(this);
                    Profiler.EndSample();
                }
            }

            public override void OnDrawGizmos() {
                foreach(IBehavior behavior in Behaviors) {
                    behavior.OnDrawGizmos(this);
                }
            }

            public override void Disable() {
                foreach(IBehavior behavior in Behaviors) {
                    behavior.Disable(this);
                } controller.Dispose();
            }
            
            public void AddBehavior(ITempBehavior behavior) {
                EntityManager.AddHandlerEvent(() => {
                    if (behavior == null) return;
                    if (!behavior.CanApply(this, settings)) return;
                    eventCtrl.RaiseEvent(Core.Events.GameEvent.Entity_AddBehavior, this, behavior);
                    _addBehavior(behavior, false); //doesn't register type as could have multiple temp effects
                    if (!active) return;
                    behavior.Initialize(this, settings, position);
                }); 
            }

            public void RemoveBehavior(ITempBehavior behavior) {
                EntityManager.AddHandlerEvent(() => {
                    if (behavior == null) return;
                    int previousCount = Behaviors.Count;
                    Behaviors = Behaviors.Where(b => !ReferenceEquals(b, behavior)).ToList();
                    if (Behaviors.Count == previousCount) return;
                    if (!active) return;
                    eventCtrl.RaiseEvent(Core.Events.GameEvent.Entity_RemoveBehavior, this, behavior);
                    behavior.Disable(this);
                });
            }

            private bool _addBehavior(IBehavior behavior, bool register = true) {
                if (behavior == null) return false;
                if (TryGetConstructor(behavior.GetType(), out object savedBehav))
                    behavior = (IBehavior) savedBehav;
                if (behavior == null) return false;
                if (register) Register(behavior.GetType(), behavior);
                Behaviors.Add(behavior);
                return true;
            }
        }

        public class AnimalController {
            public Animal entity;
            public GameObject gameObject;
            public Transform transform;
            [JsonIgnore]
            public bool active = false;

            public AnimalController(GameObject GameObject, Animal entity) {
                this.entity = entity;
                this.gameObject = GameObject.Instantiate(GameObject);
                this.transform = gameObject.transform;
                this.active = true;
            }

            public void Initialize(TerrainCollider.Transform transform) {
                this.transform.SetPositionAndRotation(
                    CPUMapManager.GSToWS(entity.position),
                    entity.transform.rotation
                );
            }

            public void Update() => this.transform.SetPositionAndRotation(CPUMapManager.GSToWS(entity.position), entity.transform.rotation);

            public void Dispose() {
                if (!active) return;
                active = false;
                entity = null;

                GameObject.Destroy(gameObject);
            }

            ~AnimalController() {
                Dispose();
            }
        }
    }
}