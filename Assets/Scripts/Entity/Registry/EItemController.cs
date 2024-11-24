using Unity.Mathematics;
using System.Runtime.InteropServices;
using System;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;

public class ItemController : EntityController
{
    private unsafe Entity* entity;
    private unsafe EItem.EItemEntity* item => (EItem.EItemEntity*)entity->obj;
    private EItem.EItemSetting settings => EItem.EItemEntity.settings;
    private bool active = false;

    private MeshRenderer meshRenderer;
    private MeshFilter meshFilter;

    public override unsafe void Initialize(IntPtr Entity)
    {
        this.entity = (Entity*)Entity;
        this.active = true;

        float3 GCoord = new (item->GCoord);
        float lerpScale = WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value.lerpScale;
        int chunkSize = WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value.mapChunkSize;
        this.transform.position = CPUDensityManager.GSToWS(GCoord - settings.collider.offset) + (float3)Vector3.up;

        meshRenderer = gameObject.GetComponent<MeshRenderer>();
        meshFilter = gameObject.GetComponent<MeshFilter>();
        SpriteExtruder.Extrude(new SpriteExtruder.ExtrudeSettings{
            ImageIndex = item->item.Index,
            SampleSize = settings.SpriteSampleSize,
            AlphaClip = settings.AlphaClip,
            ExtrudeHeight = settings.ExtrudeHeight,
        }, OnMeshRecieved);
        base.Initialize(Entity);
    }

    public void OnMeshRecieved(RBMeshes.SharedMeshInfo<RBMeshes.SVert> meshInfo){
        if(active) {
            meshFilter.sharedMesh = meshInfo.GenerateMesh(UnityEngine.Rendering.IndexFormat.UInt32);
        } meshInfo.Release();
    }

    public unsafe void FixedUpdate(){
        if(!entity->active) return;
        EntityManager.AssertEntityLocation(entity, item->GCoord);    
        TerrainColliderJob.Transform rTransform = item->tCollider.transform;
        rTransform.position = CPUDensityManager.GSToWS(rTransform.position - settings.collider.offset);
        this.transform.SetPositionAndRotation(rTransform.position, rTransform.rotation);
    }
    public override unsafe void Update()
    {
        if(!entity->active) {
            Disable();
            return;
        }
        
    }

    public unsafe override void Disable(){ 
        if(!active) return;
        active = false;

        EntityManager.ESTree.Delete((int)entity->info.SpatialId);
        Marshal.FreeHGlobal((IntPtr)item);
        Marshal.FreeHGlobal((IntPtr)entity);
        Destroy(gameObject);
        base.Disable();
     }
    public unsafe void OnDrawGizmos(){
        if(!active) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(item->tCollider.transform.position, settings.collider.size * 2);
    }

}
