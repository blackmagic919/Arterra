using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using static CPUDensityManager;
using System;

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
    public float3[] GetCorners(float3 min, float3 max)
    {
        return new float3[]
        {
            new (min.x, min.y, min.z),
            new (max.x, min.y, min.z),
            new (min.x, max.y, min.z),
            new (max.x, max.y, min.z),
            new (min.x, min.y, max.z),
            new (max.x, min.y, max.z),
            new (min.x, max.y, max.z),
            new (max.x, max.y, max.z)
        };
    }

    public float2[] GetCorners2D(float2 min, float2 max)
    {
        return new float2[]
        {
            new (min.x, min.y),
            new (max.x, min.y),
            new (max.x, max.y),
            new (min.x, max.y),
        };
    }

    public float3 TrilinearNormal(float3 posGS){
        int x0 = (int)System.Math.Floor(posGS.x); int x1 = x0 + 1;
        int y0 = (int)System.Math.Floor(posGS.y); int y1 = y0 + 1;
        int z0 = (int)System.Math.Floor(posGS.z); int z1 = z0 + 1;

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
        return -(new float3(xC, yC, zC) / 255.0f);
    }

    public int TrilinearDensity(float3 posGS){
        int x0 = (int)System.Math.Floor(posGS.x); int x1 = x0 + 1;
        int y0 = (int)System.Math.Floor(posGS.y); int y1 = y0 + 1;
        int z0 = (int)System.Math.Floor(posGS.z); int z1 = z0 + 1;

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
        return (int)Math.Round(c0 * (1 - zd) + c1 * zd);
        //This is a different way of writing *Blend* logic
    }

    public float2 BilinearNormal(float x, float y, Func<int, int, int> SampleTerrain){
        int x0 = (int)Math.Floor(x); int x1 = x0 + 1;
        int y0 = (int)Math.Floor(y); int y1 = y0 + 1;

        int c00 = SampleTerrain(x0, y0);
        int c10 = SampleTerrain(x1, y0);
        int c01 = SampleTerrain(x0, y1);
        int c11 = SampleTerrain(x1, y1);

        float xd = x - x0;
        float yd = y - y0;

        float xC = (c10 - c00) * (1 - yd) + (c11 - c01) * yd;
        float yC = (c01 - c00) * (1 - xd) + (c11 - c10) * xd;
        
        return -(new float2(xC, yC) / 255.0f);
    }

    public int BilinearDensity(float x, float y, Func<int, int, int> SampleTerrain){
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
        return (int)Math.Round(c0 * (1 - yd) + c1 * yd);
    }

    public float LinearNormal(float t, Func<int, int> SampleTerrain){
        int t0 = (int)Math.Floor(t); 
        int t1 = t0 + 1;

        int c0 = SampleTerrain(t0);
        int c1 = SampleTerrain(t1);
        
        return -((c1-c0) / 255.0f);
    }

    public int LinearDensity(float t, Func<int, int> SampleTerrain){
        int t0 = (int)Math.Floor(t); 
        int t1 = t0 + 1;

        int c0 = SampleTerrain(t0);
        int c1 = SampleTerrain(t1);
        float td = t - t0;

        return (int)Math.Round(c0 * (1 - td) + c1 * td);
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
    public bool SampleCollision(float3 originGS, float3 boundsGS, out float3 displacement){
        float3 min = math.min(originGS, originGS + boundsGS);
        float3 max = math.max(originGS, originGS + boundsGS);
        float3[] corners = GetCorners(min, max);
        displacement = float3.zero; int density; 
        //8 corners
        for(int i = 0; i < 8; i++){
            density = TrilinearDensity(corners[i]);
            if(density >= isoValue) displacement += TrilinearNormal(corners[i]);
        }
        int3 minC = (int3)math.ceil(min);
        int3 maxC = (int3)math.floor(max);

        //3*4 = 12 edges
        for(int x = minC.x; x <= maxC.x; x++){
            float2[] corners2D = GetCorners2D(min.yz, max.yz);
            for(int i = 0; i < 4; i++){
                float2 corner = corners2D[i];
                density = BilinearDensity(corner.x, corner.y, (int y, int z) => SampleTerrain(new int3(x, y, z)));
                if(density >= isoValue) displacement.yz += BilinearNormal(corner.x, corner.y, (int y, int z) => SampleTerrain(new int3(x, y, z)));
            }
        }

        for(int y = minC.y; y <= maxC.y; y++){
            float2[] corners2D = GetCorners2D(min.xz, max.xz);
            for(int i = 0; i < 4; i++){
                float2 corner = corners2D[i];
                density = BilinearDensity(corner.x, corner.y, (int x, int z) => SampleTerrain(new int3(x, y, z)));
                if(density >= isoValue) displacement.xz +=  BilinearNormal(corner.x, corner.y, (int x, int z) => SampleTerrain(new int3(x, y, z)));
            }
        }

        for(int z = minC.z; z <= maxC.z; z++){
            float2[] corners2D = GetCorners2D(min.xy, max.xy);
            for(int i = 0; i < 4; i++){
                float2 corner = corners2D[i];
                density = BilinearDensity(corner.x, corner.y, (int x, int y) => SampleTerrain(new int3(x, y, z)));
                if(density >= isoValue) displacement.xy += BilinearNormal(corner.x, corner.y, (int x, int y) => SampleTerrain(new int3(x, y, z)));
            }
        }

        //3*2 = 6 faces
        for(int x = minC.x; x <= maxC.x; x++){
            for(int y = minC.y; y <= maxC.y; y++){
                density = LinearDensity(min.z, (int z) => SampleTerrain(new int3(x,y,z)));
                if(density >= isoValue) displacement.z += LinearNormal(min.z, (int z) => SampleTerrain(new int3(x,y,z)));
                density = LinearDensity(max.z, (int z) => SampleTerrain(new int3(x,y,z)));
                if(density >= isoValue) displacement.z += LinearNormal(max.z, (int z) => SampleTerrain(new int3(x,y,z)));
            }
        }

        for(int x = minC.x; x <= maxC.x; x++){
            for(int z = minC.z; z <= maxC.z; z++){
                density = LinearDensity(min.y, (int y) => SampleTerrain(new int3(x,y,z)));
                if(density >= isoValue) displacement.y += LinearNormal(min.y, (int y) => SampleTerrain(new int3(x,y,z)));
                density = LinearDensity(max.y, (int y) => SampleTerrain(new int3(x,y,z)));
                if(density >= isoValue) displacement.y += LinearNormal(max.y, (int y) => SampleTerrain(new int3(x,y,z)));
            }
        }

        for(int y = minC.y; y <= maxC.y; y++){
            for(int z = minC.z; z <= maxC.z; z++){
                density = LinearDensity(min.x, (int x) => SampleTerrain(new int3(x,y,z)));
                if(density >= isoValue) displacement.x += LinearNormal(min.x, (int x) => SampleTerrain(new int3(x,y,z)));
                density = LinearDensity(max.x, (int x) => SampleTerrain(new int3(x,y,z)));
                if(density >= isoValue) displacement.x += LinearNormal(max.x, (int x) => SampleTerrain(new int3(x,y,z)));
            }
        }

        return math.any(displacement != float3.zero);
    }

    public bool IsCollided(float3 originGS, float3 boundsGS){
        float3 min = math.min(originGS, originGS + boundsGS);
        float3 max = math.max(originGS, originGS + boundsGS);
        float3[] corners = GetCorners(min, max);
        //8 corners
        for(int i = 0; i < 8; i++){
            if(TrilinearDensity(corners[i]) >= isoValue) 
                return true;
        }
        int3 minC = (int3)math.ceil(min);
        int3 maxC = (int3)math.floor(max);

        //3*4 = 12 edges
        for(int x = minC.x; x <= maxC.x; x++){
            float2[] corners2D = GetCorners2D(min.yz, max.yz);
            for(int i = 0; i < 4; i++){
                float2 corner = corners2D[i];
                if(BilinearDensity(corner.x, corner.y, (int y, int z) => SampleTerrain(new int3(x, y, z))) >= isoValue) 
                    return true;
            }
        }

        for(int y = minC.y; y <= maxC.y; y++){
            float2[] corners2D = GetCorners2D(min.xz, max.xz);
            for(int i = 0; i < 4; i++){
                float2 corner = corners2D[i];
                if(BilinearDensity(corner.x, corner.y, (int x, int z) => SampleTerrain(new int3(x, y, z))) >= isoValue)
                    return true;
            }
        }

        for(int z = minC.z; z <= maxC.z; z++){
            float2[] corners2D = GetCorners2D(min.xy, max.xy);
            for(int i = 0; i < 4; i++){
                float2 corner = corners2D[i];
                if(BilinearDensity(corner.x, corner.y, (int x, int y) => SampleTerrain(new int3(x, y, z))) >= isoValue)
                    return true;
            }
        }

        //3*2 = 6 faces
        for(int x = minC.x; x <= maxC.x; x++){
            for(int y = minC.y; y <= maxC.y; y++){
                if(LinearDensity(min.z, (int z) => SampleTerrain(new int3(x,y,z))) >= isoValue) return true;
                if(LinearDensity(max.z, (int z) => SampleTerrain(new int3(x,y,z))) >= isoValue) return true;
            }
        }

        for(int x = minC.x; x <= maxC.x; x++){
            for(int z = minC.z; z <= maxC.z; z++){
                if(LinearDensity(min.y, (int y) => SampleTerrain(new int3(x,y,z))) >= isoValue) return true;
                if(LinearDensity(max.y, (int y) => SampleTerrain(new int3(x,y,z))) >= isoValue) return true;
            }
        }

        for(int y = minC.y; y <= maxC.y; y++){
            for(int z = minC.z; z <= maxC.z; z++){
                if(LinearDensity(min.x, (int x) => SampleTerrain(new int3(x,y,z))) >= isoValue) return true;
                if(LinearDensity(max.x, (int x) => SampleTerrain(new int3(x,y,z))) >= isoValue) return true;
            }
        }

        return false;
    }

    private bool ResolveCollision(float3 originGS, float3 boundsGS, out float3 displacement){
        displacement = float3.zero;
        if(!SampleCollision(originGS, boundsGS, out float3 normal))
            return false;
        float dP = math.length(normal) / 2;
        normal = math.normalize(normal);
        displacement = normal * dP;

        do{
            dP /= 2;
            if(SampleCollision(displacement + originGS, boundsGS, out float3 nNormal)) {
                normal = math.normalize(nNormal);
                displacement += normal * dP;
            } else displacement -= normal * dP;
        } while(dP > Physics.defaultContactOffset); 
        return true;
    }

    float3 CancelVel(float3 vel, float3 dir){
        dir = math.normalize(dir);
        return vel - math.dot(vel, dir) * dir;
    }


    public void Start(){
        this.isoValue = (int)Math.Round(WorldStorageHandler.WORLD_OPTIONS.Rendering.value.IsoLevel * 255.0);
        this.lerpScale = WorldStorageHandler.WORLD_OPTIONS.Rendering.value.lerpScale;
        this.chunkSize = WorldStorageHandler.WORLD_OPTIONS.Rendering.value.mapChunkSize;
    }

    public void FixedUpdate(){
        if(!active) return;

        float3 posWS = transform.position;
        posWS += velocity * Time.fixedDeltaTime;
        if(useGravity) velocity += (float3)Physics.gravity * Time.fixedDeltaTime;

        float3 originGS = WSToGS(posWS) + offset;
        if(ResolveCollision(originGS, size, out float3 displacement)){
            velocity = CancelVel(velocity, displacement);
            originGS += displacement;
        };

        posWS = GSToWS(originGS - offset);
        transform.position = posWS;
    }
}
