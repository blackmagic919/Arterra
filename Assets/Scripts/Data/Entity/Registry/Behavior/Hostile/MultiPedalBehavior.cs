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

namespace Arterra.Data.Entity.Behavior
{
    public class MultiPedalSettings : PASettings, IBehaviorSetting
    {
        public Option<List<LegSettings>> Legs;

        [Serializable]
        public class LegSettings : LeadHeadBehavior.AppendageSettings {
            public float StepDistThreshold;
            public float LegRaiseHeight;
            public float MoveAccel;
            public float Friction = 0.25f;
            public float RubberbandStrength = 3.0f;
            public int OppositeLeg;
        }
        public object Clone() {
            return base.Clone(new MultiPedalSettings {
                Legs = Legs,
                AnimControl = AnimControl,
                OverrideAnimByDefault = OverrideAnimByDefault
            });
        }

        public override void Preset(uint EntityType, BehaviorEntity.AnimalSetting setting) {
            base.Preset(EntityType, setting);
        }
    }

    public class MultiPedalBehavior : IBehavior {
        private MultiPedalSettings settings;
        private LeadHeadBehavior LeadHead;
        private AnimatedBehavior animated;
        
        [JsonProperty] private Leg[] appendages;
        private IAttackable selfAtk;
        private uint LastOverrideState;

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(MultiPedalSettings), new MultiPedalSettings());
        }

        
        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: MultiPedal Behavior Requires AnimalSettings to have MultiPedalSettings");
            if (!self.Is(out LeadHead)) 
                throw new Exception("Entity: MultiPedal Behavior Requires AnimalSettings to have LeadHeadBehavior");
            if (!self.Is(out selfAtk)) 
                throw new Exception("Entity: MultiPedal Behavior Requires AnimalSettings to have IAttackable");
            if (!self.Is(out animated)) animated = null;
            SetUp(self);
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: MultiPedal Behavior Requires AnimalSettings to have MultiPedalSettings");
            if (!self.Is(out LeadHead)) 
                throw new Exception("Entity: MultiPedal Behavior Requires AnimalSettings to have LeadHeadBehavior");
            if (!self.Is(out selfAtk)) 
                throw new Exception("Entity: MultiPedal Behavior Requires AnimalSettings to have IAttackable");
            if (!self.Is(out animated)) animated = null;
            SetUp(self);
        }


        private void SetUp(BehaviorEntity.Animal self) {
            List<MultiPedalSettings.LegSettings> lSettings = settings.Legs.value;
            appendages ??= new Leg[lSettings.Count];
            Transform root = self.controller.gameObject.transform;
            for(int i = 0; i < lSettings.Count; i++){
                if(appendages[i] == null) {
                    appendages[i] = new Leg();
                    appendages[i].Initialize(self, lSettings[i], root);
                } else appendages[i].Deserialize(self, lSettings[i], root);
                this.appendages[i].SetConstraint<TwoBoneIKConstraint>();
            }
            this.LastOverrideState = 0;
        }

        public void Update(BehaviorEntity.Animal self) {
            foreach(Leg l in appendages) {
                if (selfAtk.IsDead) l.Update(self);
                else l.UpdateMovement(self);
                ApplyRubberBand(l.desiredBody, l.LegLength);
            };
        }

        public void ApplyRubberBand(float3 desiredBody, float LegLength) {
            // PD controller parameters (tune as needed)
            float kpBody = LeadHead.settings.BodyRubberBand;
            float kdBody = 2.0f * math.sqrt(kpBody);
            float kpHead = LeadHead.settings.HeadRubberBand;
            float kdHead = 2.0f * math.sqrt(kpHead);

            float3 toBody = desiredBody - LeadHead.BodyPosition;
            float dist = math.length(toBody);
            if (dist > LegLength) {
                dist -= LegLength;
                float3 dir = math.normalizesafe(toBody);
                // PD spring force for body to leg
                float3 spring = kpBody * dist * dir;
                float3 damping = -kdBody * LeadHead.BodyCollider.transform.velocity;
                LeadHead.BodyCollider.transform.velocity += (spring + damping) * EntityJob.cxt.deltaTime;
            }

            float3 desiredHead = desiredBody + LeadHead.settings.Offset;
            float3 toHead = desiredHead - LeadHead.HeadPosition;
            float3 springH = kpHead * toHead;
            float3 dampingH = -kdHead * LeadHead.HeadCollider.transform.velocity;
            LeadHead.HeadCollider.transform.velocity += (springH + dampingH) * EntityJob.cxt.deltaTime;
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
            foreach(Leg l in appendages) {
                if (l.State == Leg.StepState.Stand) Gizmos.color = Color.green;
                else if (l.State == Leg.StepState.Raise) Gizmos.color = Color.red;
                else Gizmos.color = Color.blue;
                Gizmos.DrawWireCube(CPUMapManager.GSToWS(
                    l.collider.transform.position + l.collider.transform.size/2),
                    l.collider.transform.size * 2);
                Gizmos.DrawSphere(CPUMapManager.GSToWS(l.TargetPosition), 0.25f);
            }
        }

        public void Disable(BehaviorEntity.Animal self) {
            if (appendages == null) return;
            foreach(Leg l in appendages) l.Disable();
        }


        private class Leg : LeadHeadBehavior.Appendage {
            [JsonIgnore]
            private MultiPedalBehavior mp;
            [JsonIgnore]
            public MultiPedalSettings.LegSettings settings;
            [JsonProperty]
            public float3 TargetPosition;
            public StepState State;

            [JsonIgnore] public float LegLength => collider.transform.size.y;
            [JsonIgnore] public float3 desiredBody => restPos - math.mul(mp.LeadHead.BodyCollider.transform.rotation, settings.RestOffset);
            [JsonIgnore] private float3 restPos => collider.transform.position + new float3(collider.transform.size.x, 0, collider.transform.size.z) / 2;
            public enum StepState {
                Stand,
                Raise,
                Lower
            }
            [JsonConstructor]
            public Leg() {} //Newtonsoft path
            public override void Initialize(BehaviorEntity.Animal self, LeadHeadBehavior.AppendageSettings settings, Transform root) {
                this.settings = settings as MultiPedalSettings.LegSettings;
                if(!self.Is(out mp)) throw new Exception("Entity: MultiPedalBehavior Leg expected entity to be MultiPedalBehavior");
                this.collider = new(settings.collider, mp.LeadHead.BodyPosition + settings.RestOffset) {
                    useGravity = true
                };

                base.Initialize(self, settings, root);
                SetConstraint<TwoBoneIKConstraint>();

                active = true;
                State = StepState.Stand;
            }

            public override void Deserialize(BehaviorEntity.Animal self, LeadHeadBehavior.AppendageSettings settings, Transform root) {
                this.settings = settings as MultiPedalSettings.LegSettings;
                if(!self.Is(out mp)) throw new Exception("Entity: MultiPedalBehavior Leg expected entity to be MultiPedalBehavior");

                base.Deserialize(self, settings, root);
                SetConstraint<TwoBoneIKConstraint>();

                active = true;
            }


            public void Update(BehaviorEntity.Animal self) {
                float3 origin = mp.LeadHead.BodyPosition  + math.mul(mp.LeadHead.BodyCollider.transform.rotation, settings.RestOffset);
                float dist = math.distance(origin, restPos);
                if (dist > LegLength) { //Rubber banding
                    float3 dir = math.normalizesafe(origin - restPos);
                    float strength = math.pow(dist, settings.RubberbandStrength);
                    collider.transform.velocity += strength * EntityJob.cxt.deltaTime * dir; 
                    State = StepState.Stand;
                }

                base.Update();
            }

            public void UpdateMovement(BehaviorEntity.Animal self) {
                //Only try to raise leg if the opposite leg is planted in the ground
                bool CanStartStep = mp.appendages[settings.OppositeLeg].State == StepState.Stand;
                float3 origin = mp.LeadHead.BodyPosition + math.mul(mp.LeadHead.BodyCollider.transform.rotation, settings.RestOffset);
                this.collider.useGravity = State != StepState.Raise;
                Update(self);

                this.collider.transform.velocity *= 1 - settings.Friction;
                if (State == StepState.Stand){
                    if (CPUMapManager.RayCastTerrain(origin, Vector3.down, 2*settings.collider.size.y, 
                        CPUMapManager.RayTestSolid, out float3 hit)) {
                        TargetPosition = hit;
                    } else TargetPosition = origin + (float3)(Vector3.down * settings.collider.size.y * 2);
                    if(CanStartStep && math.distance(TargetPosition, restPos) > settings.StepDistThreshold) State = StepState.Raise;
                } 
                if ( State == StepState.Lower) {
                    if (collider.SampleCollision(collider.transform.position + (float3)(0.05f * Vector3.down),
                        collider.transform.size, EntityJob.cxt.mapContext, out float3 gDir))
                        State = StepState.Stand;
                    if (math.distance(TargetPosition, restPos) < 0.05f)
                        State = StepState.Stand;
                }  if (State == StepState.Stand) return;

                float3 aim = float3.zero;
                if (State == StepState.Raise) { //Otherwise go above the target
                    if (restPos.y > TargetPosition.y + settings.LegRaiseHeight)
                        State = StepState.Lower;
                    float3 target = TargetPosition + (float3)(Vector3.up * settings.LegRaiseHeight);
                    aim = target - restPos;
                } if (State == StepState.Lower) aim = TargetPosition - restPos;

                aim = math.normalizesafe(aim);
                collider.transform.velocity += settings.MoveAccel * EntityJob.cxt.deltaTime * aim;
            }
        }
    }
}