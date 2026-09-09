using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Events;
using Arterra.Engine.Audio;
using FMOD;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class AnimalSoundBehaviorSettings : IBehaviorSetting {
        public Option<List<EventSound>> EventSounds;
        public Option<List<StateSound>> StateSounds;
        public TriggerMode triggerMode = TriggerMode.CutOff;

        [JsonIgnore] [HideInInspector] [UISetting(Ignore = true)]
        private Dictionary<GameEvent, LinkedList<SoundEffect>> _eventSounds;

        [JsonIgnore] [HideInInspector] [UISetting(Ignore = true)]
        private Dictionary<EntitySMTasks, LinkedList<StateSound>> _stateSounds;
        
        [Serializable]
        public struct EventSound {
            public GameEvent trigger;
            public SoundEffect effect;
        }

        [Serializable]
        public struct StateSound {
            public EntitySMTasks state;
            public float frequency;
            public SoundEffect effect;
        }

        [Serializable]
        public struct SoundEffect {
            public AudioEvents sound;
            public float chance;
        }

        public enum TriggerMode {
            Await,
            Multiple,
            CutOff,
        }

        public object Clone() {
            return new AnimalSoundBehaviorSettings {
                EventSounds = EventSounds,
                StateSounds = StateSounds,
                triggerMode = triggerMode,
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            _eventSounds = new Dictionary<GameEvent, LinkedList<SoundEffect>>();
            _stateSounds = new Dictionary<EntitySMTasks, LinkedList<StateSound>>();

            if (EventSounds.value != null) {
                foreach (EventSound sound in EventSounds.value) {
                    if (!_eventSounds.TryGetValue(sound.trigger, out LinkedList<SoundEffect> effects)) {
                        effects = new LinkedList<SoundEffect>();
                        _eventSounds[sound.trigger] = effects;
                    }
                    effects.AddLast(sound.effect);
                }
            }

            if (StateSounds.value != null) {
                foreach (StateSound sound in StateSounds.value) {
                    if (!_stateSounds.TryGetValue(sound.state, out LinkedList<StateSound> sounds)) {
                        sounds = new LinkedList<StateSound>();
                        _stateSounds[sound.state] = sounds;
                    }
                    sounds.AddLast(sound);
                }
            }
        }

        public IEnumerable<KeyValuePair<GameEvent, LinkedList<SoundEffect>>> GetEventSoundMap() {
            if (_eventSounds == null) yield break;
            foreach (var pair in _eventSounds)
                yield return pair;
        }

        public IEnumerable<KeyValuePair<EntitySMTasks, LinkedList<StateSound>>> GetStateSoundMap() {
            if (_stateSounds == null) yield break;
            foreach (var pair in _stateSounds)
                yield return pair;
        }

        public bool TryGetStateSound(EntitySMTasks state, out LinkedList<StateSound> sounds) {
            sounds = null;
            return _stateSounds != null && _stateSounds.TryGetValue(state, out sounds);
        }
    }

    public class AnimalSoundBehavior : SpeciesBehavior {
        [JsonIgnore] public AnimalSoundBehaviorSettings settings;

        private BehaviorEntity.Animal self;
        private StateMachineManagerBehavior manager;
        private readonly Dictionary<GameEvent, RefEventHandler> eventHandlers = new();
        private readonly Dictionary<EntitySMTasks, List<AnimalSoundBehaviorSettings.StateSound>> stateSoundRuntime = new();
        private FMOD.Studio.EventInstance currentSound;
        private bool pendingSound;
        private float stateSoundTimer;

        private EntitySMTasks lastState = EntitySMTasks.None;

        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> hierarchy) {
            hierarchy.TryAdd(Behaviors.StateMachine, hierarchy.Count);
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> hierarchy) {
            hierarchy.TryAdd(typeof(AnimalSoundBehaviorSettings), new AnimalSoundBehaviorSettings());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: AnimalSoundBehavior requires AnimalSettings to have AnimalSoundBehaviorSettings");
            if (!self.Is(out manager))
                throw new Exception("Entity: AnimalSoundBehavior requires AnimalInstance to have StateMachineManagerBehavior");

            this.self = self;
            lastState = manager.TaskIndex;
            pendingSound = false;
            currentSound = default;
            stateSoundTimer = self.random.NextFloat() * 1E5f;
            BuildStateRuntimeCache();
            RegisterEventHandlers();
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: AnimalSoundBehavior requires AnimalSettings to have AnimalSoundBehaviorSettings");
            if (!self.Is(out manager))
                throw new Exception("Entity: AnimalSoundBehavior requires AnimalInstance to have StateMachineManagerBehavior");

            this.self = self;
            lastState = manager.TaskIndex;
            pendingSound = false;
            currentSound = default;
            stateSoundTimer = self.random.NextFloat() * 1E5f;
            BuildStateRuntimeCache();
            RegisterEventHandlers();
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.JobSync || self.context == BehaviorEntity.UpdateContext.Main)
                return;
            stateSoundTimer += self.DeltaTime;

            if (!stateSoundRuntime.TryGetValue(manager.TaskIndex, out List<AnimalSoundBehaviorSettings.StateSound> stateSounds)) {
                return;
            }

            if (manager.TaskIndex != lastState) {
                lastState = manager.TaskIndex;
            }

            float previousTimer = math.max(stateSoundTimer - self.DeltaTime, 0f);
            
            foreach (AnimalSoundBehaviorSettings.StateSound sound in stateSounds) {
                float frequency = math.max(sound.frequency, 0.01f);
                float previousBucket = math.floor(previousTimer / frequency);
                float currentBucket = math.floor(stateSoundTimer / frequency);
                if (currentBucket <= previousBucket)
                    continue;

                bool started = TryPlayEffect(sound.effect);
                if (!started) continue;

                stateSoundTimer = 0;
                if (settings.triggerMode != AnimalSoundBehaviorSettings.TriggerMode.Multiple)
                    break;
            }
        }

        public override void Disable(BehaviorEntity.Animal self) {
            RemoveEventHandlers();
            stateSoundRuntime.Clear();
            this.self = null;
            manager = null;
            pendingSound = false;
            currentSound = default;
            lastState = EntitySMTasks.None;
        }

        private void RegisterEventHandlers() {
            RemoveEventHandlers();
            foreach (var pair in settings.GetEventSoundMap()) {
                GameEvent evt = pair.Key;
                LinkedList<AnimalSoundBehaviorSettings.SoundEffect> effects = pair.Value;

                RefEventHandler handler = (_, _, __) => TryPlayEffects(effects);
                eventHandlers[evt] = handler;
                self.eventCtrl.AddEventHandler(evt, handler);
            }
        }

        private void BuildStateRuntimeCache() {
            stateSoundRuntime.Clear();
            foreach (var pair in settings.GetStateSoundMap()) {
                List<AnimalSoundBehaviorSettings.StateSound> runtime = new();
                foreach (AnimalSoundBehaviorSettings.StateSound sound in pair.Value) {
                    runtime.Add(sound);
                }

                stateSoundRuntime[pair.Key] = runtime;
            }
        }

        private void RemoveEventHandlers() {
            if (self == null || eventHandlers.Count == 0) {
                eventHandlers.Clear();
                return;
            }

            foreach (var pair in eventHandlers)
                self.eventCtrl.RemoveEventHandler(pair.Key, pair.Value);

            eventHandlers.Clear();
        }

        private void TryPlayEffects(LinkedList<AnimalSoundBehaviorSettings.SoundEffect> effects) {
            if (effects == null || effects.Count == 0) return;

            for (LinkedListNode<AnimalSoundBehaviorSettings.SoundEffect> node = effects.First; node != null; node = node.Next) {
                bool started = TryPlayEffect(node.Value);
                if (started && settings.triggerMode != AnimalSoundBehaviorSettings.TriggerMode.Multiple)
                    break;
            }
        }

        private bool TryPlayEffect(AnimalSoundBehaviorSettings.SoundEffect effect) {
            if (self == null || !self.active) return false;
            if (effect.sound == AudioEvents.None) return false;

            switch (settings.triggerMode) {
                case AnimalSoundBehaviorSettings.TriggerMode.Await:
                    if (!CanStartSound()) return false;
                    break;
                case AnimalSoundBehaviorSettings.TriggerMode.CutOff:
                    if (pendingSound) return false;
                    ForceStopCurrentSound();
                    break;
                case AnimalSoundBehaviorSettings.TriggerMode.Multiple:
                default:
                    break;
            }

            float chance = math.clamp(effect.chance, 0f, 1f);
            if (chance <= 0 || self.random.NextFloat() > chance) return false;

            bool memoize = settings.triggerMode != AnimalSoundBehaviorSettings.TriggerMode.Multiple;
            if (memoize)
                pendingSound = true;

            EntityManager.AddHandlerEvent(() => {
                if (self == null || !self.active) {
                    pendingSound = false;
                    return;
                }

                FMOD.Studio.EventInstance e;
                if (self.controller?.gameObject != null)
                    e = AudioManager.CreateEventAttached(effect.sound, self.controller.gameObject);
                else
                    e = AudioManager.CreateEvent(effect.sound, self.position);

                if (memoize) {
                    currentSound = e;
                    pendingSound = false;
                }
            });

            return true;
        }

        private bool CanStartSound() {
            if (pendingSound) return false;
            if (!currentSound.isValid()) return true;

            RESULT res = currentSound.getPlaybackState(out FMOD.Studio.PLAYBACK_STATE playbackState);
            if (res != RESULT.OK) return true;
            return playbackState == FMOD.Studio.PLAYBACK_STATE.STOPPED;
        }

        private void ForceStopCurrentSound() {
            if (!currentSound.isValid()) return;

            AudioManager.StopEvent(currentSound, false);
            pendingSound = false;
            currentSound = default;
        }
    }
}