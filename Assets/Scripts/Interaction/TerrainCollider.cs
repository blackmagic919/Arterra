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
        public static readonly float3 VerticalCollisionBias = new(0, 0.05f, 0);
        public Transform transform;
        public bool useGravity;

        public static float3 TrilinearDisplacement(in float3 posGS, in MapContext cxt) {
            //Calculate Density
            int x0 = (int)Math.Floor(posGS.x); int x1 = x0 + 1;
            int y0 = (int)Math.Floor(posGS.y); int y1 = y0 + 1;
            int z0 = (int)Math.Floor(posGS.z); int z1 = z0 + 1;

            int c000 = SampleTerrain(new int3(x0, y0, z0), cxt);
            int c100 = SampleTerrain(new int3(x1, y0, z0), cxt);
            int c010 = SampleTerrain(new int3(x0, y1, z0), cxt);
            int c110 = SampleTerrain(new int3(x1, y1, z0), cxt);
            int c001 = SampleTerrain(new int3(x0, y0, z1), cxt);
            int c101 = SampleTerrain(new int3(x1, y0, z1), cxt);
            int c011 = SampleTerrain(new int3(x0, y1, z1), cxt);
            int c111 = SampleTerrain(new int3(x1, y1, z1), cxt);

            float xd = posGS.x - x0;
            float yd = posGS.y - y0;
            float zd = posGS.z - z0;

            float c00 = c000 * (1 - xd) + c100 * xd;
            float c01 = c001 * (1 - xd) + c101 * xd;
            float c10 = c010 * (1 - xd) + c110 * xd;
            float c11 = c011 * (1 - xd) + c111 * xd;

            float c0 = c00 * (1 - yd) + c10 * yd;
            float c1 = c01 * (1 - yd) + c11 * yd;
            float density = c0 * (1 - zd) + c1 * zd;
            if (density < cxt.IsoValue) return float3.zero;

            //Calculate the normal
            float xL = (c100 - c000) * (1 - yd) + (c110 - c010) * yd;
            float xU = (c101 - c001) * (1 - yd) + (c111 - c011) * yd;
            float yL = (c010 - c000) * (1 - zd) + (c011 - c001) * zd;
            float yU = (c110 - c100) * (1 - zd) + (c111 - c101) * zd;
            float zL = (c001 - c000) * (1 - xd) + (c101 - c100) * xd;
            float zU = (c011 - c010) * (1 - xd) + (c111 - c110) * xd;

            float xC = xL * (1 - zd) + xU * zd;
            float yC = yL * (1 - xd) + yU * xd;
            float zC = zL * (1 - yd) + zU * yd;

            //Because the density increases towards ground, we need to invert the normal
            float3 normal = -new float3(xC, yC, zC);
            if (math.all(normal == 0)) return normal;
            normal = math.normalize(normal);
            return normal * TrilinearGradientLength(density, posGS, normal, cxt);
        }

        public static float2 BilinearDisplacement(in float2 posGS, in int3x3 transform, int axis, in MapContext cxt) {
            int x0 = (int)Math.Floor(posGS.x); int x1 = x0 + 1;
            int y0 = (int)Math.Floor(posGS.y); int y1 = y0 + 1;

            int c00 = SampleTerrain(math.mul(transform, new int3(x0, y0, axis)), cxt);
            int c10 = SampleTerrain(math.mul(transform, new int3(x1, y0, axis)), cxt);
            int c01 = SampleTerrain(math.mul(transform, new int3(x0, y1, axis)), cxt);
            int c11 = SampleTerrain(math.mul(transform, new int3(x1, y1, axis)), cxt);

            float xd = posGS.x - x0;
            float yd = posGS.y - y0;

            float c0 = c00 * (1 - xd) + c10 * xd;
            float c1 = c01 * (1 - xd) + c11 * xd;
            float density = c0 * (1 - yd) + c1 * yd;
            if (density < cxt.IsoValue) return float2.zero;

            //Bilinear Normal
            float xC = (c10 - c00) * (1 - yd) + (c11 - c01) * yd;
            float yC = (c01 - c00) * (1 - xd) + (c11 - c10) * xd;

            float2 normal = -new float2(xC, yC);
            if (math.all(normal == 0)) return normal;
            normal = math.normalize(normal);
            return normal * BilinearGradientLength(density, posGS, normal, transform, axis, cxt);
        }

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
        public static float LinearGradientLength(float density, float pos, float normal, in int3x3 transform, in int2 plane, in MapContext cxt) {
            int corner = (int)(math.floor(pos) + math.max(normal, 0));
            float cDen = SampleTerrain(math.mul(transform, new int3(plane.x, plane.y, corner)), cxt);
            if (cDen >= density) return 0;
            return math.clamp((cxt.IsoValue - density) / (cDen - density), 0, 1) * math.abs(corner - pos);
        }
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
        private static float LinearDensity(float t, in int3x3 transform, in int2 plane, in MapContext cxt) {
            int t0 = (int)Math.Floor(t);
            int t1 = t0 + 1;

            int c0 = SampleTerrain(math.mul(transform, new int3(t0, plane.x, plane.y)), cxt);
            int c1 = SampleTerrain(math.mul(transform, new int3(t1, plane.x, plane.y)), cxt);
            float td = t - t0;

            return c0 * (1 - td) + c1 * td;
        }
        private static float BilinearDensity(float x, float y, in int3x3 transform, in int axis, in MapContext cxt) {
            int x0 = (int)Math.Floor(x); int x1 = x0 + 1;
            int y0 = (int)Math.Floor(y); int y1 = y0 + 1;

            int c00 = SampleTerrain(math.mul(transform, new int3(x0, y0, axis)), cxt);
            int c10 = SampleTerrain(math.mul(transform, new int3(x1, y0, axis)), cxt);
            int c01 = SampleTerrain(math.mul(transform, new int3(x0, y1, axis)), cxt);
            int c11 = SampleTerrain(math.mul(transform, new int3(x1, y1, axis)), cxt);

            float xd = x - x0;
            float yd = y - y0;

            float c0 = c00 * (1 - xd) + c10 * xd;
            float c1 = c01 * (1 - xd) + c11 * xd;
            return c0 * (1 - yd) + c1 * yd;
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
        private bool SampleFaceCollision(float3 originGS, float3 boundsGS, in MapContext context, out float3 displacement) {
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
        private bool SampleCornerCollision(float3 originGS, float3 boundsGS, in MapContext context, out float3 displacement) {
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

        public void Update(Entity self = null, float baseFriction = BaseFriction, bool tangible = true) {
            byte contact = TerrainInteractor.SampleContact(transform.position, transform.size, out float friction, self);
            if (!TerrainInteractor.IsTouching(contact)) friction = baseFriction;
            if (useGravity) transform.velocity.xz *= 1 - friction;
            else transform.velocity *= 1 - friction;

            transform.position += transform.velocity * cxt.totDeltaTime;
            if (useGravity) transform.velocity += cxt.gravity * cxt.totDeltaTime;

            if (TerrainInteractor.TouchSolid(contact) && tangible) {
                if (!SampleCollision(transform.position, transform.size, cxt.mapContext, out float3 displacement))
                    if(!SampleBlockCollision(transform.position, transform.size, cxt.mapContext, out displacement))
                        return;
                transform.position += displacement;
                float3 nVelocity = CancelVel(transform.velocity, displacement);
                self?.eventCtrl.RaiseEvent(Core.Events.GameEvent.Entity_HitGround, self, null, (useGravity, nVelocity.y - transform.velocity.y));
                transform.velocity = nVelocity;
            }
        }

        /// <summary> Updates the collider on a Unity Fixed Update. </summary>
        public void FixedUpdate(Entity player) {
            float friction = 0;
            bool IsTangible = !Arterra.Configuration.Config.CURRENT.GamePlay.Gamemodes.value.Intangiblity;
            byte contact = IsTangible ? TerrainInteractor.SampleContact(transform.position, transform.size, out friction, player) : (byte)0;
            if (!TerrainInteractor.IsTouching(contact)) friction = BaseFriction;
            if (useGravity) transform.velocity.xz *= 1 - friction;
            else transform.velocity *= 1 - friction;

            transform.position += transform.velocity * Time.fixedDeltaTime;
            if (useGravity) transform.velocity += (float3)Physics.gravity * Time.fixedDeltaTime;

            if (TerrainInteractor.TouchSolid(contact)) {
                if (!SampleCollision(transform.position, transform.size, cxt.mapContext, out float3 displacement))
                    if(!SampleBlockCollision(transform.position, transform.size, cxt.mapContext, out displacement))
                        return;
                float3 nVelocity = CancelVel(transform.velocity, displacement);
                player.eventCtrl.RaiseEvent(Core.Events.GameEvent.Entity_HitGround, player, null, (useGravity, nVelocity.y - transform.velocity.y));
                transform.position += displacement;
                transform.velocity = nVelocity;
            }
        }

        private bool TryGetMinimumTranslation(Bounds A, Bounds B, out float3 mtv) {
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


        public void EntityCollisionUpdate(Entity self, HashSet<Guid> ignores = null) {
            bool soft = Configuration.Config.CURRENT.GamePlay.Environment.value.useSoftCollisions;
            Bounds bounds = new (transform.position + transform.size/2, transform.size);
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
        }

        public TerrainCollider(in Settings settings, float3 position) {
            this.transform = new Transform(position, 0, settings.size, Quaternion.identity);
            this.useGravity = true;
        }

        public struct Transform {
            public float3 position;
            public Quaternion rotation;
            public float3 size;
            public float3 velocity;
            public Transform(float3 position, float3 velocity, float3 size, Quaternion rotation) {
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
