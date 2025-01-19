using UnityEngine;
using Unity.Mathematics;
using System;
using WorldConfig;
using WorldConfig.Gameplay;

namespace WorldConfig.Gameplay{
/// <summary>
/// A collection of settings that describe how the player moves.
/// Movement settings may change during gameplay.
/// </summary>
[System.Serializable]
public class Movement : ICloneable{
    /// <summary> How quickly the player slows down, in terms of world space, when touching the ground. </summary>
    public float GroundFriction = 0.25f;
    /// <summary> The maximum speed the player can run at, in terms of world space. </summary>
    public float runSpeed = 10f;
    /// <summary>  How much force is applied to the player when they jump, in terms of world space. </summary>
    public float jumpForce = 8f;
    /// <summary> How much speed the user gains when moving, in terms of world space. The acceleration is added onto velocity
    /// <see cref="GroundFriction"/> meaning the comparative strength of friction increases with velocity. </summary>
    public float acceleration = 50f;
    /// <summary> How far below the player the ground needs to be for the player to be 'on the ground'. 
    /// Being on the ground may affect their ability to jump and the friction they experience. </summary>
    public float groundStickDist = 0.05f;

    public object Clone(){
        return new Movement{
            GroundFriction = this.GroundFriction,
            runSpeed = this.runSpeed,
            jumpForce = this.jumpForce,
            acceleration = this.acceleration,
            groundStickDist = this.groundStickDist
        };
    }
}
}
public class RigidFPController : MonoBehaviour
{
    public Movement Setting => Config.CURRENT.GamePlay.Movement.value;
    public Camera cam;
    public MouseLook mouseLook;
    private TerrainCollider tCollider;
    public bool active;
    private float2 InputDir;



    public void Initialize(){
        mouseLook = new MouseLook(transform, cam.transform);
        tCollider = GetComponent<TerrainCollider>();
        tCollider.Active = false;
        active = false;

        InputDir = float2.zero;
        InputPoller.AddBinding(new InputPoller.ActionBind("Move Vertical",(float y) => InputDir.y = y), "4.0::Movement");
        InputPoller.AddBinding(new InputPoller.ActionBind("Move Horizontal", (float x) => InputDir.x = x), "4.0::Movement");
        InputPoller.AddBinding(new InputPoller.ActionBind("Jump", (_null_) => {
            float3 posGS = CPUDensityManager.WSToGS(this.transform.position) + tCollider.offset;
            if(tCollider.SampleCollision(posGS, new float3(tCollider.size.x, -Setting.groundStickDist, tCollider.size.z), out _))
                tCollider.velocity += Setting.jumpForce * (float3)Vector3.up;
        }), "4.0::Movement");
    }

    public void ActivateCharacter()
    {
        tCollider.Active = true;
        active = true;
    }

    public void Update(){
        if(!active) return;
        
        mouseLook.LookRotation (transform, cam.transform); 
        float2 desiredMove = ((float3)(cam.transform.forward*InputDir.y + cam.transform.right*InputDir.x)).xz;
        float2 deltaV = Setting.acceleration * Time.deltaTime * desiredMove;

        tCollider.velocity.xz *= 1 - Setting.GroundFriction;
        if(math.length(tCollider.velocity.xz) < Setting.runSpeed) 
            tCollider.velocity.xz += deltaV;
        InputDir = float2.zero;
    }

    public void OnDisable(){
        PlayerHandler.Release();
    }
}
