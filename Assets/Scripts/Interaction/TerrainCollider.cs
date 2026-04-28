using UnityEngine;
using Unity.Mathematics;
using System;
using Unity.Burst;
using static EntityJob;
using Newtonsoft.Json;
using static Arterra.Core.Storage.CPUMapManager;
using Arterra.Data.Entity;
using System.Collections.Generic;

/*
Future Note: Make this done on a job system
(Someone good at math do this) ->
Sample from a simplex rather than a grid(should be faster)
*/
namespace Arterra.GamePlay.Interaction {
    [System.Serializable]
    public class TerrainCollider {
        public const float BaseFriction = 0.2f;
        private const int TerrainCubeSampleCount = 8;
        private const int TerrainQuadSampleCount = 4;
        public static readonly float3 VerticalCollisionBias = new(0, 0.0f, 0);
        public Transform transform;
        public bool useGravity;

        [BurstCompile]
        private unsafe static void SampleTerrainCube(in int3 lower, in MapContext cxt, int* samples) {
            int3 upper = lower + 1;
            samples[0] = SampleTerrain(lower, cxt);
            samples[1] = SampleTerrain(new int3(lower.x, lower.y, upper.z), cxt);
            samples[2] = SampleTerrain(new int3(lower.x, upper.y, lower.z), cxt);
            samples[3] = SampleTerrain(new int3(lower.x, upper.y, upper.z), cxt);
            samples[4] = SampleTerrain(new int3(upper.x, lower.y, lower.z), cxt);
            samples[5] = SampleTerrain(new int3(upper.x, lower.y, upper.z), cxt);
            samples[6] = SampleTerrain(new int3(upper.x, upper.y, lower.z), cxt);
            samples[7] = SampleTerrain(upper, cxt);
        }

        [BurstCompile]
        private unsafe static void SampleTerrainQuad(in int2 lower, in int3x3 transform, int axis, in MapContext cxt, int* samples) {
            int2 upper = lower + 1;
            samples[0] = SampleTerrain(math.mul(transform, new int3(lower.x, lower.y, axis)), cxt);
            samples[1] = SampleTerrain(math.mul(transform, new int3(upper.x, lower.y, axis)), cxt);
            samples[2] = SampleTerrain(math.mul(transform, new int3(lower.x, upper.y, axis)), cxt);
            samples[3] = SampleTerrain(math.mul(transform, new int3(upper.x, upper.y, axis)), cxt);
        }

        [BurstCompile]
        public unsafe static float3 TrilinearDisplacement(in float3 posGS, in MapContext cxt) {
            //Calculate Density
            int3 lower = (int3)math.floor(posGS);
            float3 delta = posGS - lower;
            int* samples = stackalloc int[TerrainCubeSampleCount];
            SampleTerrainCube(lower, cxt, samples);

            float c00 = samples[0] * (1 - delta.x) + samples[4] * delta.x;
            float c01 = samples[1] * (1 - delta.x) + samples[5] * delta.x;
            float c10 = samples[2] * (1 - delta.x) + samples[6] * delta.x;
            float c11 = samples[3] * (1 - delta.x) + samples[7] * delta.x;

            float c0 = c00 * (1 - delta.y) + c10 * delta.y;
            float c1 = c01 * (1 - delta.y) + c11 * delta.y;
            float density = c0 * (1 - delta.z) + c1 * delta.z;
            if (density < cxt.IsoValue) return float3.zero;

            //Calculate the normal
            float xL = (samples[4] - samples[0]) * (1 - delta.y) + (samples[6] - samples[2]) * delta.y;
            float xU = (samples[5] - samples[1]) * (1 - delta.y) + (samples[7] - samples[3]) * delta.y;
            float yL = (samples[2] - samples[0]) * (1 - delta.z) + (samples[3] - samples[1]) * delta.z;
            float yU = (samples[6] - samples[4]) * (1 - delta.z) + (samples[7] - samples[5]) * delta.z;
            float zL = (samples[1] - samples[0]) * (1 - delta.x) + (samples[5] - samples[4]) * delta.x;
            float zU = (samples[3] - samples[2]) * (1 - delta.x) + (samples[7] - samples[6]) * delta.x;

            float xC = xL * (1 - delta.z) + xU * delta.z;
            float yC = yL * (1 - delta.x) + yU * delta.x;
            float zC = zL * (1 - delta.y) + zU * delta.y;

            //Because the density increases towards ground, we need to invert the normal
            float3 normal = -new float3(xC, yC, zC);
            if (math.all(normal == 0)) return normal;
            normal = math.normalize(normal);
            return normal * TrilinearGradientLength(density, posGS, normal, cxt);
        }

