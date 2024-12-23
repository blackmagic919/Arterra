using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using System;
using Unity.Burst;
using static EntityJob;
using static CPUDensityManager;

/*
Future Note: Make this done on a job system
(Someone good at math do this) ->
Sample from a simplex rather than a grid(should be faster)
*/

[BurstCompile][System.Serializable]
public struct TerrainColliderJob
{
    public Transform transform;
    public float3 velocity;

    [BurstCompile]
    public readonly float3 TrilinearDisplacement(in float3 posGS, in MapContext cxt){
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
        if(density < cxt.IsoValue) return float3.zero;
    
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
        if(math.all(normal == 0)) return normal;
        normal = math.normalize(normal);
        return normal * TrilinearGradientLength(density, posGS, normal, cxt);
    }

    [BurstCompile]
    public readonly float2 BilinearDisplacement(in float2 posGS, in int3x3 transform, int axis, in MapContext cxt){
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
        if(density < cxt.IsoValue) return float2.zero;

        //Bilinear Normal
        float xC = (c10 - c00) * (1 - yd) + (c11 - c01) * yd;
        float yC = (c01 - c00) * (1 - xd) + (c11 - c10) * xd;
        
        float2 normal = -new float2(xC, yC);
        if(math.all(normal == 0)) return normal;
        normal = math.normalize(normal);
        return normal * BilinearGradientLength(density, posGS, normal, transform, axis, cxt);
    }

    [BurstCompile]
    public readonly float LinearDisplacement(float t, in int3x3 transform, in int2 plane, in MapContext cxt){
        int t0 = (int)Math.Floor(t); 
        int t1 = t0 + 1;

        int c0 = SampleTerrain(math.mul(transform, new int3(plane.x, plane.y, t0)), cxt);
        int c1 = SampleTerrain(math.mul(transform, new int3(plane.x, plane.y, t1)), cxt);
        float td = t - t0;

        float density = c0 * (1 - td) + c1 * td;
        if(density < cxt.IsoValue) return 0;
        float normal = math.sign(-(c1-c0));
        return normal * LinearGradientLength(density, t, normal, transform, plane, cxt); //Normal
    }
    [BurstCompile]
    public readonly float LinearGradientLength(float density, float pos, float normal, in int3x3 transform, in int2 plane, in MapContext cxt){
        int corner = (int)(math.floor(pos) + math.max(normal, 0));
        float cDen = SampleTerrain(math.mul(transform, new int3(plane.x, plane.y, corner)), cxt);
        if(cDen >= density) return 0;
        return (cxt.IsoValue - density) / (cDen - density) * math.abs(corner - pos);
    }
    [BurstCompile]
    public readonly float BilinearGradientLength(float density, float2 pos, float2 normal, in int3x3 transform, int axis, in MapContext cxt){
        float2 tMax = 1.0f / math.abs(normal); float eDen;
        tMax.x *= normal.x >= 0 ? 1 - math.frac(pos.x) : math.frac(pos.x);
        tMax.y *= normal.y >= 0 ? 1 - math.frac(pos.y) : math.frac(pos.y);

        float t = math.cmin(tMax);
        pos += normal * t; 

        //xz-plane flips yz, flip xy component
        if(tMax.y >= tMax.x) eDen = LinearDensity(pos.y, math.mul(transform, YXZTrans), new int2((int)pos.x, axis), cxt);
        else eDen = LinearDensity(pos.x, transform, new int2((int)pos.y, axis), cxt); 
        if(eDen >= density) return 0;

        return  (cxt.IsoValue - density) / (eDen - density) * t;
    }

