using System;
using System.Collections.Generic;
using Arterra.Core.Events;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.GamePlay;
using Arterra.GamePlay.UI;
using Arterra.Utils;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using Arterra.GamePlay.Interaction;

namespace Arterra.Data.Entity.Behavior {
    /// <summary>
    /// Controls camera responsiveness, sensitivity, and rotation limits.
    /// </summary>
    [Serializable]
    public class PlayerCameraSettings : IBehaviorSetting {
        ///<summary>Name of settings object in UI generation</summary>
        [JsonIgnore] public static string Name => "Camera";
        /// <summary>Mouse/controller look sensitivity multiplier.</summary>
        public float Sensitivity = 2f;
        /// <summary>Whether camera pitch is clamped around the x-axis.</summary>
        public bool clampVerticalRotation = true;
        /// <summary>Minimum pitch angle (look down limit).</summary>
        public float MinimumX = -90f;
        /// <summary>Maximum pitch angle (look up limit).</summary>
        public float MaximumX = 90f;
        /// <summary>If true, camera and character rotation are interpolated.</summary>
        public bool smooth;
        /// <summary>Interpolation speed when <see cref="smooth"/> is enabled.</summary>
        public float smoothTime = 5f;

        public object Clone() {
            return new PlayerCameraSettings {
                Sensitivity = Sensitivity,
                clampVerticalRotation = clampVerticalRotation,
                MinimumX = MinimumX,
                MaximumX = MaximumX,
                smooth = smooth,
                smoothTime = smoothTime,
            };
        }
    }

    /// <summary>
    /// Manages the player's camera across first-person and third-person modes.
    /// Also implements <see cref="IMultiCollider"/> to expose the camera's full facing
    /// direction and eye position to the base entity, overriding the default body collider bindings.
    /// </summary>
    public class PlayerCameraBehavior : SpeciesBehavior, IMultiCollider {
        [JsonIgnore] public PlayerCameraSettings settings;
        [JsonIgnore] private IMultiCollider baseCollider;
        private Modifier mod;

        // IMultiCollider — delegates collider data from the base ColliderUpdateBehavior while
        // overriding Rotation and HeadPosition to reflect the active camera perspective.
        [JsonIgnore] public GamePlay.Interaction.TerrainCollider Collider => baseCollider.Collider;
        [JsonIgnore] public GamePlay.Interaction.TerrainCollider PathCollider => baseCollider.PathCollider;
        [JsonIgnore] public Quaternion Rotation {
            get => perspectives?[activePersp].Facing ?? baseCollider.Rotation;
            set => baseCollider.Rotation = value;
        }

        internal const float height = 0.65f;
        internal static float heightReal => Config.CURRENT.Quality.Terrain.value.lerpScale * height;
        /// <summary>Returns the camera's eye position in grid space, used as the entity head.</summary>
        [JsonIgnore] public float3 HeadPosition => baseCollider.HeadPosition + new float3(0, height, 0);
        private float Sensitivity => Modifier.Get(mod, MSettings.CameraSensitivity, settings.Sensitivity);
        private float MinimumX => Modifier.Get(mod, MSettings.MinimumX, settings.MinimumX);
        private float MaximumX => Modifier.Get(mod, MSettings.MaximumX, settings.MaximumX);

        private bool hasBindings;
        internal bool moved;
        private BehaviorEntity.Animal self;
        private VitalityBehavior vit;

        internal float2 lookDelta;
        internal Quaternion cameraLocalRotation = Quaternion.identity;
        internal float3 cameraLocalPosition;
        internal int cullingMask;

        private ICameraPerspective[] perspectives;
        private int activePersp;

        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> hierarchy) {
            // Camera orientation depends on the body collider being initialized first.
            hierarchy.TryAdd(Behaviors.Collider, hierarchy.Count);
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> hierarchy) {
            hierarchy.TryAdd(typeof(PlayerCameraSettings), new PlayerCameraSettings());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: PlayerCameraBehavior requires PlayerCameraSettings");
            if (!self.Is(out vit)) vit = null;
            if (!self.Is(out mod)) mod = null;
            if (!self.Is(out baseCollider))
                throw new Exception("Entity: PlayerCameraBehavior requires ColliderUpdateBehavior");

            InitPerspectives();
            self.Register<IMultiCollider>(this);
            self.Register(this);
            this.self = self;

            BindInput();
            DeserializeCameraState();
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: PlayerCameraBehavior requires PlayerCameraSettings");
            if (!self.Is(out vit)) vit = null;
            if (!self.Is(out mod)) mod = null;
            if (!self.Is(out baseCollider))
                throw new Exception("Entity: PlayerCameraBehavior requires ColliderUpdateBehavior");

            InitPerspectives();
            self.Register<IMultiCollider>(this);
            self.Register(this);
            this.self = self;

            BindInput();
            DeserializeCameraState();
        }

