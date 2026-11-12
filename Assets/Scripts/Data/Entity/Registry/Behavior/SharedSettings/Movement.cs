using System;
using Arterra.Core.Events;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Data.Entity;
using TerrainCollider = Arterra.GamePlay.Interaction.TerrainCollider;
using Arterra.Data.Entity.Behavior;
using System.Collections.Generic;
using static Arterra.Data.Entity.Behavior.PathFinderBehavior;
using Arterra.Utils;

namespace Arterra.Data.Entity.Behavior {
    public enum TransitionMovement {
        Jump = 0,
        Land = 1
    }

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
        public float rotSpeed;//~180

        //This is in game-ticks not real-time
        const uint pathPersistence = 200;
        private const float FirstRecoveryThreshold = 0.5f;
        private const float SecondRecoveryThreshold = 0.75f;
        private const float FirstRecoveryHeight = 1f;
        private const float SecondRecoveryHeight = 1.5f;
        private const byte FirstRecoveryAttempt = 1;
        private const byte SecondRecoveryAttempt = 2;

        public object Clone() {
            return new Movement {
                walkSpeed = walkSpeed,
                runSpeed = runSpeed,
                pathDistance = pathDistance,
                rotSpeed = rotSpeed
            };
        }

        public readonly struct Steering {
            public readonly Quaternion Rotation;
            public readonly float3 Aim;
            public readonly Optional<TransitionMovement> Mode;
            public readonly float3 Destination;
            public readonly FollowType Type;
            internal readonly PathData Path;
            internal readonly int Index;

            public Steering(Quaternion rotation, float3 aim, Optional<TransitionMovement> mode = default,
                float3 destination = default, PathData path = null, int index = -1, FollowType followType = FollowType.Planar) {
                Rotation = rotation;
                Aim = aim;
                Mode = mode;
                Destination = destination;
                Type = followType;
                Path = path;
                Index = index;
            }
        }

        private const float GroundCheckDepth = 0.05f;
        private const float AccelerationTime = 0.05f;

        private static Steering IdleSteering(TerrainCollider collider) {
            return new Steering(collider.transform.rotation, float3.zero);
        }

        private static Steering DirectMovement(TransitionMovement mode, ref PathInfo finder, TerrainCollider collider) {
            switch (mode) {
                case TransitionMovement.Jump:
                    return DirectAirborne(ref finder, collider, false);
                case TransitionMovement.Land:
                    return DirectAirborne(ref finder, collider, true);
                default:
                    finder.hasPath = false;
                    return IdleSteering(collider);
            }
        }

        private static Steering DirectAirborne(ref PathInfo finder, TerrainCollider collider, bool landing) {
            PathStep step = finder.path[finder.currentInd];
            int3 destination = finder.currentPos + step.Delta;
            float3 offset = destination - (float3)collider.transform.position;
            float3 planar = new float3(offset.x, 0, offset.z);

            bool horizontallyReached = math.lengthsq(planar) <= 0.1f * 0.1f;
            if (horizontallyReached) {
                bool grounded = IsGrounded(collider);
                // Takeoff cannot complete before leaving the ground. A landing cannot
                // complete while we are still flying over its horizontal destination.
                if (landing ? grounded && collider.transform.velocity.y <= 0 : !grounded) {
                    Progress(ref finder);
                    if (finder.currentInd >= finder.path.Length) finder.hasPath = false;
                    return IdleSteering(collider);
                }
            }

            Quaternion rotation = math.lengthsq(planar) > 1E-6f
                ? Quaternion.LookRotation(planar) : collider.transform.rotation;
            return new Steering(rotation, math.normalizesafe(planar), step.Movement, destination,
                finder.path, finder.currentInd);
        }

        private static bool IsGrounded(TerrainCollider collider) {
            float3 size = collider.transform.size;
            return TerrainCollider.SampleCollision((float3)collider.transform.position,
                new float3(size.x, -GroundCheckDepth, size.z));
        }

