using System;
using MapStorage;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;


namespace WorldConfig.Gameplay.Player {
    /// <summary> Controls how the camera rotates and responds to player input. Notably
    /// settings controlling the camera's responsiveness, sensitivity, and limits to 
    /// its rotation are contained here.  </summary>
    [Serializable]
    public struct Camera {
        /// <summary> How sensitive the camera is to mouse movements. A higher number will require smaller
        /// movements for larger rotations </summary>
        public float Sensitivity; //2f
        /// <summary>Whether or not camera rotations are clamped around the x-axis</summary>
        public bool clampVerticalRotation; //true
        /// <summary>The minimum x-axis angle that the camera can rotate; the farthest the player can look down</summary>
        public float MinimumX; //-90f
        /// <summary>The maximum x-axis angle that the camera can rotate; the farthest the player can look up</summary>
        public float MaximumX; //90f
        /// <summary>Whether rotations should slowly follow player inputs. If false, rotations will jump to the forward axis, 
        /// possibly making the rotation seem rough and jittery </summary>
        public bool smooth;
        /// <summary>If <see cref="smooth"/>, the time, in seconds to rotate to a new orientation.</summary>
        public float smoothTime; //5f
    }

    public class PlayerCamera {
        private static Camera S => Config.CURRENT.GamePlay.Player.value.Camera;
        private WeakReference<PlayerStreamer.Player> _context;
        private PlayerStreamer.Player context => _context.TryGetTarget(out PlayerStreamer.Player tg) ? tg : null;
        private ref Quaternion characterRot => ref context.collider.transform.rotation;
        [JsonIgnore]
        public Quaternion Facing => perspectives[activePersp].rotation;
        [JsonProperty]
        private TerrainCollider.Transform camTsf;
        [JsonProperty]
        private int cullingMask;
        private bool moved;
        private float2 Rot;

        private ICameraPerspective[] perspectives;
        private int activePersp = 0;

        public PlayerCamera() { camTsf.rotation = Quaternion.identity; }

        public void Deserailize(PlayerStreamer.Player context) {
            this._context = new WeakReference<PlayerStreamer.Player>(context);
            cullingMask = PlayerHandler.Camera.GetChild(0).GetComponent<UnityEngine.Camera>().cullingMask;
            moved = true;

            activePersp = 0;
            perspectives = new ICameraPerspective[]{
                new FirstPersonCamera(this),
                new LockedThirdPerson(this),
                new FreeRotateThirdPerson(this),
            }; perspectives[0].Activate();
        }

        public static void Initialize() {
            InputPoller.AddBinding(new InputPoller.ActionBind("Look Horizontal", LookX), "4.5::Movement");
            InputPoller.AddBinding(new InputPoller.ActionBind("Look Vertical", LookY), "4.5::Movement");
            InputPoller.AddBinding(new InputPoller.ActionBind("Toggle Perspective", TogglePerspective), "2.5::Subscene");
        }
        private static void LookX(float x) {
            PlayerCamera self = PlayerHandler.data.camera; //reaquire active self
            self.Rot.x = x * S.Sensitivity;
            self.moved = true;
        }

        private static void LookY(float y) {
            PlayerCamera self = PlayerHandler.data.camera; //reaquire active self
            self.Rot.y = y * S.Sensitivity;
            self.moved = true;
        }

        private static void TogglePerspective(float _) {
            PlayerCamera self = PlayerHandler.data.camera; //reaquire active self
            self.activePersp = (self.activePersp + 1) % self.perspectives.Length;
            self.perspectives[self.activePersp].Activate();
        }

        static Quaternion ClampRotationAroundXAxis(Quaternion q) {
            q.x /= q.w;
            q.y /= q.w;
            q.z /= q.w;
            q.w = 1.0f;

            float angleX = 2.0f * Mathf.Rad2Deg * Mathf.Atan(q.x);
            angleX = Mathf.Clamp(angleX, S.MinimumX, S.MaximumX);
            q.x = Mathf.Tan(0.5f * Mathf.Deg2Rad * angleX);

            return q;
        }

        static float GetAngleX(Quaternion q) {
            q.x /= q.w;
            q.y /= q.w;
            q.z /= q.w;
            q.w = 1.0f;

            return 2.0f * Mathf.Rad2Deg * Mathf.Atan(q.x);
        }

        static uint RayTestSolid(int3 coord) {
            MapData pointInfo = CPUMapManager.SampleMap(coord);
            return (uint)pointInfo.viscosity;
        }

