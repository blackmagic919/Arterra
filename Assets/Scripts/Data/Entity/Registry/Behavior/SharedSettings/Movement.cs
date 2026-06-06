using System;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Data.Entity;
using TerrainCollider = Arterra.GamePlay.Interaction.TerrainCollider;
using Arterra.Data.Entity.Behavior;
using System.Collections.Generic;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class Movement : IBehaviorSetting {

        public enum FollowType {
            Planar = 0,
            Move3D = 1,
            Full3D = 2,
        }
        public float walkSpeed;
        public float runSpeed;
        public int pathDistance;//~31
        public float acceleration; //~100
        public float rotSpeed;//~180

        //This is in game-ticks not real-time
        const uint pathPersistence = 200;

        public object Clone() {
            return new Movement {
                walkSpeed = walkSpeed,
                runSpeed = runSpeed,
                pathDistance = pathDistance,
                acceleration = acceleration,
                rotSpeed = rotSpeed
            };
        }

        public static float3 RandomDirection(ref Unity.Mathematics.Random random) {
            float3 normal = new(random.NextFloat(-1, 1), random.NextFloat(-1, 1), random.NextFloat(-1, 1));
            if (math.length(normal) == 0) return math.forward();
            else return math.normalizesafe(normal);
        }

        public static float3 RandomDirection2D(ref Unity.Mathematics.Random random) {
            float3 normal = new(random.NextFloat(-1, 1), 0, random.NextFloat(-1, 1));
            if (math.length(normal) == 0) return math.forward();
            else return math.normalizesafe(normal);
        }

        public static float3 Normalize(float3 var) {
            if (math.all(var == 0)) return new float3(0, 1, 0);
            return math.normalize(var);
        }

        public static (Quaternion, float3) StaticDirect(EntitySetting.ProfileInfo profile, ref PathFinder.PathInfo finder, TerrainCollider tCollider, FollowType movement = FollowType.Planar) {
            //Entity has fallen off path
            finder.stepDuration++;
            if (math.any(math.abs(tCollider.transform.position - finder.currentPos) > profile.bounds)) finder.hasPath = false;
            if (finder.currentInd == finder.path.Length) finder.hasPath = false;
            if (finder.stepDuration > pathPersistence) { finder.hasPath = false; }
            if (!finder.hasPath) return (tCollider.transform.rotation, float3.zero);
            byte dir = finder.path[finder.currentInd];
            int3 nextPos = finder.currentPos + new int3((dir / 9) - 1, (dir / 3 % 3) - 1, (dir % 3) - 1);
            if (!PathFinder.VerifyProfile(nextPos, profile, EntityJob.cxt)) { finder.hasPath = false; }

            float3 aim = Normalize(nextPos - (float3)tCollider.transform.position);
            Quaternion rot = Quaternion.identity;
            switch(movement) {
                case FollowType.Planar:
                    aim = math.normalizesafe(new float3(aim.x, 0, aim.z));
                    if (math.any(aim != 0)) rot = Quaternion.LookRotation(aim);
                    break;
                case FollowType.Move3D:
                    if (math.any(aim.xz != 0))  rot = Quaternion.LookRotation(new float3(aim.x, 0, aim.z));
                    break;
                default:
                    if (math.any(aim != 0)) rot = Quaternion.LookRotation(aim);
                    break;
            }

            if (math.all(math.abs(tCollider.transform.position - nextPos) <= 1)) {
                finder.currentPos = nextPos;
                finder.stepDuration = 0;
                finder.currentInd++;
            } return (rot, aim);
        }

        public static (Quaternion, float3) DynamicDirect(EntitySetting.ProfileInfo profile, ref PathFinder.PathInfo finder, TerrainCollider tCollider, float3 target, FollowType movement = FollowType.Planar) {
            finder.stepDuration++;
            if (math.any(math.abs(tCollider.transform.position - finder.currentPos) > profile.bounds))
                finder.hasPath = false;
            if (finder.currentInd >= finder.path.Length) finder.hasPath = false;
            if (finder.stepDuration > pathPersistence) finder.hasPath = false;
            if (!finder.hasPath) return (tCollider.transform.rotation, float3.zero);

            byte dir = finder.path[finder.currentInd];
            int3 nextPos = finder.currentPos + new int3((dir / 9) - 1, (dir / 3 % 3) - 1, (dir % 3) - 1);
            if (!PathFinder.VerifyProfile(nextPos, profile, EntityJob.cxt)) { finder.hasPath = false; }
            if (math.distance(tCollider.transform.position, target) < math.distance(finder.destination, target))
                finder.hasPath = false;

            float3 aim = Normalize(nextPos - (float3)tCollider.transform.position);
            Quaternion rot;
            switch(movement) {
                case FollowType.Planar:
                    aim = math.normalizesafe(new float3(aim.x, 0, aim.z));
                    rot = Quaternion.LookRotation(aim);
                    break;
                case FollowType.Move3D:
                    rot = Quaternion.LookRotation(new float3(aim.x, 0, aim.z));
                    break;
                default:
                    rot = Quaternion.LookRotation(aim);
                    break;
            }

            if (math.all(math.abs(tCollider.transform.position - nextPos) <= 1)) {
                finder.currentPos = nextPos;
                finder.stepDuration = 0;
                finder.currentInd++;
            } return (rot, aim);
        }


        public static (Quaternion, float3) StaticDirect(List<PathFinder.MatProfileE> profile, uint3 bounds, ref PathFinder.PathInfo finder, TerrainCollider tCollider, FollowType movement = FollowType.Planar) {
            //Entity has fallen off path
            finder.stepDuration++;
            if (math.any(math.abs(tCollider.transform.position - finder.currentPos) > bounds)) finder.hasPath = false;
            if (finder.currentInd == finder.path.Length) finder.hasPath = false;
            if (finder.stepDuration > pathPersistence) { finder.hasPath = false; }
            if (!finder.hasPath) return  (tCollider.transform.rotation, float3.zero);
            byte dir = finder.path[finder.currentInd];
            int3 nextPos = finder.currentPos + new int3((dir / 9) - 1, (dir / 3 % 3) - 1, (dir % 3) - 1);
            if (!PathFinder.VerifyMatProfile(nextPos, bounds, profile)) { finder.hasPath = false; }

            float3 aim = Normalize(nextPos - (float3)tCollider.transform.position);
            Quaternion rot;
            switch(movement) {
                case FollowType.Planar:
                    aim = math.normalizesafe(new float3(aim.x, 0, aim.z));
                    rot = Quaternion.LookRotation(aim);
                    break;
                case FollowType.Move3D:
                    rot = Quaternion.LookRotation(new float3(aim.x, 0, aim.z));
                    break;
                default:
                    rot = Quaternion.LookRotation(aim);
                    break;
            }

            if (math.all(math.abs(tCollider.transform.position - nextPos) <= 1)) {
                finder.currentPos = nextPos;
                finder.stepDuration = 0;
                finder.currentInd++;
            } return (rot, aim);
        }

        struct BoidDMtrx {
            public float3 SeperationDir;
            public float3 AlignmentDir;
            public float3 CohesionDir;
            public uint count;
        }
    }
}