        public static void FollowStatic(Entity self, TerrainCollider collider, ref PathInfo finder,
            EntitySetting.ProfileInfo profile, float moveSpeed, float rotationSpeed, float timeStep,
            GameEvent moveType, FollowType movement = FollowType.Planar) {
            Steering steering = StaticDirect(profile, ref finder, collider, movement);
            ApplyMovement(self, collider, ref finder, steering, moveSpeed, rotationSpeed, timeStep, moveType);
        }

        public static void FollowStatic(Entity self, TerrainCollider collider, ref PathInfo finder,
            List<MatProfileE> profile, uint3 bounds, float moveSpeed, float rotationSpeed, float timeStep,
            GameEvent moveType, FollowType movement = FollowType.Planar) {
            Steering steering = StaticDirect(profile, bounds, ref finder, collider, movement);
            ApplyMovement(self, collider, ref finder, steering, moveSpeed, rotationSpeed, timeStep, moveType);
        }

        public static void FollowDynamic(Entity self, TerrainCollider collider, ref PathInfo finder,
            EntitySetting.ProfileInfo profile, float3 target, float moveSpeed, float rotationSpeed, float timeStep,
            GameEvent moveType, FollowType movement = FollowType.Planar) {
            Steering steering = DynamicDirect(profile, ref finder, collider, target, movement);
            ApplyMovement(self, collider, ref finder, steering, moveSpeed, rotationSpeed, timeStep, moveType);
        }

        private static void ApplyMovement(Entity self, TerrainCollider collider, ref PathInfo finder, Steering steering,
            float moveSpeed, float rotationSpeed, float timeStep, GameEvent moveType) {
            if (!steering.Mode.Enabled) {
                FollowDefault(self, collider, steering, moveSpeed, rotationSpeed, timeStep, moveType);
                if (steering.Type == FollowType.Planar && finder.hasPath) {
                    float height = RecoveryJumpHeight(finder.stepDuration, finder.recoveryAttempts);
                    if (height > 0 && TryJump(self, collider, height)) {
                        finder.recoveryAttempts |= height == SecondRecoveryHeight
                            ? (byte)(FirstRecoveryAttempt | SecondRecoveryAttempt) : FirstRecoveryAttempt;
                    }
                }
                return;
            }

            FollowMovement(steering.Mode.Value, self, collider, steering, moveSpeed, rotationSpeed, timeStep, moveType);
        }

        private static void FollowDefault(Entity self, TerrainCollider collider, Steering steering,
            float moveSpeed, float rotationSpeed, float timeStep, GameEvent moveType) {
            float acceleration = moveSpeed / AccelerationTime;
            collider.transform.rotation = Quaternion.RotateTowards(collider.transform.rotation, steering.Rotation,
                rotationSpeed * timeStep);

            if (math.length(collider.transform.velocity) >= moveSpeed) return;
            float3 deltaVelocity = acceleration * timeStep * steering.Aim;
            if (math.all(deltaVelocity == 0)) return;

            RefTuple<float3> context = new RefTuple<float3>(deltaVelocity);
            self.eventCtrl.RaiseEvent(moveType, self.info.rtEntityId, null, context);
            collider.transform.velocity += context.Value;
        }

        private static void FollowMovement(TransitionMovement mode, Entity self, TerrainCollider collider,
            Steering steering, float moveSpeed, float rotationSpeed, float timeStep, GameEvent moveType) {
            switch (mode) {
                case TransitionMovement.Jump:
                    FollowJump(self, collider, steering, moveSpeed, rotationSpeed, timeStep, moveType);
                    break;
                case TransitionMovement.Land:
                    FollowLanding(self, collider, steering, moveSpeed, rotationSpeed, timeStep, moveType);
                    break;
            }
        }

        private static void FollowJump(Entity self, TerrainCollider collider, Steering steering,
            float moveSpeed, float rotationSpeed, float timeStep, GameEvent moveType) {
            float gravity = -EntityJob.cxt.gravity.y;
            int3 delta = steering.Path[steering.Index].Delta;
            float launchSpeed = JumpSpeed(delta.y, gravity);
            if (!collider.UseGravity || launchSpeed <= 0) return;

            TryJump(self, collider, delta.y);

            // Derive speed from the edge so it remains stable throughout takeoff.
            float distance = math.length((float2)delta.xz);
            float airSpeed = math.max(moveSpeed, distance / (launchSpeed / gravity));
            FollowAirborne(self, collider, steering, airSpeed, rotationSpeed, timeStep, moveType);
        }

