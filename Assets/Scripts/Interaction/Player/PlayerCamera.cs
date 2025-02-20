using System;
using Unity.Mathematics;
using UnityEngine;


namespace WorldConfig.Gameplay.Player{
    /// <summary> Controls how the camera rotates and responds to player input. Notably
    /// settings controlling the camera's responsiveness, sensitivity, and limits to 
    /// its rotation are contained here.  </summary>
    [Serializable]
    public struct Camera
    {
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

    public class PlayerCamera{
        public static Camera S => Config.CURRENT.GamePlay.Player.value.Camera;
        private bool active;
        private float2 Rot;
        private Quaternion m_CharacterTargetRot;
        private Quaternion m_CameraTargetRot;

        public PlayerCamera(ref PlayerStreamer.Player character)
        {
            m_CharacterTargetRot = character.rotation;
            m_CameraTargetRot = character.cameraRot;
            active = true;

            InputPoller.AddBinding(new InputPoller.ActionBind("Look Horizontal", LookX), "4.0::Movement");
            InputPoller.AddBinding(new InputPoller.ActionBind("Look Vertical", LookY), "4.0::Movement");
        }

        private void LookX(float x){
            Rot.x = x * S.Sensitivity;
            active = true;
        }

        private void LookY(float y){
            Rot.y = y * S.Sensitivity;
            active = true;
        }


        public void LookRotation(ref PlayerStreamer.Player character)
        {
            if(!active) return;
            active = false;

            m_CharacterTargetRot *= Quaternion.Euler (0f, Rot.y, 0f);
            m_CameraTargetRot *= Quaternion.Euler (-Rot.x, 0f, 0f);

            if(S.clampVerticalRotation)
                m_CameraTargetRot = ClampRotationAroundXAxis(m_CameraTargetRot);

            if(S.smooth)
            {
                character.rotation = Quaternion.Slerp (character.rotation, m_CharacterTargetRot,
                    S.smoothTime * Time.deltaTime);
                character.cameraRot = Quaternion.Slerp (character.cameraRot, m_CameraTargetRot,
                    S.smoothTime * Time.deltaTime);
            }
            else
            {
                character.rotation = m_CharacterTargetRot;
                character.cameraRot = m_CameraTargetRot;
            }
        }

        static Quaternion ClampRotationAroundXAxis(Quaternion q)
        {
            q.x /= q.w;
            q.y /= q.w;
            q.z /= q.w;
            q.w = 1.0f;

            float angleX = 2.0f * Mathf.Rad2Deg * Mathf.Atan (q.x);

            angleX = Mathf.Clamp (angleX, S.MinimumX, S.MaximumX);

            q.x = Mathf.Tan (0.5f * Mathf.Deg2Rad * angleX);

            return q;
        }
    }
}
