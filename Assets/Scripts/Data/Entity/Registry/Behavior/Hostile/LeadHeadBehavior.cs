using System;
using System.Collections.Generic;
using Newtonsoft.Json;

using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using TerrainCollider = Arterra.GamePlay.Interaction.TerrainCollider;

namespace Arterra.Data.Entity.Behavior {
    public interface IMultiCollider {
        [JsonIgnore] public TerrainCollider Collider {get;}
        [JsonIgnore] public TerrainCollider PathCollider {get;}
        [JsonIgnore] public Quaternion Rotation {
            get => Collider.transform.rotation;
            set => Collider.transform.rotation = value;
        }
    }

    [Serializable]
    public struct AnimOverride {
        public string AnimName;
        public uint LegControlBitmap;
    }

    public class LeadHeadSettings : IBehaviorSetting {
        public float3 Offset = new (0, 5, 0);
        public TerrainCollider.Settings Collider;
        public float HeadRubberBand = 5.0f;
        public float BodyRubberBand = 1.0f;
        public object Clone() {
            return new LeadHeadSettings {
                Offset = Offset,
                Collider = Collider,
                HeadRubberBand = HeadRubberBand,
                BodyRubberBand = BodyRubberBand,
            };
        }
    }

    public class PASettings {
        [UISetting(Ignore = true)]
        public List<AnimOverride> AnimControl;
        [UISetting(Ignore = true)][JsonIgnore]
        public Dictionary<int, uint> AnimToggle;
        public bool OverrideAnimByDefault = false;

        public object Clone(PASettings self) {
            self.AnimControl = AnimControl;
            self.OverrideAnimByDefault = OverrideAnimByDefault;
            return self;
        }