        private static void FollowLanding(Entity self, TerrainCollider collider, Steering steering,
            float moveSpeed, float rotationSpeed, float timeStep, GameEvent moveType) {
            float3 offset = steering.Destination - (float3)collider.transform.position;
            float flightTime = LandingTime(collider.transform.velocity.y, -offset.y, -EntityJob.cxt.gravity.y);
            float airSpeed = flightTime > 0
                ? math.max(moveSpeed, math.length(offset.xz) / flightTime) : math.max(0, moveSpeed);
            FollowAirborne(self, collider, steering, airSpeed, rotationSpeed, timeStep, moveType);
        }

        private static void FollowAirborne(Entity self, TerrainCollider collider, Steering steering,
            float airSpeed, float rotationSpeed, float timeStep, GameEvent moveType) {
            collider.transform.rotation = Quaternion.RotateTowards(collider.transform.rotation, steering.Rotation,
                rotationSpeed * timeStep);

            float3 deltaVelocity = PlanarAcceleration(collider.transform.velocity, steering.Aim, airSpeed, timeStep);
            if (math.all(deltaVelocity == 0)) return;

            RefTuple<float3> context = new RefTuple<float3>(deltaVelocity);
            self.eventCtrl.RaiseEvent(moveType, self.info.rtEntityId, null, context);
            collider.transform.velocity += context.Value;
        }

        private static bool TryJump(Entity self, TerrainCollider collider, float height) {
            float launchSpeed = JumpSpeed(height, -EntityJob.cxt.gravity.y);
            if (!collider.UseGravity || launchSpeed <= 0 || collider.transform.velocity.y > 0)
                return false;
            if (!IsGrounded(collider)) return false;

            float3 impulse = new float3(0, launchSpeed, 0);
            collider.transform.velocity += impulse;
            self.eventCtrl.RaiseEvent(GameEvent.Action_Jump, self.info.rtEntityId, null, impulse);
            return true;
        }

        internal static float RecoveryJumpHeight(float stepDuration, byte attempts) {
            if (stepDuration > pathPersistence) return 0;
            if (stepDuration >= pathPersistence * SecondRecoveryThreshold
                && (attempts & SecondRecoveryAttempt) == 0) return SecondRecoveryHeight;
            if (stepDuration >= pathPersistence * FirstRecoveryThreshold
                && (attempts & FirstRecoveryAttempt) == 0) return FirstRecoveryHeight;
            return 0;
        }

        internal static float JumpSpeed(float height, float gravity) {
            return height > 0 && gravity > 0 ? math.sqrt(2 * gravity * height) : 0;
        }

        internal static float LandingTime(float verticalSpeed, float heightAboveLanding, float gravity) {
            if (gravity <= 0) return 0;
            float discriminant = verticalSpeed * verticalSpeed + 2 * gravity * heightAboveLanding;
            return discriminant < 0 ? 0 : math.max(0, (verticalSpeed + math.sqrt(discriminant)) / gravity);
        }

