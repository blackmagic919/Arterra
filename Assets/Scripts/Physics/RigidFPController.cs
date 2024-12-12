using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using System;

public class RigidFPController : MonoBehaviour
{
    public RigidFPControllerSettings Setting => WorldStorageHandler.WORLD_OPTIONS.GamePlay.Movement.value;
    [System.Serializable]
    public class RigidFPControllerSettings : ICloneable{
        public float GroundFriction = 0.25f;
        public float runSpeed = 10f;
        public float jumpForce = 8f;
        public float acceleration = 50f;
        public float groundStickDist = 0.05f;

        public object Clone(){
            return new RigidFPControllerSettings{
                GroundFriction = this.GroundFriction,
                runSpeed = this.runSpeed,
                jumpForce = this.jumpForce,
                acceleration = this.acceleration,
                groundStickDist = this.groundStickDist
            };
        }
    }
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
        InputPoller.AddBinding("Move Vertical", "GamePlay", (float y) => InputDir.y = y);
        InputPoller.AddBinding("Move Horizontal", "GamePlay", (float x) => InputDir.x = x);
        InputPoller.AddBinding("Jump", "GamePlay", (_null_) => {
            float3 posGS = CPUDensityManager.WSToGS(this.transform.position) + tCollider.offset;
            if(tCollider.SampleCollision(posGS, new float3(tCollider.size.x, -Setting.groundStickDist, tCollider.size.z), out _))
                tCollider.velocity += Setting.jumpForce * (float3)Vector3.up;
        });
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

        float3 posGS = CPUDensityManager.WSToGS(this.transform.position) + tCollider.offset;
        if(tCollider.SampleCollision(posGS, new float3(tCollider.size.x, -Setting.groundStickDist, tCollider.size.z), out _)){
            tCollider.velocity.y *= 1 - Setting.GroundFriction;
            tCollider.useGravity = false;
        }else tCollider.useGravity = true;
        tCollider.velocity.xz *= 1 - Setting.GroundFriction;

        if(math.length(tCollider.velocity.xz) < Setting.runSpeed) 
            tCollider.velocity.xz += deltaV;
        InputDir = float2.zero;
    }

    public void OnDisable(){
        PlayerHandler.Release();
    }
}
