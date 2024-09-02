using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using static CPUDensityManager;
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
    public MouseLook mouseLook = new MouseLook();
    private TerrainCollider tCollider;
    [Range(0, 1)]
    public RigidFPControllerSettings setting = null;
    public bool active;

    private Vector2 GetInput()
    {
        
        Vector2 input = new Vector2
        {
            x = Input.GetAxis("Horizontal"),
            y = Input.GetAxis("Vertical")
        };
        return input;
    }


    public void Initialize(RigidFPControllerSettings setting){
        this.setting = setting;
        mouseLook.Init(transform, cam.transform);
        tCollider = GetComponent<TerrainCollider>();
        tCollider.Active = false;
        active = false;
    }

    public void ActivateCharacter()
    {
        tCollider.Active = true;
        active = true;
    }

    

    public void Update(){
        if(!active) return;

        float2 input = GetInput();
        mouseLook.LookRotation (transform, cam.transform);
        float2 desiredMove = ((float3)(cam.transform.forward*input.y + cam.transform.right*input.x)).xz;
        float2 deltaV = setting.acceleration * Time.deltaTime * desiredMove;

        float3 posGS = CPUDensityManager.WSToGS(this.transform.position) + tCollider.offset;
        if(tCollider.SampleCollision(posGS, new float3(tCollider.size.x, -setting.groundStickDist, tCollider.size.z), out float3 normal)){
            if(Input.GetKeyDown(KeyCode.Space)) tCollider.velocity += setting.jumpForce * (float3)Vector3.up;
            tCollider.velocity.y *= 1 - setting.GroundFriction;
            tCollider.useGravity = false;
        }else tCollider.useGravity = true;
        tCollider.velocity.xz *= 1 - setting.GroundFriction;

        if(math.length(tCollider.velocity.xz) < setting.runSpeed) 
            tCollider.velocity.xz += deltaV;
    }
}
