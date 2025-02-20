using UnityEngine;
using Unity.Mathematics;
using static CPUMapManager;
using System;
using WorldConfig;
using Newtonsoft.Json;

/*
Future Note: Make this done on a job system
(Someone good at math do this) ->
Sample from a simplex rather than a grid(should be faster)
*/
public class PlayerCollider
{
    private float IsoValue => (float)Math.Round(Config.CURRENT.Quality.Terrain.value.IsoLevel * 255.0);
    public float3 velocity;

    public float3 TrilinearDisplacement(float3 posGS){
        //Calculate Density
        int x0 = (int)Math.Floor(posGS.x); int x1 = x0 + 1;
        int y0 = (int)Math.Floor(posGS.y); int y1 = y0 + 1;
        int z0 = (int)Math.Floor(posGS.z); int z1 = z0 + 1;

        int c000 = SampleTerrain(new int3(x0, y0, z0));
        int c100 = SampleTerrain(new int3(x1, y0, z0));
        int c010 = SampleTerrain(new int3(x0, y1, z0));
        int c110 = SampleTerrain(new int3(x1, y1, z0));
        int c001 = SampleTerrain(new int3(x0, y0, z1));
        int c101 = SampleTerrain(new int3(x1, y0, z1));
        int c011 = SampleTerrain(new int3(x0, y1, z1));
        int c111 = SampleTerrain(new int3(x1, y1, z1));

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
        if(density < IsoValue) return float3.zero;
    
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
        return normal * TrilinearGradientLength(density, posGS, normal);
    }

    public float2 BilinearDisplacement(float x, float y, Func<int, int, int> SampleTerrain){
        int x0 = (int)Math.Floor(x); int x1 = x0 + 1;
        int y0 = (int)Math.Floor(y); int y1 = y0 + 1;

        int c00 = SampleTerrain(x0, y0);
        int c10 = SampleTerrain(x1, y0);
        int c01 = SampleTerrain(x0, y1);
        int c11 = SampleTerrain(x1, y1);

        float xd = x - x0;
        float yd = y - y0;

        float c0 = c00 * (1 - xd) + c10 * xd;
        float c1 = c01 * (1 - xd) + c11 * xd;
        float density = c0 * (1 - yd) + c1 * yd;
        if(density < IsoValue) return float2.zero;

        //Bilinear Normal
        float xC = (c10 - c00) * (1 - yd) + (c11 - c01) * yd;
        float yC = (c01 - c00) * (1 - xd) + (c11 - c10) * xd;
        
        float2 normal = -new float2(xC, yC);
        if(math.all(normal == 0)) return normal;
        normal = math.normalize(normal);
        return normal * BilinearGradientLength(density, new float2(x,y), normal, SampleTerrain);
    }


    public float LinearDisplacement(float t, Func<int, int> SampleTerrain){
        int t0 = (int)Math.Floor(t); 
        int t1 = t0 + 1;

        int c0 = SampleTerrain(t0);
        int c1 = SampleTerrain(t1);
        float td = t - t0;

        float density = c0 * (1 - td) + c1 * td;
        if(density < IsoValue) return 0;
        float normal = math.sign(-(c1-c0));
        return normal * LinearGradientLength(density, t, normal, SampleTerrain); //Normal
    }

    public float LinearGradientLength(float density, float pos, float normal, Func<int, int> SampleTerrain){
        int corner = (int)(math.floor(pos) + math.max(normal, 0));
        float cDen = SampleTerrain(corner);
        if(cDen >= density) return 0;
        return math.clamp((IsoValue - density) / (cDen - density), 0, 1) * math.abs(corner - pos);
    }
    public float BilinearGradientLength(float density, float2 pos, float2 normal, Func<int, int, int> SampleTerrain){
        float2 tMax = 1.0f / math.abs(normal); float eDen;
        tMax.x *= normal.x >= 0 ? 1 - math.frac(pos.x) : math.frac(pos.x);
        tMax.y *= normal.y >= 0 ? 1 - math.frac(pos.y) : math.frac(pos.y);

        float t = math.cmin(tMax);
        pos += normal * t; 

        if(tMax.y >= tMax.x){ eDen = LinearDensity(pos.y, (int y) => SampleTerrain(Mathf.RoundToInt(pos.x), y));} 
        else { eDen = LinearDensity(pos.x, (int x) => SampleTerrain(x, Mathf.RoundToInt(pos.y))); } 
        if(eDen >= density) return 0;

        return math.clamp((IsoValue - density) / (eDen - density), 0, 1) * t;
    }