        [BurstCompile]
        public unsafe static float2 BilinearDisplacement(in float2 posGS, in int3x3 transform, int axis, in MapContext cxt) {
            int2 lower = (int2)math.floor(posGS);
            float2 delta = posGS - lower;
            int* samples = stackalloc int[TerrainQuadSampleCount];
            SampleTerrainQuad(lower, transform, axis, cxt, samples);

            float c0 = samples[0] * (1 - delta.x) + samples[1] * delta.x;
            float c1 = samples[2] * (1 - delta.x) + samples[3] * delta.x;
            float density = c0 * (1 - delta.y) + c1 * delta.y;
            if (density < cxt.IsoValue) return float2.zero;

            //Bilinear Normal
            float xC = (samples[1] - samples[0]) * (1 - delta.y) + (samples[3] - samples[2]) * delta.y;
            float yC = (samples[2] - samples[0]) * (1 - delta.x) + (samples[3] - samples[1]) * delta.x;

            float2 normal = -new float2(xC, yC);
            if (math.all(normal == 0)) return normal;
            normal = math.normalize(normal);
            return normal * BilinearGradientLength(density, posGS, normal, transform, axis, cxt);
        }

        [BurstCompile]
        public static float LinearDisplacement(float t, in int3x3 transform, in int2 plane, in MapContext cxt) {
            int t0 = (int)Math.Floor(t);
            int t1 = t0 + 1;

            int c0 = SampleTerrain(math.mul(transform, new int3(plane.x, plane.y, t0)), cxt);
            int c1 = SampleTerrain(math.mul(transform, new int3(plane.x, plane.y, t1)), cxt);
            float td = t - t0;

            float density = c0 * (1 - td) + c1 * td;
            if (density < cxt.IsoValue) return 0;
            float normal = math.sign(-(c1 - c0));
            return normal * LinearGradientLength(density, t, normal, transform, plane, cxt); //Normal
        }
        [BurstCompile]
        public static float LinearGradientLength(float density, float pos, float normal, in int3x3 transform, in int2 plane, in MapContext cxt) {
            int corner = (int)(math.floor(pos) + math.max(normal, 0));
            float cDen = SampleTerrain(math.mul(transform, new int3(plane.x, plane.y, corner)), cxt);
            if (cDen >= density) return 0;
            return math.clamp((cxt.IsoValue - density) / (cDen - density), 0, 1) * math.abs(corner - pos);
        }
        [BurstCompile]
        public static float BilinearGradientLength(float density, float2 pos, float2 normal, in int3x3 transform, int axis, in MapContext cxt) {
            float2 tMax = 1.0f / math.abs(normal); float eDen;
            tMax.x *= normal.x >= 0 ? 1 - math.frac(pos.x) : math.frac(pos.x);
            tMax.y *= normal.y >= 0 ? 1 - math.frac(pos.y) : math.frac(pos.y);

            float t = math.cmin(tMax);
            pos += normal * t;

            //xz-plane flips yz, flip xy component
            if (tMax.y >= tMax.x) eDen = LinearDensity(pos.y, math.mul(transform, YXZTrans), new int2((int)pos.x, axis), cxt);
            else eDen = LinearDensity(pos.x, transform, new int2((int)pos.y, axis), cxt);
            if (eDen >= density) return 0;

            return math.clamp((cxt.IsoValue - density) / (eDen - density), 0, 1) * t;
        }

