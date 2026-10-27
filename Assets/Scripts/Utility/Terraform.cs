using System;
using System.Collections.Concurrent;
using Arterra.Core.Storage;
using Unity.Mathematics;
using UnityEngine;
using static Arterra.Core.Storage.CPUMapManager;

namespace Arterra.Utils
{
    public static class Terraforming {
        public static uint IsoValue => CPUMapManager.IsoValue;
        /// <summary> Basic Function given to RayCastTerrain to test if it intersect solid ground </summary>
        /// <param name="coord"></param>
        /// <returns></returns>
        public static uint RayTestSolid(int3 GCoord) {
            MapData pointInfo = CPUMapManager.SampleMap(GCoord);
            return (uint)pointInfo.viscosity;
        }


        /// <summary> Performs a precise raycast on the IsoSurface represented through the underlying <i>density</i> information given by
        /// the callback with the specific <see cref="IsoValue"/> specified. A raycast determines whether a ray intersects with the IsoSurface
        /// and if-so at what point. This function does not use any generated lookup structures and operates on the raw <see cref="MapData"/>
        /// stored in <see cref="SectionedMemory"/>. </summary>
        /// <remarks> This function uses a 3D voxel line drawing algorithm provided here http://www.cse.yorku.ca/~amana/research/grid.pdf and it
        /// is linear in complexity with respect to <i>rayLength</i>. </remarks>
        /// <param name="oGS">The origin of the ray in Grid Space</param>
        /// <param name="rayDir">The direction of the ray.</param>
        /// <param name="rayLength">The maximum length of the ray</param>
        /// <param name="callback">A callback that returns a density value given an integer Grid Space Location of a map entry. </param>
        /// <param name="hit">If the ray intersects the IsoSurface, the position in GridSpace of the first intersection. </param>
        /// <returns>Whether or not the ray intersects the IsoSurface in less than <i>rayLength</i> distance from the rayOrigin(<i>oGS</i>)</returns>
        //Algorithm here -> http://www.cse.yorku.ca/~amana/research/grid.pdf
        public static bool RayCastTerrain(float3 oGS, float3 rayDir, float rayLength, Func<int3, uint> callback, out float3 hit) {
            int3 step = (int3)math.sign(rayDir);
            int3 GCoord = (int3)math.floor(oGS);
            int3 sPlane = math.max(step, 0);
            //If rayDir is negative, then GCoord is the next plane
            //Otherwise GCoord + sPlane is the next plane

            float3 tDelta = 1.0f / math.abs(rayDir); float3 tMax = tDelta;
            tMax.x *= rayDir.x >= 0 ? 1 - (oGS.x - GCoord.x) : (oGS.x - GCoord.x);
            tMax.y *= rayDir.y >= 0 ? 1 - (oGS.y - GCoord.y) : (oGS.y - GCoord.y);
            tMax.z *= rayDir.z >= 0 ? 1 - (oGS.z - GCoord.z) : (oGS.z - GCoord.z);

            uint density = 0;
            uint nDensity = density;
            hit = oGS;
            do {
                if (tMax.x < tMax.y) {
                    if (tMax.x < tMax.z) {
                        tMax.x += tDelta.x;
                        nDensity = GetRayPlaneIntersectionX(ref hit, rayDir, GCoord.x + sPlane.x, callback);
                        GCoord.x += step.x;
                    } else {
                        tMax.z += tDelta.z;
                        nDensity = GetRayPlaneIntersectionZ(ref hit, rayDir, GCoord.z + sPlane.z, callback);
                        GCoord.z += step.z;
                    }
                } else {
                    if (tMax.y < tMax.z) {
                        tMax.y += tDelta.y;
                        nDensity = GetRayPlaneIntersectionY(ref hit, rayDir, GCoord.y + sPlane.y, callback);
                        GCoord.y += step.y;
                    } else {
                        tMax.z += tDelta.z;
                        nDensity = GetRayPlaneIntersectionZ(ref hit, rayDir, GCoord.z + sPlane.z, callback);
                        GCoord.z += step.z;
                    }
                }

                if (nDensity >= IsoValue) {
                    float t = Mathf.InverseLerp(density, nDensity, IsoValue);
                    hit = oGS * (1 - t) + hit * t;
                    return true;
                }

                density = nDensity;
                oGS = hit;
            } while (Mathf.Min(tMax.x, tMax.y, tMax.z) < rayLength);
            return false;
        }
        
