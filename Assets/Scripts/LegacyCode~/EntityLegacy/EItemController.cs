using Unity.Mathematics;
using System.Runtime.InteropServices;
using System;
using UnityEngine;
using static TerrainGeneration.Readback.IVertFormat;
using TerrainGeneration.Readback;
using WorldConfig;
using WorldConfig.Generation.Entity;

public class ItemController : EntityController
{
    private EItem.EItemEntity entity;
    private EItem.EItemSetting settings => EItem.EItemEntity.settings;
    private bool active = false;

    private MeshRenderer meshRenderer;
    private MeshFilter meshFilter;

    public override void Initialize(Entity Entity)
    {
        this.entity = (EItem.EItemEntity)Entity;
        this.active = true;

        float3 GCoord = new (entity.GCoord);
        this.transform.position = CPUDensityManager.GSToWS(GCoord - settings.collider.offset) + (float3)Vector3.up;

        meshRenderer = gameObject.GetComponent<MeshRenderer>();
        meshFilter = gameObject.GetComponent<MeshFilter>();
        SpriteExtruder.Extrude(new SpriteExtruder.ExtrudeSettings{
            ImageIndex = entity.item.Slot.TexIndex,
            SampleSize = settings.SpriteSampleSize,
            AlphaClip = settings.AlphaClip,
            ExtrudeHeight = settings.ExtrudeHeight,
        }, OnMeshRecieved);
        base.Initialize(Entity);
    }

    private void OnMeshRecieved(ReadbackTask<SVert>.SharedMeshInfo meshInfo){
        if(active) {
            meshFilter.sharedMesh = meshInfo.GenerateMesh(UnityEngine.Rendering.IndexFormat.UInt32);
        } meshInfo.Release();
    }

    public void FixedUpdate(){
        if(!entity.active) return;
        EntityManager.AssertEntityLocation(entity, entity.GCoord);    
        TerrainCollider.Transform rTransform = entity.tCollider.transform;
        rTransform.position = CPUDensityManager.GSToWS(rTransform.position - settings.collider.offset);
        this.transform.SetPositionAndRotation(rTransform.position, rTransform.rotation);
    }
    public override void Update()
    {
        if(!entity.active) {
            Disable();
            return;
        }
    }

    public override void Disable(){ 
        if(!active) return;
        active = false;

        Destroy(gameObject);
        base.Disable();
     }
    public void OnDrawGizmos(){
        if(!active) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(entity.tCollider.transform.position, settings.collider.size * 2);
    }

}
