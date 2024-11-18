using Unity.Mathematics;
using System.Runtime.InteropServices;
using System;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;

public class FoxController : EntityController
{
    private Animator animator;
    private unsafe Entity* entity;
    private unsafe Fox.FoxEntity* fox => (Fox.FoxEntity*)entity->obj;
    private Fox.FoxSetting settings => Fox.FoxEntity.settings;
    private bool active = false;

    public override unsafe void Initialize(IntPtr Entity)
    {
        this.entity = (Entity*)Entity;
        this.active = true;

        float3 GCoord = new (fox->GCoord);
        float lerpScale = WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value.lerpScale;
        int chunkSize = WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value.mapChunkSize;
        animator = this.GetComponent<Animator>();
        this.transform.position = CPUDensityManager.GSToWS(GCoord - settings.collider.offset) + (float3)Vector3.up * 1;
        base.Initialize(Entity);
    }

    public unsafe void FixedUpdate(){
        if(!entity->active) return;
        EntityManager.AssertEntityLocation(entity, fox->GCoord);    
        TerrainColliderJob.Transform rTransform = fox->tCollider.transform;
        rTransform.position = CPUDensityManager.GSToWS(rTransform.position - settings.collider.offset);
        this.transform.SetPositionAndRotation(rTransform.position, rTransform.rotation);
    }
    public override unsafe void Update()
    {
        if(!entity->active) {
            Release();
            return;
        }

        if(fox->TaskIndex == 2) 
            animator.SetBool("IsWalking", true);
        else {
            animator.SetBool("IsWalking", false);
            if(fox->TaskDuration > 2.0f) animator.SetBool("IsSitting", true);
            else animator.SetBool("IsSitting", false);
        }
        
    }

    public override void Disable(){ Release(); }

    private unsafe void Release(){
        if(!active) return;
        active = false;

        if(fox->pathFinder.hasPath) UnsafeUtility.Free(fox->pathFinder.path, Unity.Collections.Allocator.Persistent);
        EntityManager.ESTree.Delete((int)entity->info.SpatialId);
        Marshal.FreeHGlobal((IntPtr)fox);
        Marshal.FreeHGlobal((IntPtr)entity);
        Destroy(gameObject);
    }

    public unsafe void OnDrawGizmos(){
        if(!active) return;
        Gizmos.color = Color.red; 
        Gizmos.DrawWireCube(transform.position, settings.collider.size * 2);
        if(fox->pathFinder.hasPath){
            PathFinder.PathInfo pathFinder = fox->pathFinder;
            int ind = pathFinder.currentInd;
            while(ind != pathFinder.pathLength){
                int dir = pathFinder.path[ind];
                int3 dest = pathFinder.currentPos + new int3((dir / 9) - 1, (dir / 3 % 3) - 1, (dir % 3) - 1);
                Gizmos.DrawLine(CPUDensityManager.GSToWS(pathFinder.currentPos - settings.collider.offset), CPUDensityManager.GSToWS(dest - settings.collider.offset));
                pathFinder.currentPos = dest;
                ind++;
            }
        }
    }
}