        /// <summary> Performs a precise cylinder cast IsoSurface represented through the underlying <i>density</i> information given by
        /// the callback with the specific <see cref="IsoValue"/> specified. The cylinder cast determines whether the cylinder 
        /// (actually rectangular prism) intersects with the IsoSurface and if-so, the first point it intersects. </summary>
        /// <remarks> This function uses a 3D voxel line drawing algorithm provided here http://www.cse.yorku.ca/~amana/research/grid.pdf and it
        /// is linear in complexity with respect to <i>rayLength</i>. </remarks>
        /// <param name="oGS">The origin of the cylinder in Grid Space</param>
        /// <param name="rayDir">The direction of the cylinder.</param>
        /// <param name="radius"> The radius of the cylinder in Grid Space</param>
        /// <param name="rayLength">The maximum length of the cylinder</param>
        /// <param name="callback">A callback that returns a density value given an integer Grid Space Location of a map entry. </param>
        /// <param name="hit">If the ray intersects the IsoSurface, the position in GridSpace of the first intersection. </param>
        /// <returns>Whether or not the ray intersects the IsoSurface in less than <i>rayLength</i> distance from the rayOrigin(<i>oGS</i>)</returns>
        public static bool CylinderCastTerrain(float3 oGS, float3 rayDir, float radius, float rayLength, Func<int3, uint> callback, out float3 hit) {
            if (math.lengthsq(rayDir) < 1e-12f) throw new ArgumentException("Cast direction must be nonzero.", nameof(rayDir));
            rayDir = math.normalize(rayDir);
            //Get tangent vectors
            float3 a = rayDir.x < 0 ? new float3(1, 1, 1) : new float3(-1, 1, -1);
            float3 tangent = math.cross(rayDir, a);
            if (math.lengthsq(tangent) < 1e-6f)
                tangent = math.cross(rayDir, new float3(0, 1, 0));
            float3 r1 = math.normalizesafe(tangent);
            float3 r2 = math.cross(rayDir, r1);
            return RectangularCastTerrain(oGS, rayDir, r1 * radius, r2 * radius, rayLength, callback, out hit);
        }

