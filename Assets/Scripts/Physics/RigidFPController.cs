using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using System;

public class RigidFPController : MonoBehaviour
{
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
    [Range(0, 1)]
    public RigidFPControllerSettings setting = null;
    public bool active;
    private float2 InputDir;



    public void Initialize(RigidFPControllerSettings setting){
        this.setting = setting;
        mouseLook = new MouseLook(transform, cam.transform);
        tCollider = GetComponent<TerrainCollider>();
        tCollider.Active = false;
        active = false;

        InputDir = float2.zero;
        InputPoller.AddBinding(new InputPoller.Binding("Move Vertical", "GamePlay", InputPoller.BindPoll.Axis, (float y) => InputDir.y = y));
        InputPoller.AddBinding(new InputPoller.Binding("Move Horizontal", "GamePlay", InputPoller.BindPoll.Axis, (float x) => InputDir.x = x));
        InputPoller.AddBinding(new InputPoller.Binding("Jump", "GamePlay", InputPoller.BindPoll.Down, (_null_) => {
            float3 posGS = CPUDensityManager.WSToGS(this.transform.position) + tCollider.offset;
            if(tCollider.SampleCollision(posGS, new float3(tCollider.size.x, -setting.groundStickDist, tCollider.size.z), out _))
                tCollider.velocity += setting.jumpForce * (float3)Vector3.up;
        }));
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
        float2 deltaV = setting.acceleration * Time.deltaTime * desiredMove;

        float3 posGS = CPUDensityManager.WSToGS(this.transform.position) + tCollider.offset;
        if(tCollider.SampleCollision(posGS, new float3(tCollider.size.x, -setting.groundStickDist, tCollider.size.z), out _)){
            tCollider.velocity.y *= 1 - setting.GroundFriction;
            tCollider.useGravity = false;
        }else tCollider.useGravity = true;
        tCollider.velocity.xz *= 1 - setting.GroundFriction;

        if(math.length(tCollider.velocity.xz) < setting.runSpeed) 
            tCollider.velocity.xz += deltaV;
        InputDir = float2.zero;
    }

    public void OnDisable(){
        PlayerHandler.Release();
    }
}