    public float TrilinearGradientLength(float density, float3 pos, float3 normal){
        float3 tMax = 1.0f / math.abs(normal); float fDen;
        tMax.x *= normal.x >= 0 ? 1 - math.frac(pos.x) : math.frac(pos.x);
        tMax.y *= normal.y >= 0 ? 1 - math.frac(pos.y) : math.frac(pos.y);
        tMax.z *= normal.z >= 0 ? 1 - math.frac(pos.z) : math.frac(pos.z);

        float t = math.cmin(tMax); 
        pos += normal * t; 

        if(t == tMax.x){ fDen = BilinearDensity(pos.y, pos.z, (int y, int z) => SampleTerrain(new int3(Mathf.RoundToInt(pos.x), y, z)));} 
        else if(t == tMax.y){fDen = BilinearDensity(pos.x, pos.z, (int x, int z) => SampleTerrain(new int3(x, Mathf.RoundToInt(pos.y), z)));}
        else{fDen = BilinearDensity(pos.x, pos.y, (int x, int y) => SampleTerrain(new int3(x, y, Mathf.RoundToInt(pos.z))));}
        if(fDen >= density) return 0;

        return math.clamp((IsoValue - density) / (fDen - density), 0, 1) * t;
    }

    private static float LinearDensity(float t, Func<int, int> SampleTerrain){
        int t0 = (int)Math.Floor(t); 
        int t1 = t0 + 1;

        int c0 = SampleTerrain(t0);
        int c1 = SampleTerrain(t1);
        float td = t - t0;

        return c0 * (1 - td) + c1 * td;
    }

    private static float BilinearDensity(float x, float y, Func<int, int, int> SampleTerrain){
        int x0 = (int)Math.Floor(x); int x1 = x0 + 1;
        int y0 = (int)Math.Floor(y); int y1 = y0 + 1;

        int c00 = SampleTerrain(x0, y0);
        int c10 = SampleTerrain(x1, y0);
        int c01 = SampleTerrain(x0, y1);
        int c11 = SampleTerrain(x1, y1);

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


    private bool SampleFaceCollision(float3 originGS, float3 boundsGS, out float3 displacement){
        float3 min = math.min(originGS, originGS + boundsGS);
        float3 max = math.max(originGS, originGS + boundsGS);
        int3 minC = (int3)math.ceil(min); float3 minDis = float3.zero;
        int3 maxC = (int3)math.floor(max); float3 maxDis = float3.zero;
        float dis;

        //3*2 = 6 faces
        for(int x = minC.x; x <= maxC.x; x++){
            for(int y = minC.y; y <= maxC.y; y++){
                dis = LinearDisplacement(min.z, (int z) => SampleTerrain(new int3(x,y,z)));
                maxDis.z = math.max(dis, maxDis.z); minDis.z = math.min(dis, minDis.z);
                dis = LinearDisplacement(max.z, (int z) => SampleTerrain(new int3(x,y,z)));
                maxDis.z = math.max(dis, maxDis.z); minDis.z = math.min(dis, minDis.z);
            }
        }

        for(int x = minC.x; x <= maxC.x; x++){
            for(int z = minC.z; z <= maxC.z; z++){
                dis = LinearDisplacement(min.y, (int y) => SampleTerrain(new int3(x,y,z)));
                maxDis.y = math.max(dis, maxDis.y); minDis.y = math.min(dis, minDis.y);
                dis = LinearDisplacement(max.y, (int y) => SampleTerrain(new int3(x,y,z)));
                maxDis.y = math.max(dis, maxDis.y); minDis.y = math.min(dis, minDis.y);
            }
        }

        for(int y = minC.y; y <= maxC.y; y++){
            for(int z = minC.z; z <= maxC.z; z++){
                dis = LinearDisplacement(min.x, (int x) => SampleTerrain(new int3(x,y,z)));
                maxDis.x = math.max(dis, maxDis.x); minDis.x = math.min(dis, minDis.x);
                dis = LinearDisplacement(max.x, (int x) => SampleTerrain(new int3(x,y,z)));
                maxDis.x = math.max(dis, maxDis.x); minDis.x = math.min(dis, minDis.x);
            }
        }

        displacement = maxDis + minDis;
        return math.any(displacement != float3.zero);
    }

    private bool SampleEdgeCollision(float3 originGS, float3 boundsGS, out float3 displacement){
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
                dis = BilinearDisplacement(corner.x, corner.y, (int y, int z) => SampleTerrain(new int3(x, y, z)));
                maxDis.yz = math.max(dis, maxDis.yz); minDis.yz = math.min(dis, minDis.yz);
            }
        }