        /// <summary> Casts a rectangular cross-section centered on oGS. halfHeight and halfWidth
        /// are scaled, mutually perpendicular tangent vectors perpendicular to rayDir.
        /// The corners are oGS +/- halfHeight +/- halfWidth; rayLength is in grid units.
        /// Uses the same voxel-plane sampling as CylinderCastTerrain (not an exact volume sweep). </summary>
        public static bool RectangularCastTerrain(float3 oGS, float3 rayDir, float3 halfHeight, float3 halfWidth,
            float rayLength, Func<int3, uint> callback, out float3 hit) {
            if (math.lengthsq(rayDir) < 1e-12f) throw new ArgumentException("Cast direction must be nonzero.", nameof(rayDir));
            rayDir = math.normalize(rayDir);
            float3 r1 = halfHeight, r2 = halfWidth;
            // A zero component is a corner-selection tie, not a collapsed edge.
            float3 signR1 = math.select(new float3(-1), new float3(1), rayDir * r1 >= 0);
            float3 signR2 = math.select(new float3(-1), new float3(1), rayDir * r2 >= 0);

            float3 xInt = oGS;
            float3 yInt = oGS;
            float3 zInt = oGS;

            xInt += signR1.x * r1; xInt += signR2.x * r2;
            yInt += signR1.y * r1; yInt += signR2.y * r2;
            zInt += signR1.z * r1; zInt += signR2.z * r2;

            int3 GCoord = (int3)math.floor(new float3(xInt.x, yInt.y, zInt.z));
            int3 step = (int3)math.sign(rayDir);
            int3 sPlane = math.max(step, 0);

            float3 tDelta = 1.0f / math.abs(rayDir); float3 tMax = tDelta;
            tMax.x *= rayDir.x >= 0 ? 1 - math.frac(xInt.x) : math.frac(xInt.x);
            tMax.y *= rayDir.y >= 0 ? 1 - math.frac(yInt.y) : math.frac(yInt.y);
            tMax.z *= rayDir.z >= 0 ? 1 - math.frac(zInt.z) : math.frac(zInt.z);
            tMax = math.select(tMax, new float3(float.PositiveInfinity), rayDir == 0);
            signR1 *= -1; signR2 *= -1; //point opposite


            float3 nHit = oGS + rayDir * rayLength;
            hit = nHit;
            float progress = 0;
            float hitD = 0;
            float hitDistance = rayLength;
            bool found = false;
            do {
                bool candidate = false;
                if (tMax.x < tMax.y) {
                    if (tMax.x < tMax.z) {
                        int xPlane = GCoord.x + sPlane.x;
                        var corners = ProjectSquare(xInt, r1 * signR1.x, r2 * signR2.x, rayDir, 'x', xPlane);
                        if (Get2DQuadrilateralIntersect(corners, (y, z) => callback(new(xPlane, y, z)), out hitD, out float2 p)) {
                            nHit = GetBackwardsIntersect(new float3(xPlane, p.x, p.y), -rayDir, (uint)hitD, callback);
                            candidate = true;
                        }
                        GCoord.x += step.x;
                        tMax.x += tDelta.x;
                    } else {
                        int zPlane = GCoord.z + sPlane.z;
                        var corners = ProjectSquare(zInt, r1 * signR1.z, r2 * signR2.z, rayDir, 'z', zPlane);
                        if (Get2DQuadrilateralIntersect(corners, (x, y) => callback(new(x, y, zPlane)), out hitD, out float2 p)) {
                            nHit = GetBackwardsIntersect(new float3(p.x, p.y, zPlane), -rayDir, (uint)hitD, callback);
                            candidate = true;
                        }
                        GCoord.z += step.z;
                        tMax.z += tDelta.z;
                    }
                } else {
                    if (tMax.y < tMax.z) {
                        int yPlane = GCoord.y + sPlane.y;
                        var corners = ProjectSquare(yInt, r1 * signR1.y, r2 * signR2.y, rayDir, 'y', yPlane);
                        if (Get2DQuadrilateralIntersect(corners, (x, z) => callback(new(x, yPlane, z)), out hitD, out float2 p)) {
                            nHit = GetBackwardsIntersect(new float3(p.x, yPlane, p.y), -rayDir, (uint)hitD, callback);
                            candidate = true;
                        }
                        GCoord.y += step.y;
                        tMax.y += tDelta.y;
                    } else {
                        int zPlane = GCoord.z + sPlane.z;
                        var corners = ProjectSquare(zInt, r1 * signR1.z, r2 * signR2.z, rayDir, 'z', zPlane);
                        if (Get2DQuadrilateralIntersect(corners, (x, y) => callback(new(x, y, zPlane)), out hitD, out float2 p)) {
                            nHit = GetBackwardsIntersect(new float3(p.x, p.y, zPlane), -rayDir, (uint)hitD, callback);
                            candidate = true;
                        }
                        GCoord.z += step.z;
                        tMax.z += tDelta.z;
                    }
                }
                // Compare travel along the sweep, not distance from its centerline.
                float distance = math.dot(nHit - oGS, rayDir);
                if (candidate && distance >= 0 && distance <= hitDistance) {
                    hit = nHit;
                    hitDistance = distance;
                    found = true;
                }
                progress = Mathf.Min(tMax.x, tMax.y, tMax.z);
                if (found && progress >= hitDistance) return true;
            } while (progress <= rayLength);
            return found;
        }

        private static uint GetRayPlaneIntersectionX(ref float3 rayOrigin, float3 rayDir, int XPlane, Func<int3, uint> SampleMap) {
            rayOrigin = GetRayOriginX(rayOrigin, rayDir, XPlane);
            return BilinearDensity(rayOrigin.y, rayOrigin.z, (int y, int z) => SampleMap(new int3(XPlane, y, z)));
        }

        private static uint GetRayPlaneIntersectionY(ref float3 rayOrigin, float3 rayDir, int YPlane, Func<int3, uint> SampleMap) {
            rayOrigin = GetRayOriginY(rayOrigin, rayDir, YPlane);
            return BilinearDensity(rayOrigin.x, rayOrigin.z, (int x, int z) => SampleMap(new int3(x, YPlane, z)));
        }

