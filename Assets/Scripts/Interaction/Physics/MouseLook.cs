using System;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public class MouseLook
{
    public float XSensitivity = 2f;
    public float YSensitivity = 2f;
    public bool clampVerticalRotation = true;
    public float MinimumX = -90F;
    public float MaximumX = 90F;
    public bool smooth;
    public float smoothTime = 5f;
    public static bool active;

    private float2 Rot;


    private Quaternion m_CharacterTargetRot;
    private Quaternion m_CameraTargetRot;

    public MouseLook(Transform character, Transform camera)
    {
        m_CharacterTargetRot = character.localRotation;
        m_CameraTargetRot = camera.localRotation;
        active = true;

        InputPoller.AddBinding(new InputPoller.ActionBind("Look Horizontal", LookX), "4.0::Movement");
        InputPoller.AddBinding(new InputPoller.ActionBind("Look Vertical", LookY), "4.0::Movement");
    }

    private void LookX(float x){
        Rot.x = x * XSensitivity;
        active = true;
    }

    private void LookY(float y){
        Rot.y = y * YSensitivity;
        active = true;
    }


    public void LookRotation(Transform character, Transform camera)
    {
        if(!active) return;
        active = false;

        m_CharacterTargetRot *= Quaternion.Euler (0f, Rot.y, 0f);
        m_CameraTargetRot *= Quaternion.Euler (-Rot.x, 0f, 0f);

        if(clampVerticalRotation)
            m_CameraTargetRot = ClampRotationAroundXAxis (m_CameraTargetRot);

        if(smooth)
        {
            character.localRotation = Quaternion.Slerp (character.localRotation, m_CharacterTargetRot,
                smoothTime * Time.deltaTime);
            camera.localRotation = Quaternion.Slerp (camera.localRotation, m_CameraTargetRot,
                smoothTime * Time.deltaTime);
        }
        else
        {
            character.localRotation = m_CharacterTargetRot;
            camera.localRotation = m_CameraTargetRot;
        }
    }

    Quaternion ClampRotationAroundXAxis(Quaternion q)
    {
        q.x /= q.w;
        q.y /= q.w;
        q.z /= q.w;
        q.w = 1.0f;

        float angleX = 2.0f * Mathf.Rad2Deg * Mathf.Atan (q.x);

        angleX = Mathf.Clamp (angleX, MinimumX, MaximumX);

        q.x = Mathf.Tan (0.5f * Mathf.Deg2Rad * angleX);

        return q;
    }

}
