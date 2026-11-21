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
using Arterra.Utils;

namespace Arterra.Data.Entity.Behavior {
    public class MultiPedalSettings : PASettings, IBehaviorSetting {
        public Option<List<LegSettings>> Legs;
        public Option<List<EntitySMTasks>> NonRotatingStates = new() { value = new() };
        [Range(0f, 60f)] public float MaxSlopeTilt = 35f;
        [Min(0f)] public float SlopeTiltResponse = 8f;

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
                NonRotatingStates = NonRotatingStates,
                MaxSlopeTilt = MaxSlopeTilt,
                SlopeTiltResponse = SlopeTiltResponse,
                AnimControl = AnimControl,
                OverrideAnimByDefault = OverrideAnimByDefault
            });
        }

        public override void Preset(uint EntityType, BehaviorEntity.AnimalSetting setting) {
            base.Preset(EntityType, setting);
        }
    }

    public class MultiPedalBehavior : SpeciesBehavior {
        private const float GroundContactDepth = 0.05f;
        private const float MinSupportSpread = 1e-4f;

        private MultiPedalSettings settings;
        private LeadHeadBehavior LeadHead;
        private AnimatedBehavior animated;
        private Vector3 supportUp = Vector3.up;
        private Vector3 targetSupportUp = Vector3.up;
        
        [JsonProperty] private Leg[] appendages;
        private IVitality selfAtk;
        private uint LastOverrideState;

        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.LeadHead, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(MultiPedalSettings), new MultiPedalSettings());
        }

        
        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: MultiPedal Behavior Requires AnimalSettings to have MultiPedalSettings");
            if (!self.Is(out LeadHead)) 
                throw new Exception("Entity: MultiPedal Behavior Requires AnimalSettings to have LeadHeadBehavior");
            if (!self.Is(out selfAtk)) 
                throw new Exception("Entity: MultiPedal Behavior Requires AnimalSettings to have IAttackable");
            if (!self.Is(out animated)) animated = null;
            SetUp(self);
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
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
                    appendages[i] = new Leg(i);
                    appendages[i].Initialize(self, lSettings[i], root);
                } else appendages[i].Deserialize(self, lSettings[i], root);
                this.appendages[i].SetConstraint<TwoBoneIKConstraint>();
            }
            this.LastOverrideState = 0;
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.Simulation) CoreUpdate(self);
            if (self.context == BehaviorEntity.UpdateContext.Visual) ControllerUpdate(self);
        }

        private void CoreUpdate(BehaviorEntity.Animal self) {
            foreach (Leg leg in appendages) {
                if (selfAtk.IsDead) leg.Update(self);
                else leg.UpdateMovement(self);
            }

            ApplyRubberBands(self.DeltaTime);
        }

        internal Quaternion GetRigRotation(BehaviorEntity.Animal self, Quaternion heading) {
            if (selfAtk.IsDead || settings.MaxSlopeTilt <= 0f || settings.SlopeTiltResponse <= 0f)
                return heading;

            if (settings.NonRotatingStates.value is { Count: > 0 } states
                && self.Is(out StateMachineManagerBehavior manager) && states.Contains(manager.TaskIndex)) {
                supportUp = Vector3.up;
                targetSupportUp = Vector3.up;
                return heading;
            }

            if (TryGetFootPlaneNormal(out Vector3 normal)) {
                float maxTilt = Mathf.Min(settings.MaxSlopeTilt, 60f);
                targetSupportUp = Vector3.RotateTowards(Vector3.up, normal,
                    maxTilt * Mathf.Deg2Rad, 0f);
            }

            float blend = 1f - Mathf.Exp(-settings.SlopeTiltResponse * self.DeltaTime);
            supportUp = Vector3.Slerp(supportUp, targetSupportUp, blend);

            Vector3 forward = heading * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) return heading;

            // Solve for the vertical component on the plane while keeping the horizontal heading.
            forward.y = -Vector3.Dot(forward, supportUp) / supportUp.y;
            return Quaternion.LookRotation(forward, supportUp);
        }

        private bool TryGetFootPlaneNormal(out Vector3 normal) {
            float3 bodyPosition = LeadHead.BodyPosition;
            int count = 0;
            float3 sum = float3.zero;
            float3 squares = float3.zero;
            float3 products = float3.zero;

            foreach (Leg leg in appendages) {
                if (leg.State == Leg.StepState.Raise || leg.State == Leg.StepState.SearchRaise
                    || leg.IsRecovering) continue;

                float3 offset = leg.FootPosition - bodyPosition;
                count++;
                sum += offset;
                squares += offset * offset;
                products += offset.xxy * offset.yzz;
            }

            normal = Vector3.up;
            if (count < 3) return false;

            // Least-squares fit of height as a function of horizontal position.
            float3 variance = squares - sum * sum / count;
            float3 covariance = products - sum.xxy * sum.yzz / count;
            float determinant = variance.x * variance.z - covariance.y * covariance.y;
            float spread = variance.x + variance.z;
            if (determinant <= MinSupportSpread * spread * spread) return false;

            float slopeX = (variance.z * covariance.x - covariance.y * covariance.z) / determinant;
            float slopeZ = (variance.x * covariance.z - covariance.y * covariance.x) / determinant;
            normal = math.normalize(new float3(-slopeX, 1f, -slopeZ));
            return true;
        }

        private void ControllerUpdate(BehaviorEntity.Animal self) {
            if (self.controller.gameObject == null) return;
            if (animated == null) return;

            foreach(var c in appendages) c.UpdateJobSync();
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

        private void ApplyRubberBands(float deltaTime) {
            float3 bodyPosition = LeadHead.BodyPosition;
            float3 headPosition = LeadHead.HeadPosition;
            float3 headOffset = math.mul(LeadHead.HeadCollider.transform.rotation, LeadHead.settings.Offset);
            float3 bodyDisplacement = float3.zero;
            float3 headDisplacement = float3.zero;
            float3 bodyFootVelocitySum = float3.zero;
            float3 headFootVelocitySum = float3.zero;
            int stretchedLegCount = 0;
            int headLegCount = 0;

            foreach (Leg leg in appendages) {
                if (leg.State == Leg.StepState.SearchRaise || leg.IsRecovering) continue;

                float3 desiredBody = leg.desiredBody;
                float3 footVelocity = leg.velocity;
                headDisplacement += desiredBody + headOffset - headPosition;
                headFootVelocitySum += footVelocity;
                headLegCount++;

                float3 toDesiredBody = desiredBody - bodyPosition;
                float distance = math.length(toDesiredBody);
                if (distance > leg.LegLength) {
                    bodyDisplacement += toDesiredBody * ((distance - leg.LegLength) / distance);
                    bodyFootVelocitySum += footVelocity;
                    stretchedLegCount++;
                }
            }

            ApplySpring(LeadHead.BodyCollider, LeadHead.settings.BodyRubberBand,
                bodyDisplacement, bodyFootVelocitySum, stretchedLegCount, deltaTime);
            if (headLegCount == 0) {
                ApplySpring(LeadHead.HeadCollider, LeadHead.settings.HeadRubberBand,
                    bodyPosition + headOffset - headPosition, LeadHead.BodyCollider.transform.velocity, 1, deltaTime);
                return;
            }

            ApplySpring(LeadHead.HeadCollider, LeadHead.settings.HeadRubberBand,
                headDisplacement, headFootVelocitySum, headLegCount, deltaTime);
        }

        private static void ApplySpring(TerrainCollider collider, float stiffness, float3 displacement,
            float3 footVelocitySum, int legCount, float deltaTime) {
            if (legCount == 0) return;

            float3 velocity = collider.transform.velocity;
            float3 relativeVelocity = velocity - footVelocitySum / legCount;
            // Use the combined stiffness of the contributing springs for a single damping term.
            float damping = 2f * math.sqrt(stiffness * legCount);
            collider.transform.velocity = velocity + (stiffness * displacement - damping * relativeVelocity) * deltaTime;
        }

        public void UpdateController(BehaviorEntity.Animal self, BehaviorEntity.AnimalController controller) {
            if (controller.gameObject == null) return;
            if (animated == null) return;

            foreach(var c in appendages) c.UpdateJobSync();
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

        public override void OnDrawGizmos(BehaviorEntity.Animal self) {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(CPUMapManager.GSToWS(LeadHead.HeadPosition), LeadHead.settings.Collider.size * 2);
            foreach (Leg leg in appendages) {
                Gizmos.color = leg.State switch {
                    Leg.StepState.Stand => Color.green,
                    Leg.StepState.Raise => Color.red,
                    Leg.StepState.Lower => Color.blue,
                    Leg.StepState.SearchRaise => Color.yellow,
                    Leg.StepState.SearchDown => Color.cyan,
                    Leg.StepState.Recover => Color.magenta,
                    Leg.StepState.Retract => new Color(1f, 0.5f, 0f),
                    _ => Color.white
                };
                Gizmos.DrawWireCube(CPUMapManager.GSToWS(
                    (float3)leg.collider.transform.position + leg.collider.transform.size / 2),
                    leg.collider.transform.size * 2);
                Gizmos.DrawSphere(CPUMapManager.GSToWS(leg.TargetPosition), 0.25f);
            }
        }

        public override void Disable(BehaviorEntity.Animal self) {
            if (appendages == null) return;
            foreach(Leg l in appendages) l?.Disable();
        }


        private class Leg : LeadHeadBehavior.Appendage {
            private const string LegGravityStateName = "MultiPedalBehavior::Leg";
            private const float OverextendedPlantDuration = 0.35f;
            private const float StallDuration = 0.5f;
            private const float BlockedStepRetryDelay = 0.5f;
            private const float RetractionSpeed = 4f;

            [JsonIgnore] private MultiPedalBehavior mp;
            [JsonIgnore] public MultiPedalSettings.LegSettings settings;
            [JsonProperty] public float3 TargetPosition;
            public StepState State;
            [JsonProperty] private int index;
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)] private float3 retractionOffset;
            private float overextendedPlantTime;
            private float stallTime;
            private float bestDistance;
            private float stepRetryTime;

            [JsonIgnore] public float LegLength => collider.transform.size.y;
            [JsonIgnore] public float3 desiredBody => FootPosition - RotatedRestOffset;
            private float3 RotatedRestOffset => math.mul(mp.LeadHead.HeadCollider.transform.rotation, settings.RestOffset);
            [JsonIgnore] public float3 FootPosition =>
                (float3)collider.transform.position + new float3(collider.transform.size.x, 0f, collider.transform.size.z) / 2f;
            [JsonIgnore] public bool IsRecovering => State == StepState.Recover || State == StepState.Retract;
            [JsonIgnore] public override bool ParticipatesInEntityCollisions => State != StepState.Retract;

            private bool IsRaising => State == StepState.Raise || State == StepState.SearchRaise;
            private bool IsLowering => State == StepState.Lower || State == StepState.SearchDown;
            private float StanceRadius => math.max(LegLength, math.length(settings.RestOffset));
            private float MaxFootReach => StanceRadius + LegLength;

            private bool IsGrounded() {
                float3 size = collider.transform.size;
                return TerrainCollider.SampleCollision((float3)collider.transform.position,
                    new float3(size.x, -GroundContactDepth, size.z));
            }

            private bool TryGetGroundBelow(float3 origin, out float3 point) {
                float minY = origin.y - 2f * LegLength;
                float3 probeOrigin = origin;
                probeOrigin.y = math.clamp(mp.LeadHead.BodyOrigin.y + GroundContactDepth, minY, origin.y);
                return Terraforming.TryFindVerticalGround(probeOrigin, minY, origin.y + LegLength,
                    Terraforming.RayTestSolid, out point);
            }

            private float GetLiftClearance(float3 foot, float rise) {
                if (rise <= GroundContactDepth) return rise;

                float3 size = collider.transform.size;
                float3 top = foot + math.up() * size.y;
                if (TerrainCollider.SampleCollision(top - new float3(size.x, 0f, size.z) * 0.5f,
                    new float3(size.x, 0f, size.z)))
                    return 0f;

                if (Terraforming.RectangularCastTerrain(top, Vector3.up,
                    new float3(size.x * 0.5f, 0f, 0f), new float3(0f, 0f, size.z * 0.5f),
                    rise + GroundContactDepth, Terraforming.RayTestSolid, out float3 hit))
                    return math.clamp(hit.y - top.y - GroundContactDepth, 0f, rise);

                return rise;
            }

            public enum StepState {
                Stand,
                Raise,
                Lower,
                SearchRaise,
                SearchDown,
                Recover,
                Retract
            }
            [JsonConstructor]
            public Leg() {} //Newtonsoft path
            public Leg(int index) { this.index = index; } //Newtonsoft path

            public override void Initialize(BehaviorEntity.Animal self, LeadHeadBehavior.AppendageSettings settings, Transform root) {
                this.settings = settings as MultiPedalSettings.LegSettings;
                if(!self.Is(out mp)) throw new Exception("Entity: MultiPedalBehavior Leg expected entity to be MultiPedalBehavior");
                this.collider = new(settings.collider, mp.LeadHead.BodyPosition + settings.RestOffset, $"MultiPedal+{index}");
                this.collider.SetGravityState(LegGravityStateName, TerrainCollider.GravityPriority.Override, true);
                
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
                if (IsRecovering) {
                    State = StepState.Stand;
                    retractionOffset = float3.zero;
                }
                UpdateFoot(self, mp.LeadHead.BodyPosition + RotatedRestOffset,
                    planted: false, canExtend: IsLowering || State == StepState.SearchRaise);
            }

            private void UpdateFoot(BehaviorEntity.Animal self, float3 origin, bool planted, bool canExtend) {
                if (planted) {
                    collider.transform.velocity = float3.zero;
                } else {
                    float3 toRest = origin - FootPosition;
                    float distance = math.length(toRest);
                    if (distance > LegLength) {
                        float stretch = canExtend ? distance - LegLength : distance;
                        float strength = math.pow(stretch, settings.RubberbandStrength);
                        collider.transform.velocity += strength * self.DeltaTime * toRest / distance;
                        if (!canExtend) State = StepState.Stand;
                    }
                }

                base.Update();
            }

            public void UpdateMovement(BehaviorEntity.Animal self) {
                float3 bodyPosition = mp.LeadHead.BodyPosition;
                float deltaTime = self.DeltaTime;
                if (TryUpdateRetraction(self, bodyPosition, deltaTime)) return;

                float3 origin = bodyPosition + RotatedRestOffset;
                if (State == StepState.Recover) {
                    UpdateRecovery(self, bodyPosition, origin, deltaTime);
                    return;
                }

                Leg oppositeLeg = mp.appendages[settings.OppositeLeg];
                bool canStartStep = oppositeLeg.State == StepState.Stand || oppositeLeg.IsRecovering;
                bool planted = State == StepState.Stand && IsGrounded();
                collider.SetGravityState(LegGravityStateName, TerrainCollider.GravityPriority.Override, !IsRaising);
                UpdateFoot(self, origin, planted, canExtend: State != StepState.Stand);
                if (TryUpdateRetraction(self, bodyPosition, deltaTime)) return;

                collider.transform.velocity *= 1f - settings.Friction;
                if (State == StepState.Stand) stepRetryTime = math.max(0f, stepRetryTime - deltaTime);
                if (State == StepState.Stand && canStartStep && stepRetryTime == 0f) {
                    stallTime = 0f;
                    float3 foot = FootPosition;
                    float footRadius = math.cmin(collider.transform.size.xz) * 0.5f;
                    if (TryGetGroundBelow(origin, out float3 hit)) {
                        TargetPosition = hit;
                        float stepDistance = math.distance(hit, foot);
                        float restDistance = math.distance(origin, foot);
                        bool relievesReachLimit = planted && restDistance > LegLength
                            && math.distancesq(hit.xz, foot.xz) > footRadius * footRadius
                            && math.distance(origin, hit) < restDistance;
                        if (stepDistance > settings.StepDistThreshold || relievesReachLimit)
                            State = StepState.Raise;
                    } else {
                        if (math.distancesq(origin.xz, foot.xz) > footRadius * footRadius || !IsGrounded()) {
                            TargetPosition = new float3(origin.x, foot.y, origin.z);
                            State = StepState.SearchRaise;
                        }
                    }
                }

                if (State == StepState.Stand && planted && math.distance(origin, FootPosition) > LegLength + GroundContactDepth)
                    overextendedPlantTime += deltaTime;
                else
                    overextendedPlantTime = 0f;

                if (overextendedPlantTime >= OverextendedPlantDuration) {
                    BeginRecovery(origin);
                    return;
                }

                if (IsLowering) {
                    if (IsGrounded()
                        || (State == StepState.Lower && math.distance(TargetPosition, FootPosition) < 0.05f))
                        State = StepState.Stand;
                }
                if (State == StepState.Stand) return;

                float3 footPosition = FootPosition;
                float3 aim = TargetPosition - footPosition;
                if (IsRaising) {
                    float3 headUp = math.mul(mp.LeadHead.HeadCollider.transform.rotation, math.up());
                    float3 lift = headUp * settings.LegRaiseHeight;
                    float landingClearance = GetLiftClearance(TargetPosition, lift.y);
                    bool lowClearance = landingClearance < lift.y;
                    if (lowClearance) lift *= landingClearance / lift.y;

                    float3 liftAim = aim + lift;
                    float clearance = GetLiftClearance(footPosition, liftAim.y);
                    lowClearance |= clearance < liftAim.y;
                    if (lowClearance) {
                        liftAim.xz = aim.xz;
                        liftAim.y = clearance;
                    }

                    float footRadius = math.cmin(collider.transform.size.xz) * 0.5f;
                    bool readyToLower = math.dot(liftAim, headUp) <= GroundContactDepth
                        && (!lowClearance || math.lengthsq(aim.xz) <= footRadius * footRadius);
                    if (readyToLower) {
                        State = State == StepState.SearchRaise ? StepState.SearchDown : StepState.Lower;
                    } else {
                        if (RecoverIfLiftStalled(origin, math.length(liftAim), deltaTime)) return;

                        aim = liftAim;
                    }
                }

                if (State == StepState.SearchDown) aim.y = -LegLength;

                aim = math.normalizesafe(aim);
                collider.transform.velocity += settings.MoveAccel * deltaTime * aim;
            }

            private bool RecoverIfLiftStalled(float3 origin, float distance, float deltaTime) {
                if (!HasStalled(distance, deltaTime)) return false;

                BeginRecovery(origin);
                stepRetryTime = BlockedStepRetryDelay;
                return true;
            }

            private bool HasStalled(float distance, float deltaTime) {
                if (stallTime == 0f || bestDistance - distance >= GroundContactDepth) {
                    bestDistance = distance;
                    stallTime = 0f;
                }

                stallTime += deltaTime;
                return stallTime >= StallDuration;
            }

            private void BeginRecovery(float3 origin) {
                overextendedPlantTime = 0f;
                stallTime = 0f;
                TargetPosition = origin;
                State = StepState.Recover;
            }

            private void UpdateRecovery(BehaviorEntity.Animal self, float3 bodyPosition, float3 origin, float deltaTime) {
                collider.SetGravityState(LegGravityStateName, TerrainCollider.GravityPriority.Override, true);
                base.Update();
                if (TryUpdateRetraction(self, bodyPosition, deltaTime)) return;

                collider.transform.velocity *= 1f - settings.Friction;

                TargetPosition = origin;
                float3 foot = FootPosition;
                float3 aim = origin - foot;
                bool grounded = IsGrounded();
                float distance = math.length(aim);
                if (distance <= LegLength) {
                    State = grounded ? StepState.Stand : StepState.SearchDown;
                    return;
                }

                if (HasStalled(distance, deltaTime)) {
                    BeginRetraction(foot - bodyPosition);
                    UpdateRetraction(self, bodyPosition, deltaTime);
                    return;
                }

                if (grounded && aim.y < 0f) {
                    // Move across the ledge before trying to descend through it.
                    float footRadius = math.cmin(collider.transform.size.xz) * 0.5f;
                    if (math.lengthsq(aim.xz) <= footRadius * footRadius)
                        aim.xz = bodyPosition.xz - foot.xz;
                    aim.y = 0f;
                }

                collider.transform.velocity += settings.MoveAccel * deltaTime * math.normalizesafe(aim);
            }

            private bool TryUpdateRetraction(BehaviorEntity.Animal self, float3 bodyPosition, float deltaTime) {
                if (State != StepState.Retract) {
                    float3 offset = FootPosition - bodyPosition;
                    float maxReach = MaxFootReach;
                    if (math.lengthsq(offset) <= maxReach * maxReach) return false;

                    BeginRetraction(offset);
                }

                UpdateRetraction(self, bodyPosition, deltaTime);
                return true;
            }

            private void BeginRetraction(float3 footOffset) {
                float distance = math.length(footOffset);
                retractionOffset = distance > MaxFootReach ? footOffset * (MaxFootReach / distance) : footOffset;
                stallTime = 0f;
                overextendedPlantTime = 0f;
                stepRetryTime = BlockedStepRetryDelay;
                State = StepState.Retract;
            }

            private void UpdateRetraction(BehaviorEntity.Animal self, float3 bodyPosition, float deltaTime) {
                // Use the collider center so the retracted foot fits inside the body's clearance.
                float3 centerOffset = new(0f, -LegLength * 0.5f, 0f);
                retractionOffset = Vector3.MoveTowards(retractionOffset, centerOffset, RetractionSpeed * deltaTime);
                TargetPosition = bodyPosition + centerOffset;

                float3 foot = bodyPosition + retractionOffset;
                float3 size = collider.transform.size;
                float3 colliderOrigin = foot - new float3(size.x, 0f, size.z) * 0.5f;
                collider.transform.position = colliderOrigin;
                collider.transform.velocity = float3.zero;
                UpdateSpatialBounds();

                float stanceRadius = StanceRadius;
                if (math.lengthsq(retractionOffset) > stanceRadius * stanceRadius
                    || TerrainCollider.SampleCollision(colliderOrigin, size) || collider.HasEntityOverlap(self))
                    return;

                retractionOffset = float3.zero;
                TargetPosition = foot;
                collider.SetGravityState(LegGravityStateName, TerrainCollider.GravityPriority.Override, true);
                State = IsGrounded() ? StepState.Stand : StepState.SearchDown;
            }
        }
    }
}
