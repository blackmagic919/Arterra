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
    /// <summary> The maximum speed the player can walk at, in terms of world space. </summary>
    public float walkSpeed = 10f;
    /// <summary> The maximum speed the player can run at, in terms of world space. </summary>
    public float runSpeed = 15f;
    /// <summary>  How much force is applied to the player when they jump, in terms of world space. </summary>
    public float jumpForce = 8f;
    /// <summary> How much speed the user gains when moving, in terms of world space. The acceleration is added onto velocity
    /// <see cref="GroundFriction"/> meaning the comparative strength of friction increases with velocity. </summary>
    public float acceleration = 50f;
    /// <summary> How far below the player the ground needs to be for the player to be 'on the ground'. 
    /// Being on the ground may affect their ability to jump and the friction they experience. </summary>
    public float groundStickDist = 0.05f;
    /// <summary> Whether the player can fly. </summary>
    public bool EnableFlying = false;
    /// <summary> The collider that represents the player's physical shape. </summary>
    public Profile Collider;

    public object Clone(){
        return new Movement{
            GroundFriction = this.GroundFriction,
            walkSpeed = this.walkSpeed,
            jumpForce = this.jumpForce,
            acceleration = this.acceleration,
            groundStickDist = this.groundStickDist
        };
    }
    [Serializable]
    /// <summary> The collider that represents the player's physical shape. </summary>
    public struct Profile{
        /// <summary>  The size of the collider. </summary>
        public float3 size;
        /// <summary> The offset of the collider from the player's position. </summary>
        public float3 offset;
    }
}
}
public class RigidFPController : MonoBehaviour
{
    public WorldConfig.Gameplay.Movement Setting => Config.CURRENT.GamePlay.Movement.value;
    public Camera cam;
    public MouseLook mouseLook;
    private TerrainCollider tCollider;
    private float2 InputDir;
    private float moveSpeed;
    private bool IsFlying;
    public bool active;



    public void Initialize(){
        mouseLook = new MouseLook(transform, cam.transform);
        moveSpeed = Setting.walkSpeed;
        IsFlying = false;

        InputDir = float2.zero;
        InputPoller.AddBinding(new InputPoller.ActionBind("Move Vertical",(float y) => InputDir.y = y), "4.0::Movement");
        InputPoller.AddBinding(new InputPoller.ActionBind("Move Horizontal", (float x) => InputDir.x = x), "4.0::Movement");
        InputPoller.AddBinding(new InputPoller.ActionBind("Sprint", (float x) => {moveSpeed = Setting.runSpeed;}), "4.0::Movement");
        InputPoller.AddBinding(new InputPoller.ActionBind("Jump", (_null_) => {
            float3 posGS = CPUMapManager.WSToGS(this.transform.position) + Setting.Collider.offset;
            if(tCollider.SampleCollision(posGS, new float3(Setting.Collider.size.x, -Setting.groundStickDist, Setting.Collider.size.z), out _))
                tCollider.velocity += Setting.jumpForce * (float3)Vector3.up;
        }), "4.0::Movement");

        InputPoller.AddBinding(new InputPoller.ActionBind("ToggleFly", (_null_) => {
            IsFlying = !IsFlying;
        }), "4.0::Movement");
        InputPoller.AddBinding(new InputPoller.ActionBind("Ascend", (_null_) => {
            if(!IsFlying || !Setting.EnableFlying) return;
            if(tCollider.velocity.y < moveSpeed) tCollider.velocity.y += Setting.acceleration * Time.deltaTime;
        }), "4.0::Movement");
        InputPoller.AddBinding(new InputPoller.ActionBind("Descend", (_null_) => {
            if(!IsFlying || !Setting.EnableFlying) return;
            if(tCollider.velocity.y > -moveSpeed) tCollider.velocity.y -= Setting.acceleration * Time.deltaTime;
        }), "4.0::Movement");
        
    }

    public void ActivateCharacter()
    {
        tCollider = new TerrainCollider(Setting.Collider);
        active = true;
    }

    public void Update(){
        if(!active) return;

        mouseLook.LookRotation(transform, cam.transform); 
        float2 desiredMove = ((float3)(cam.transform.forward*InputDir.y + cam.transform.right*InputDir.x)).xz;
        float2 deltaV = Setting.acceleration * Time.deltaTime * desiredMove;

        if(IsFlying) tCollider.velocity.y *= 1 - Setting.GroundFriction;
        tCollider.velocity.xz *= 1 - Setting.GroundFriction;
        if(math.length(tCollider.velocity.xz) < moveSpeed) 
            tCollider.velocity.xz += deltaV;
        moveSpeed = Setting.walkSpeed;
        InputDir = float2.zero;
    }

    private void FixedUpdate(){
        if(!active) return;
        if(!Setting.EnableFlying) IsFlying = false;
        if(IsFlying) tCollider.FixedUpdate(this.transform, false);
        else tCollider.FixedUpdate(this.transform, true);
    }

    public void OnDisable(){
        PlayerHandler.Release();
    }
}
