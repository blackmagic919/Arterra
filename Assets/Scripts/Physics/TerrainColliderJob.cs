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
    public float3 TrilinearDisplacement(in float3 posGS, in MapContext cxt){
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
        int density = (int)Math.Round(c0 * (1 - zd) + c1 * zd);
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
        else return math.normalize(normal) * (density - cxt.IsoValue + 1) / (255.0f - cxt.IsoValue);
    }

    [BurstCompile]
    public float2 BilinearDisplacement(in float2 posGS, in int3x3 transform, int axis, in MapContext cxt){
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
        int density = (int)Math.Round(c0 * (1 - yd) + c1 * yd);
        if(density < cxt.IsoValue) return float2.zero;

        //Bilinear Normal
        float xC = (c10 - c00) * (1 - yd) + (c11 - c01) * yd;
        float yC = (c01 - c00) * (1 - xd) + (c11 - c10) * xd;
        
        float2 normal = -new float2(xC, yC);
        if(math.all(normal == 0)) return normal;
        else return math.normalize(normal) * (density - cxt.IsoValue + 1) / (255.0f - cxt.IsoValue);
    }

    [BurstCompile]
    public float LinearDisplacement(float t, in int3 axis, in int3 plane, in MapContext cxt){
        int t0 = (int)Math.Floor(t); 
        int t1 = t0 + 1;

        int c0 = SampleTerrain(t0 * axis + plane, cxt);
        int c1 = SampleTerrain(t1 * axis + plane, cxt);
        float td = t - t0;

        int density = (int)Math.Round(c0 * (1 - td) + c1 * td);
        if(density < cxt.IsoValue) return 0;
        else return math.sign(-(c1-c0)) * (density - cxt.IsoValue + 1) / (255.0f - cxt.IsoValue); //Normal
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

    private readonly int3 AxisX => new(1, 0, 0);
    private readonly int3 AxisY => new(0, 1, 0);
    private readonly int3 AxisZ => new(0, 0, 1);
    
    private readonly int3x3 XZPlane => new (1, 0, 0, 0, 0, 1, 0, 1, 0);
    private readonly int3x3 XYPlane => new (1, 0, 0, 0, 1, 0, 0, 0, 1);
    private readonly int3x3 YZPlane => new (0, 0, 1, 1, 0, 0, 0, 1, 0);
    
    public bool SampleCollision(in float3 originGS, in float3 boundsGS, in MapContext context, out float3 displacement){
        float3 min = math.min(originGS, originGS + boundsGS);
        float3 max = math.max(originGS, originGS + boundsGS);
        displacement = float3.zero; 
        //8 corners
        for(int i = 0; i < 8; i++) {
            int3 index = new (i%2, i/2%2, i/4);
            float3 corner = min * index + max * (1 - index);
            displacement += TrilinearDisplacement(corner, context);
        }
        int3 minC = (int3)math.ceil(min);
        int3 maxC = (int3)math.floor(max);
        
        //3*4 = 12 edges
        for(int x = minC.x; x <= maxC.x; x++){
            for(int i = 0; i < 4; i++){
                int2 index = new (i%2, i/2%2);
                float2 corner = min.yz * index + max.yz * (1 - index);
                displacement.yz += BilinearDisplacement(corner, YZPlane, x, context);
            }
        }

        for(int y = minC.y; y <= maxC.y; y++){
            for(int i = 0; i < 4; i++){
                int2 index = new (i%2, i/2%2);
                float2 corner = min.xz * index + max.xz * (1 - index);
                displacement.xz += BilinearDisplacement(corner, XZPlane, y, context);
            }
        }

        for(int z = minC.z; z <= maxC.z; z++){
            for(int i = 0; i < 4; i++){
                int2 index = new (i%2, i/2%2);
                float2 corner = min.xy * index + max.xy * (1 - index);
                displacement.xy += BilinearDisplacement(corner, XYPlane, z, context);
            }
        }

        //3*2 = 6 faces
        for(int x = minC.x; x <= maxC.x; x++){
            for(int y = minC.y; y <= maxC.y; y++){
                displacement.z += LinearDisplacement(min.z, AxisZ, AxisX * x + AxisY * y, context);
                displacement.z += LinearDisplacement(max.z, AxisZ, AxisX * x + AxisY * y, context);
            }
        }

        for(int x = minC.x; x <= maxC.x; x++){
            for(int z = minC.z; z <= maxC.z; z++){
                displacement.y += LinearDisplacement(min.y, AxisY, AxisX * x + AxisZ * z, context);
                displacement.y += LinearDisplacement(max.y, AxisY, AxisX * x + AxisZ * z, context);
            }
        }

        for(int y = minC.y; y <= maxC.y; y++){
            for(int z = minC.z; z <= maxC.z; z++){
                displacement.x += LinearDisplacement(min.x, AxisX, AxisY * y + AxisZ * z, context);
                displacement.x += LinearDisplacement(max.x, AxisX, AxisY * y + AxisZ * z, context);
            }
        }

        return math.any(displacement != float3.zero);
    }

    public unsafe bool IsGrounded(float stickDist, in Settings settings, in MapContext cxt) => SampleCollision(transform.position, new float3(settings.size.x, -stickDist, settings.size.z), cxt, out _);
    
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