        private static uint GetRayPlaneIntersectionZ(ref float3 rayOrigin, float3 rayDir, int ZPlane, Func<int3, uint> SampleMap) {
            rayOrigin = GetRayOriginZ(rayOrigin, rayDir, ZPlane);
            return BilinearDensity(rayOrigin.x, rayOrigin.y, (int x, int y) => SampleMap(new int3(x, y, ZPlane)));
        }

        private static float3 GetRayOriginX(float3 rayOrigin, float3 rayDir, int XPlane) {
            float t = (XPlane - rayOrigin.x) / rayDir.x;
            return new(XPlane, rayOrigin.y + t * rayDir.y, rayOrigin.z + t * rayDir.z);
        }

        private static float3 GetRayOriginY(float3 rayOrigin, float3 rayDir, int YPlane) {
            float t = (YPlane - rayOrigin.y) / rayDir.y;
            return new(rayOrigin.x + t * rayDir.x, YPlane, rayOrigin.z + t * rayDir.z);
        }

        private static float3 GetRayOriginZ(float3 rayOrigin, float3 rayDir, int ZPlane) {
            float t = (ZPlane - rayOrigin.z) / rayDir.z;
            return new(rayOrigin.x + t * rayDir.x, rayOrigin.y + t * rayDir.y, ZPlane);
        }

        private static float3 GetBackwardsIntersect(float3 hit, float3 backDir, uint hitDens, Func<int3, uint> SampleMap) {
            int3 step = (int3)math.sign(backDir);
            int3 GCoord = (int3)math.floor(hit);
            // When going backwards from a grid plane, sample the previous plane,
            // rather than intersecting the same point again at distance zero.
            GCoord -= math.select(new int3(0), new int3(1), (backDir < 0) & (hit == (float3)GCoord));
            int3 sPlane = math.max(step, 0);

            float3 tDelta = 1.0f / math.abs(backDir); float3 tMax = tDelta;
            tMax.x *= backDir.x >= 0 ? 1 - (hit.x - GCoord.x) : (hit.x - GCoord.x);
            tMax.y *= backDir.y >= 0 ? 1 - (hit.y - GCoord.y) : (hit.y - GCoord.y);
            tMax.z *= backDir.z >= 0 ? 1 - (hit.z - GCoord.z) : (hit.z - GCoord.z);

            float3 nonHit = hit;
            tMax = math.select(tMax, new float3(float.PositiveInfinity), backDir == 0);
            GCoord += sPlane; //Next plane on each axis, not only X.
            uint nonDens = 0;
            if (tMax.x < tMax.y) {
                if (tMax.x < tMax.z) nonDens = GetRayPlaneIntersectionX(ref nonHit, backDir, GCoord.x, SampleMap);
                else nonDens = GetRayPlaneIntersectionZ(ref nonHit, backDir, GCoord.z, SampleMap);
            } else {
                if (tMax.y < tMax.z) nonDens = GetRayPlaneIntersectionY(ref nonHit, backDir, GCoord.y, SampleMap);
                else nonDens = GetRayPlaneIntersectionZ(ref nonHit, backDir, GCoord.z, SampleMap);
            }

            float t = math.clamp((hitDens - IsoValue) / math.max((float)hitDens - nonDens, 1.0f), 0, 1);
            return hit + t * (nonHit - hit);
        }

        private static (float2, float2, float2, float2) ProjectSquare(
            float3 bottom, float3 left, float3 up, float3 rayDir,
            char axis, int plane
        ) {
            float3 c00 = bottom;
            float3 c01 = bottom + up * 2;
            float3 c10 = bottom + left * 2;
            float3 c11 = bottom + (left + up) * 2;
            return axis switch {
                'x' => (
                    GetRayOriginX(c00, rayDir, plane).yz, GetRayOriginX(c01, rayDir, plane).yz,
                    GetRayOriginX(c10, rayDir, plane).yz, GetRayOriginX(c11, rayDir, plane).yz
                ),
                'y' => (
                    GetRayOriginY(c00, rayDir, plane).xz, GetRayOriginY(c01, rayDir, plane).xz,
                    GetRayOriginY(c10, rayDir, plane).xz, GetRayOriginY(c11, rayDir, plane).xz
                ),
                _ => (
                    GetRayOriginZ(c00, rayDir, plane).xy, GetRayOriginZ(c01, rayDir, plane).xy,
                    GetRayOriginZ(c10, rayDir, plane).xy, GetRayOriginZ(c11, rayDir, plane).xy
                ),
            };
        }

