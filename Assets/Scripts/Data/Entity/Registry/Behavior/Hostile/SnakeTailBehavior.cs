using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

using Arterra.Configuration;
using Arterra.Editor;
using Unity.Mathematics;
using System.Linq;
using TerrainCollider = Arterra.GamePlay.Interaction.TerrainCollider;
using Arterra.Core.Storage;
using UnityEngine.Animations.Rigging;
using Unity.Burst.Intrinsics;
using Arterra.Data.Item;

namespace Arterra.Data.Entity.Behavior
{
    public class SnakeTailSettings : PASettings, IBehaviorSetting
    {
        public Option<List<SegmentSettings>> Segments;
        public Option<AnimationCurve> SlitherX;
        public Option<AnimationCurve> SlitherY;
        public float SlitherSpeed;
        public float MoveAccel;

       [Serializable]
        public class SegmentSettings : LeadHeadBehavior.AppendageSettings {
            public float MoveAccel;
            public float Phase;
            public float RBStrength = 1.0f;
        }
        public object Clone() {
            return base.Clone(new SnakeTailSettings {
                Segments = Segments,
                SlitherX = SlitherX,
                SlitherY = SlitherY,
                SlitherSpeed = SlitherSpeed,
                MoveAccel = MoveAccel,
            });
        }

        public override void Preset(uint EntityType, BehaviorEntity.AnimalSetting setting) {
            base.Preset(EntityType, setting);
        }
    }

    public class SnakeTailBehavior : IBehavior {
        private SnakeTailSettings settings;
        private LeadHeadBehavior LeadHead;
        private AnimatedBehavior animated;
        
