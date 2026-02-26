using System;
using System.Collections.Generic;
using Arterra.Utils;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class StateMachineManagerSettings : IBehaviorSetting {
        public EntitySMTasks StartTask;
        public float AverageStartDuration = 1;
        public float AverageStartDurationVariance = 0;
        public float ContactDistance = 1;
        public object Clone() {
            return new StateMachineManagerSettings {
                StartTask = StartTask,
                ContactDistance = ContactDistance,
                AverageStartDuration = AverageStartDuration
            };
        }
    }

    //Ordering loosely defines override priority
    public static class EntitySMBase { 
        public const int Wander = 0;
        public const int Play = 10000;
        public const int Desire = 20000;
        public const int Sustenance = 30000;
        public const int Survival = 40000;
        public const int Urgent = 50000;
        public const int Final = 60000;
    }
    public enum EntitySMTasks {
        None = -1,

        Idle = EntitySMBase.Wander + 0, 
        RandomPath = EntitySMBase.Wander + 100, 
        FollowPath = EntitySMBase.Wander + 200,
        ChaseFriends = EntitySMBase.Wander + 300,

        FindMate = EntitySMBase.Desire + 0,
        ChaseMate = EntitySMBase.Desire + 100,
        Reproduce =  EntitySMBase.Desire + 200,
        FollowRider = EntitySMBase.Desire + 300,
        FindPreyPlant = EntitySMBase.Sustenance + 0,
        FindPreyEntity =  EntitySMBase.Sustenance + 100,
        ChasePreyPlant = EntitySMBase.Sustenance + 200,
        ChasePreyEntity = EntitySMBase.Sustenance + 300,
        EatPlant = EntitySMBase.Sustenance + 400,
        EatEntity = EntitySMBase.Sustenance + 500,
        AttackTarget = EntitySMBase.Sustenance + 600,

        RunFromPredator = EntitySMBase.Survival + 0,
        RunFromTarget = EntitySMBase.Urgent + 0,
        ChaseTarget = EntitySMBase.Urgent + 100,
        Retaliate = EntitySMBase.Urgent + 200,
        Death = EntitySMBase.Final,
    }

    public class StateMachineManagerBehavior : IBehavior {
        [JsonIgnore] public StateMachineManagerSettings settings;
        [JsonIgnore] private AnimatedBehavior animator;

        [JsonIgnore] private Dictionary<EntitySMTasks, Func<bool>> ConditionalTransitions;
        [JsonIgnore] private Dictionary<EntitySMTasks, string> StateAnimations;
        [JsonIgnore] private EntitySMTasks AnimatorTask;

        public Guid TaskTarget;
        public int3 TaskPosition;
        public EntitySMTasks TaskIndex;
        public float TaskDuration;

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {}
        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(StateMachineManagerSettings), new StateMachineManagerSettings());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: StateMachineManagerBehavior Requires AnimalSettings to have StateMachineManagerSettings");
            if (!self.Is(out animator)) animator = null; else {
                AnimatorTask = EntitySMTasks.None;
                StateAnimations = new Dictionary<EntitySMTasks, string>();
            }

            ConditionalTransitions = new Dictionary<EntitySMTasks, Func<bool> >();
            TaskIndex = settings.StartTask;
            TaskPosition = (int3)GCoord;
            TaskDuration = (float)CustomUtility.Sample(self.random, settings.AverageStartDuration, settings.AverageStartDurationVariance);
            TaskTarget = Guid.Empty;
            self.Register(this);

        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: StateMachineManagerBehavior Requires AnimalSettings to have StateMachineManagerSettings");
            if (!self.Is(out animator)) animator = null; else {
                AnimatorTask = EntitySMTasks.None;
                StateAnimations = new Dictionary<EntitySMTasks, string>();
            }

            ConditionalTransitions = new Dictionary<EntitySMTasks, Func<bool>>();
            self.Register(this);
        }

        public void Update(BehaviorEntity.Animal self) {
            TaskDuration -= EntityJob.cxt.deltaTime;
        }

        public void UpdateController(BehaviorEntity.Animal self, BehaviorEntity.AnimalController controller) {
#if UNITY_EDITOR
            if (UnityEditor.Selection.Contains(controller.gameObject)) Debug.Log(TaskIndex);
#endif
            if(animator == null) return;
            if (AnimatorTask == TaskIndex) return;
            if (StateAnimations.TryGetValue(AnimatorTask, out string animation)) animator.SetBool(animation, false);
            AnimatorTask = TaskIndex;
            if (StateAnimations.TryGetValue(AnimatorTask, out animation)) animator.SetBool(animation, true);
        }

        public void RegisterTransition(EntitySMTasks name, Func<bool> condition = null) =>
            ConditionalTransitions[name] = condition;

        public void RegisterAnimation(EntitySMTasks name, string animation) {
            if (StateAnimations == null) return;
            if (String.IsNullOrEmpty(animation)) return;
            StateAnimations[name] = animation;
        }

        public bool Transition(EntitySMTasks dest) {
            if (dest == EntitySMTasks.None) return false;
            if (ConditionalTransitions.TryGetValue(dest, out Func<bool> cond))
                if (!cond()) return false;

            TaskIndex = dest;
            return true;
        }
    }
}