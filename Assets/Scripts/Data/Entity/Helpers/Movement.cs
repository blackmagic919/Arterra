using System;
using Unity.Mathematics;
using UnityEngine;
using WorldConfig.Generation.Entity;

[Serializable]
public struct Movement {
    public Genetics.GeneFeature walkSpeed;
    public Genetics.GeneFeature runSpeed;
    public int pathDistance;//~31
    public float acceleration; //~100
    public float rotSpeed;//~180
    public float AverageIdleTime; //~2.5

    //This is in game-ticks not real-time
    const uint pathPersistence = 200;

    public void InitGenome(uint entityType) {
        Genetics.AddGene(entityType, ref walkSpeed);
        Genetics.AddGene(entityType, ref runSpeed);
    }

    private static float3 Normalize(float3 var) {
        if (math.all(var == 0)) return new float3(0, 1, 0);
        return math.normalize(var);
    }

    public static void FollowStaticPath(EntitySetting.ProfileInfo profile, ref PathFinder.PathInfo finder, ref TerrainCollider tCollider, float moveSpeed,
                        float rotSpeed, float acceleration, bool AllowVerticalRotation = false) {
        //Entity has fallen off path
        finder.stepDuration++;
        if (math.any(math.abs(tCollider.transform.position - finder.currentPos) > profile.bounds)) finder.hasPath = false;
        if (finder.currentInd == finder.path.Length) finder.hasPath = false;
        if (finder.stepDuration > pathPersistence) { finder.hasPath = false; }
        if (!finder.hasPath) return;
        byte dir = finder.path[finder.currentInd];
        int3 nextPos = finder.currentPos + new int3((dir / 9) - 1, (dir / 3 % 3) - 1, (dir % 3) - 1);
        if (!PathFinder.VerifyProfile(nextPos, profile, EntityJob.cxt)) { finder.hasPath = false; }

        float3 aim = Normalize(nextPos - tCollider.transform.position);
        Quaternion rot = tCollider.transform.rotation;
        if (!AllowVerticalRotation) {
            if (math.any(aim.xz != 0)) {
                aim = math.normalize(new float3(aim.x, 0, aim.z));
                rot = Quaternion.LookRotation(aim);
            }
        } else rot = Quaternion.LookRotation(aim);

        tCollider.transform.rotation = Quaternion.RotateTowards(tCollider.transform.rotation, rot, rotSpeed * EntityJob.cxt.deltaTime);
        if (math.length(tCollider.velocity) < moveSpeed)
            tCollider.velocity += acceleration * EntityJob.cxt.deltaTime * aim;

        int3 GCoord = (int3)math.floor(tCollider.transform.position);
        if (math.all(math.abs(GCoord - nextPos) <= 1)) {
            finder.currentPos = nextPos;
            finder.stepDuration = 0;
            finder.currentInd++;
        }
    }

    public static void FollowDynamicPath(EntitySetting.ProfileInfo profile, ref PathFinder.PathInfo finder, ref TerrainCollider tCollider, float3 target,
                            float moveSpeed, float rotSpeed, float acceleration, bool AllowVerticalRotation = false) {
        finder.stepDuration++;
        if (math.any(math.abs(tCollider.transform.position - finder.currentPos) > profile.bounds)) finder.hasPath = false;
        if (finder.currentInd >= finder.path.Length) finder.hasPath = false;
        if (finder.stepDuration > pathPersistence) finder.hasPath = false;

        if (!finder.hasPath) return;
        byte dir = finder.path[finder.currentInd];
        int3 nextPos = finder.currentPos + new int3((dir / 9) - 1, (dir / 3 % 3) - 1, (dir % 3) - 1);
        if (!PathFinder.VerifyProfile(nextPos, profile, EntityJob.cxt)) { finder.hasPath = false; }
        if (math.distance(tCollider.transform.position, target) < math.distance(finder.destination, target))
            finder.hasPath = false;

        float3 aim = Normalize(nextPos - tCollider.transform.position);
        Quaternion rot = tCollider.transform.rotation;
        if (!AllowVerticalRotation) {
            if (math.any(aim.xz != 0)) {
                aim = math.normalize(new float3(aim.x, 0, aim.z));
                rot = Quaternion.LookRotation(aim);
            }
        } else rot = Quaternion.LookRotation(aim);

        tCollider.transform.rotation = Quaternion.RotateTowards(tCollider.transform.rotation, rot, rotSpeed * EntityJob.cxt.deltaTime);
        if (math.length(tCollider.velocity) < moveSpeed)
            tCollider.velocity += acceleration * EntityJob.cxt.deltaTime * aim;

        int3 GCoord = (int3)math.floor(tCollider.transform.position);
        if (math.all(math.abs(GCoord - nextPos) <= 1)) {
            finder.currentPos = nextPos;
            finder.stepDuration = 0;
            finder.currentInd++;
        }
    }

    [Serializable]
    public struct Aquatic {
        public Genetics.GeneFeature DrownTime;
        //Threshold at which the entity will try to swim to the surface
        public Genetics.GeneFeature SurfaceThreshold;
        public float JumpStickDistance;
        public float JumpStrength;
        public EntitySetting.ProfileInfo SurfaceProfile;

        public void InitGenome(uint entityType) {
            Genetics.AddGene(entityType, ref DrownTime);
            Genetics.AddGene(entityType, ref SurfaceThreshold);
        }
    }

    [Serializable]
    public struct Flight {
        //Starts after the profile of the ground entity
        public EntitySetting.ProfileInfo profile;
        public Genetics.GeneFeature AverageFlightTime; //120
        public Genetics.GeneFeature FlyBiasWeight; //0.25\
        [Range(0, 1)]
        public float VerticalFreedom;

        public void InitGenome(uint entityType) {
            Genetics.AddGene(entityType, ref AverageFlightTime);
            Genetics.AddGene(entityType, ref FlyBiasWeight);
        }
    }
    
    [Serializable]
    public struct BoidFlight {
        public EntitySetting.ProfileInfo profile;
        public Genetics.GeneFeature AverageFlightTime; //120
        public Genetics.GeneFeature SeperationWeight; //0.75
        public Genetics.GeneFeature AlignmentWeight; //0.5
        public Genetics.GeneFeature CohesionWeight; //0.25
        public int InfluenceDist; //24
        public int PathDist; //5
        public int MaxSwarmSize; //8
        
        public void InitGenome(uint entityType) {
            Genetics.AddGene(entityType, ref AverageFlightTime);
            Genetics.AddGene(entityType, ref SeperationWeight);
            Genetics.AddGene(entityType, ref AlignmentWeight);
            Genetics.AddGene(entityType, ref CohesionWeight);
        }
    }
}