        private static bool Get2DQuadrilateralIntersect(
            (float2, float2, float2, float2) corners,
            Func<int, int, uint> SampleMap, out float density, out float2 pos
        ) {
            pos = float2.zero; density = 0;
            //Ordered, c00 is closest and c11 is farthest
            var (c00, c01, c10, c11) = corners;
            int2 min = (int2)math.ceil(math.min(math.min(c00, c01), math.min(c10, c11)));
            int2 max = (int2)math.floor(math.max(math.max(c00, c01), math.max(c10, c11)));
            float2 e1 = c01 - c00;
            float2 e2 = c10 - c00;
            float denom = e1.x * e2.y - e1.y * e2.x;
            //degenerate parallelogram
            if (math.abs(denom) < 1e-6f) return false;

            if ((density = BilinearDensity(c00.x, c00.y, SampleMap)) >= IsoValue)
                { pos = c00; return true; }

            if ((density = Get2DEdgeIntersect(c00, c01, SampleMap, out pos)) >= IsoValue)
                return true;
            if ((density = Get2DEdgeIntersect(c00, c10, SampleMap, out pos)) >= IsoValue)
                return true;

            if ((density = BilinearDensity(c01.x, c01.y, SampleMap)) >= IsoValue)
                { pos = c01; return true;}
            if ((density = BilinearDensity(c10.x, c10.y, SampleMap)) >= IsoValue)
                { pos = c10; return true;}

            for (int x = min.x; x <= max.x; x++) {
                for (int y = min.y; y <= max.y; y++) {
                    pos = new float2(x, y);
                    if (!IsInside((int2)pos)) continue;
                    if ((density = SampleMap(x, y)) >= IsoValue) return true;
                }
            }

            if ((density = Get2DEdgeIntersect(c01, c11, SampleMap, out pos)) >= IsoValue)
                return true;
            if ((density = Get2DEdgeIntersect(c10, c11, SampleMap, out pos)) >= IsoValue)
                return true;

            if ((density = BilinearDensity(c11.x, c11.y, SampleMap)) >= IsoValue)
                { pos = c11; return true; }
            return false;

            bool IsInside(int2 c) {
                float2 d = c - c00;
                float u = (d.x * e2.y - d.y * e2.x) / denom;
                float v = (e1.x * d.y - e1.y * d.x) / denom;
                return u >= 0 && u <= 1 && v >= 0 && v <= 1;
            }

            static float Get2DEdgeIntersect(float2 c0, float2 c1, Func<int, int, uint> SampleMap, out float2 pos) {
                int2 min = (int2)math.ceil(math.min(c0, c1));
                int2 max = (int2)math.floor(math.max(c0, c1));
                float2 len = math.abs(c1 - c0);
                for (int x = min.x; len.x > 0 && x <= max.x; x++) {
                    float t = math.abs(c0.x - x) / len.x;
                    pos.x = x; pos.y = c0.y + t * (c1.y - c0.y);
                    float density = LinearDensity(pos.y, ry => SampleMap(x, ry));
                    if (density >= IsoValue) return density;
                }
                for (int y = min.y; len.y > 0 && y <= max.y; y++) {
                    float t = math.abs(c0.y - y) / len.y;
                    pos.y = y; pos.x = c0.x + t * (c1.x - c0.x);
                    float density = LinearDensity(pos.x, rx => SampleMap(rx, y));
                    if (density >= IsoValue) return density;
                }
                pos = float2.zero;
                return 0;
            }
        }

        private static uint BilinearDensity(float x, float y, Func<int, int, uint> SampleMap) {
            int x0 = (int)Math.Floor(x); int x1 = x0 + 1;
            int y0 = (int)Math.Floor(y); int y1 = y0 + 1;

            uint c00 = SampleMap(x0, y0);
            uint c10 = SampleMap(x1, y0);
            uint c01 = SampleMap(x0, y1);
            uint c11 = SampleMap(x1, y1);

            float xd = x - x0;
            float yd = y - y0;

            float c0 = c00 * (1 - xd) + c10 * xd;
            float c1 = c01 * (1 - xd) + c11 * xd;
            return (uint)Math.Round(c0 * (1 - yd) + c1 * yd);
        }