        [BurstCompile]
        public static float TrilinearGradientLength(float density, float3 pos, float3 normal, in MapContext cxt) {
            float3 tMax = 1.0f / math.abs(normal); float fDen;
            tMax.x *= normal.x >= 0 ? 1 - math.frac(pos.x) : math.frac(pos.x);
            tMax.y *= normal.y >= 0 ? 1 - math.frac(pos.y) : math.frac(pos.y);
            tMax.z *= normal.z >= 0 ? 1 - math.frac(pos.z) : math.frac(pos.z);

            float t = math.cmin(tMax);
            pos += normal * t;

            if (t == tMax.x) { fDen = BilinearDensity(pos.y, pos.z, YZPlane, (int)pos.x, cxt); } else if (t == tMax.y) { fDen = BilinearDensity(pos.x, pos.z, XZPlane, (int)pos.y, cxt); } else { fDen = BilinearDensity(pos.x, pos.y, XYPlane, (int)pos.z, cxt); }
            if (fDen >= density) return 0;

            return math.clamp((cxt.IsoValue - density) / (fDen - density), 0, 1) * t;
        }
        [BurstCompile]
        private static float LinearDensity(float t, in int3x3 transform, in int2 plane, in MapContext cxt) {
            int t0 = (int)Math.Floor(t);
            int t1 = t0 + 1;

            int c0 = SampleTerrain(math.mul(transform, new int3(t0, plane.x, plane.y)), cxt);
            int c1 = SampleTerrain(math.mul(transform, new int3(t1, plane.x, plane.y)), cxt);
            float td = t - t0;

            return c0 * (1 - td) + c1 * td;
        }
        [BurstCompile]
        private unsafe static float BilinearDensity(float x, float y, in int3x3 transform, in int axis, in MapContext cxt) {
            float2 pos = new(x, y);
            int2 lower = (int2)math.floor(pos);
            float2 delta = pos - lower;
            int* samples = stackalloc int[TerrainQuadSampleCount];
            SampleTerrainQuad(lower, transform, axis, cxt, samples);

            float c0 = samples[0] * (1 - delta.x) + samples[1] * delta.x;
            float c1 = samples[2] * (1 - delta.x) + samples[3] * delta.x;
            return c0 * (1 - delta.y) + c1 * delta.y;
        }