        for(int y = minC.y; y <= maxC.y; y++){
            for(int i = 0; i < 4; i++){
                int2 index = new (i%2, i/2%2);
                float2 corner = min.xz * index + max.xz * (1 - index);
                dis = BilinearDisplacement(corner.x, corner.y, (int x, int z) => SampleTerrain(new int3(x, y, z)));
                maxDis.xz = math.max(dis, maxDis.xz); minDis.xz = math.min(dis, minDis.xz);
            }
        }

        for(int z = minC.z; z <= maxC.z; z++){
            for(int i = 0; i < 4; i++){
                int2 index = new (i%2, i/2%2);
                float2 corner = min.xy * index + max.xy * (1 - index);
                dis = BilinearDisplacement(corner.x, corner.y, (int x, int y) => SampleTerrain(new int3(x, y, z)));
                maxDis.xy = math.max(dis, maxDis.xy); minDis.xy = math.min(dis, minDis.xy);
            }
        } 
        
        displacement = maxDis + minDis;
        return math.any(displacement != float3.zero);
    }

    private bool SampleCornerCollision(float3 originGS, float3 boundsGS, out float3 displacement){
        float3 min = math.min(originGS, originGS + boundsGS);
        float3 max = math.max(originGS, originGS + boundsGS);
        float3 maxDis = float3.zero; float3 minDis = float3.zero;

        //8 corners
        for(int i = 0; i < 8; i++) {
            int3 index = new (i%2, i/2%2, i/4);
            float3 corner = min * index + max * (1 - index);
            float3 dis = TrilinearDisplacement(corner);
            maxDis = math.max(dis, maxDis);
            minDis = math.min(dis, minDis);
        }
        
        displacement = maxDis + minDis;
        return math.any(displacement != float3.zero);
    }

    public bool SampleCollision(float3 originGS, float3 boundsGS, out float3 displacement){
        float3 origin = originGS;
        SampleCornerCollision(origin, boundsGS, out displacement);
        origin += displacement; 
        SampleEdgeCollision(origin, boundsGS, out displacement);
        origin += displacement;
        SampleFaceCollision(origin, boundsGS, out displacement);
        displacement += origin - originGS;
        
        return math.any(displacement != float3.zero);
    }

    static float3 CancelVel(float3 vel, float3 dir){
        dir = math.normalize(dir);
        return vel - math.dot(vel, dir) * dir;
    }

    public void FixedUpdate(PlayerStreamer.Player data, TerrainColliderJob.Settings settings){
        float3 posGS = data.position;
        posGS += velocity * Time.fixedDeltaTime;

        float3 originGS = posGS + settings.offset;
        if(SampleCollision(originGS, settings.size, out float3 displacement)){
            velocity = CancelVel(velocity, displacement);
            originGS += displacement;
        };

        data.position = originGS - settings.offset;
    }
}