        private static float LinearDensity(float t, Func<int, uint> SampleTerrain) {
            int t0 = (int)Math.Floor(t);
            int t1 = t0 + 1;

            uint c0 = SampleTerrain(t0);
            uint c1 = SampleTerrain(t1);
            float td = t - t0;

            return c0 * (1 - td) + c1 * td;
        }

        /// <summary> Provides a method for smooth terrain modification in an area around a point. Provides a callback
        /// providing the mapData of a specific map entry to be modified and the amount to modify for a smooth
        /// terraform. Specifically, in a circular region around <i>tPointGS</i> this amount falls off proportionate
        /// to distance from the center. </summary>
        /// <param name="tPointGS">The center in grid space of the circle that is terraformed</param>
        /// <param name="terraformRadius">The radius in grid space of the circle that is terraformed</param>
        /// <param name="handleTerraform">The callback that is given the map coordinate, how much to modify it, and
        /// is responsible for modifying the terrian and returning whether or not it was modified </param>
        /// <param name="handlePreTerraform">The optional callback that will be called for every map coordinate before
        /// any calls to <i>handleTerraform</i> is ever made, returning whether or not to terminate the terraform operation. </param>
        public static void Terraform(
            float3 tPointGS,
            int terraformRadius,
            Func<int3, float, bool> handleTerraform,
            Func<int3, bool> handlePreTerraform = null
        ) {
            int3 tPointGSInt = (int3)math.round(tPointGS);

            if (handlePreTerraform != null) {
                bool terminated = false;
                Utils.CustomUtility.OrderedLoop(terraformRadius, GCoord => {
                    if (terminated) return;
                    GCoord += tPointGSInt;
                    int3 dR = GCoord - tPointGSInt;
                    int sqrDistWS = dR.x * dR.x + dR.y * dR.y + dR.z * dR.z;
                    float brushStrength = 1.0f - Mathf.InverseLerp(0, terraformRadius * terraformRadius + 1, sqrDistWS);
                    terminated = handlePreTerraform.Invoke(GCoord);
                });
                if (terminated) return;
            }

            Utils.CustomUtility.OrderedLoop(terraformRadius, GCoord => {
                GCoord += tPointGSInt;
                int3 dR = GCoord - tPointGSInt;
                int sqrDistWS = dR.x * dR.x + dR.y * dR.y + dR.z * dR.z;
                float brushStrength = 1.0f - Mathf.InverseLerp(0, terraformRadius * terraformRadius + 1, sqrDistWS);
                handleTerraform(GCoord, brushStrength);
            });
        }

        /// <summary> Finds a singular location that can be validly terraformed, as determined by the <paramref name="canTerraform"/> callback. 
        /// If found, the location returned will be the closest position to the center of the terraformed area (<paramref name="tPointGS"/>). </summary>
        /// <param name="tPointGS">The center in grid space of the circle that is terraformed</param>
        /// <param name="terraformRadius">The radius in grid space of the circle that is terraformed</param>
        /// <param name="canTerraform">The callback that will be called for every map coordinate to determine if the point 
        /// can be terraformed</param>
        /// <param name="terraformPoint">If a valid coordinate was successfully found, the coordinate
        /// which can be terraformed. </param>
        /// <returns> Whether or not a location was found </returns>
        public static bool FindTerraformable(
            float3 tPointGS,
            int terraformRadius,
            Func<int3, bool> canTerraform,
            out int3 terraformPoint
        ) {
            int3 tfmPt = 0;
            bool terminated = false;
            int3 tPointGSInt = (int3)math.round(tPointGS);
            Utils.CustomUtility.OrderedLoop(terraformRadius, GCoord => {
                if (terminated) return;
                GCoord += tPointGSInt;
                int3 dR = GCoord - tPointGSInt;
                int sqrDistWS = dR.x * dR.x + dR.y * dR.y + dR.z * dR.z;
                float brushStrength = 1.0f - Mathf.InverseLerp(0, terraformRadius * terraformRadius + 1, sqrDistWS);
                terminated = canTerraform.Invoke(GCoord);
                tfmPt = GCoord;
            });
            terraformPoint = tfmPt;
            return terminated;
        }
    }
}
