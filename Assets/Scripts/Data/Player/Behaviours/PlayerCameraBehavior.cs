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
    /// </summary>
    public class PlayerCameraBehavior : IBehavior {
        [JsonIgnore] public static PlayerCameraBehavior Active { get; private set; }
        [JsonIgnore] public PlayerCameraSettings settings;

        private bool hasBindings;
        private bool moved;
        private BehaviorEntity.Animal self;

        private float2 lookDelta;
        private Quaternion cameraLocalRotation = Quaternion.identity;
        private float3 cameraLocalPosition;
        private int cullingMask;

        private Quaternion smoothCharacterRot;
        private Quaternion smoothCameraRot;
        private float yaw;
        private float pitch;

        private PerspectiveMode activePerspective;

        private enum PerspectiveMode {
            FirstPerson,
            LockedThirdPerson,
            FreeRotateThirdPerson,
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> hierarchy) {
            hierarchy.TryAdd(typeof(PlayerCameraSettings), new PlayerCameraSettings());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: PlayerCameraBehavior requires PlayerCameraSettings");

            this.self = self;
            Active = this;
            self.Register(this);
            BindInput();
            DeserializeCameraState();
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: PlayerCameraBehavior requires PlayerCameraSettings");

            this.self = self;
            Active = this;
            self.Register(this);
            BindInput();
            DeserializeCameraState();
        }

        public void Disable(BehaviorEntity.Animal self) {
            if (ReferenceEquals(Active, this)) {
                Active = null;
            }

            if (ReferenceEquals(this.self, self)) {
                this.self = null;
            }
        }

        public void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.Job)
                return;
            if (self.context == BehaviorEntity.UpdateContext.Fixed)
                return;
            
            if (!IsControlledPlayer()) return;
            if (!moved) return;
            moved = false;

            if (activePerspective == PerspectiveMode.FirstPerson) {
                UpdateFirstPerson();
            } else if (activePerspective == PerspectiveMode.LockedThirdPerson) {
                UpdateLockedThirdPerson();
            } else {
                UpdateFreeThirdPerson();
            }

            ApplyCameraTransform();
            lookDelta = float2.zero;
        }

        private bool IsControlledPlayer() {
            if (self == null || !self.active) return false;
            if (PlayerHandler.data == null) return false;
            return PlayerHandler.data.info.rtEntityId == self.info.rtEntityId;
        }

        private void DeserializeCameraState() {
            if (PlayerHandler.Camera == null || PlayerHandler.Camera.childCount == 0) return;
            UnityEngine.Camera camera = PlayerHandler.Camera.GetChild(0).GetComponent<UnityEngine.Camera>();
            cullingMask = camera != null ? camera.cullingMask : -1;

            moved = true;
            activePerspective = PerspectiveMode.FirstPerson;
            ActivatePerspective(activePerspective);
        }

        private void TogglePerspective(float _) {
            if (!IsControlledPlayer()) return;

            activePerspective = (PerspectiveMode)(((int)activePerspective + 1) % 3);
            ActivatePerspective(activePerspective);
            moved = true;
        }

        private void ActivatePerspective(PerspectiveMode mode) {
            int selfLayer = LayerMask.NameToLayer("Self");

            if (mode == PerspectiveMode.FirstPerson) {
                // Hide the player model in first-person to avoid clipping into self mesh.
                cameraLocalPosition = new float3(0, 2.5f, 0);
                cameraLocalRotation.eulerAngles = new(cameraLocalRotation.eulerAngles.x, 0, 0);
                cullingMask &= ~(1 << selfLayer);
                smoothCharacterRot = self.Rotation;
                smoothCameraRot = cameraLocalRotation;
                PlayerCrosshair.EnableCrosshair();
            } else if (mode == PerspectiveMode.LockedThirdPerson) {
                // Locked third-person stays behind the character while sharing yaw with body rotation.
                cameraLocalPosition = new float3(0, 2.5f, 0);
                cameraLocalRotation.eulerAngles = new(cameraLocalRotation.eulerAngles.x, 0, 0);
                cullingMask |= 1 << selfLayer;
                smoothCharacterRot = self.Rotation;
                smoothCameraRot = cameraLocalRotation;
                PlayerCrosshair.DisableCrosshair();
                SetLockedCameraOffset();
            } else {
                // Free third-person allows camera orbit around player-controlled yaw/pitch.
                cullingMask |= 1 << selfLayer;
                pitch = 0;
                yaw = 180;
                smoothCharacterRot = self.Rotation;
                smoothCameraRot = Quaternion.AngleAxis(yaw, Vector3.up);
                cameraLocalRotation = smoothCameraRot;
                PlayerCrosshair.DisableCrosshair();
                SetFreeCameraOffset();
            }

            ApplyCameraTransform();
        }

        private void UpdateFirstPerson() {
            smoothCharacterRot *= Quaternion.Euler(0f, lookDelta.y, 0f);
            smoothCameraRot *= Quaternion.Euler(-lookDelta.x, 0f, 0f);

            if (settings.clampVerticalRotation)
                smoothCameraRot = ClampRotationAroundXAxis(smoothCameraRot);

            if (settings.smooth) {
                self.Rotation = Quaternion.Slerp(self.Rotation, smoothCharacterRot, settings.smoothTime * self.DeltaTime);
                cameraLocalRotation = Quaternion.Slerp(cameraLocalRotation, smoothCharacterRot, settings.smoothTime * self.DeltaTime);
            } else {
                self.Rotation = smoothCharacterRot;
                cameraLocalRotation = smoothCameraRot;
            }

            RefTuple<(float, float)> cxt = (GetAngleX(cameraLocalRotation), lookDelta.y);
            self.eventCtrl.RaiseEvent(GameEvent.Action_LookGradual, self, null, cxt);
        }

        private void UpdateLockedThirdPerson() {
            smoothCharacterRot *= Quaternion.Euler(0f, lookDelta.y, 0f);
            smoothCameraRot *= Quaternion.Euler(-lookDelta.x, 0f, 0f);

            if (settings.clampVerticalRotation)
                smoothCameraRot = ClampRotationAroundXAxis(smoothCameraRot);

            if (settings.smooth) {
                self.Rotation = Quaternion.Slerp(self.Rotation, smoothCharacterRot, settings.smoothTime * self.DeltaTime);
                cameraLocalRotation = Quaternion.Slerp(cameraLocalRotation, smoothCameraRot, settings.smoothTime * self.DeltaTime);
            } else {
                self.Rotation = smoothCharacterRot;
                cameraLocalRotation = smoothCameraRot;
            }

            SetLockedCameraOffset();
            RefTuple<(float, float)> cxt = (GetAngleX(cameraLocalRotation), lookDelta.y);
            self.eventCtrl.RaiseEvent(GameEvent.Action_LookGradual, self, null, cxt);
        }

        private void UpdateFreeThirdPerson() {
            float rotation = 0;
            if (self.Is(out PlayerMovementBehavior movement)) {
                // Consume lateral movement input as orbit/turn input while in free-rotate mode.
                rotation = movement.ConsumeHorizontalMovementInput(settings.Sensitivity);
            }

            smoothCharacterRot *= Quaternion.Euler(0, rotation, 0);
            yaw -= rotation;
            yaw += lookDelta.y;
            pitch -= lookDelta.x;
            if (settings.clampVerticalRotation)
                pitch = Mathf.Clamp(pitch, settings.MinimumX, settings.MaximumX);

            smoothCameraRot = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, Vector3.right);
            if (settings.smooth) {
                self.Rotation = Quaternion.Slerp(self.Rotation, smoothCharacterRot, settings.smoothTime * self.DeltaTime);
                cameraLocalRotation = Quaternion.Slerp(cameraLocalRotation, smoothCameraRot, settings.smoothTime * self.DeltaTime);
            } else {
                self.Rotation = smoothCharacterRot;
                cameraLocalRotation = smoothCameraRot;
            }

            SetFreeCameraOffset();
            RefTuple<(float, float)> cxt = (GetAngleX(cameraLocalRotation), rotation);
            self.eventCtrl.RaiseEvent(GameEvent.Action_LookGradual, self, null, cxt);
        }

        private static uint RayTestSolid(int3 coord) {
            MapData pointInfo = CPUMapManager.SampleMap(coord);
            return (uint)pointInfo.viscosity;
        }

        private void SetLockedCameraOffset() {
            const float height = 2.5f;
            const float distance = 10f;

            float backDist = distance;
            float distGS = distance / Config.CURRENT.Quality.Terrain.value.lerpScale;
            // Pull the camera forward if terrain obstructs the ideal follow distance.
            if (CPUMapManager.RayCastTerrain(self.head, Facing * Vector3.back, distGS, RayTestSolid, out float3 hitPt))
                backDist = math.distance(hitPt, self.head);

            float3 backOffset = math.mul(math.normalize(cameraLocalRotation), new float3(0, 0, -backDist));
            cameraLocalPosition = (float3)Vector3.up * height + backOffset;
        }

        private void SetFreeCameraOffset() {
            const float height = 2.5f;
            const float distance = 10f;

            float backDist = distance;
            float distGS = distance / Config.CURRENT.Quality.Terrain.value.lerpScale;
            float3 dir = math.mul(math.normalize(cameraLocalRotation), new float3(0, 0, -1));
            dir = math.mul(math.normalize(self.Rotation), dir);

            // Collision check is based on full orbit direction (player yaw + camera pitch/yaw).
            if (CPUMapManager.RayCastTerrain(self.head, dir, distGS, RayTestSolid, out float3 hitPt))
                backDist = math.distance(hitPt, self.head);

            float3 backOffset = math.mul(math.normalize(cameraLocalRotation), new float3(0, 0, -backDist));
            cameraLocalPosition = (float3)Vector3.up * height + backOffset;
        }

        private Quaternion Facing {
            get {
                if (activePerspective != PerspectiveMode.FreeRotateThirdPerson) {
                    return math.mul(math.normalize(self.Rotation), math.normalize(cameraLocalRotation));
                }

                Quaternion yawRot = Quaternion.Euler(0f, self.Rotation.eulerAngles.y, 0f);
                Quaternion pitchRot = Quaternion.Euler(cameraLocalRotation.eulerAngles.x, 0f, 0f);
                return math.mul(yawRot, pitchRot);
            }
        }

        private void ApplyCameraTransform() {
            if (PlayerHandler.Camera == null) return;
            PlayerHandler.Camera.SetLocalPositionAndRotation((float3)cameraLocalPosition, cameraLocalRotation);

            if (PlayerHandler.Camera.childCount == 0) return;
            UnityEngine.Camera camera = PlayerHandler.Camera.GetChild(0).GetComponent<UnityEngine.Camera>();
            if (camera == null) return;
            if (camera.cullingMask == cullingMask) return;
            camera.cullingMask = cullingMask;
        }

        private Quaternion ClampRotationAroundXAxis(Quaternion q) {
            q.x /= q.w;
            q.y /= q.w;
            q.z /= q.w;
            q.w = 1.0f;

            float angleX = 2.0f * Mathf.Rad2Deg * Mathf.Atan(q.x);
            angleX = Mathf.Clamp(angleX, settings.MinimumX, settings.MaximumX);
            q.x = Mathf.Tan(0.5f * Mathf.Deg2Rad * angleX);

            return q;
        }

        private float GetAngleX(Quaternion q) {
            q.x /= q.w;
            q.y /= q.w;
            q.z /= q.w;
            q.w = 1.0f;

            float lookPitch = 2.0f * Mathf.Rad2Deg * Mathf.Atan(q.x);
            return -(Mathf.InverseLerp(settings.MinimumX, settings.MaximumX, lookPitch) * 2 - 1);
        }

        private void BindInput() {
            if (hasBindings) return;
            hasBindings = true;

            InputPoller.AddBinding(new ActionBind("Look Horizontal", LookX), "PlayerCamera:LH", "4.5::Movement");
            InputPoller.AddBinding(new ActionBind("Look Vertical", LookY), "PlayerCamera:LV", "4.5::Movement");
            InputPoller.AddBinding(new ActionBind("Toggle Perspective", TogglePerspective), "PlayerCamera:TP", "2.5::Subscene");
        }

        private void LookX(float x) {
            lookDelta.x = x * settings.Sensitivity;
            moved = true;
        }

        private void LookY(float y) {
            lookDelta.y = y * settings.Sensitivity;
            moved = true;
        }
    }
}
