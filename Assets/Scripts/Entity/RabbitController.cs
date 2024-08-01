using Unity.Mathematics;
using System.Runtime.InteropServices;
using System;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;

public class RabbitController : EntityController
{
    private TerrainCollider tCollider;
    private Animator animator;
    private unsafe Entity* entity;
    private unsafe Rabbit.RabbitEntity* Rabbit => (Rabbit.RabbitEntity*)entity->obj;
    private bool active = false;
    public float GroundStickDist = 0.05f;
    public float moveSpeed = 10f;
    public float acceleration = 50f;
    public float friction = 0.075f;
    public float rotSpeed = 90f;

    public override unsafe void Initialize(IntPtr Entity)
    {
        this.entity = (Entity*)Entity;
        this.active = true;

        float3 GCoord = new (Rabbit->GCoord);
        float lerpScale = WorldStorageHandler.WORLD_OPTIONS.Rendering.value.lerpScale;
        int chunkSize = WorldStorageHandler.WORLD_OPTIONS.Rendering.value.mapChunkSize;
        tCollider = this.GetComponent<TerrainCollider>();
        animator = this.GetComponent<Animator>();
        tCollider.Active = true;
        this.transform.position = CPUDensityManager.GSToWS(GCoord - tCollider.offset) + (float3)Vector3.up * 1;
    }

    public unsafe bool IsGrounded(){return tCollider.IsCollided(CPUDensityManager.WSToGS(this.transform.position), new float3(tCollider.size.x, -GroundStickDist, tCollider.size.z));}

    public unsafe void FixedUpdate(){
        if(!entity->active) return;
        EntityManager.AssertEntityLocation(entity, Rabbit->GCoord);    
    }
    public override unsafe void Update()
    {
        if(!entity->active) {
            Release();
            return;
        }

        Rabbit->GCoord = (int3)math.floor(CPUDensityManager.WSToGS(this.transform.position) + tCollider.offset);
        //This is synchronous so it's safe to access the entity's GCoord

        if(Rabbit->pathFinder.hasPath != 0) FollowPath(&Rabbit->pathFinder);
        else if(IsGrounded()){
            tCollider.velocity *= 1 - friction;
            tCollider.useGravity = false;
        } else tCollider.useGravity = true;
    }

    private unsafe void FollowPath(Rabbit.RabbitEntity.PathInfo* pathFinder){
        if((pathFinder->hasPath & 0x2) == 0) {ReleasePath(); return;} //Fulfill promise to release
        if(pathFinder->currentInd == pathFinder->pathLength) {pathFinder->hasPath &= 0x3; return;}
        else if((pathFinder->hasPath & 0x4) == 0) {
            animator.SetBool("IsMoving", true);
            pathFinder->hasPath |= 0x4;
        }

        int dir = pathFinder->path[pathFinder->currentInd];
        int3 dest = pathFinder->currentPos + new int3((dir / 9) - 1, (dir / 3 % 3) - 1, (dir % 3) - 1);
        if(math.all(Rabbit->GCoord == dest)){
            pathFinder->currentPos = dest;
            pathFinder->currentInd++;
        } else {
            float3 aim = math.normalize(dest - Rabbit->GCoord);
            this.transform.rotation = Quaternion.RotateTowards(this.transform.rotation, Quaternion.LookRotation(aim), rotSpeed * Time.deltaTime);
            if(math.length(tCollider.velocity) < moveSpeed) 
                tCollider.velocity += acceleration * Time.deltaTime * aim;
            tCollider.velocity *= 1 - friction;
        }
    }

    public override void OnDisable()
    {
        Release();
    }

    private unsafe void Release(){
        if(!active) return;
        active = false;

        if(Rabbit->pathFinder.hasPath != 0) UnsafeUtility.Free(Rabbit->pathFinder.path, Unity.Collections.Allocator.Persistent);
        EntityManager.ESTree.Delete((int)entity->info.SpatialId);
        Marshal.FreeHGlobal((IntPtr)Rabbit);
        Marshal.FreeHGlobal((IntPtr)entity);
        Destroy(gameObject);
    }

    private unsafe void ReleasePath(){
        if(Rabbit->pathFinder.hasPath != 0) 
            UnsafeUtility.Free(Rabbit->pathFinder.path, Unity.Collections.Allocator.Persistent);
        animator.SetBool("IsMoving", false);
        Rabbit->pathFinder.hasPath = 0x0;
    }

    public unsafe void OnDrawGizmos(){
        if(!active) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(transform.position, new Vector3(tCollider.size.x, tCollider.size.y, tCollider.size.z) * 2);
        if((Rabbit->pathFinder.hasPath & 0x4) != 0){
            Rabbit.RabbitEntity.PathInfo pathFinder = Rabbit->pathFinder;
            int ind = pathFinder.currentInd;
            while(ind != pathFinder.pathLength){
                int dir = pathFinder.path[ind];
                int3 dest = pathFinder.currentPos + new int3((dir / 9) - 1, (dir / 3 % 3) - 1, (dir % 3) - 1);
                Gizmos.DrawLine(CPUDensityManager.GSToWS(pathFinder.currentPos - tCollider.offset), CPUDensityManager.GSToWS(dest - tCollider.offset));
                pathFinder.currentPos = dest;
                ind++;
            }
        }
    }
}