        /*z
        * ^     .---3----.
        * |    /|       /|
        * |   4 |      3 |    y
        * |  .--4-4---.  3   /\
        * |  |  |     |  |   /
        * |  1  .---2-2--.  /
        * |  | 1      | 2  /
        * | xyz___1___./  /
        * +---------> x  /
        * z
        * ^     8--------7
        * |    /|  2    /|
        * |   / |  4   / |    y
        * |  5--+-----6  |   /\
        * |  |5 |     | 6|   /
        * |  |  4-3---+--3  /
        * |  | /   1  | /  /
        * |  1________2/  /
        * +---------> x  /
        */
        private static int3x3 YXZTrans => new(0, 1, 0, 1, 0, 0, 0, 0, 1); //yxz, flips xz component
        private static int3x3 XZPlane => new(1, 0, 0, 0, 0, 1, 0, 1, 0);//xzy
        private static int3x3 XYPlane => new(1, 0, 0, 0, 1, 0, 0, 0, 1);//xyz
        private static int3x3 YZPlane => new(0, 0, 1, 1, 0, 0, 0, 1, 0); //zxy
        [BurstCompile]
        private static bool SampleFaceCollision(float3 originGS, float3 boundsGS, in MapContext context, out float3 displacement) {
            float3 min = math.min(originGS, originGS + boundsGS);
            float3 max = math.max(originGS, originGS + boundsGS);
            int3 minC = (int3)math.ceil(min); float3 minDis = float3.zero;
            int3 maxC = (int3)math.floor(max); float3 maxDis = float3.zero;
            float dis;

            //3*2 = 6 faces
            for (int x = minC.x; x <= maxC.x; x++) {
                for (int y = minC.y; y <= maxC.y; y++) {
                    dis = LinearDisplacement(min.z, XYPlane, new int2(x, y), context);
                    maxDis.z = math.max(maxDis.z, dis); minDis.z = math.min(minDis.z, dis);
                    dis = LinearDisplacement(max.z, XYPlane, new int2(x, y), context);
                    maxDis.z = math.max(maxDis.z, dis); minDis.z = math.min(minDis.z, dis);
                }
            }

            for (int x = minC.x; x <= maxC.x; x++) {
                for (int z = minC.z; z <= maxC.z; z++) {
                    dis = LinearDisplacement(min.y, XZPlane, new int2(x, z), context);
                    maxDis.y = math.max(maxDis.y, dis); minDis.y = math.min(minDis.y, dis);
                    dis = LinearDisplacement(max.y, XZPlane, new int2(x, z), context);
                    maxDis.y = math.max(maxDis.y, dis); minDis.y = math.min(minDis.y, dis);
                }
            }

            for (int y = minC.y; y <= maxC.y; y++) {
                for (int z = minC.z; z <= maxC.z; z++) {
                    dis = LinearDisplacement(min.x, YZPlane, new int2(y, z), context);
                    maxDis.x = math.max(maxDis.x, dis); minDis.x = math.min(minDis.x, dis);
                    dis = LinearDisplacement(max.x, YZPlane, new int2(y, z), context);
                    maxDis.x = math.max(maxDis.x, dis); minDis.x = math.min(minDis.x, dis);
                }
            }

            displacement = maxDis + minDis;
            return math.any(displacement != float3.zero);
        }
        [BurstCompile]
        private static bool SampleEdgeCollision(float3 originGS, float3 boundsGS, in MapContext context, out float3 displacement) {
            float3 min = math.min(originGS, originGS + boundsGS);
            float3 max = math.max(originGS, originGS + boundsGS);
            int3 minC = (int3)math.ceil(min); float3 minDis = float3.zero;
            int3 maxC = (int3)math.floor(max); float3 maxDis = float3.zero;
            float2 dis;

            //3*4 = 12 edges
            for (int x = minC.x; x <= maxC.x; x++) {
                for (int i = 0; i < 4; i++) {
                    int2 index = new(i % 2, i / 2 % 2);
                    float2 corner = min.yz * index + max.yz * (1 - index);
                    dis = BilinearDisplacement(corner, YZPlane, x, context);
                    maxDis.yz = math.max(maxDis.yz, dis); minDis.yz = math.min(minDis.yz, dis);
                }
            }

            for (int y = minC.y; y <= maxC.y; y++) {
                for (int i = 0; i < 4; i++) {
                    int2 index = new(i % 2, i / 2 % 2);
                    float2 corner = min.xz * index + max.xz * (1 - index);
                    dis = BilinearDisplacement(corner, XZPlane, y, context);
                    maxDis.xz = math.max(maxDis.xz, dis); minDis.xz = math.min(minDis.xz, dis);
                }
            }

            for (int z = minC.z; z <= maxC.z; z++) {
                for (int i = 0; i < 4; i++) {
                    int2 index = new(i % 2, i / 2 % 2);
                    float2 corner = min.xy * index + max.xy * (1 - index);
                    dis = BilinearDisplacement(corner, XYPlane, z, context);
                    maxDis.xy = math.max(maxDis.xy, dis); minDis.xy = math.min(minDis.xy, dis);
                }
            }

            displacement = maxDis + minDis;
            return math.any(displacement != float3.zero);
        }
        [BurstCompile]
        private static bool SampleCornerCollision(float3 originGS, float3 boundsGS, in MapContext context, out float3 displacement) {
            float3 min = math.min(originGS, originGS + boundsGS);
            float3 max = math.max(originGS, originGS + boundsGS);
            float3 maxDis = float3.zero; float3 minDis = float3.zero;
            float3 dis;

            //8 corners
            for (int i = 0; i < 8; i++) {
                int3 index = new(i % 2, i / 2 % 2, i / 4);
                float3 corner = min * index + max * (1 - index);
                dis = TrilinearDisplacement(corner, context);
                maxDis = math.max(maxDis, dis); minDis = math.min(minDis, dis);
            }

            displacement = maxDis + minDis;
            return math.any(displacement != float3.zero);
        }

