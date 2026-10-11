using System;
using System.Collections.Generic;
using System.Collections;
using Arterra.Core.Events;
using Arterra.Engine.Terrain;
using Arterra.GamePlay;
using Arterra.Core;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Engine.Audio;

namespace Arterra.Data.Entity.Behavior {
    /// <summary>
    /// Settings that control how player animation/effect hooks resolve scene objects.
    /// </summary>
    [Serializable]
    public class PlayerEffectsSettings : IBehaviorSetting {
        ///<summary>Name of settings object in UI generation</summary>
        [JsonIgnore] public static string Name => "Animator";
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
    public class PlayerEffectsBehavior : SpeciesBehavior {
        [JsonIgnore] public PlayerEffectsSettings settings;
        private readonly Dictionary<GameEvent, RefEventHandler> visualHandlers = new();

        private PhysicalitySetting physicality;
        private BehaviorEntity.Animal self;
        private Animator animator;
        private GameObject heldItem;
        private float3 movement;
        private bool isSwimming;
        private bool isShaking;
        private FMOD.Studio.EventInstance SubmergedSound;

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> hierarchy) {
            hierarchy.TryAdd(typeof(PlayerEffectsSettings), new PlayerEffectsSettings());
        }

        public override void InitializeVisual(BehaviorEntity.Animal self) {
            var setting = self.settings;
            if (!setting.Is(out settings))
                throw new Exception("Entity: PlayerEffectsBehavior requires PlayerEffectsSettings");
            if (!setting.Is(out physicality)) physicality = null;

            this.self = self;
            self.Register(this);
            ResolveAnimator(self);
            isSwimming = false;
            isShaking = false;
            BindEvents(self);
        }



        public override void DisposeVisual(BehaviorEntity.Animal self) {
            foreach (var pair in visualHandlers)
                self.eventCtrl.RemoveEventHandler(pair.Key, pair.Value);
            visualHandlers.Clear();
            AudioManager.StopEvent(SubmergedSound, false);
            SubmergedSound = default;
            this.self = null;

            if (heldItem != null) {
                GameObject.Destroy(heldItem);
                heldItem = null;
            }

            animator = null;
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context != BehaviorEntity.UpdateContext.Visual) return;
            if (animator == null) return;

            bool mounted = self.Is(out PlayerMovementBehavior movementBehavior) && movementBehavior.IsMounted;
            animator.SetBool("IsSitting", mounted);
            if (mounted) animator.SetBool("IsJumping", false);
            float3 velocity = self.IsOwner ? movement : self.velocity;
            float speed = Mathf.Lerp(animator.GetFloat("Speed"), math.length(velocity), 0.35f);
            animator.SetFloat("Speed", speed);
            if (self.IsOwner) {
                float angle = Mathf.Atan2(velocity.z, velocity.x) / Mathf.PI;
                animator.SetFloat("MoveTheta", Mathf.Lerp(animator.GetFloat("MoveTheta"), angle, 0.35f));
            }
        }

        // Movement captures the presentation value; Animator writes happen in Visual.
        public void PlayAnimatorMove(float3 normVel) => movement = normVel;

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

        private void Bind(GameEvent type, RefEventHandler handler) {
            visualHandlers[type] = handler;
            self.eventCtrl.AddEventHandler(type, handler);
        }

        private void BindEvents(BehaviorEntity.Animal self) {
            // Keep animator states in sync with entity events raised by movement/interaction behaviors.
            Bind(GameEvent.Action_Jump, (_, _, __) => SetBool("IsJumping", true));
            Bind(GameEvent.Item_ConsumeFood, (_, _, __) => SetTrigger("Eat"));
            Bind(GameEvent.Action_RemoveTerrain, (_, _, __) => SetTrigger("Place"));
            Bind(GameEvent.Action_PlaceTerrain, (_, _, __) => SetTrigger("Place"));
            Bind(GameEvent.Action_LookDirect, (_, _, cxt) => PlayLookDirect(cxt));
            Bind(GameEvent.System_Deserialize, PlayDeserialize);
            Bind(GameEvent.Entity_Death, (_, _, __) => SetBool("IsDead", true));
            Bind(GameEvent.Entity_Damaged, PlayDamaged);
            Bind(GameEvent.Entity_HitGround, PlayTouchdown);
            Bind(GameEvent.Entity_InLiquid, PlayStartSwim);
            Bind(GameEvent.Entity_InGas, PlayStopSwim);
            Bind(GameEvent.Action_MountRideable, (_, _, __) => {
                SetBool("IsSitting", true);
                SetBool("IsJumping", false);
                SetBool("IsSwimming", false);
            });
            Bind(GameEvent.Action_DismountRideable, (_, _, __) => SetBool("IsSitting", false));
            Bind(GameEvent.Entity_Attack, (_, _, __) => SetTrigger(UnityEngine.Random.Range(0, 2) == 0 ? "PunchR" : "PunchL"));
            Bind(GameEvent.Item_HoldTool, (_, _, cxt) => PlayHoldTool(cxt));
            Bind(GameEvent.Item_UnholdTool, (_, _, __) => PlayUnholdTool());
            Bind(GameEvent.Item_UseTool, (_, _, cxt) => {
                if (cxt is string anim) SetTrigger(anim);
            });
            Bind(GameEvent.Item_DrawBow, (_, _, __) => SetDrawingBow(true));
            Bind(GameEvent.Item_ReleaseBow, (_, _, __) => SetDrawingBow(false));
            Bind(GameEvent.Action_LookGradual, (_, _, cxt) => {
                if (cxt is not RefTuple<(float, float)> look) return;
                SetBool("DisableShuffle", false);
                SetFloat("LookY", look.Value.Item1);
                float yaw = GetFloat("LookX");
                yaw = (yaw + look.Value.Item2) * 0.75f;
                SetFloat("LookX", yaw);
            });
        }