        public void Update(Transform CamTsf) {
            perspectives[activePersp].Update();
            CamTsf.SetLocalPositionAndRotation(camTsf.position, camTsf.rotation);
            UnityEngine.Camera cam = CamTsf.GetChild(0).GetComponent<UnityEngine.Camera>();
            if (cullingMask == cam.cullingMask) return;
            cam.cullingMask = cullingMask;
        }

        static void SetAnimatorLook(float pitch, float deltaYaw) {
            float angleX = -(Mathf.InverseLerp(S.MinimumX, S.MaximumX, pitch) * 2 - 1);
            PlayerHandler.data.animator.SetFloat("LookY", angleX);
            float yaw = PlayerHandler.data.animator.GetFloat("LookX");
            yaw = (yaw + deltaYaw) * 0.75f;
            PlayerHandler.data.animator.SetFloat("LookX", yaw);
        }

        private interface ICameraPerspective {
            public void Activate();
            public void Update();
            public Quaternion rotation { get; }
        }

        private class FirstPersonCamera : ICameraPerspective {
            private WeakReference<PlayerCamera> _camera;
            private Quaternion SmoothCharacterRot;
            private Quaternion SmoothCameraRot;
            private PlayerCamera camera => _camera.TryGetTarget(out PlayerCamera tg) ? tg : null;
            public Quaternion rotation => math.mul(math.normalize(camera.characterRot), math.normalize(camera?.camTsf.rotation ?? Quaternion.identity));
            public FirstPersonCamera(PlayerCamera cm) {
                this._camera = new WeakReference<PlayerCamera>(cm);
            }
            public void Activate() {
                camera.camTsf.position = new float3(0, 2.5f, 0);
                camera.camTsf.rotation.eulerAngles = new(camera.camTsf.rotation.eulerAngles.x, 0, 0);
                camera.cullingMask &= ~(1 << LayerMask.NameToLayer("Self"));
                SmoothCharacterRot = camera.characterRot;
                SmoothCameraRot = camera.camTsf.rotation;
            }
            public void Update() {
                if (!camera.moved) return;
                camera.moved = false;
                
                SmoothCharacterRot *= Quaternion.Euler(0f, camera.Rot.y, 0f);
                SmoothCameraRot *= Quaternion.Euler(-camera.Rot.x, 0f, 0f);

                if (S.clampVerticalRotation)
                    SmoothCameraRot = ClampRotationAroundXAxis(SmoothCameraRot);

                if (S.smooth) {
                    camera.characterRot = Quaternion.Slerp(camera.characterRot, SmoothCharacterRot,
                        S.smoothTime * Time.deltaTime);
                    camera.camTsf.rotation = Quaternion.Slerp(camera.camTsf.rotation, SmoothCharacterRot,
                        S.smoothTime * Time.deltaTime);
                } else {
                    camera.characterRot = SmoothCharacterRot;
                    camera.camTsf.rotation = SmoothCameraRot;
                }
                SetAnimatorLook(GetAngleX(camera.camTsf.rotation), camera.Rot.y);
            }
        }

        private class LockedThirdPerson : ICameraPerspective {
            private readonly WeakReference<PlayerCamera> _camera;
            private Quaternion SmoothCharacterRot;
            private Quaternion SmoothCameraRot;
            private PlayerCamera camera => _camera.TryGetTarget(out PlayerCamera tg) ? tg : null;
            public Quaternion rotation => math.mul(math.normalize(camera.characterRot), math.normalize(camera?.camTsf.rotation ?? Quaternion.identity));
            const float height = 2.5f;
            const float distance = 10f;
            public LockedThirdPerson(PlayerCamera cm) {
                this._camera = new WeakReference<PlayerCamera>(cm);
            }
            public void Activate() {
                camera.camTsf.position = new float3(0, 2.5f, 0);
                camera.camTsf.rotation.eulerAngles = new(camera.camTsf.rotation.eulerAngles.x, 0, 0);
                camera.cullingMask |= 1 << LayerMask.NameToLayer("Self");
                SmoothCharacterRot = camera.characterRot;
                SmoothCameraRot = camera.camTsf.rotation;
            }
            public void Update() {
                if (!camera.moved) return;
                camera.moved = false;

                SmoothCharacterRot *= Quaternion.Euler(0f, camera.Rot.y, 0f);
                SmoothCameraRot *= Quaternion.Euler(-camera.Rot.x, 0f, 0f);

                if (S.clampVerticalRotation)
                    SmoothCameraRot = ClampRotationAroundXAxis(SmoothCameraRot);

                if (S.smooth) {
                    camera.characterRot = Quaternion.Slerp(camera.characterRot, SmoothCharacterRot,
                        S.smoothTime * Time.deltaTime);
                    camera.camTsf.rotation = Quaternion.Slerp(camera.camTsf.rotation, SmoothCameraRot,
                        S.smoothTime * Time.deltaTime);
                } else {
                    camera.characterRot = SmoothCharacterRot;
                    camera.camTsf.rotation = SmoothCameraRot;
                }
                SetCameraOffset(camera);
                SetAnimatorLook(GetAngleX(camera.camTsf.rotation), camera.Rot.y);
            }