        [BurstCompile]
        static bool SampleBlockCollision(float3 originGS, float3 boundsGS, in MapContext cxt, out float3 displacement) {
            float3 aMin = math.min(originGS, originGS + boundsGS);
            float3 aMax = math.max(originGS, originGS + boundsGS);

            int3 vMin = (int3)math.floor(aMin);
            int3 vMax = (int3)math.ceil(aMax); // inclusive cells

            float3 pushPos = float3.zero; // positive direction pushes
            float3 pushNeg = float3.zero; // negative direction pushes
            displacement = float3.zero;

            for (int z = vMin.z; z <= vMax.z; z++)
            for (int y = vMin.y; y <= vMax.y; y++)
            for (int x = vMin.x; x <= vMax.x; x++)
            {
                int den = SampleTerrain(new int3(x,y,z), cxt);
                if (den < cxt.IsoValue) continue; // not solid

                float3 bMin = new float3(x, y, z) - 0.5f;
                float3 bMax = bMin + 0.5f;
                aMin += displacement;
                aMax += displacement;

                // Overlap depths (positive means overlapping)
                float ox = math.min(aMax.x, bMax.x) - math.max(aMin.x, bMin.x);
                float oy = math.min(aMax.y, bMax.y) - math.max(aMin.y, bMin.y);
                float oz = math.min(aMax.z, bMax.z) - math.max(aMin.z, bMin.z);
                if (ox <= 0 || oy <= 0 || oz <= 0) continue;

                // Pick smallest overlap axis for MTV
                if (ox <= oy && ox <= oz)
                {
                    float s = (aMin.x + aMax.x) < (bMin.x + bMax.x) ? -ox : ox;
                    if (s > 0) pushPos.x = math.max(pushPos.x, s);
                    else       pushNeg.x = math.min(pushNeg.x, s);
                }
                else if (oy <= ox && oy <= oz)
                {
                    float s = (aMin.y + aMax.y) < (bMin.y + bMax.y) ? -oy : oy;
                    if (s > 0) pushPos.y = math.max(pushPos.y, s);
                    else       pushNeg.y = math.min(pushNeg.y, s);
                }
                else
                {
                    float s = (aMin.z + aMax.z) < (bMin.z + bMax.z) ? -oz : oz;
                    if (s > 0) pushPos.z = math.max(pushPos.z, s);
                    else       pushNeg.z = math.min(pushNeg.z, s);
                }
                
                displacement = pushPos + pushNeg;
            }

            return math.any(displacement != 0);
        }


        public bool SampleCollision(float3 originGS, float3 boundsGS) => TerrainInteractor.TouchSolid(TerrainInteractor.SampleContact(originGS, boundsGS, out float friction, null));
        public bool SampleCollision(in float3 originGS, in float3 boundsGS, in MapContext context, out float3 displacement) {
            return SampleCollisionBurst(originGS, boundsGS, context, out displacement);
        }

        [BurstCompile]
        private static bool SampleCollisionBurst(in float3 originGS, in float3 boundsGS, in MapContext context, out float3 displacement) {
            float3 origin = originGS;
            SampleCornerCollision(origin, boundsGS, context, out displacement);
            origin += displacement;
            SampleEdgeCollision(origin, boundsGS, context, out displacement);
            origin += displacement;
            SampleFaceCollision(origin, boundsGS, context, out displacement);
            displacement += origin - originGS;

            return math.any(displacement != float3.zero);
        }


        public static float3 CancelVel(in float3 vel, in float3 norm) {
            float3 dir = math.normalize(-norm);
            //So surface animals don't get stuck when only edge/face collision
            if (dir.y == 0) dir -= VerticalCollisionBias;
            float magnitude = math.dot(vel, dir);
            if (magnitude < 0) return vel; //Already moving out
            return vel - magnitude * dir;
        }

