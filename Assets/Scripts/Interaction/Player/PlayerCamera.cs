using System;
using MapStorage;
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
        private static ref PlayerStreamer.Player data => ref PlayerHandler.data;
        private static ref Quaternion m_CharacterTargetRot => ref data.collider.transform.rotation;
        private static ref Quaternion m_CameraTargetRot => ref data.camera.rotation;
        public float3 forward => perspectives[activePersp].forward;
        public float3 right => perspectives[activePersp].right;
        public Transform subTsf => transform.GetChild(0);
        private UnityEngine.Camera camera;
        private Transform transform;
        private bool moved;
        private float2 Rot;

        private ICameraPerspective[] perspectives;
        private int activePersp = 0;

        public PlayerCamera() {
            moved = true;
            transform = GameObject.Find("CameraHandler").transform;
            camera = subTsf.GetComponent<UnityEngine.Camera>();
            InputPoller.AddBinding(new InputPoller.ActionBind("Look Horizontal", LookX), "4.5::Movement");
            InputPoller.AddBinding(new InputPoller.ActionBind("Look Vertical", LookY), "4.5::Movement");
            InputPoller.AddBinding(new InputPoller.ActionBind("Toggle Perspective", TogglePerspective), "2.5::Subscene");

            activePersp = 0;
            perspectives = new ICameraPerspective[]{
                new FirstPersonCamera(),
                new LockedThirdPerson(),
                new FreeRotateThirdPerson(),
            }; perspectives[0].Activate(this);

            ReParent(data.player.transform);
        }

        public void ReParent(Transform t) => transform.SetParent(t, worldPositionStays: false);

        private void LookX(float x) {
            Rot.x = x * S.Sensitivity;
            moved = true;
        }

        private void LookY(float y) {
            Rot.y = y * S.Sensitivity;
            moved = true;
        }

        private void TogglePerspective(float _) {
            activePersp = (activePersp + 1) % perspectives.Length;
            perspectives[activePersp].Activate(this);
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

        public void Update() {
            perspectives[activePersp].Update(this);
            transform.SetLocalPositionAndRotation(data.camera.position, data.camera.rotation);
        }

        static void SetAnimatorLook(float pitch, float deltaYaw) {
            float angleX = -(Mathf.InverseLerp(S.MinimumX, S.MaximumX, pitch) * 2 - 1);
            PlayerHandler.data.animator.SetFloat("LookY", angleX);
            float yaw = PlayerHandler.data.animator.GetFloat("LookX");
            yaw = (yaw + deltaYaw) * 0.75f;
            PlayerHandler.data.animator.SetFloat("LookX", yaw);
        }

        private interface ICameraPerspective {
            public void Activate(PlayerCamera cm);
            public void Update(PlayerCamera cm);
            public float3 forward { get; }
            public float3 right { get; }
        }

        private class FirstPersonCamera : ICameraPerspective {
            public float3 forward => PlayerHandler.camera.transform.forward;
            public float3 right => PlayerHandler.camera.transform.right;
            public void Activate(PlayerCamera cm) {
                data.camera.position = new float3(0, 2.5f, 0);
                data.camera.rotation.eulerAngles = new(data.camera.rotation.eulerAngles.x, 0, 0);
                cm.camera.cullingMask &= ~(1 << LayerMask.NameToLayer("Self"));
            }
            public void Update(PlayerCamera cm) {
                if (!cm.moved) return;
                cm.moved = false;

                m_CharacterTargetRot *= Quaternion.Euler(0f, cm.Rot.y, 0f);
                m_CameraTargetRot *= Quaternion.Euler(-cm.Rot.x, 0f, 0f);

                if (S.clampVerticalRotation)
                    m_CameraTargetRot = ClampRotationAroundXAxis(m_CameraTargetRot);

                if (S.smooth) {
                    data.collider.transform.rotation = Quaternion.Slerp(data.collider.transform.rotation, m_CharacterTargetRot,
                        S.smoothTime * Time.deltaTime);
                    data.camera.rotation = Quaternion.Slerp(data.camera.rotation, m_CameraTargetRot,
                        S.smoothTime * Time.deltaTime);
                } else {
                    data.collider.transform.rotation = m_CharacterTargetRot;
                    data.camera.rotation = m_CameraTargetRot;
                }
                SetAnimatorLook(GetAngleX(m_CameraTargetRot), cm.Rot.y);
            }
        }

        private class LockedThirdPerson : ICameraPerspective {
            public float3 forward => PlayerHandler.camera.transform.forward;
            public float3 right => PlayerHandler.camera.transform.right;
            const float height = 2.5f;
            const float distance = 10f;
            public void Activate(PlayerCamera cm) {
                cm.camera.cullingMask |= 1 << LayerMask.NameToLayer("Self");
                SetCameraOffset(cm);
            }
            public void Update(PlayerCamera cm) {
                if (!cm.moved) return;
                cm.moved = false;

                m_CharacterTargetRot *= Quaternion.Euler(0f, cm.Rot.y, 0f);
                m_CameraTargetRot *= Quaternion.Euler(-cm.Rot.x, 0f, 0f);

                if (S.clampVerticalRotation)
                    m_CameraTargetRot = ClampRotationAroundXAxis(m_CameraTargetRot);

                if (S.smooth) {
                    data.collider.transform.rotation = Quaternion.Slerp(data.collider.transform.rotation, m_CharacterTargetRot,
                        S.smoothTime * Time.deltaTime);
                    data.camera.rotation = Quaternion.Slerp(data.camera.rotation, m_CameraTargetRot,
                        S.smoothTime * Time.deltaTime);
                } else {
                    data.collider.transform.rotation = m_CharacterTargetRot;
                    data.camera.rotation = m_CameraTargetRot;
                }
                SetCameraOffset(cm);
                SetAnimatorLook(GetAngleX(m_CameraTargetRot), cm.Rot.y);
            }

            private void SetCameraOffset(PlayerCamera cm) {
                float backDist = distance;
                float distGS = distance / Config.CURRENT.Quality.Terrain.value.lerpScale;
                if (CPUMapManager.RayCastTerrain(data.position, -cm.forward, distGS, RayTestSolid, out float3 hitPt))
                    backDist = math.distance(hitPt, data.position);

                float3 backOffset = math.mul(math.normalize(data.camera.rotation), new float3(0, 0, -backDist));
                //Camera local rotation will have Y-axis rotation applied to it
                data.camera.position = (float3)Vector3.up * height + backOffset;
            }
        }

        private class FreeRotateThirdPerson : ICameraPerspective {
            public float3 forward {
                get {
                    Quaternion yaw = Quaternion.Euler(0f, data.collider.transform.rotation.eulerAngles.y, 0f);
                    Quaternion pitch = Quaternion.Euler(data.camera.rotation.eulerAngles.x, 0f, 0f);

                    Quaternion combined = math.mul(yaw, pitch);
                    return math.mul(combined, new float3(0, 0, 1));
                }
            }
            public float3 right {
                get {
                    Quaternion yaw = Quaternion.Euler(0f, data.collider.transform.rotation.eulerAngles.y, 0f);
                    Quaternion pitch = Quaternion.Euler(data.camera.rotation.eulerAngles.x, 0f, 0f);

                    Quaternion combined = math.mul(yaw, pitch);
                    return math.mul(combined, new float3(1, 0, 0));
                }
            }
            const float height = 2.5f;
            const float distance = 10f;
            private float yaw;
            private float pitch;
            public void Activate(PlayerCamera cm) {
                cm.camera.cullingMask |= 1 << LayerMask.NameToLayer("Self");
                
                pitch = 0; yaw = 180;
                data.camera.rotation = Quaternion.AngleAxis(yaw, Vector3.up);
                SetCameraOffset(cm);
            }

            public void Update(PlayerCamera cm) {
                if (!cm.moved) return;
                cm.moved = false;

                float rotation = PlayerMovement.InputDir.x * S.Sensitivity;
                m_CharacterTargetRot *= Quaternion.Euler(0, rotation, 0);
                PlayerMovement.InputDir.x = 0; //Stop lateral movement
                yaw -= rotation;

                yaw += cm.Rot.y;
                pitch -= cm.Rot.x;
                if (S.clampVerticalRotation)
                    pitch = Mathf.Clamp(pitch, S.MinimumX, S.MaximumX);

                m_CameraTargetRot = Quaternion.AngleAxis(yaw, Vector3.up) *
                                    Quaternion.AngleAxis(pitch, Vector3.right);
                if (S.smooth) {
                    data.collider.transform.rotation = Quaternion.Slerp(data.collider.transform.rotation, m_CharacterTargetRot,
                        S.smoothTime * Time.deltaTime);
                    data.camera.rotation = Quaternion.Slerp(data.camera.rotation, m_CameraTargetRot,
                        S.smoothTime * Time.deltaTime);
                } else {
                    data.collider.transform.rotation = m_CharacterTargetRot;
                    data.camera.rotation = m_CameraTargetRot;
                }
                SetCameraOffset(cm);
                SetAnimatorLook(GetAngleX(m_CameraTargetRot), rotation);
            }

            private void SetCameraOffset(PlayerCamera cm) {
                float backDist = distance;
                float distGS = distance / Config.CURRENT.Quality.Terrain.value.lerpScale;
                float3 dir = math.mul(math.normalize(data.camera.rotation), new float3(0, 0, -1));
                dir = math.mul(math.normalize(data.collider.transform.rotation), dir);
                if (CPUMapManager.RayCastTerrain(data.position, dir, distGS, RayTestSolid, out float3 hitPt))
                    backDist = math.distance(hitPt, data.position);
                
                float3 backOffset = math.mul(math.normalize(data.camera.rotation), new float3(0, 0, -backDist));
                data.camera.position = (float3)Vector3.up * height + backOffset;
            }
        }
    }
}