        public virtual void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            AnimToggle = new Dictionary<int, uint>();
            foreach(AnimOverride anim in AnimControl) {
                int hash = Animator.StringToHash(anim.AnimName);
                AnimToggle.TryAdd(hash, anim.LegControlBitmap);
            }
        }
    }

    public class LeadHeadBehavior : IBehavior, IMultiCollider {
        [JsonIgnore] public TerrainCollider BodyCollider;
        public TerrainCollider HeadCollider;

        [JsonIgnore] public LeadHeadSettings settings;
        [JsonIgnore] public TerrainCollider Collider => HeadCollider;
        [JsonIgnore] public TerrainCollider PathCollider => BodyCollider;
        [JsonIgnore] public Quaternion Rotation {
            get => BodyCollider.transform.rotation;
            set => BodyCollider.transform.rotation = value;
        }

        private ColliderUpdateBehavior BodyCUpdate;

        [JsonIgnore]
        public float3 BodyPosition {
            get => BodyCollider.transform.position + BodyCollider.transform.size / 2;
            set => BodyCollider.transform.position = value - BodyCollider.transform.size / 2;
        }
        [JsonIgnore]
        public float3 BodyOrigin {
            get => BodyCollider.transform.position;
            set => BodyCollider.transform.position = value;
        }

        [JsonIgnore]
        public float3 HeadPosition {
            get => HeadCollider.transform.position + HeadCollider.transform.size / 2;
            set => HeadCollider.transform.position = value - HeadCollider.transform.size / 2;
        }
        [JsonIgnore]
        public float3 HeadOrigin {
            get => HeadCollider.transform.position;
            set => HeadCollider.transform.position = value;
        }

        public void Update(BehaviorEntity.Animal self) {
            BodyCollider.useGravity = HeadCollider.useGravity;
            HeadCollider.transform.rotation = BodyCollider.transform.rotation;
            switch (BodyCUpdate.settings.interactType) {
                case ColliderUpdateSettings.InteractType.Regular:
                    HeadCollider.Update(self);
                    HeadCollider.EntityCollisionUpdate(self, BodyCUpdate.IgnoredEntities);
                    break;
                case ColliderUpdateSettings.InteractType.NoEntity:
                    HeadCollider.Update(self);
                    break;
                case ColliderUpdateSettings.InteractType.NoGround:
                    HeadCollider.Update(self, tangible: false);
                    HeadCollider.EntityCollisionUpdate(self, BodyCUpdate.IgnoredEntities);
                    break;
                case ColliderUpdateSettings.InteractType.None:
                    HeadCollider.Update(self, tangible: false);
                    break;
                default:
                    break;
            }
        }
        
        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.Collider, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(LeadHeadSettings), new LeadHeadSettings());
        }


        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: LeadHead Behavior Requires AnimalSettings to have LeadHeadSettings");
            if (!self.Is(out BodyCUpdate)) 
                throw new Exception("Entity: LeadHead Behavior Requires AnimalSettings to have ColliderUpdateBehavior");
            this.HeadCollider = new TerrainCollider(settings.Collider, GCoord + settings.Offset);
            this.BodyCollider = BodyCUpdate.collider;
            self.Register<IMultiCollider>(this);
            HeadCollider.useGravity = false;
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: LeadHead Behavior Requires AnimalSettings to have LeadHeadSettings");
            if (!self.Is(out BodyCUpdate)) 
                throw new Exception("Entity: LeadHead Behavior Requires AnimalSettings to have ColliderUpdateBehavior");
            this.BodyCollider = BodyCUpdate.collider;
            self.Register<IMultiCollider>(this);
            HeadCollider.useGravity = false;
        }

        public abstract class Appendage : Entity {
            public TerrainCollider collider;
            [JsonIgnore] public AppendageSettings aSettings;
            [JsonIgnore] protected Transform Controller;
            [JsonIgnore] protected IRigConstraint constraint;
            [JsonIgnore] protected BehaviorEntity.Animal self;
            protected bool visible;
            [JsonConstructor]
            public Appendage() {} //Newtonsoft path

            public override ref TerrainCollider.Transform transform => ref collider.transform;
            public override void Initialize(EntitySetting _1, GameObject _2, float3 _3){}
            public override void Deserialize(EntitySetting _1, GameObject _2, out int3 _3){_3 = default;}

            public virtual bool IsProxy<T>(out T instance) {
                if (this is T t) {
                    instance = t;
                    return true;
                }

                instance = default!;
                return false;
            }
            public override bool Is<T>(out T instance) {
                if(IsProxy(out instance)) return true;
                return self.Is(out instance);
            }

            public virtual void Initialize(BehaviorEntity.Animal animal, AppendageSettings settings, Transform root) {
                this.aSettings = settings;
                visible = false;

                this.info.entityId = Guid.NewGuid();
                this.info.rtEntityId = animal.info.entityId;
                this.info.entityType = animal.info.entityType;
                this.self = animal;

                this.Controller = root.Find(settings.BonePath);
                if (Controller == null) Debug.Log(settings.BonePath);
                RegisterAppendage();
            }
            
            public virtual void Deserialize(BehaviorEntity.Animal animal, AppendageSettings settings, Transform root) {
                this.aSettings = settings;
                visible = false;
                this.self = animal;

                this.Controller = root.Find(settings.BonePath);
                if (Controller == null) Debug.Log(settings.BonePath);
                RegisterAppendage();
            }

            private void RegisterAppendage() {
                if (!EntityManager.EntityIndex.TryAdd(info.entityId, this))
                    throw new Exception("Failed to register leg for entity");
                Bounds bounds = new (collider.transform.position, collider.transform.size);
                EntityManager.ESTree.Insert(bounds, info.entityId);
            }

            public override void Update() {
                EntityManager.AddHandlerEvent(() => {
                    Bounds bounds = new Bounds(collider.transform.position, collider.transform.size);
                    EntityManager.ESTree.AssertEntityLocation(info.entityId, bounds);  
                });
                this.collider.Update(self);
                this.collider.EntityCollisionUpdate(self);
            }

            public void SetConstraint<TConstraint>() => this.constraint = Controller.transform.parent.GetComponent(typeof(TConstraint)) as IRigConstraint;
            public virtual void UpdateController() {
                if (!visible) return;
                float3 BonePos = collider.transform.position + aSettings.BoneOffset;
                Controller.SetPositionAndRotation(Arterra.Core.Storage.CPUMapManager.GSToWS(BonePos), collider.transform.rotation * Quaternion.Euler(-90f, 0f, 0f));
            }

            public void SetActive(bool enabled) {
                if (visible != enabled) {
                    visible = enabled;
                    constraint.weight = visible ? 1 : 0;
                } if (!visible) return;
            }
            public override void Disable() {
                this.self = null;
                if (!active) return;
                active = false;

                EntityManager.EntityIndex.Remove(info.entityId);
                EntityManager.ESTree.Delete(info.entityId);
            }
        }

        public abstract class AppendageSettings {
            public TerrainCollider.Settings collider;
            //The offset relative to the center of the body collider
            // of the center of the appendge collider
            public float3 RestOffset;
            public float3 BoneOffset;
            public string BonePath;
        }
    }
}