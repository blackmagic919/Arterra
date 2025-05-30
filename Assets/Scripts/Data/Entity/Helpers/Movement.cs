using System;
using Unity.Mathematics;
using UnityEngine;
using WorldConfig.Generation.Entity;

[Serializable]
public struct Movement{
    public float walkSpeed;
    public float runSpeed; 
    public float acceleration; //~100
    public float rotSpeed;//~180
    public int pathDistance;//~31
    public float AverageIdleTime; //~2.5

    //This is in game-ticks not real-time
    const uint pathPersistence = 200;

    public static void FollowStaticPath(EntitySetting.ProfileInfo profile, ref PathFinder.PathInfo finder, ref TerrainColliderJob tCollider, float moveSpeed,
                        float rotSpeed, float acceleration, bool AllowVerticalRotation = false){  
        //Entity has fallen off path
        finder.stepDuration++;
        if(math.any(math.abs(tCollider.transform.position - finder.currentPos) > profile.bounds)) finder.hasPath = false;
        if(finder.currentInd == finder.path.Length) finder.hasPath = false;
        if(finder.stepDuration > pathPersistence) { finder.hasPath = false; }
        if(!finder.hasPath) return;
        byte dir = finder.path[finder.currentInd];
        int3 nextPos = finder.currentPos + new int3((dir / 9) - 1, (dir / 3 % 3) - 1, (dir % 3) - 1);
        if(!PathFinder.VerifyProfile(nextPos, profile, EntityJob.cxt)){ finder.hasPath = false; return; }

        int3 GCoord = (int3)math.floor(tCollider.transform.position);
        if(math.all(math.abs(GCoord - nextPos) <= 1)){
            finder.currentPos = nextPos;
            finder.stepDuration = 0;
            finder.currentInd++;
        } else {
            float3 aim = math.normalize(nextPos - GCoord);
            Quaternion rot = tCollider.transform.rotation;
            if(!AllowVerticalRotation) { if(math.any(aim.xz != 0)){
                rot = Quaternion.LookRotation(new Vector3(aim.x, 0, aim.z));
            }} else rot = Quaternion.LookRotation(new Vector3(aim.x, aim.y, aim.z));
            
            tCollider.transform.rotation = Quaternion.RotateTowards(tCollider.transform.rotation, rot, rotSpeed * EntityJob.cxt.deltaTime);
            if(math.length(tCollider.velocity) < moveSpeed) 
                tCollider.velocity += acceleration * EntityJob.cxt.deltaTime * aim;
        }
    }

    public static void FollowDynamicPath(EntitySetting.ProfileInfo profile, ref PathFinder.PathInfo finder, ref TerrainColliderJob tCollider, float3 target,
                            float moveSpeed, float rotSpeed, float acceleration, bool AllowVerticalRotation = false){
        finder.stepDuration++;
        if(math.any(math.abs(tCollider.transform.position - finder.currentPos) > profile.bounds)) finder.hasPath = false;
        if(finder.currentInd >= finder.path.Length) finder.hasPath = false;
        if(finder.stepDuration > pathPersistence) finder.hasPath = false;
        if(math.distance(tCollider.transform.position, target) < math.distance(finder.destination, target))
            finder.hasPath = false;
        if(!finder.hasPath) return;
        byte dir = finder.path[finder.currentInd];
        int3 nextPos = finder.currentPos + new int3((dir / 9) - 1, (dir / 3 % 3) - 1, (dir % 3) - 1);
        if(!PathFinder.VerifyProfile(nextPos, profile, EntityJob.cxt)){ finder.hasPath = false; return; }

        int3 GCoord = (int3)math.floor(tCollider.transform.position);
        if(math.all(math.abs(GCoord - nextPos) <= 1)){
            finder.currentPos = nextPos;
            finder.stepDuration = 0;
            finder.currentInd++;
        } else {
            float3 aim = math.normalize(nextPos - GCoord);
            Quaternion rot = tCollider.transform.rotation;
            if(!AllowVerticalRotation) { if(math.any(aim.xz != 0)){
                rot = Quaternion.LookRotation(new Vector3(aim.x, 0, aim.z));
            }} else rot = Quaternion.LookRotation(new Vector3(aim.x, aim.y, aim.z));

            tCollider.transform.rotation = Quaternion.RotateTowards(tCollider.transform.rotation, rot, rotSpeed * EntityJob.cxt.deltaTime);
            if(math.length(tCollider.velocity) < moveSpeed) 
                tCollider.velocity += acceleration * EntityJob.cxt.deltaTime * aim;
        }
    }
}