    [BurstCompile]
    public readonly float TrilinearGradientLength(float density, float3 pos, float3 normal, in MapContext cxt){
        float3 tMax = 1.0f / math.abs(normal); float fDen;
        tMax.x *= normal.x >= 0 ? 1 - math.frac(pos.x) : math.frac(pos.x);
        tMax.y *= normal.y >= 0 ? 1 - math.frac(pos.y) : math.frac(pos.y);
        tMax.z *= normal.z >= 0 ? 1 - math.frac(pos.z) : math.frac(pos.z);

        float t = math.cmin(tMax); 
        pos += normal * t; 

        if(t == tMax.x){ fDen = BilinearDensity(pos.y, pos.z, YZPlane, (int)pos.x, cxt);} 
        else if(t == tMax.y){fDen = BilinearDensity(pos.x, pos.z, XZPlane, (int)pos.y, cxt);}
        else{fDen = BilinearDensity(pos.x, pos.y, XYPlane, (int)pos.z, cxt);}
        if(fDen >= density) return 0;

        return (cxt.IsoValue - density) / (fDen - density) * t;
    }
    [BurstCompile]
    private readonly float LinearDensity(float t, in int3x3 transform, in int2 plane, in MapContext cxt){
        int t0 = (int)Math.Floor(t); 
        int t1 = t0 + 1;

        int c0 = SampleTerrain(math.mul(transform, new int3(t0, plane.x, plane.y)), cxt);
        int c1 = SampleTerrain(math.mul(transform, new int3(t1, plane.x, plane.y)), cxt);
        float td = t - t0;

        return c0 * (1 - td) + c1 * td;
    }
    [BurstCompile]
    private readonly float BilinearDensity(float x, float y, in int3x3 transform, in int axis, in MapContext cxt){
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
    private readonly int3x3 YXZTrans => new (0, 1, 0, 1, 0, 0, 0, 0, 1); //yxz, flips xz component
    private readonly int3x3 XZPlane => new (1, 0, 0, 0, 0, 1, 0, 1, 0);//xzy
    private readonly int3x3 XYPlane => new (1, 0, 0, 0, 1, 0, 0, 0, 1);//xyz
    private readonly int3x3 YZPlane => new (0, 0, 1, 1, 0, 0, 0, 1, 0); //zxy
    [BurstCompile]
    private bool SampleFaceCollision(float3 originGS, float3 boundsGS, in MapContext context, out float3 displacement){
        float3 min = math.min(originGS, originGS + boundsGS);
        float3 max = math.max(originGS, originGS + boundsGS);
        int3 minC = (int3)math.ceil(min); float3 minDis = float3.zero;
        int3 maxC = (int3)math.floor(max); float3 maxDis = float3.zero;
        float dis;

        //3*2 = 6 faces
        for(int x = minC.x; x <= maxC.x; x++){
            for(int y = minC.y; y <= maxC.y; y++){
                dis = LinearDisplacement(min.z, XYPlane, new int2(x,y), context);
                maxDis.z = math.max(maxDis.z, dis); minDis.z = math.min(minDis.z, dis);
                dis = LinearDisplacement(max.z, XYPlane, new int2(x,y), context);
                maxDis.z = math.max(maxDis.z, dis); minDis.z = math.min(minDis.z, dis);
            }
        }

        for(int x = minC.x; x <= maxC.x; x++){
            for(int z = minC.z; z <= maxC.z; z++){
                dis = LinearDisplacement(min.y, XZPlane, new int2(x,z), context);
                maxDis.y = math.max(maxDis.y, dis); minDis.y = math.min(minDis.y, dis);
                dis = LinearDisplacement(max.y, XZPlane, new int2(x,z), context);
                maxDis.y = math.max(maxDis.y, dis); minDis.y = math.min(minDis.y, dis);
            }
        }

        for(int y = minC.y; y <= maxC.y; y++){
            for(int z = minC.z; z <= maxC.z; z++){
                dis = LinearDisplacement(min.x, YZPlane, new int2(y,z), context);
                maxDis.x = math.max(maxDis.x, dis); minDis.x = math.min(minDis.x, dis);
                dis = LinearDisplacement(max.x, YZPlane, new int2(y,z), context);
                maxDis.x = math.max(maxDis.x, dis); minDis.x = math.min(minDis.x, dis);
            }
        }

        displacement = maxDis + minDis;
        return math.any(displacement != float3.zero);
    }
    [BurstCompile]
    private bool SampleEdgeCollision(float3 originGS, float3 boundsGS, in MapContext context, out float3 displacement){
        float3 min = math.min(originGS, originGS + boundsGS);
        float3 max = math.max(originGS, originGS + boundsGS);
        int3 minC = (int3)math.ceil(min); float3 minDis = float3.zero;
        int3 maxC = (int3)math.floor(max); float3 maxDis = float3.zero;
        float2 dis;

        //3*4 = 12 edges
        for(int x = minC.x; x <= maxC.x; x++){
            for(int i = 0; i < 4; i++){
                int2 index = new (i%2, i/2%2);
                float2 corner = min.yz * index + max.yz * (1 - index);
                dis = BilinearDisplacement(corner, YZPlane, x, context);
                maxDis.yz = math.max(maxDis.yz, dis); minDis.yz = math.min(minDis.yz, dis);
            }
        }

        for(int y = minC.y; y <= maxC.y; y++){
            for(int i = 0; i < 4; i++){
                int2 index = new (i%2, i/2%2);
                float2 corner = min.xz * index + max.xz * (1 - index);
                dis = BilinearDisplacement(corner, XZPlane, y, context);
                maxDis.xz = math.max(maxDis.xz, dis); minDis.xz = math.min(minDis.xz, dis);
            }
        }

        for(int z = minC.z; z <= maxC.z; z++){
            for(int i = 0; i < 4; i++){
                int2 index = new (i%2, i/2%2);
                float2 corner = min.xy * index + max.xy * (1 - index);
                dis = BilinearDisplacement(corner, XYPlane, z, context);
                maxDis.xy = math.max(maxDis.xy, dis); minDis.xy = math.min(minDis.xy, dis);
            }
        }
        
        displacement = maxDis + minDis;
        return math.any(displacement != float3.zero);
    }
    [BurstCompile]
    private bool SampleCornerCollision(float3 originGS, float3 boundsGS, in MapContext context, out float3 displacement){
        float3 min = math.min(originGS, originGS + boundsGS);
        float3 max = math.max(originGS, originGS + boundsGS);
        float3 maxDis = float3.zero; float3 minDis = float3.zero;
        float3 dis;

        //8 corners
        for(int i = 0; i < 8; i++) {
            int3 index = new (i%2, i/2%2, i/4);
            float3 corner = min * index + max * (1 - index);
            dis = TrilinearDisplacement(corner, context);
            maxDis = math.max(maxDis, dis); minDis = math.min(minDis, dis);
        }
        
        displacement = maxDis + minDis;
        return math.any(displacement != float3.zero);
    }
    [BurstCompile]
    public bool SampleCollision(in float3 originGS, in float3 boundsGS, in MapContext context, out float3 displacement){
        float3 origin = originGS;
        SampleCornerCollision(origin, boundsGS, context ,out displacement);
        origin += displacement; 
        SampleEdgeCollision(origin, boundsGS, context, out displacement);
        origin += displacement;
        SampleFaceCollision(origin, boundsGS, context, out displacement);
        displacement += origin - originGS;
        
        return math.any(displacement != float3.zero);
    }

    public unsafe bool IsGrounded(float stickDist, in Settings settings, in MapContext cxt) => SampleCollision(transform.position, new float3(settings.size.x, -stickDist, settings.size.z), cxt, out _);
    public unsafe bool GetGroundDir(float stickDist, in Settings settings, in MapContext cxt, out float3 dir) => SampleCollision(transform.position, new float3(settings.size.x, -stickDist, settings.size.z), cxt, out dir);
    
    [BurstCompile]
    float3 CancelVel(in float3 vel, in float3 norm){
        float3 dir = math.normalize(norm);
        return vel - math.dot(vel, dir) * dir;
    }

    [BurstCompile]
    public void Update(in Context cxt, in Settings settings){
        transform.position += velocity * cxt.deltaTime;
        if(settings.useGravity) velocity += cxt.gravity * cxt.deltaTime;

        if(SampleCollision(transform.position, settings.size, cxt.mapContext, out float3 displacement)){
            velocity = CancelVel(velocity, displacement);
            transform.position += displacement;
        };
    }

    [BurstCompile]
    public struct Transform{
        public float3 position;
        public Quaternion rotation;
        public Transform(float3 position, Quaternion rotation){
            this.position = position;
            this.rotation = rotation;
        }
    }

    [Serializable]
    public struct Settings{
        public float3 size;
        public float3 offset;
        public bool useGravity;
    }
}
