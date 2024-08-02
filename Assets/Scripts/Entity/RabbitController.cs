using Unity.Mathematics;
using System.Runtime.InteropServices;
using System;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;

public class RabbitController : EntityController
{
    private Animator animator;
    private unsafe Entity* entity;
    private unsafe Rabbit.RabbitEntity* Rabbit => (Rabbit.RabbitEntity*)entity->obj;
    private bool active = false;

    private bool IsMoving = false;

    public override unsafe void Initialize(IntPtr Entity)
    {
        this.entity = (Entity*)Entity;
        this.active = true;

        float3 GCoord = new (Rabbit->GCoord);
        float lerpScale = WorldStorageHandler.WORLD_OPTIONS.Rendering.value.lerpScale;
        int chunkSize = WorldStorageHandler.WORLD_OPTIONS.Rendering.value.mapChunkSize;
        animator = this.GetComponent<Animator>();
        this.transform.position = CPUDensityManager.GSToWS(GCoord - Rabbit->tCollider.offset) + (float3)Vector3.up * 1;
    }

    public unsafe void FixedUpdate(){
        if(!entity->active) return;
        EntityManager.AssertEntityLocation(entity, Rabbit->GCoord);    
        TerrainColliderJob.Transform rTransform = Rabbit->tCollider.transform;
        rTransform.position = CPUDensityManager.GSToWS(rTransform.position - Rabbit->tCollider.offset);
        this.transform.SetPositionAndRotation(rTransform.position, rTransform.rotation);
    }
    public override unsafe void Update()
    {
        if(!entity->active) {
            Release();
            return;
        }

        if(Rabbit->pathFinder.hasPath && !IsMoving) 
            animator.SetBool("IsMoving", IsMoving = true);
        else if(!Rabbit->pathFinder.hasPath && IsMoving)
            animator.SetBool("IsMoving", IsMoving = false);
        
    }

    public override void OnDisable()
    {
        Release();
    }

    private unsafe void Release(){
        if(!active) return;
        active = false;

        if(Rabbit->pathFinder.hasPath) UnsafeUtility.Free(Rabbit->pathFinder.path, Unity.Collections.Allocator.Persistent);
        EntityManager.ESTree.Delete((int)entity->info.SpatialId);
        Marshal.FreeHGlobal((IntPtr)Rabbit);
        Marshal.FreeHGlobal((IntPtr)entity);
        Destroy(gameObject);
    }

    public unsafe void OnDrawGizmos(){
        if(!active) return;
        Gizmos.color = Color.red; 
        TerrainColliderJob tCollider = Rabbit->tCollider;
        Gizmos.DrawWireCube(transform.position, new Vector3(tCollider.size.x, tCollider.size.y, tCollider.size.z) * 2);
        if(Rabbit->pathFinder.hasPath){
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
