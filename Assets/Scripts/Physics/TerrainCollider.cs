using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using static CPUDensityManager;
using System;

/*
Future Note: Make this done on a job system
(Someone good at math do this) ->
Sample from a simplex rather than a grid(should be faster)
*/
public class TerrainCollider : MonoBehaviour
{
    public float3 size;
    public float3 offset;
    private int isoValue;
    public int IsoValue => isoValue;
    private float lerpScale;
    public float LerpScale => lerpScale;
    private int chunkSize;
    public int ChunkSize => chunkSize;
    private bool active = true;
    public bool Active{get => active; set => active = value;}


    public float3 velocity;
    public bool useGravity;

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
        int density = (int)Math.Round(c0 * (1 - zd) + c1 * zd);
        if(density < isoValue) return float3.zero;
    
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
        else return math.normalize(normal) * GDist(density);
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
        int density = (int)Math.Round(c0 * (1 - yd) + c1 * yd);
        if(density < isoValue) return float2.zero;

        //Bilinear Normal
        float xC = (c10 - c00) * (1 - yd) + (c11 - c01) * yd;
        float yC = (c01 - c00) * (1 - xd) + (c11 - c10) * xd;
        
        float2 normal = -new float2(xC, yC);
        if(math.all(normal == 0)) return normal;
        else return math.normalize(normal) * GDist(density);
    }


    public float LinearDisplacement(float t, Func<int, int> SampleTerrain){
        int t0 = (int)Math.Floor(t); 
        int t1 = t0 + 1;

        int c0 = SampleTerrain(t0);
        int c1 = SampleTerrain(t1);
        float td = t - t0;

        int density = (int)Math.Round(c0 * (1 - td) + c1 * td);
        if(density < isoValue) return 0;
        else return math.sign(-(c1-c0)) * GDist(density); //Normal
    }

    //Add 1 to always move the player if collided
    public float GDist(int density) => (density - isoValue + 1) / (255.0f - isoValue);

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
    public bool SampleCollision(float3 originGS, float3 boundsGS, out float3 displacement){
        float3 min = math.min(originGS, originGS + boundsGS);
        float3 max = math.max(originGS, originGS + boundsGS);
        displacement = float3.zero; 
        //8 corners
        for(int i = 0; i < 8; i++) {
            int3 index = new (i%2, i/2%2, i/4);
            float3 corner = min * index + max * (1 - index);
            displacement += TrilinearDisplacement(corner);
        }
        int3 minC = (int3)math.ceil(min);
        int3 maxC = (int3)math.floor(max);

        //3*4 = 12 edges
        for(int x = minC.x; x <= maxC.x; x++){
            for(int i = 0; i < 4; i++){
                int2 index = new (i%2, i/2%2);
                float2 corner = min.yz * index + max.yz * (1 - index);
                displacement.yz += BilinearDisplacement(corner.x, corner.y, (int y, int z) => SampleTerrain(new int3(x, y, z)));
            }
        }

        for(int y = minC.y; y <= maxC.y; y++){
            for(int i = 0; i < 4; i++){
                int2 index = new (i%2, i/2%2);
                float2 corner = min.xz * index + max.xz * (1 - index);
                displacement.xz += BilinearDisplacement(corner.x, corner.y, (int x, int z) => SampleTerrain(new int3(x, y, z)));
            }
        }

        for(int z = minC.z; z <= maxC.z; z++){
            for(int i = 0; i < 4; i++){
                int2 index = new (i%2, i/2%2);
                float2 corner = min.xy * index + max.xy * (1 - index);
                displacement.xy += BilinearDisplacement(corner.x, corner.y, (int x, int y) => SampleTerrain(new int3(x, y, z)));
            }
        }

        //3*2 = 6 faces
        for(int x = minC.x; x <= maxC.x; x++){
            for(int y = minC.y; y <= maxC.y; y++){
                displacement.z += LinearDisplacement(min.z, (int z) => SampleTerrain(new int3(x,y,z)));
                displacement.z += LinearDisplacement(max.z, (int z) => SampleTerrain(new int3(x,y,z)));
            }
        }

        for(int x = minC.x; x <= maxC.x; x++){
            for(int z = minC.z; z <= maxC.z; z++){
                displacement.y += LinearDisplacement(min.y, (int y) => SampleTerrain(new int3(x,y,z)));
                displacement.y += LinearDisplacement(max.y, (int y) => SampleTerrain(new int3(x,y,z)));
            }
        }

        for(int y = minC.y; y <= maxC.y; y++){
            for(int z = minC.z; z <= maxC.z; z++){
                displacement.x += LinearDisplacement(min.x, (int x) => SampleTerrain(new int3(x,y,z)));
                displacement.x += LinearDisplacement(max.x, (int x) => SampleTerrain(new int3(x,y,z)));
            }
        }

        return math.any(displacement != float3.zero);
    }

    float3 CancelVel(float3 vel, float3 dir){
        dir = math.normalize(dir);
        return vel - math.dot(vel, dir) * dir;
    }


    public void Start(){
        this.isoValue = (int)Math.Round(WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value.IsoLevel * 255.0);
        this.lerpScale = WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value.lerpScale;
        this.chunkSize = WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value.mapChunkSize;
    }

    public void FixedUpdate(){
        if(!active) return;

        float3 posWS = transform.position;
        posWS += velocity * Time.fixedDeltaTime;
        if(useGravity) velocity += (float3)Physics.gravity * Time.fixedDeltaTime;

        float3 originGS = WSToGS(posWS) + offset;
        if(SampleCollision(originGS, size, out float3 displacement)){
            velocity = CancelVel(velocity, displacement);
            originGS += displacement;
        };

        posWS = GSToWS(originGS - offset);
        transform.position = posWS;
    }
}
