using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Events;
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
                new StateAnimation { task = EntitySMTasks.DecidePath, Animation = "IsIdling" },
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

        public Option<List<EventAnim>> EventAnims;


        [UIHidden][HideInInspector][JsonIgnore]
        public Dictionary<EntitySMTasks, string> _stateAnims;
        [UIHidden][HideInInspector][JsonIgnore]
        public Dictionary<GameEvent, AnimTrigger> _eventAnims;
        [Serializable]
        public struct EventAnim {
            public GameEvent trigger;
            public AnimTrigger anim;
        }

        [Serializable]
        public struct AnimTrigger {
            public string name;
            public AnimParam paramEffect;
            public enum AnimParam {
                BoolOn, BoolOff, Trigger
            }
        }


        public object Clone() {
            return new AnimatedSettings {
                AnimatorPath = AnimatorPath,
                EventAnims = EventAnims,
                StateAnimations = StateAnimations
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            _stateAnims = new Dictionary<EntitySMTasks, string>();
            _eventAnims = new Dictionary<GameEvent, AnimTrigger>();

            if (StateAnimations.value != null) {
                foreach(StateAnimation sm in StateAnimations.value) {
                    _stateAnims[sm.task] = sm.Animation;
                }
            }

            if (EventAnims.value != null) {
                foreach(EventAnim em in EventAnims.value) {
                    _eventAnims[em.trigger] = em.anim;
                }
            }
        }

        [Serializable]
        public struct StateAnimation {
            public EntitySMTasks task;
            public string Animation;
        }

    }
    //ToDo: Support multiple paths for animator
    public class AnimatedBehavior : SpeciesBehavior {
        [JsonIgnore] public Animator animator;
        [JsonIgnore] public AnimatedSettings settings;

        private StateMachineManagerBehavior manager;
        private EntitySMTasks AnimatorTask;
        private BehaviorEntity.Animal self;
        private readonly Dictionary<GameEvent, RefEventHandler> eventHandlers = new();

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(AnimatedSettings), new AnimatedSettings());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Animated Behavior Requires AnimalSettings to have AnimatedSettings");
            if (!self.Is(out manager)) manager = null;
            AnimatorTask = EntitySMTasks.None;
            animator = self.controller.transform.Find(settings.AnimatorPath).GetComponent<Animator>();
            RegisterEventHandlers(self);
        }
        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Animated Behavior Requires AnimalSettings to have AnimatedSettings");
            if (!self.Is(out manager)) manager = null;
            AnimatorTask = EntitySMTasks.None;
            animator = self.controller.transform.Find(settings.AnimatorPath).GetComponent<Animator>();
            RegisterEventHandlers(self);
        }

        public override void Disable(BehaviorEntity.Animal self) {
            RemoveEventHandlers();
            this.self = null;
            animator = null;
        }

        private void RegisterEventHandlers(BehaviorEntity.Animal self) {
            RemoveEventHandlers();
            this.self = self;
            if (settings._eventAnims == null) return;

            foreach (var pair in settings._eventAnims) {
                AnimatedSettings.AnimTrigger animation = pair.Value;
                RefEventHandler handler = (_, _, __) => QueueEventAnimation(animation);
                eventHandlers[pair.Key] = handler;
                self.eventCtrl.AddEventHandler(pair.Key, handler);
            }
        }

        private void RemoveEventHandlers() {
            if (self != null) {
                foreach (var pair in eventHandlers)
                    self.eventCtrl.RemoveEventHandler(pair.Key, pair.Value);
            }
            eventHandlers.Clear();
        }

        private void QueueEventAnimation(AnimatedSettings.AnimTrigger animation) {
            BehaviorEntity.Animal owner = self;
            EntityManager.AddHandlerEvent(() => {
                if (owner == null || !ReferenceEquals(self, owner) || !owner.active) return;
                if (animator == null || !animator.enabled) return;
                if (string.IsNullOrEmpty(animation.name)) return;

                switch (animation.paramEffect) {
                    case AnimatedSettings.AnimTrigger.AnimParam.BoolOn:
                        animator.SetBool(animation.name, true);
                        break;
                    case AnimatedSettings.AnimTrigger.AnimParam.BoolOff:
                        animator.SetBool(animation.name, false);
                        break;
                    case AnimatedSettings.AnimTrigger.AnimParam.Trigger:
                        animator.SetTrigger(animation.name);
                        break;
                }
            });
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (manager == null) return;
            if (AnimatorTask == manager.TaskIndex) return;
            //We have to persist animation on server because some simulations depend on the animator
            if (!self.ShouldUpdateAnimations())
                return;
            if (animator == null || !animator.enabled) return;
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