            private void SetCameraOffset(PlayerCamera cm) {
                float backDist = distance;
                float distGS = distance / Config.CURRENT.Quality.Terrain.value.lerpScale;
                if (CPUMapManager.RayCastTerrain(cm.context.position, cm.Facing * Vector3.back, distGS, RayTestSolid, out float3 hitPt))
                    backDist = math.distance(hitPt, cm.context.position);

                float3 backOffset = math.mul(math.normalize(cm.camTsf.rotation), new float3(0, 0, -backDist));
                //Camera local rotation will have Y-axis rotation applied to it
                cm.camTsf.position = (float3)Vector3.up * height + backOffset;
            }
        }

        private class FreeRotateThirdPerson : ICameraPerspective {
            private readonly WeakReference<PlayerCamera> _camera;
            private Quaternion SmoothCharacterRot;
            private Quaternion SmoothCameraRot;
            private PlayerCamera camera => _camera.TryGetTarget(out PlayerCamera tg) ? tg : null;
            public Quaternion rotation {
                get {
                    Quaternion yaw = Quaternion.Euler(0f, camera.characterRot.eulerAngles.y, 0f);
                    Quaternion pitch = Quaternion.Euler(camera.camTsf.rotation.eulerAngles.x, 0f, 0f);

                    return math.mul(yaw, pitch);
                }
            }
            const float height = 2.5f;
            const float distance = 10f;
            private float yaw;
            private float pitch;
            public FreeRotateThirdPerson(PlayerCamera cm) {
                this._camera = new WeakReference<PlayerCamera>(cm);
            }
            public void Activate() {
                camera.cullingMask |= 1 << LayerMask.NameToLayer("Self");
                pitch = 0; yaw = 180;
                SmoothCharacterRot = camera.characterRot;
                SmoothCameraRot = Quaternion.AngleAxis(yaw, Vector3.up);
                camera.camTsf.rotation = SmoothCameraRot;
                SetCameraOffset(camera);
            }

            public void Update() {
                if (!camera.moved) return;
                camera.moved = false;

                float rotation = PlayerMovement.InputDir.x * S.Sensitivity;
                SmoothCharacterRot *= Quaternion.Euler(0, rotation, 0);
                PlayerMovement.InputDir.x = 0; //Stop lateral movement
                yaw -= rotation;

                yaw += camera.Rot.y;
                pitch -= camera.Rot.x;
                if (S.clampVerticalRotation)
                    pitch = Mathf.Clamp(pitch, S.MinimumX, S.MaximumX);

                SmoothCameraRot = Quaternion.AngleAxis(yaw, Vector3.up) *
                                    Quaternion.AngleAxis(pitch, Vector3.right);
                if (S.smooth) {
                    camera.characterRot = Quaternion.Slerp(camera.characterRot, SmoothCharacterRot,
                        S.smoothTime * Time.deltaTime);
                    camera.camTsf.rotation = Quaternion.Slerp(camera.camTsf.rotation, SmoothCameraRot,
                        S.smoothTime * Time.deltaTime);
                } else {
                    camera.characterRot = SmoothCharacterRot;
                    camera.camTsf.rotation = SmoothCameraRot;
                }
                SetCameraOffset(camera);
                SetAnimatorLook(GetAngleX(camera.camTsf.rotation), rotation);
            }
            
            private static void SetCameraOffset(PlayerCamera cm) {
                float backDist = distance;
                float distGS = distance / Config.CURRENT.Quality.Terrain.value.lerpScale;
                float3 dir = math.mul(math.normalize(cm.camTsf.rotation), new float3(0, 0, -1));
                dir = math.mul(math.normalize(cm.characterRot), dir);
                if (CPUMapManager.RayCastTerrain(cm.context.position, dir, distGS, RayTestSolid, out float3 hitPt))
                    backDist = math.distance(hitPt, cm.context.position);

                float3 backOffset = math.mul(math.normalize(cm.camTsf.rotation), new float3(0, 0, -backDist));
                cm.camTsf.position = (float3)Vector3.up * height + backOffset;
            }
        }
    }
}
