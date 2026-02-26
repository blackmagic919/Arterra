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

namespace Arterra.Data.Entity.Behavior {
    public interface IBehaviorSetting : ICloneable {
        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            
        }
        public void Unset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            
        }
    }


    public interface IBehavior {
        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord){}
        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){}
        public void Update(BehaviorEntity.Animal self){}
        public void UpdateController(BehaviorEntity.Animal self, BehaviorEntity.AnimalController controller){}
        public void Disable(BehaviorEntity.Animal self){}
        public void OnDrawGizmos(BehaviorEntity.Animal self) {}
        public void AddBehaviorDependencies(Dictionary<Behaviors, int> Behaviors){}
        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> Settings){}
    }


    public static class BehaviorTypes {
        public const int Base = 0;
        public const int Creature = 1000;
        public const int StateMachine = 10000;
    }

    public enum Behaviors {
        None = -1,
        Animator = BehaviorTypes.Base + 0,
        MapInteraction = BehaviorTypes.Base + 1,
        Indicators = BehaviorTypes.Base + 3,
        Rideable = BehaviorTypes.Base + 4,

        StateMachine = BehaviorTypes.Creature + 0,
        Attack = BehaviorTypes.Creature + 1,
        Genetics = BehaviorTypes.Creature + 2,
        Pathfinding = BehaviorTypes.Creature + 3,
        Reproduction = BehaviorTypes.Creature + 4,
        Vitality = BehaviorTypes.Creature + 5,
        Feedable = BehaviorTypes.Creature + 6,
        Relations = BehaviorTypes.Creature + 7,

        IdleState = BehaviorTypes.StateMachine + 0,
        RandomWalkState = BehaviorTypes.StateMachine + 10,
        ChaseFriendsState = BehaviorTypes.StateMachine + 15,
        ChaseMateState = BehaviorTypes.StateMachine + 20,
        ReproduceState = BehaviorTypes.StateMachine + 30,
        RidedState = BehaviorTypes.StateMachine + 35,
        ChasePlantState = BehaviorTypes.StateMachine + 40,
        ChasePreyState = BehaviorTypes.StateMachine + 50,
        ConsumeMaterialState = BehaviorTypes.StateMachine + 60,
        ConsumeEntityState = BehaviorTypes.StateMachine + 70,
        AttackState = BehaviorTypes.StateMachine + 80,
        ChaseAttackerState = BehaviorTypes.StateMachine + 90,
        RunFromPredator = BehaviorTypes.StateMachine + 100,
        RunFromAttacker = BehaviorTypes.StateMachine + 110,
        DeathState = BehaviorTypes.StateMachine + 120,
        
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

            {Behaviors.StateMachine, () => new StateMachineManagerBehavior()},
            {Behaviors.Attack, () => new AttackBehavior()},
            {Behaviors.Genetics, () => new GeneticsBehavior()},
            {Behaviors.Pathfinding, () => new PathFinderBehavior()},
            {Behaviors.Reproduction, () => new ReproductionBehavior()},
            {Behaviors.Vitality, () => new VitalityBehavior()},
            {Behaviors.Feedable, () => new FeedableBehavior()},
            {Behaviors.Relations, () => new RelationsBehavior()},

            {Behaviors.IdleState, () => new IdleStateBehavior()},
            {Behaviors.RandomWalkState, () => new RandomWalkBehavior()},
            {Behaviors.ChaseFriendsState, () => new ChaseFriendsBehavior()},
            {Behaviors.ChaseMateState, () => new ChaseMateBehavior()},
            {Behaviors.ReproduceState, () => new ReproductionBehavior()},
            {Behaviors.RidedState, () => new RidableStateBehavior()},
            {Behaviors.ChasePlantState, () => new ChasePlantBehavior()},
            {Behaviors.ChasePreyState, () => new ChasePreyBehavior()},
            {Behaviors.ConsumeMaterialState, () => new ConsumeMaterialBehavior()},
            {Behaviors.ConsumeEntityState, () => new ConsumeEntityBehavior()},
            {Behaviors.AttackState, () => new AttackTargetBehavior()},
            {Behaviors.ChaseAttackerState, () => new ChaseAttackerBehavior()},
            {Behaviors.RunFromPredator, () => new RunFromPredatorBehavior()},
            {Behaviors.RunFromAttacker, () => new RunFromAttackerBehavior()},
            {Behaviors.DeathState, () => new DeathBehavior()},
        };

        [SerializeField, HideInInspector] private int _prevBehaviorCount = 0;
        //Verify dependencies and apply 
        public override void OnValidate() {
            base.OnValidate();
            AnimalSetting settings = _Setting.value;
            Dictionary<Behaviors, int> BehaviorHeirarchy = new ();
            Dictionary<Type, IBehaviorSetting> SettingsHeirarchy = new ();

            //Get full sorted behavior dependency heirarchy
            if (settings.Behaviors.value == null) return;
            //Make sure new elements are always added as None and don't get Deduped
            if (settings.Behaviors.value.Count > _prevBehaviorCount) {
                for(; _prevBehaviorCount < settings.Behaviors.value.Count; _prevBehaviorCount++)
                    settings.Behaviors.value[_prevBehaviorCount] = Behaviors.None - _prevBehaviorCount;
            } else _prevBehaviorCount = settings.Behaviors.value.Count;

            foreach(Behaviors name in settings.Behaviors.value) {
                if (name <= Behaviors.None) BehaviorHeirarchy.Add(name, BehaviorHeirarchy.Count);
                if (!BehaviorTemplates.TryGetValue(name, out var getBehavior))
                    continue;
                IBehavior behavior = getBehavior.Invoke();
                behavior.AddBehaviorDependencies(BehaviorHeirarchy);
                BehaviorHeirarchy.TryAdd(name, BehaviorHeirarchy.Count);
            }

            var SortingList = BehaviorHeirarchy.ToList();
            SortingList.Sort((a, b) => a.Value.CompareTo(b.Value));
            var BehaviorList = SortingList.Select(a => a.Key).ToList();
            settings.Behaviors.value = BehaviorList;

            //Get all required settings
            foreach(Behaviors name in BehaviorList) {
                if (!BehaviorTemplates.TryGetValue(name, out var getBehavior))
                    continue;
                IBehavior behavior = getBehavior.Invoke();
                behavior.AddSettingsDependencies(SettingsHeirarchy);
            }

            //Merge Settings
            foreach (var setting in settings.Settings.value) {
                SettingsHeirarchy[setting.value.GetType()] = setting.value;
            }

            settings.Settings.value = SettingsHeirarchy.Values
                .Select(a => new ReferenceOption<IBehaviorSetting>{value = a})
                .ToList();
        }

        [Serializable]
        public class AnimalSetting : EntitySetting{
            public Option<List<Behaviors> > Behaviors;
            [TypeNameElementListAttribute("value")]
            public Option<List<ReferenceOption<IBehaviorSetting>> > Settings;

            [JsonIgnore]
            public Dictionary <Type, object> DynamicTypes;

            public override void Preset(uint entityType)
            {
                DynamicTypes = new Dictionary<Type, object>();
                foreach(ReferenceOption<IBehaviorSetting> setting in Settings.value) {
                    setting.value.Preset(entityType, this);
                    Register(setting.value.GetType(), setting.value);
                }

                base.Preset(entityType);
            }

            public void Register<TInterface>(TInterface instance) => DynamicTypes.TryAdd(typeof(TInterface), instance);

            public void Register(Type type, IBehaviorSetting instance) => DynamicTypes.TryAdd(type, instance);

            public bool Is<TInstance>(out TInstance instance) {
                bool IsType = DynamicTypes.TryGetValue(typeof(TInstance), out object value);
                instance = (TInstance)value;
                return IsType;
            } 
        }

        //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
        //**If you release here the controller might still be accessing it
        public class Animal : Entity{
            [JsonIgnore] public AnimalSetting settings;
            [JsonIgnore] public Dictionary <Type, object> DynamicTypes;
            [JsonIgnore] public int3 GCoord => (int3)math.floor(origin);
            [JsonIgnore] public AnimalController controller;
            public Unity.Mathematics.Random random;
            public List<IBehavior> Behaviors;
            public override ref TerrainCollider.Transform transform => ref collider.transform; 
            public HashSet<Guid> IgnoredEntities;
            public TerrainCollider collider;


            public void Register<TInterface>(TInterface instance) => DynamicTypes.TryAdd(typeof(TInterface), instance);
            public void Register(Type type, IBehaviorSetting instance) => DynamicTypes.TryAdd(type, instance);
            
            public override bool Is<TInstance>(out TInstance instance) {
                bool IsType = DynamicTypes.TryGetValue(typeof(TInstance), out object value);
                instance = (TInstance) value;
                return IsType;
            } 

            public override void Initialize(EntitySetting setting, GameObject Controller, float3 GCoord) {
                settings = (AnimalSetting)setting;
                collider = new TerrainCollider(settings.collider, GCoord);
                this.random = new Unity.Mathematics.Random((uint)GetHashCode());
                this.controller = new AnimalController(Controller, this);
                
                Behaviors = new List<IBehavior>();
                DynamicTypes = new Dictionary<Type, object>();
                foreach(Behaviors name in settings.Behaviors.value) {
                    if (!BehaviorTemplates.TryGetValue(name, out var getBehavior))
                        continue;
                    IBehavior behavior = getBehavior.Invoke();
                    if (behavior == null) continue;
                    behavior.Initialize(this, settings, GCoord);
                    Behaviors.Add(behavior);
                }
            }

            public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord) {
                settings = (AnimalSetting)setting;
                DynamicTypes = new Dictionary<Type, object>();
                this.controller = new AnimalController(Controller, this);
                GCoord = this.GCoord;

                foreach(IBehavior behavior in Behaviors) {
                    behavior.Deserialize(this, settings, ref GCoord);
                }
            }

            public override void Update() {
                if (!active) return;
                collider.Update(this);
                collider.EntityCollisionUpdate(this, IgnoredEntities);
                EntityManager.AddHandlerEvent(controller.Update);
                foreach(IBehavior behavior in Behaviors) {
                    behavior.Update(this);
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
        }
        public class AnimalController {
            public Animal entity;
            public GameObject gameObject;
            public Transform transform;
            private bool active = false;

            public AnimalController(GameObject GameObject, Animal entity) {
                this.entity = entity;
                this.gameObject = GameObject.Instantiate(GameObject);
                this.transform = gameObject.transform;
                this.active = true;

                transform.position = CPUMapManager.GSToWS(entity.position);
            }

            public void Update() {
                if (!active) return;
                if (!entity.active) return;
                if (gameObject == null) return;

                this.transform.SetPositionAndRotation(CPUMapManager.GSToWS(entity.position), entity.collider.transform.rotation);
                foreach(IBehavior behavior in entity.Behaviors) {
                    behavior.UpdateController(entity, this);
                }
            }

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