        private void PlayDeserialize(object source, object target, object cxt) {
            if (source is not Guid sourceId) return;
            if (!EntityManager.TryGetEntity(sourceId, out Entity entity)) return;
            if (!entity.Is(out IVitality attackable)) return;
            if (!attackable.IsDead) return;
            SetBool("IsDead", true);
        }

        private void PlayStartSwim(object srcId, object _, object density) {
            if (srcId is not Guid sourceId) return;
            if (!EntityManager.TryGetEntity(sourceId, out Entity source)) return;
            if (source.Is(out IVitality attackable) && attackable.IsDead)
                return;
            SetBool("IsSwimming", true);
            SetBool("IsJumping", false);
            SetBool("IsSitting", false);

            if (isSwimming) return;
            isSwimming = true;

            AudioManager.StopEvent(SubmergedSound, false);
            if (self.IsOwner) SubmergedSound = AudioManager.CreateEvent(AudioEvents.Action_WaterSubmerged);
        }

        private void PlayStopSwim(object srcId, object target, object density) {
            if (srcId is not Guid sourceId) return;
            if (!EntityManager.TryGetEntity(sourceId, out Entity source)) return;
            if (source.Is(out IVitality attackable) && attackable.IsDead)
                return;
            SetBool("IsSwimming", false);

            AudioManager.StopEvent(SubmergedSound, false);
            if (!isSwimming) return;
            isSwimming = false;
        }

        private void PlayTouchdown(object srcId, object tgtId, object cxt) {
            if (srcId is not Guid sourceId) return;
            if (!EntityManager.TryGetEntity(sourceId, out Entity source))
                return;
            if (!source.Is(out IVitality vit) || vit.IsDead)
                return;

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

        private void PlayDamaged(object srcId, object tgtId, object cxt) {
            if (srcId is not Guid sourceId) return;
            if (!EntityManager.TryGetEntity(sourceId, out Entity source)) return;
            if (!source.Is(out IVitality living)) return;
            if (living.IsDead) return;

            if (cxt is not RefTuple<(float, float3)> damaged) return;
            float thresh = physicality != null ? physicality.MaxHealth * 0.01f : 0.1f;
            if (damaged.Value.Item1 < thresh) return;

            if (self.IsOwner && !isShaking) ArterraRuntime.MainCoroutines.Enqueue(CameraShake(0.2f, 0.25f));

            quaternion wsToOs = math.inverse(self.transform.rotation);
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
            if (isShaking || self == null || !self.IsOwner || !self.VisualActive) yield break;
            isShaking = true;

            Transform cameraLocal = PlayerHandler.Camera.GetChild(0);
            Quaternion originRotation = cameraLocal.localRotation;
            Quaternion randomRotation = Quaternion.Euler(new Vector3(0, 0, UnityEngine.Random.Range(-180f, 180f) * rotationStrength));

            float elapsed = 0.0f;
            while (elapsed < duration && self?.VisualActive == true && self.IsOwner) {
                cameraLocal.localRotation = Quaternion.Slerp(cameraLocal.localRotation, randomRotation, elapsed / duration);
                elapsed += self.DeltaTime;
                yield return null;
            }

            elapsed = 0.0f;
            while (elapsed < duration && self?.VisualActive == true && self.IsOwner) {
                cameraLocal.localRotation = Quaternion.Slerp(cameraLocal.localRotation, originRotation, elapsed / duration);
                elapsed += self.DeltaTime;
                yield return null;
            }

            if (self?.IsOwner == true) cameraLocal.localRotation = originRotation;
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
