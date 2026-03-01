using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.Data.Entity.Behavior;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {

    public class AnimatedSettings : IBehaviorSetting {
        public string AnimatorPath = "";
        public Option<List<StateAnimation>> StateAnimations = new () {
            value = new () {
                new StateAnimation { task = EntitySMTasks.Idle, Animation = "IsIdling" },
                new StateAnimation { task = EntitySMTasks.RandomPath, Animation = "IsWalking" },
                new StateAnimation { task = EntitySMTasks.FollowPath, Animation = "IsWalking" },
                new StateAnimation { task = EntitySMTasks.ChaseFriends, Animation = "IsWalking" },
                new StateAnimation { task = EntitySMTasks.FindMate, Animation = "IsWalking" },
                new StateAnimation { task = EntitySMTasks.ChaseMate, Animation = "IsWalking" },
                new StateAnimation { task = EntitySMTasks.Reproduce, Animation = "IsCuddling" },
                new StateAnimation { task = EntitySMTasks.FollowRider, Animation = "IsWalking" },
                new StateAnimation { task = EntitySMTasks.ChasePreyPlant, Animation = "IsWalking" },
                new StateAnimation { task = EntitySMTasks.ChasePreyEntity, Animation = "IsRunning" },
                new StateAnimation { task = EntitySMTasks.EatPlant, Animation = "IsEating" },
                new StateAnimation { task = EntitySMTasks.EatEntity, Animation = "IsEating" },
                new StateAnimation { task = EntitySMTasks.AttackTarget, Animation = "IsAttacking" },
                new StateAnimation { task = EntitySMTasks.RunFromPredator, Animation = "IsRunning" },
                new StateAnimation { task = EntitySMTasks.RunFromTarget, Animation = "IsRunning" },
                new StateAnimation { task = EntitySMTasks.ChaseTarget, Animation = "IsRunning" },
                new StateAnimation { task = EntitySMTasks.Retaliate, Animation = "IsAttacking" },
                new StateAnimation { task = EntitySMTasks.Death, Animation = "IsDead" },
            }
        };

        [UISetting(Ignore = true)][HideInInspector][JsonIgnore]
        public Dictionary<EntitySMTasks, string> _stateAnims;

        public object Clone() {
            return new AnimatedSettings {
                AnimatorPath = AnimatorPath,
                StateAnimations = StateAnimations
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            _stateAnims = new Dictionary<EntitySMTasks, string>();

            if (StateAnimations.value == null) return;
            foreach(StateAnimation sm in StateAnimations.value) {
                _stateAnims[sm.task] = sm.Animation;
            }
        }

        [Serializable]
        public struct StateAnimation {
            public EntitySMTasks task;
            public string Animation;
        }

    }
    //ToDo: Support multiple paths for animator
    public class AnimatedBehavior : IBehavior {
        [JsonIgnore] public Animator animator;
        [JsonIgnore] public AnimatedSettings settings;

        private StateMachineManagerBehavior manager;
        private EntitySMTasks AnimatorTask;

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(AnimatedSettings), new AnimatedSettings());
        }
        
        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Animated Behavior Requires AnimalSettings to have AnimatedSettings");
            if (!self.Is(out manager)) manager = null;
            AnimatorTask = EntitySMTasks.None;
            animator = self.controller.transform.Find(settings.AnimatorPath).GetComponent<Animator>();
        }
        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Animated Behavior Requires AnimalSettings to have AnimatedSettings");
            if (!self.Is(out manager)) manager = null;
            AnimatorTask = EntitySMTasks.None;
            animator = self.controller.transform.Find(settings.AnimatorPath).GetComponent<Animator>();
        }

        public void UpdateController(BehaviorEntity.Animal self, BehaviorEntity.AnimalController controller) {
            if (manager == null) return;
            if (AnimatorTask == manager.TaskIndex) return;
            if (settings._stateAnims.TryGetValue(AnimatorTask, out string animation)) animator.SetBool(animation, false);
            AnimatorTask = manager.TaskIndex;
            if (settings._stateAnims.TryGetValue(AnimatorTask, out animation)) animator.SetBool(animation, true);
        }

        public string GetCurrentAnimation() {
            if (settings._stateAnims.TryGetValue(AnimatorTask, out string animation))
                return animation;
            return null;
        }
        
        public void SetBool(string name, bool value) => animator.SetBool(name, value);
        public void SetTrigger(string name) => animator.SetTrigger(name);
    }
}