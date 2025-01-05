using Unity.Mathematics;
using System.Runtime.InteropServices;
using System;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;

public class RabbitController : EntityController
{
    private Animator animator;
    private unsafe Entity* entity;
    private unsafe Rabbit.RabbitEntity* rabbit => (Rabbit.RabbitEntity*)entity->obj;
    private Rabbit.RabbitSetting settings => Rabbit.RabbitEntity.settings;
    private bool active = false;

    public override unsafe void Initialize(IntPtr Entity)
    {
        this.entity = (Entity*)Entity;
        this.active = true;

        float3 GCoord = new (rabbit->GCoord);
        float lerpScale = WorldOptions.CURRENT.Quality.Rendering.value.lerpScale;
        int chunkSize = WorldOptions.CURRENT.Quality.Rendering.value.mapChunkSize;
        animator = this.GetComponent<Animator>();
        this.transform.position = CPUDensityManager.GSToWS(GCoord - settings.collider.offset) + (float3)Vector3.up * 1;
        base.Initialize(Entity);
    }

    public unsafe void FixedUpdate(){
        if(!entity->active) return;
        EntityManager.AssertEntityLocation(entity, rabbit->GCoord);    
        TerrainColliderJob.Transform rTransform = rabbit->tCollider.transform;
        rTransform.position = CPUDensityManager.GSToWS(rTransform.position - settings.collider.offset);
        this.transform.SetPositionAndRotation(rTransform.position, rTransform.rotation);
    }
    public override unsafe void Update()
    {
        if(!entity->active) {
            Disable();
            return;
        }

        if(rabbit->TaskIndex == 2) 
            animator.SetBool("IsMoving", true);
        else {
            animator.SetBool("IsMoving", false);
            if(rabbit->TaskDuration > 2.0f) animator.SetBool("IsScratching", true);
            else animator.SetBool("IsScratching", false);
        }
        
    }

    public unsafe override void Disable(){ 
        if(!active) return;
        active = false;

        if(rabbit->pathFinder.hasPath) UnsafeUtility.Free(rabbit->pathFinder.path, Unity.Collections.Allocator.Persistent);
        EntityManager.ESTree.Delete((int)entity->info.SpatialId);
        Marshal.FreeHGlobal((IntPtr)rabbit);
        Marshal.FreeHGlobal((IntPtr)entity);
        Destroy(gameObject);
        base.Disable();
     }

    public unsafe void OnDrawGizmos(){
        if(!active) return;
        //Gizmos.color = Color.green;
        //Gizmos.DrawSphere(CPUDensityManager.GSToWS(rabbit->GCoord), 0.1f);
        Gizmos.color = Color.red; 
        Gizmos.DrawWireCube(transform.position, settings.collider.size * 2);
        if(rabbit->pathFinder.hasPath){
            PathFinder.PathInfo pathFinder = rabbit->pathFinder;
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
