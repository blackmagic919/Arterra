using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using static CPUDensityManager;
using System;

public class RigidFPController : MonoBehaviour
{
    public Camera cam;
    public MouseLook mouseLook = new MouseLook();
    private TerrainCollider tCollider;
    [Range(0, 1)]
    public float GroundFriction = 0.25f;
    public float runSpeed = 10f;
    public float jumpForce = 8f;
    public float acceleration = 50f;
    public float groundStickDist = 0.05f;
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


    public void Start(){
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
        float3 desiredMove = cam.transform.forward*input.y + cam.transform.right*input.x;
        float3 deltaV = acceleration * Time.deltaTime * desiredMove;

        float3 posGS = CPUDensityManager.WSToGS(this.transform.position) + tCollider.offset;
        if(tCollider.SampleCollision(posGS, new float3(tCollider.size.x, -groundStickDist, tCollider.size.z), out float3 normal)){
            if(Input.GetKeyDown(KeyCode.Space)) tCollider.velocity += jumpForce * (float3)Vector3.up;
            else deltaV = Vector3.ProjectOnPlane(deltaV, normal);
            tCollider.velocity *= 1 - GroundFriction;
            tCollider.useGravity = false;
        }else tCollider.useGravity = true;

        if(math.length(tCollider.velocity) < runSpeed) 
            tCollider.velocity += deltaV;
    }
}