        [JsonProperty] private TailSegment[] appendages;
        private IAttackable selfAtk;
        private float SlitherProgress;
        private uint LastOverrideState;

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.LeadHead, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(SnakeTailSettings), new SnakeTailSettings());
        }

        
        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: MultiPedal Behavior Requires AnimalSettings to have SnakeTailSettings");
            if (!self.Is(out LeadHead)) 
                throw new Exception("Entity: MultiPedal Behavior Requires AnimalSettings to have LeadHeadBehavior");
            if (!self.Is(out selfAtk)) 
                throw new Exception("Entity: MultiPedal Behavior Requires AnimalSettings to have IAttackable");
            if (!self.Is(out animated)) animated = null;
            SlitherProgress = 0;
            SetUp(self);
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: MultiPedal Behavior Requires AnimalSettings to have SnakeTailSettings");
            if (!self.Is(out LeadHead)) 
                throw new Exception("Entity: MultiPedal Behavior Requires AnimalSettings to have LeadHeadBehavior");
            if (!self.Is(out selfAtk)) 
                throw new Exception("Entity: MultiPedal Behavior Requires AnimalSettings to have IAttackable");
            if (!self.Is(out animated)) animated = null;
            SetUp(self);
        }


        private void SetUp(BehaviorEntity.Animal self) {
            List<SnakeTailSettings.SegmentSettings> lSettings = settings.Segments.value;
            appendages ??= new TailSegment[lSettings.Count];
            Transform root = self.controller.gameObject.transform;
            for(int i = 0; i < lSettings.Count; i++){
                if(appendages[i] == null) {
                    appendages[i] = new TailSegment();
                    appendages[i].Initialize(self, lSettings[i], root);
                } else appendages[i].Deserialize(self, lSettings[i], root);
            }
            AttachSegments(self);
            this.LastOverrideState = 0;
        }

        public void Update(BehaviorEntity.Animal self) {
            SlitherProgress += EntityJob.cxt.deltaTime * settings.SlitherSpeed;
            SlitherProgress = math.frac(SlitherProgress);

            float3 desiredHead = LeadHead.BodyPosition + LeadHead.settings.Offset;
            LeadHead.HeadCollider.transform.velocity += EntityJob.cxt.deltaTime * (desiredHead - LeadHead.HeadPosition) * settings.MoveAccel;
            float3 origin = appendages[0].desiredBody;
            ApplyRubberBand(LeadHead.HeadCollider, origin, LeadHead.settings.HeadRubberBand);
            origin = LeadHead.HeadPosition - LeadHead.settings.Offset;
            ApplyRubberBand(LeadHead.BodyCollider, origin, LeadHead.settings.BodyRubberBand);

            foreach(TailSegment l in appendages) {
                if (selfAtk.IsDead) l.Update(self);
                else l.UpdateMovement(self);
            };
        }

        private void AttachSegments(BehaviorEntity.Animal self) {
            //Attach segments together
            TerrainCollider prev = LeadHead.HeadCollider;
            for(int i = 0; i < appendages.Length - 1; i++){
                appendages[i].AttachSegment(self, prev, appendages[i+1]);
                prev = appendages[i].collider;
            } appendages[^1].AttachSegment(self, prev, default, false);
        }

        private static void ApplyRubberBand(TerrainCollider collider, float3 origin, float strength) {
                float3 center = collider.transform.position + collider.transform.size/2;
                float3 dir = math.normalizesafe(origin - center);
                collider.transform.velocity += strength * EntityJob.cxt.deltaTime * dir; 
            }

        public void UpdateController(BehaviorEntity.Animal self, BehaviorEntity.AnimalController controller) {
            if (controller.gameObject == null) return;
            if (animated == null) return;

            foreach(var c in appendages) c.UpdateController();
            int hash = animated.animator.GetCurrentAnimatorStateInfo(0).shortNameHash; 
            if(!settings.AnimToggle.TryGetValue(hash, out uint bitmap))
                bitmap = settings.OverrideAnimByDefault ? 0xFFFFFFFF : 0;
            if (bitmap != LastOverrideState) {
                LastOverrideState = bitmap;
                for(int i = 0; i < appendages.Length; i++) {
                    appendages[i].SetActive(((bitmap >> i) & 0x1) != 0);
                }
            }
        }

        public void OnDrawGizmos(BehaviorEntity.Animal self) {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(CPUMapManager.GSToWS(LeadHead.HeadPosition), LeadHead.settings.Collider.size * 2);
            foreach(TailSegment l in appendages) {
                Gizmos.color = Color.blue;
                Gizmos.DrawWireCube(CPUMapManager.GSToWS(
                    l.collider.transform.position + l.collider.transform.size/2),
                    l.collider.transform.size * 2);
            }
        }

        public void Disable(BehaviorEntity.Animal self) {
            if (appendages == null) return;
            foreach(TailSegment l in appendages) l.Disable();
        }


        private class TailSegment : LeadHeadBehavior.Appendage, IAttackable {
            [JsonIgnore]
            private SnakeTailBehavior st;
            [JsonIgnore]
            public SnakeTailSettings.SegmentSettings settings;
            private IAttackable selfAtk;

            //Ignored
            private TerrainCollider PrevSegment;
            private TailSegment NextSegment;
            private bool HasNext;

            private float3 PrevCenter => PrevSegment.transform.position + PrevSegment.transform.size / 2;
            private float3 NextCenter => NextSegment.collider.transform.position + NextSegment.collider.transform.size / 2;
            [JsonIgnore]  public float3 desiredBody => center - math.mul(PrevSegment.transform.rotation, settings.RestOffset);
            [JsonIgnore]  private float3 center => collider.transform.position + collider.transform.size / 2;

            public bool IsDead => selfAtk.IsDead;
            public void Interact(Entity caller, IItem item = null) => selfAtk.Interact(caller, item);
            public IItem Collect(Entity caller, float collectRate) => selfAtk.Collect(caller, collectRate);
            public bool TakeDamage(float damage, float3 knockback, Entity attacker = null) {
                if (selfAtk.TakeDamage(damage, float3.zero, attacker)) {
                    velocity += knockback;
                    return true;
                } return false;
            }
            

            [JsonConstructor]
            public TailSegment() {} //Newtonsoft path
            public override void Initialize(BehaviorEntity.Animal self, LeadHeadBehavior.AppendageSettings settings, Transform root) {
                this.settings = settings as SnakeTailSettings.SegmentSettings;
                this.collider = new TerrainCollider(settings.collider, self.position);
                collider.useGravity = false;

                base.Initialize(self, settings, root);
                SetConstraint<MultiParentConstraint>();

                if(!self.Is(out selfAtk)) throw new Exception("Entity: SnakeTail expected entity to be IAttackable"); 
                if(!self.Is(out st)) throw new Exception("Entity: SnakeTail expected entity to be SnakeTaileBehavior");
                active = false;
            }

            public override void Deserialize(BehaviorEntity.Animal self, LeadHeadBehavior.AppendageSettings settings, Transform root) {
                base.Deserialize(self, settings, root);
                SetConstraint<MultiParentConstraint>();

                this.settings = settings as SnakeTailSettings.SegmentSettings;
                if (!self.Is(out selfAtk)) throw new Exception("Entity: SnakeTail expected entity to be IAttackable"); 
                if(!self.Is(out st)) throw new Exception("Entity: SnakeTail expected entity to be SnakeTaileBehavior");
                active = true;
            }

            public void AttachSegment(BehaviorEntity.Animal self, TerrainCollider PrevSegment, TailSegment NextSegment, bool HasNext = true) {
                this.PrevSegment = PrevSegment;
                this.NextSegment = NextSegment;
                this.HasNext = HasNext;
                if (active) return;
                
                //Finish setup
                this.collider.transform.position = PrevCenter + settings.RestOffset;
                this.collider.transform.rotation = self.transform.rotation;
                active = true;
            }

            public void Update(BehaviorEntity.Animal self) {
                if(HasNext) {
                    ApplyRubberBand(
                        NextCenter - math.mul(collider.transform.rotation, NextSegment.settings.RestOffset),
                        NextSegment.settings.RBStrength
                    );
                }

                float3 origin = PrevCenter + math.mul(PrevSegment.transform.rotation, settings.RestOffset);
                ApplyRubberBand(origin, settings.RBStrength);
                base.Update();
            }

            private void ApplyRubberBand(float3 origin, float strength) {
                float dist = math.distance(origin, center);
                if (dist > collider.transform.size.y) { //Rubber banding
                    float3 dir = math.normalizesafe(origin - center);
                    strength *= math.distance(origin, center);
                    collider.transform.velocity += strength * EntityJob.cxt.deltaTime * dir; 
                }
            }

            public void UpdateMovement(BehaviorEntity.Animal self) {
                Update(self);
                this.collider.transform.velocity *= 0.75f;

                float3 aim;
                float3 origin = PrevCenter + math.mul(PrevSegment.transform.rotation, settings.RestOffset);
                float t = math.frac(st.SlitherProgress + settings.Phase);
                float3 xOff = math.mul(PrevSegment.transform.rotation, st.settings.SlitherX.value.Evaluate(t) * Vector3.up);
                float3 yOff = math.mul(PrevSegment.transform.rotation, st.settings.SlitherY.value.Evaluate(t) * Vector3.right);
                aim = (origin - center) + xOff + yOff; 

                aim = math.normalizesafe(aim);
                if (Quaternion.Angle(PrevSegment.transform.rotation, collider.transform.rotation) > 15) 
                    collider.transform.rotation = Quaternion.Slerp(collider.transform.rotation, PrevSegment.transform.rotation, EntityJob.cxt.deltaTime);   
            
                collider.transform.velocity += settings.MoveAccel * EntityJob.cxt.deltaTime * aim;
            }
        }
    }
}