        internal static float3 PlanarAcceleration(float3 velocity, float3 aim, float speed, float timeStep) {
            if (speed <= 0 || timeStep <= 0) return float3.zero;

            // Limit our acceleration, never the existing velocity from other forces.
            float remainingSpeed = math.max(0, speed - math.length(velocity.xz));
            float acceleration = speed / AccelerationTime;
            aim.y = 0;
            return math.normalizesafe(aim) * math.min(acceleration * timeStep, remainingSpeed);
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

        private static Steering StaticDirect(EntitySetting.ProfileInfo profile, ref PathInfo finder, TerrainCollider tCollider, FollowType movement = FollowType.Planar) {
            //Entity has fallen off path
            finder.stepDuration++;
            if (IsOffPath(finder, tCollider, profile.bounds, movement)) finder.hasPath = false;
            if (finder.path == null || finder.currentInd >= finder.path.Length) finder.hasPath = false;
            if (finder.stepDuration > pathPersistence) { finder.hasPath = false; }
            if (!finder.hasPath) return IdleSteering(tCollider);
            PathStep step = finder.path[finder.currentInd];
            int3 nextPos = finder.currentPos + step.Delta;
            if (!TransitionPathfinderBehavior.ValidateStep(step, finder.currentPos, profile, EntityJob.cxt)) {
                finder.hasPath = false;
                return IdleSteering(tCollider);
            }

            if (step.Movement.Enabled)
                return DirectMovement(step.Movement.Value, ref finder, tCollider);


            float3 aim = Normalize(nextPos - (float3)tCollider.transform.position);
            Quaternion rot = Quaternion.identity;
            switch(movement) {
                case FollowType.Planar:
                    aim = math.normalizesafe(new float3(aim.x, 0, aim.z));
                    if (math.lengthsq(aim) > 1E-6f) rot = Quaternion.LookRotation(aim);
                    break;
                case FollowType.Move3D:
                    if (math.lengthsq(aim) > 1E-6f)  rot = Quaternion.LookRotation(new float3(aim.x, 0, aim.z));
                    break;
                default:
                    if (math.lengthsq(aim) > 1E-6F) rot = Quaternion.LookRotation(aim);
                    break;
            }

            double3 dist = nextPos - tCollider.transform.position;
            double3 waypointDist = math.abs(dist);
            if (math.all(waypointDist <= 1)) Progress(ref finder);
            else if (math.all(waypointDist.xz <= 1) && movement == FollowType.Planar) {
                if (TryGetShadowOrigin(tCollider, (float)math.sign(dist.y), out float originY)
                    && math.abs(originY - nextPos.y) <= 1) Progress(ref finder);
            }

            return new Steering(rot, aim, followType: movement);
        }

        private static bool IsOffPath(PathInfo finder, TerrainCollider tCollider, uint3 bounds, FollowType movement) {
            if (finder.path == null || finder.currentInd >= finder.path.Length) return true;
            float3 position = (float3)tCollider.transform.position;
            // Planar paths use the ground shadow as their origin. Compare in that
            // same space so the collider's height above the ground is not path error.
            if (movement == FollowType.Planar && TryGetShadowOrigin(tCollider, -1, out float shadowY))
                position.y = shadowY;
            PathStep step = finder.path[finder.currentInd];
            float3 start = finder.currentPos;
            if (step.Group < 0) return math.any(math.abs(position - start) > bounds);
            float3 delta = step.Delta;
            float lengthSquared = math.lengthsq(delta);
            float fraction = lengthSquared == 0 ? 0 : math.saturate(math.dot(position - start, delta) / lengthSquared);
            return math.any(math.abs(position - (start + fraction * delta)) > bounds);
        }

        internal static bool TryGetShadowOrigin(TerrainCollider tCollider, float signY, out float originY) {
            TerrainCollider.Transform transform = tCollider.transform;
            float3 halfSize = transform.size/2f; originY = 0;
            if (math.abs(signY) != 1) return false;
            if (!Terraforming.RectangularCastTerrain(
                (float3)transform.position + halfSize,
                Vector3.up * signY,
                halfSize.x * Vector3.right,
                halfSize.z * Vector3.forward,
                transform.size.y * 2,
                Terraforming.RayTestSolid,
                out float3 hitPt
            )) return false;
            originY = hitPt.y;
            return true;
        }

        static void Progress(ref PathInfo finder) {
            PathStep step = finder.path[finder.currentInd];
            int3 nextPos = finder.currentPos + step.Delta;
            finder.currentPos = nextPos;
            finder.stepDuration = 0;
            finder.recoveryAttempts = 0;
            finder.currentInd++;
        }

        private static Steering DynamicDirect(EntitySetting.ProfileInfo profile, ref PathInfo finder, TerrainCollider tCollider, float3 target, FollowType movement = FollowType.Planar) {
            finder.stepDuration++;
            if (IsOffPath(finder, tCollider, profile.bounds, movement))
                finder.hasPath = false;
            if (finder.path == null || finder.currentInd >= finder.path.Length) finder.hasPath = false;
            if (finder.stepDuration > pathPersistence) finder.hasPath = false;
            if (!finder.hasPath) return IdleSteering(tCollider);

            PathStep step = finder.path[finder.currentInd];
            int3 nextPos = finder.currentPos + step.Delta;
            if (!TransitionPathfinderBehavior.ValidateStep(step, finder.currentPos, profile, EntityJob.cxt)) {
                finder.hasPath = false;
                return IdleSteering(tCollider);
            }

            if (step.Movement.Enabled)
                return DirectMovement(step.Movement.Value, ref finder, tCollider);

            if (math.distance(tCollider.transform.position, target) < math.distance(finder.destination, target)) {
                finder.hasPath = false;
                return IdleSteering(tCollider);
            }

            float3 aim = Normalize(nextPos - (float3)tCollider.transform.position);
            Quaternion rot = tCollider.transform.rotation;
            switch(movement) {
                case FollowType.Planar:
                    aim = math.normalizesafe(new float3(aim.x, 0, aim.z));
                    if (math.any(aim != 0)) rot = Quaternion.LookRotation(aim);
                    break;
                case FollowType.Move3D:
                    float3 planarAim = new float3(aim.x, 0, aim.z);
                    if (math.any(planarAim != 0)) rot = Quaternion.LookRotation(planarAim);
                    break;
                default:
                    if (math.any(aim != 0)) rot = Quaternion.LookRotation(aim);
                    break;
            }

            //Planar movements cannot direct vertical velocity
            double3 dist = nextPos - tCollider.transform.position;
            double3 waypointDist = math.abs(dist);
            if (math.all(waypointDist <= 1)) Progress(ref finder);
            else if (math.all(waypointDist.xz <= 1) && movement == FollowType.Planar) {
                if (TryGetShadowOrigin(tCollider, (float)math.sign(dist.y), out float originY)
                    && math.abs(originY - nextPos.y) <= 1) Progress(ref finder);
            } 

            return new Steering(rot, aim, followType: movement);
        }


        private static Steering StaticDirect(List<MatProfileE> profile, uint3 bounds, ref PathInfo finder, TerrainCollider tCollider, FollowType movement = FollowType.Planar) {
            //Entity has fallen off path
            finder.stepDuration++;
            if (IsOffPath(finder, tCollider, bounds, movement)) finder.hasPath = false;
            if (finder.path == null || finder.currentInd >= finder.path.Length) finder.hasPath = false;
            if (finder.stepDuration > pathPersistence) { finder.hasPath = false; }
            if (!finder.hasPath) return IdleSteering(tCollider);
            PathStep step = finder.path[finder.currentInd];
            int3 nextPos = finder.currentPos + step.Delta;
            if (!TransitionPathfinderBehavior.ValidateStep(step, finder.currentPos, bounds, profile)) {
                finder.hasPath = false;
                return IdleSteering(tCollider);
            }

            if (step.Movement.Enabled)
                return DirectMovement(step.Movement.Value, ref finder, tCollider);


            float3 aim = Normalize(nextPos - (float3)tCollider.transform.position);
            Quaternion rot = tCollider.transform.rotation;
            switch(movement) {
                case FollowType.Planar:
                    aim = math.normalizesafe(new float3(aim.x, 0, aim.z));
                    if (math.any(aim != 0)) rot = Quaternion.LookRotation(aim);
                    break;
                case FollowType.Move3D:
                    float3 planarAim = new float3(aim.x, 0, aim.z);
                    if (math.any(planarAim != 0)) rot = Quaternion.LookRotation(planarAim);
                    break;
                default:
                    if (math.any(aim != 0)) rot = Quaternion.LookRotation(aim);
                    break;
            }

            double3 dist = nextPos - tCollider.transform.position;
            double3 waypointDist = math.abs(dist);
            if (math.all(waypointDist <= 1)) Progress(ref finder);
            else if (math.all(waypointDist.xz <= 1) && movement == FollowType.Planar) {
                if (TryGetShadowOrigin(tCollider, (float)math.sign(dist.y), out float originY)
                    && math.abs(originY - nextPos.y) <= 1) Progress(ref finder);
            } 

            return new Steering(rot, aim, followType: movement);
        }


        struct BoidDMtrx {
            public float3 SeperationDir;
            public float3 AlignmentDir;
            public float3 CohesionDir;
            public uint count;
        }
    }
}
