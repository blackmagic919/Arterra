using System;
using System.Collections.Generic;
using System.Collections;
using Arterra.Configuration;
using Arterra.Core.Events;
using Arterra.Data.Entity;
using Arterra.Engine.Terrain;
using Arterra.GamePlay;
using Arterra.Utils;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    /// <summary>
    /// Settings that control how player animation/effect hooks resolve scene objects.
    /// </summary>
    [Serializable]
    public class PlayerEffectsSettings : IBehaviorSetting {
        public string AnimatorPath = "Player";

        public object Clone() {
            return new PlayerEffectsSettings {
                AnimatorPath = AnimatorPath,
            };
        }
    }

    /// <summary>
    /// Bridges gameplay events and movement state into animator parameters and triggers.
    /// </summary>
    public class PlayerEffectsBehavior : IBehavior {
        [JsonIgnore] public static PlayerEffectsBehavior Active { get; private set; }
        [JsonIgnore] public PlayerEffectsSettings settings;

        private PhysicalitySetting physicality;
        private BehaviorEntity.Animal self;
        private Animator animator;
        private GameObject heldItem;
        private bool isSwimming;
        private bool isShaking;

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> hierarchy) {
            hierarchy.TryAdd(typeof(PlayerEffectsSettings), new PlayerEffectsSettings());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: PlayerEffectsBehavior requires PlayerEffectsSettings");
            if (!setting.Is(out physicality)) physicality = null;

            this.self = self;
            Active = this;
            self.Register(this);
            ResolveAnimator(self);
            isSwimming = false;
            isShaking = false;
            BindEvents(self);
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: PlayerEffectsBehavior requires PlayerEffectsSettings");
            if (!setting.Is(out physicality)) physicality = null;

            this.self = self;
            Active = this;
            self.Register(this);
            ResolveAnimator(self);
            isSwimming = false;
            isShaking = false;
            BindEvents(self);
        }

        public void Disable(BehaviorEntity.Animal self) {
            if (ReferenceEquals(Active, this)) {
                Active = null;
            }

            if (ReferenceEquals(this.self, self)) {
                this.self = null;
            }

            if (heldItem != null) {
                GameObject.Destroy(heldItem);
                heldItem = null;
            }

            animator = null;
        }

        public void Update(BehaviorEntity.Animal self) {
            if (animator == null) return;
            if (self.context == BehaviorEntity.UpdateContext.Job)
                return;
            if (self.context == BehaviorEntity.UpdateContext.Fixed)
                return;

            float speed = Mathf.Lerp(animator.GetFloat("Speed"), math.length(self.velocity), 0.35f);
            animator.SetFloat("Speed", speed);
        }

        public void PlayAnimatorMove(float3 normVel) {
            if (animator == null) return;

            float speed = Mathf.Lerp(animator.GetFloat("Speed"), (float)math.length(normVel), 0.35f);
            float angleTheta = Mathf.Atan2(normVel.z, normVel.x) / Mathf.PI;
            angleTheta = Mathf.Lerp(animator.GetFloat("MoveTheta"), angleTheta, 0.35f);

            animator.SetFloat("Speed", speed);
            animator.SetFloat("MoveTheta", angleTheta);
        }

        private void ResolveAnimator(BehaviorEntity.Animal self) {
            if (self.controller == null || self.controller.transform == null) {
                animator = null;
                return;
            }

            if (!string.IsNullOrEmpty(settings.AnimatorPath)) {
                Transform anchor = self.controller.transform.Find(settings.AnimatorPath);
                if (anchor != null) {
                    animator = anchor.GetComponent<Animator>();
                }
            }

            if (animator == null) {
                animator = self.controller.transform.GetComponentInChildren<Animator>();
            }
        }

        private void BindEvents(BehaviorEntity.Animal self) {
            // Keep animator states in sync with entity events raised by movement/interaction behaviors.
            self.eventCtrl.AddEventHandler(GameEvent.Action_Jump, (_, _, __) => SetBool("IsJumping", true));
            self.eventCtrl.AddEventHandler(GameEvent.Item_ConsumeFood, (_, _, __) => SetTrigger("Eat"));
            self.eventCtrl.AddEventHandler(GameEvent.Action_RemoveTerrain, (_, _, __) => SetTrigger("Place"));
            self.eventCtrl.AddEventHandler(GameEvent.Action_PlaceTerrain, (_, _, __) => SetTrigger("Place"));
            self.eventCtrl.AddEventHandler(GameEvent.Action_LookDirect, (_, _, cxt) => PlayLookDirect(cxt));
            self.eventCtrl.AddEventHandler(GameEvent.System_Deserialize, PlayDeserialize);
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Death, (_, _, __) => SetBool("IsDead", true));
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Damaged, PlayDamaged);
            self.eventCtrl.AddEventHandler(GameEvent.Entity_HitGround, PlayTouchdown);
            self.eventCtrl.AddEventHandler(GameEvent.Entity_InLiquid, PlayStartSwim);
            self.eventCtrl.AddEventHandler(GameEvent.Entity_InGas, PlayStopSwim);
            self.eventCtrl.AddEventHandler(GameEvent.Action_MountRideable, (_, _, __) => {
                SetBool("IsSitting", true);
                SetBool("IsJumping", false);
                SetBool("IsSwimming", false);
            });
            self.eventCtrl.AddEventHandler(GameEvent.Action_DismountRideable, (_, _, __) => SetBool("IsSitting", false));
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Attack, (_, _, __) => SetTrigger(UnityEngine.Random.Range(0, 2) == 0 ? "PunchR" : "PunchL"));
            self.eventCtrl.AddEventHandler(GameEvent.Item_HoldTool, (_, _, cxt) => PlayHoldTool(cxt));
            self.eventCtrl.AddEventHandler(GameEvent.Item_UnholdTool, (_, _, __) => PlayUnholdTool());
            self.eventCtrl.AddEventHandler(GameEvent.Item_UseTool, (_, _, cxt) => {
                if (cxt is string anim) SetTrigger(anim);
            });
            self.eventCtrl.AddEventHandler(GameEvent.Item_DrawBow, (_, _, __) => SetDrawingBow(true));
            self.eventCtrl.AddEventHandler(GameEvent.Item_ReleaseBow, (_, _, __) => SetDrawingBow(false));
            self.eventCtrl.AddEventHandler(GameEvent.Action_LookGradual, (_, _, cxt) => {
                if (cxt is not RefTuple<(float, float)> look) return;
                SetBool("DisableShuffle", false);
                SetFloat("LookY", look.Value.Item1);
                float yaw = GetFloat("LookX");
                yaw = (yaw + look.Value.Item2) * 0.75f;
                SetFloat("LookX", yaw);
            });
        }

        private void PlayDeserialize(object source, object target, object cxt) {
            if (source is not Entity entity) return;
            if (!entity.Is(out IAttackable attackable)) return;
            if (!attackable.IsDead) return;
            SetBool("IsDead", true);
        }

        private void PlayStartSwim(object source, object target, object density) {
            SetBool("IsSwimming", true);
            SetBool("IsJumping", false);
            SetBool("IsSitting", false);

            if (isSwimming) return;
            isSwimming = true;

            if (source is Entity entity) {
                Indicators.PlayWaterSplash(entity, physicality.weight);
            }
        }

        private void PlayStopSwim(object source, object target, object density) {
            SetBool("IsSwimming", false);

            if (!isSwimming) return;
            isSwimming = false;

            if (source is Entity entity) {
                Indicators.PlayWaterSplash(entity, physicality.weight);
            }
        }

        private void PlayTouchdown(object source, object target, object cxt) {
            if (source is not Entity entity) return;
            if (!entity.Is(out IAttackable attackable)) return;
            if (attackable.IsDead) return;

            if (animator == null) return;
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            if (!state.IsName("hRig_fall")) return;
            SetBool("IsJumping", false);
        }

        private void PlayLookDirect(object cxt) {
            if (cxt is not RefTuple<(float, float)> look) return;
            SetBool("DisableShuffle", true);
            SetFloat("LookY", look.Value.Item1);
            SetFloat("LookX", look.Value.Item2);
        }

        private void PlayDamaged(object source, object target, object cxt) {
            if (source is not Entity entity) return;
            if (!entity.Is(out IAttackable attackable)) return;
            if (attackable.IsDead) return;
            if (cxt is not RefTuple<(float, float3)> damaged) return;

            if (!isShaking) {
                OctreeTerrain.MainCoroutines.Enqueue(CameraShake(0.2f, 0.25f));
            }

            quaternion wsToOs = math.inverse(PlayerHandler.data.transform.rotation);
            float3 knockback = math.mul(wsToOs, damaged.Value.Item2);
            if (math.lengthsq(knockback) < 1E-6f) return;
            knockback = math.normalize(knockback);

            SetFloat("KnockZ", knockback.z);
            float xStr = 1 - math.abs(knockback.z);
            SetFloat("KnockX", knockback.x * xStr);
            float yStr = (1 - math.abs(knockback.z)) * xStr;
            SetFloat("KnockY", knockback.y * yStr);
        }

        private void PlayHoldTool(object cxt) {
            if (self?.controller?.transform == null) return;
            if (cxt is not GameObject model || model == null) return;

            Transform handle = self.controller.transform.Find("Player/hRig/spine/spine.001/spine.002/spine.003/shoulder.L/upper_arm.L/forearm.L/hand.L/handle");
            if (handle == null) return;

            if (heldItem != null) {
                GameObject.Destroy(heldItem);
            }

            heldItem = GameObject.Instantiate(model, handle, false);
            SetOptionalHoldTools(true);
        }

        private void PlayUnholdTool() {
            if (heldItem != null) {
                GameObject.Destroy(heldItem);
                heldItem = null;
            }

            SetDrawingBow(false);
            SetOptionalHoldTools(false);
        }

        private void SetDrawingBow(bool value) {
            SetBool("IsDrawingBow", value);

            if (heldItem?.TryGetComponent(out Animator bowAnimator) == true) {
                if (HasParameter(bowAnimator, "IsDrawingBow", AnimatorControllerParameterType.Bool)) {
                    bowAnimator.SetBool("IsDrawingBow", value);
                }
            }
        }

        private void SetOptionalHoldTools(bool value) {
            // Some animator versions expose Hold_Tools/HoldTools while others do not.
            if (animator == null) return;

            if (HasParameter(animator, "Hold_Tools", AnimatorControllerParameterType.Bool)) {
                animator.SetBool("Hold_Tools", value);
            }

            if (HasParameter(animator, "HoldTools", AnimatorControllerParameterType.Bool)) {
                animator.SetBool("HoldTools", value);
            }
        }

        private static bool HasParameter(Animator animator, string parameterName, AnimatorControllerParameterType type) {
            if (animator == null) return false;
            foreach (AnimatorControllerParameter parameter in animator.parameters) {
                if (parameter.type == type && parameter.name == parameterName) {
                    return true;
                }
            }

            return false;
        }

        private IEnumerator CameraShake(float duration, float rotationStrength) {
            if (isShaking) yield break;
            isShaking = true;

            Transform cameraLocal = PlayerHandler.Camera.GetChild(0);
            Quaternion originRotation = cameraLocal.localRotation;
            Quaternion randomRotation = Quaternion.Euler(new Vector3(0, 0, UnityEngine.Random.Range(-180f, 180f) * rotationStrength));

            float elapsed = 0.0f;
            while (elapsed < duration) {
                cameraLocal.localRotation = Quaternion.Slerp(cameraLocal.localRotation, randomRotation, elapsed / duration);
                elapsed += self.DeltaTime;
                yield return null;
            }

            elapsed = 0.0f;
            while (elapsed < duration) {
                cameraLocal.localRotation = Quaternion.Slerp(cameraLocal.localRotation, originRotation, elapsed / duration);
                elapsed += self.DeltaTime;
                yield return null;
            }

            cameraLocal.localRotation = originRotation;
            isShaking = false;
        }

        private void SetBool(string key, bool value) {
            if (animator == null) return;
            animator.SetBool(key, value);
        }

        private float GetFloat(string key) {
            if (animator == null) return 0;
            return animator.GetFloat(key);
        }

        private void SetFloat(string key, float value) {
            if (animator == null) return;
            animator.SetFloat(key, value);
        }

        private void SetTrigger(string key) {
            if (animator == null) return;
            animator.SetTrigger(key);
        }
    }
}