        private bool TryResolveTerrainCollision(Entity self, ref float maxFriction, ref byte contactMask, bool resolveSolid = true) {
            byte contact = TerrainInteractor.SampleContact((float3)transform.position, transform.size, out float friction, self);
            contactMask |= contact;
            maxFriction = math.max(maxFriction, friction);

            if (!resolveSolid || !TerrainInteractor.TouchSolid(contact))
                return false;

            if (!SampleCollisionBurst((float3)transform.position, transform.size, cxt.mapContext, out float3 displacement))
                if (!SampleBlockCollision((float3)transform.position, transform.size, cxt.mapContext, out displacement))
                    return false;

            transform.position += displacement;
            float3 nVelocity = CancelVel(transform.velocity, displacement);
            self?.eventCtrl.RaiseEvent(Core.Events.GameEvent.Entity_HitGround, self, null, (useGravity, nVelocity.y - transform.velocity.y));
            transform.velocity = nVelocity;
            return true;
        }

        private bool SweepMoveVoxelBoundaries(float3 deltaPosition, Entity self, ref float maxFriction, ref byte contactMask) {
            double3 startPosition = transform.position;

            if (math.lengthsq(deltaPosition) <= 0)
                return false;
                
            float3 leadingCorner = GetLeadingCorner((float3)startPosition, transform.size, deltaPosition);
            float3 tDelta = new (
                math.abs(deltaPosition.x) < 1e-6f ? float.PositiveInfinity : 1.0f / math.abs(deltaPosition.x),
                math.abs(deltaPosition.y) < 1e-6f ? float.PositiveInfinity : 1.0f / math.abs(deltaPosition.y),
                math.abs(deltaPosition.z) < 1e-6f ? float.PositiveInfinity : 1.0f / math.abs(deltaPosition.z)
            );

            float3 tMax = new (
                GetFirstBoundaryProgress(leadingCorner.x, deltaPosition.x),
                GetFirstBoundaryProgress(leadingCorner.y, deltaPosition.y),
                GetFirstBoundaryProgress(leadingCorner.z, deltaPosition.z)
            );

            const float tEpsilon = 1e-5f;
            while (true) {
                float tCross = math.cmin(tMax);
                if (!float.IsFinite(tCross) || tCross > 1.0f)
                    break;

                float tProbe = math.clamp(tCross * 0.95f, 0, 1);
                transform.position = startPosition + deltaPosition * tProbe;
                if (TryResolveTerrainCollision(self, ref maxFriction, ref contactMask))
                    return true; // first collision wins

                if (math.abs(tMax.x - tCross) <= tEpsilon) tMax.x += tDelta.x;
                if (math.abs(tMax.y - tCross) <= tEpsilon) tMax.y += tDelta.y;
                if (math.abs(tMax.z - tCross) <= tEpsilon) tMax.z += tDelta.z;
            }

            transform.position = startPosition + deltaPosition;
            return TryResolveTerrainCollision(self, ref maxFriction, ref contactMask);

            static float3 GetLeadingCorner(float3 originGS, float3 boundsGS, float3 moveDelta) {
                float3 min = math.min(originGS, originGS + boundsGS);
                float3 max = math.max(originGS, originGS + boundsGS);
                float3 center = (max + min) / 2;
                return new float3(
                    moveDelta.x == 0 ? center.x : moveDelta.x > 0 ? max.x : min.x,
                    moveDelta.y == 0 ? center.y : moveDelta.y > 0 ? max.y : min.y,
                    moveDelta.z == 0 ? center.z : moveDelta.z > 0 ? max.z : min.z
                );
            }

            static float GetFirstBoundaryProgress(float originAxis, float deltaAxis) {
                if (math.abs(deltaAxis) < 1e-6f) return float.PositiveInfinity;
                float frac = originAxis - math.floor(originAxis);
                float edgeDist = deltaAxis >= 0 ? 1.0f - frac : frac;
                return edgeDist / math.abs(deltaAxis);
            }
        }

        public void Update(Entity self = null, float baseFriction = BaseFriction, bool tangible = true) {
            float3 deltaPosition = transform.velocity * cxt.totDeltaTime;
            float maxFriction = 0;
            byte contactMask = 0;

            if (tangible) SweepMoveVoxelBoundaries(deltaPosition, self, ref maxFriction, ref contactMask);
            else transform.position += deltaPosition;
            
            float friction = TerrainInteractor.IsTouching(contactMask) ? maxFriction : baseFriction;
            if (useGravity) transform.velocity.xz *= 1 - friction;
            else transform.velocity *= 1 - friction;

            if (useGravity) transform.velocity += cxt.gravity * cxt.totDeltaTime;
        }

