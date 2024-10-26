using Unity.Mathematics;
using System.Runtime.InteropServices;
using System;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;

public class BirdController : EntityController
{
    private Animator animator;
    private unsafe Entity* entity;
    private unsafe Bird.BirdEntity* bird => (Bird.BirdEntity*)entity->obj;
    private Bird.BirdSetting settings => Bird.BirdEntity.settings;
    private bool active = false;

    public override unsafe void Initialize(IntPtr Entity)
    {
        this.entity = (Entity*)Entity;
        this.active = true;

        float3 GCoord = new (bird->GCoord);
        float lerpScale = WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value.lerpScale;
        int chunkSize = WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value.mapChunkSize;
        animator = this.GetComponent<Animator>();
        this.transform.position = CPUDensityManager.GSToWS(GCoord - settings.collider.offset) + (float3)Vector3.up * 1;
    }

    public unsafe void FixedUpdate(){
        if(!entity->active) return;
        EntityManager.AssertEntityLocation(entity, bird->GCoord);    
        TerrainColliderJob.Transform rTransform = bird->tCollider.transform;
        rTransform.position = CPUDensityManager.GSToWS(rTransform.position - settings.collider.offset);
        this.transform.SetPositionAndRotation(rTransform.position, rTransform.rotation);
    }
    public override unsafe void Update()
    {
        if(!entity->active) {
            Release();
            return;
        }
        
        if(bird->TaskIndex == 1){
            animator.SetBool("IsFlying", true);
            if(bird->tCollider.velocity.y >= 0) animator.SetBool("IsAscending", true);
            else animator.SetBool("IsAscending", false);
        }
        else{
            animator.SetBool("IsFlying", false);
            if(bird->TaskDuration > 2.0f) animator.SetBool("IsHopping", true);
            else animator.SetBool("IsHopping", false);
        }
    }

    public override void OnDisable()
    {
        Release();
    }

    private unsafe void Release(){
        if(!active) return;
        active = false;

        if(bird->pathFinder.hasPath) UnsafeUtility.Free(bird->pathFinder.path, Unity.Collections.Allocator.Persistent);
        EntityManager.ESTree.Delete((int)entity->info.SpatialId);
        Marshal.FreeHGlobal((IntPtr)bird);
        Marshal.FreeHGlobal((IntPtr)entity);
        Destroy(gameObject);
    }

    public unsafe void OnDrawGizmos(){
        if(!active) return;
        Gizmos.color = Color.green; 
        TerrainColliderJob tCollider = bird->tCollider;
        Gizmos.DrawWireCube(transform.position, settings.collider.size * 2);
        float3 location = tCollider.transform.position - settings.collider.offset;
        Gizmos.DrawLine(CPUDensityManager.GSToWS(location), CPUDensityManager.GSToWS(location + bird->flightDirection));
        /*if(bird->pathFinder.hasPath){
            PathFinder.PathInfo pathFinder = bird->pathFinder;
            int ind = pathFinder.currentInd;
            while(ind != pathFinder.pathLength){
                int dir = pathFinder.path[ind];
                int3 dest = pathFinder.currentPos + new int3((dir / 9) - 1, (dir / 3 % 3) - 1, (dir % 3) - 1);
                Gizmos.DrawLine(CPUDensityManager.GSToWS(pathFinder.currentPos - settings.collider.offset), CPUDensityManager.GSToWS(dest - settings.collider.offset));
                pathFinder.currentPos = dest;
                ind++;
            }
        }*/
    }
}