        public override void Disable(BehaviorEntity.Animal self) {
            UnbindInput();
            this.self = null;
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.Job)
                return;
            if (self.context == BehaviorEntity.UpdateContext.Fixed)
                return;

            perspectives[activePersp].Update();
            ApplyCameraTransform();
            lookDelta = float2.zero;
        }

        private void InitPerspectives() {
            perspectives = new ICameraPerspective[] {
                new FirstPersonCamera(this),
                new LockedThirdPersonCamera(this),
                new FreeRotateThirdPersonCamera(this),
            };
            activePersp = 0;
        }

        private void DeserializeCameraState() {
            if (PlayerHandler.Camera == null || PlayerHandler.Camera.childCount == 0) return;
            Camera camera = PlayerHandler.Camera.GetChild(0).GetComponent<UnityEngine.Camera>();
            cullingMask = camera != null ? camera.cullingMask : -1;

            moved = true;
            activePersp = 0;
            perspectives[activePersp].Activate();
        }

        private void TogglePerspective(float _) {
            activePersp = (activePersp + 1) % perspectives.Length;
            perspectives[activePersp].Activate();
            moved = true;
        }

        private void ApplyCameraTransform() {
            if (PlayerHandler.Camera == null) return;
            PlayerHandler.Camera.SetLocalPositionAndRotation(GetCameraScaledLocalPosition(), cameraLocalRotation);

            if (PlayerHandler.Camera.childCount == 0) return;
            Camera camera = PlayerHandler.Camera.GetChild(0).GetComponent<Camera>();
            if (camera == null) return;
            if (camera.cullingMask == cullingMask) return;
            camera.cullingMask = cullingMask;
        }

        private Vector3 GetCameraScaledLocalPosition() {
            Transform cameraTransform = PlayerHandler.Camera;
            if (cameraTransform == null || cameraTransform.parent == null)
                return cameraLocalPosition;

            // cameraLocalPosition is authored as an unscaled offset in the parent's local axes.
            Vector3 desiredWorldOffset = cameraTransform.parent.rotation * (Vector3)cameraLocalPosition;
            return cameraTransform.parent.worldToLocalMatrix.MultiplyVector(desiredWorldOffset);
        }

        internal static uint RayTestSolid(int3 coord) {
            MapData pointInfo = CPUMapManager.SampleMap(coord);
            return (uint)pointInfo.viscosity;
        }

        internal Quaternion ClampRotationAroundXAxis(Quaternion q) {
            q.x /= q.w;
            q.y /= q.w;
            q.z /= q.w;
            q.w = 1.0f;

            float angleX = 2.0f * Mathf.Rad2Deg * Mathf.Atan(q.x);
            angleX = Mathf.Clamp(angleX, MinimumX, MaximumX);
            q.x = Mathf.Tan(0.5f * Mathf.Deg2Rad * angleX);

            return q;
        }

        internal float GetAngleX(Quaternion q) {
            q.x /= q.w;
            q.y /= q.w;
            q.z /= q.w;
            q.w = 1.0f;

            float lookPitch = 2.0f * Mathf.Rad2Deg * Mathf.Atan(q.x);
            return -(Mathf.InverseLerp(MinimumX, MaximumX, lookPitch) * 2 - 1);
        }

        private string LookHorizontalName => $"PlayerCamera:LH::{self.info.entityId}"; 
        private string LookVerticalName => $"PlayerCamera:LV::{self.info.entityId}";
        private string TPerspectiveName => $"PlayerCamera:TP::{self.info.entityId}"; 
        private void BindInput() {
            if (hasBindings) return;
            hasBindings = true;

            self.eventCtrl.AddContextlessEventHandler(GameEvent.Entity_Death, (_, _) => self.RemoveBehavior(Id));
            InputPoller.AddBinding(new ActionBind("Look Horizontal", LookX), LookHorizontalName, "4.5::Movement");
            InputPoller.AddBinding(new ActionBind("Look Vertical", LookY), LookVerticalName, "4.5::Movement");
            InputPoller.AddBinding(new ActionBind("Toggle Perspective", TogglePerspective), TPerspectiveName, "2.5::Subscene");
        }

        private void UnbindInput() {
            if (!hasBindings) return;
            hasBindings = false;

            InputPoller.RemoveBinding(LookHorizontalName, "4.5::Movement");
            InputPoller.RemoveBinding(LookVerticalName, "4.5::Movement");
            InputPoller.RemoveBinding(TPerspectiveName, "2.5::Subscene");
        }

        private void LookX(float x) {
            lookDelta.x = x * Sensitivity;
            moved = true;
        }

        private void LookY(float y) {
            lookDelta.y = y * Sensitivity;
            moved = true;
        }

        // ─── Perspective interface ────────────────────────────────────────────────

        private interface ICameraPerspective {
            void Activate();
            void Update();
            Quaternion Facing { get; }
        }

        // ─── First-person perspective ─────────────────────────────────────────────

        private class FirstPersonCamera : ICameraPerspective {
            private readonly PlayerCameraBehavior camera;
            private Quaternion smoothCharacterRot;
            private Quaternion smoothCameraRot;

            public Quaternion Facing => math.mul(
                math.normalize(camera.baseCollider.Collider.transform.rotation),
                math.normalize(camera.cameraLocalRotation));

            public FirstPersonCamera(PlayerCameraBehavior cam) => camera = cam;

            public void Activate() {
                camera.cameraLocalPosition = new float3(0, heightReal, 0);
                camera.cameraLocalRotation.eulerAngles = new(camera.cameraLocalRotation.eulerAngles.x, 0, 0);
                camera.cullingMask &= ~(1 << LayerMask.NameToLayer("Self"));
                smoothCharacterRot = camera.baseCollider.Collider.transform.rotation;
                smoothCameraRot = camera.cameraLocalRotation;
                PlayerCrosshair.EnableCrosshair();
            }

            public void Update() {
                if (!camera.moved) return;
                camera.moved = false;

                smoothCharacterRot *= Quaternion.Euler(0f, camera.lookDelta.y, 0f);
                smoothCameraRot *= Quaternion.Euler(-camera.lookDelta.x, 0f, 0f);

                if (camera.settings.clampVerticalRotation)
                    smoothCameraRot = camera.ClampRotationAroundXAxis(smoothCameraRot);

                if (camera.settings.smooth) {
                    camera.baseCollider.Collider.transform.rotation = Quaternion.Slerp(
                        camera.baseCollider.Collider.transform.rotation, smoothCharacterRot,
                        camera.settings.smoothTime * camera.self.DeltaTime);
                    camera.cameraLocalRotation = Quaternion.Slerp(
                        camera.cameraLocalRotation, smoothCameraRot,
                        camera.settings.smoothTime * camera.self.DeltaTime);
                } else {
                    camera.baseCollider.Collider.transform.rotation = smoothCharacterRot;
                    camera.cameraLocalRotation = smoothCameraRot;
                }

                RefTuple<(float, float)> cxt = (camera.GetAngleX(camera.cameraLocalRotation), camera.lookDelta.y);
                camera.self.eventCtrl.RaiseEvent(GameEvent.Action_LookGradual, camera.self, null, cxt);
            }
        }

        // ─── Locked third-person perspective ─────────────────────────────────────

        private class LockedThirdPersonCamera : ICameraPerspective {
            private readonly PlayerCameraBehavior camera;
            private Quaternion smoothCharacterRot;
            private Quaternion smoothCameraRot;
            const float distance = 2.5f;

            public Quaternion Facing => math.mul(
                math.normalize(camera.baseCollider.Collider.transform.rotation),
                math.normalize(camera.cameraLocalRotation));

            public LockedThirdPersonCamera(PlayerCameraBehavior cam) => camera = cam;

            public void Activate() {
                camera.cameraLocalPosition = new float3(0, 2.5f, 0);
                camera.cameraLocalRotation.eulerAngles = new(camera.cameraLocalRotation.eulerAngles.x, 0, 0);
                camera.cullingMask |= 1 << LayerMask.NameToLayer("Self");
                smoothCharacterRot = camera.baseCollider.Collider.transform.rotation;
                smoothCameraRot = camera.cameraLocalRotation;
                PlayerCrosshair.DisableCrosshair();
                SetCameraOffset();
            }

            public void Update() {
                if (!camera.moved) return;
                camera.moved = false;

                smoothCharacterRot *= Quaternion.Euler(0f, camera.lookDelta.y, 0f);
                smoothCameraRot *= Quaternion.Euler(-camera.lookDelta.x, 0f, 0f);

                if (camera.settings.clampVerticalRotation)
                    smoothCameraRot = camera.ClampRotationAroundXAxis(smoothCameraRot);

                if (camera.settings.smooth) {
                    camera.baseCollider.Collider.transform.rotation = Quaternion.Slerp(
                        camera.baseCollider.Collider.transform.rotation, smoothCharacterRot,
                        camera.settings.smoothTime * camera.self.DeltaTime);
                    camera.cameraLocalRotation = Quaternion.Slerp(
                        camera.cameraLocalRotation, smoothCameraRot,
                        camera.settings.smoothTime * camera.self.DeltaTime);
                } else {
                    camera.baseCollider.Collider.transform.rotation = smoothCharacterRot;
                    camera.cameraLocalRotation = smoothCameraRot;
                }

                SetCameraOffset();
                RefTuple<(float, float)> cxt = (camera.GetAngleX(camera.cameraLocalRotation), camera.lookDelta.y);
                camera.self.eventCtrl.RaiseEvent(GameEvent.Action_LookGradual, camera.self, null, cxt);
            }

            private void SetCameraOffset() {
                float backDist = distance;
                float distGS = distance / Config.CURRENT.Quality.Terrain.value.lerpScale;
                // Pull the camera forward if terrain obstructs the ideal follow distance.
                if (CPUMapManager.RayCastTerrain(camera.self.head, Facing * Vector3.back, distGS, RayTestSolid, out float3 hitPt))
                    backDist = math.distance(hitPt, camera.self.head);

                float3 backOffset = math.mul(math.normalize(camera.cameraLocalRotation), new float3(0, 0, -backDist));
                camera.cameraLocalPosition = (float3)Vector3.up * heightReal + backOffset;
            }
        }

        // ─── Free-rotate third-person perspective ─────────────────────────────────

        private class FreeRotateThirdPersonCamera : ICameraPerspective {
            private readonly PlayerCameraBehavior camera;
            private Quaternion smoothCharacterRot;
            private Quaternion smoothCameraRot;
            private float yaw;
            private float pitch;
            const float distance = 2.5f;

            public Quaternion Facing {
                get {
                    Quaternion yawRot = Quaternion.Euler(0f, camera.baseCollider.Collider.transform.rotation.eulerAngles.y, 0f);
                    Quaternion pitchRot = Quaternion.Euler(camera.cameraLocalRotation.eulerAngles.x, 0f, 0f);
                    return math.mul(yawRot, pitchRot);
                }
            }

            public FreeRotateThirdPersonCamera(PlayerCameraBehavior cam) => camera = cam;

            public void Activate() {
                camera.cullingMask |= 1 << LayerMask.NameToLayer("Self");
                pitch = 0; yaw = 180;
                smoothCharacterRot = camera.baseCollider.Collider.transform.rotation;
                smoothCameraRot = Quaternion.AngleAxis(yaw, Vector3.up);
                camera.cameraLocalRotation = smoothCameraRot;
                PlayerCrosshair.DisableCrosshair();
                SetCameraOffset();
            }

            public void Update() {
                if (!camera.moved) return;
                camera.moved = false;

                float rotation = 0;
                if (camera.self.Is(out PlayerMovementBehavior movement)) {
                    // Consume lateral movement input as orbit/turn input while in free-rotate mode.
                    rotation = movement.ConsumeHorizontalMovementInput(camera.Sensitivity);
                }

                smoothCharacterRot *= Quaternion.Euler(0, rotation, 0);
                yaw -= rotation;
                yaw += camera.lookDelta.y;
                pitch -= camera.lookDelta.x;
                if (camera.settings.clampVerticalRotation)
                    pitch = Mathf.Clamp(pitch, camera.MinimumX, camera.MaximumX);

                smoothCameraRot = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, Vector3.right);
                if (camera.settings.smooth) {
                    camera.baseCollider.Collider.transform.rotation = Quaternion.Slerp(
                        camera.baseCollider.Collider.transform.rotation, smoothCharacterRot,
                        camera.settings.smoothTime * camera.self.DeltaTime);
                    camera.cameraLocalRotation = Quaternion.Slerp(
                        camera.cameraLocalRotation, smoothCameraRot,
                        camera.settings.smoothTime * camera.self.DeltaTime);
                } else {
                    camera.baseCollider.Collider.transform.rotation = smoothCharacterRot;
                    camera.cameraLocalRotation = smoothCameraRot;
                }

                SetCameraOffset();
                RefTuple<(float, float)> cxt = (camera.GetAngleX(camera.cameraLocalRotation), rotation);
                camera.self.eventCtrl.RaiseEvent(GameEvent.Action_LookGradual, camera.self, null, cxt);
            }

            private void SetCameraOffset() {
                float backDist = distance;
                float distGS = distance / Config.CURRENT.Quality.Terrain.value.lerpScale;
                float3 dir = math.mul(math.normalize(camera.cameraLocalRotation), new float3(0, 0, -1));
                dir = math.mul(math.normalize(camera.baseCollider.Collider.transform.rotation), dir);

                // Collision check is based on full orbit direction (player yaw + camera pitch/yaw).
                if (CPUMapManager.RayCastTerrain(camera.self.head, dir, distGS, RayTestSolid, out float3 hitPt))
                    backDist = math.distance(hitPt, camera.self.head);

                float3 backOffset = math.mul(math.normalize(camera.cameraLocalRotation), new float3(0, 0, -backDist));
                camera.cameraLocalPosition = (float3)Vector3.up * heightReal + backOffset;
            }
        }
    }
}