        /// <summary> Updates the collider on a Unity Fixed Update. </summary>
        public void FixedUpdate(Entity player) {
            bool IsTangible = !Arterra.Configuration.Config.CURRENT.GamePlay.Gamemodes.value.Intangiblity;
            float3 deltaPosition = transform.velocity * Time.fixedDeltaTime;
            float maxFriction = 0;
            byte contactMask = 0;

            if (IsTangible) SweepMoveVoxelBoundaries(deltaPosition, player, ref maxFriction, ref contactMask);
            else transform.position += deltaPosition;

            float friction = TerrainInteractor.IsTouching(contactMask) ? maxFriction : BaseFriction;
            if (useGravity) transform.velocity.xz *= 1 - friction;
            else transform.velocity *= 1 - friction;

            if (useGravity) transform.velocity += (float3)Physics.gravity * Time.fixedDeltaTime;
        }


        public void EntityCollisionUpdate(Entity self, HashSet<Guid> ignores = null) {
            bool soft = Configuration.Config.CURRENT.GamePlay.Environment.value.useSoftCollisions;
            Bounds bounds = new ((float3)transform.position + transform.size / 2, transform.size);
            EntityManager.ESTree.Query(bounds, cEntity => {
                Bounds cBounds = new (cEntity.position , cEntity.transform.size);
                if (cEntity.info.rtEntityId == self.info.rtEntityId) return;
                if (ignores != null && ignores.Contains(cEntity.info.rtEntityId)) return;
                if (!TryGetMinimumTranslation(bounds, cBounds, out float3 mtv)) return;
                float totalWeight = self.weight + cEntity.weight;
                if (totalWeight == 0) {
                    // Both entities are weightless, split impact equally
                    transform.position += mtv * 0.5f;
                    cEntity.transform.position -= mtv * 0.5f;
                    return;
                }

                float myFactor = cEntity.weight / totalWeight;
                float otherFactor = self.weight / totalWeight;

                if (soft) {
                    transform.velocity += myFactor * mtv;
                    cEntity.transform.velocity -= otherFactor * mtv;   
                } else {
                    transform.velocity = CancelVel(transform.velocity, mtv);
                    cEntity.velocity = CancelVel(cEntity.velocity, -mtv);

                    transform.position += myFactor * mtv;
                    cEntity.transform.position -= otherFactor * mtv;
                }
            });

            static bool TryGetMinimumTranslation(Bounds A, Bounds B, out float3 mtv) {
                Vector3 delta = A.center - B.center; mtv = default;
                float dx = (B.extents.x + A.extents.x) - Mathf.Abs(delta.x);
                float dy = (B.extents.y + A.extents.y) - Mathf.Abs(delta.y);
                float dz = (B.extents.z + A.extents.z) - Mathf.Abs(delta.z);

                if (dx <= 0 || dy <= 0 || dz <= 0)
                    return false;


                if (dx < dy && dx < dz)
                    mtv = new Vector3(dx * Mathf.Sign(delta.x), 0, 0);
                else if (dy < dz)
                    mtv = new Vector3(0, dy * Mathf.Sign(delta.y), 0);
                else
                    mtv = new Vector3(0, 0, dz * Mathf.Sign(delta.z));
                return true;
            }
        }

        public TerrainCollider(in Settings settings, double3 position) {
            this.transform = new Transform(position, 0, settings.size, Quaternion.identity);
            this.useGravity = true;
        }

        public struct Transform {
            public double3 position;
            public Quaternion rotation;
            public float3 size;
            public float3 velocity;
            public Transform(double3 position, float3 velocity, float3 size, Quaternion rotation) {
                this.position = position;
                this.rotation = rotation;
                this.size = size;
                this.velocity = velocity;
            }
        }

        [Serializable]
        public struct Settings {
            public float3 size;
        }
    }
}
