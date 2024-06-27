using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
public static class CPUNoiseSampler
{

    public static float SampleTerrainHeight(float3 pos){
        float3 pos2D = new (pos.x, 0, pos.z);

        List<Option<NoiseData> > NoiseDict = WorldStorageHandler.WORLD_OPTIONS.WorldOptions.Noise.value;
        SurfaceCreatorSettings surface = WorldStorageHandler.WORLD_OPTIONS.WorldOptions.Surface.value;

        float PVNoise = SampleNoise(NoiseDict[surface.TerrainPVDetail].value, pos2D) * 2 - 1;
        float continental = SampleNoise(NoiseDict[surface.TerrainContinentalDetail].value, pos2D);
        float erosion = SampleNoise(NoiseDict[surface.TerrainErosionDetail].value, pos2D);
        float terrainHeight = (continental + PVNoise * erosion) * surface.MaxTerrainHeight + surface.terrainOffset;

        return terrainHeight;
    }

    

    public static float SampleNoise(NoiseData sampler, float3 pos){
        return interpolateValue(GetRawNoise(sampler, pos), sampler);
    }

    static uint Search(float targetValue, Vector4[] spline)
    {   
        uint index = 1; //must have at least 2 points
        for(; index < spline.Length; index++){
            if(spline[index].x >= targetValue)
                break;
        }
        return index;
    }

    static float interpolateValue(float value, NoiseData sampler){
        Vector4[] spline = sampler.SplineKeys;
        uint upperBoundIndex = Search(value, spline);

        float4 upperBound = spline[upperBoundIndex];
        float4 lowerBound = spline[upperBoundIndex - 1];

        float progress = Mathf.InverseLerp(lowerBound.x, upperBound.x, value);
        float dt = upperBound.x - lowerBound.x;

        float lowerAnchor = lowerBound.y + lowerBound.w * dt;
        float upperAnchor = upperBound.y - upperBound.z * dt;

        return Mathf.Lerp(
            Mathf.Lerp(lowerBound.y, lowerAnchor, progress), 
            Mathf.Lerp(upperAnchor, upperBound.y, progress), 
            progress
        );
    }

    public static float GetRawNoise(NoiseData sampler, float3 pos){
        float3[] offsets = sampler.OctaveOffsets.Select((Vector3 a) => new float3(a.x, a.y, a.z)).ToArray();

        float amplitude = 1;
        float frequency = 1;
        float noiseHeight = 0;
        for(int i = 0; i < offsets.Length; i++){
            float3 sample = (pos + offsets[i]) / sampler.noiseScale * frequency;
            float perlinValue = (SNoise(sample) + 1.0f) / 2.0f;
            noiseHeight = Mathf.Lerp(noiseHeight, perlinValue, amplitude);

            amplitude *= sampler.persistance;
            frequency *= sampler.lacunarity;
        }

        return noiseHeight;
    }
    
    private static float3 Mod289(float3 x)
    {
        return x - Floor(x / 289.0f) * 289.0f;
    }

    private static float4 Mod289(float4 x)
    {
        return x - Floor(x / 289.0f) * 289.0f;
    }

    private static float4 Permute(float4 x)
    {
        return Mod289((x * 34.0f + 1.0f) * x);
    }

    private static float4 TaylorInvSqrt(float4 r)
    {
        return 1.79284291400159f - r * 0.85373472095314f;
    }

    public static float SNoise(float3 v)
    {
        float2 C = new float2(1.0f / 6.0f, 1.0f / 3.0f);

        // First corner
        float3 i = Floor(v + Vector3.Dot(v, new float3(C.y, C.y, C.y)));
        float3 x0 = v - i + Vector3.Dot(i, new float3(C.x, C.x, C.x));

        // Other corners
        float3 g = Step(yzx(x0), x0);
        float3 l = 1.0f - g;
        float3 i1 = Vector3.Min(g, zxy(l));
        float3 i2 = Vector3.Max(g, zxy(l));

        // x1 = x0 - i1 + 1.0 * C.xxx;
        // x2 = x0 - i2 + 2.0 * C.xxx;
        // x3 = x0 - 1.0 + 3.0 * C.xxx;
        float3 x1 = x0 - i1 + new float3(C.x, C.x, C.x);
        float3 x2 = x0 - i2 + new float3(C.y, C.y, C.y);
        float3 x3 = x0 - 0.5f;

        // Permutations
        i = Mod289(i); // Avoid truncation effects in permutation
        float4 p = Permute(Permute(Permute(i.z + new float4(0.0f, i1.z, i2.z, 1.0f))
                                         + i.y + new float4(0.0f, i1.y, i2.y, 1.0f))
                                         + i.x + new float4(0.0f, i1.x, i2.x, 1.0f));

        // Gradients: 7x7 points over a square, mapped onto an octahedron.
        // The ring size 17*17 = 289 is close to a multiple of 49 (49*6 = 294)
        float4 j = p - 49.0f * Floor(p / 49.0f);  // mod(p,7*7)

        float4 x_ = Floor(j / 7.0f);
        float4 y_ = Floor(j - 7.0f * x_);  // mod(j,7)

        float4 x = (x_ * 2.0f + 0.5f) / 7.0f - 1.0f;
        float4 y = (y_ * 2.0f + 0.5f) / 7.0f - 1.0f;

        float4 h = 1.0f - Abs(x) - Abs(y);

        float4 b0 = new (x.x, x.y, y.x, y.y);
        float4 b1 = new (x.z, x.w, y.z, y.w);

        float4 s0 = Floor(b0) * 2.0f + 1.0f;
        float4 s1 = Floor(b1) * 2.0f + 1.0f;
        float4 sh = -Step(h, float4.zero);

        float4 a0 = xzyw(b0) + xzyw(s0) * xxyy(sh);
        float4 a1 = xzyw(b1) + xzyw(s1) * zzww(sh);

        float3 g0 = new(a0.x, a0.y, h.x);
        float3 g1 = new(a0.z, a0.w, h.y);
        float3 g2 = new(a1.x, a1.y, h.z);
        float3 g3 = new(a1.z, a1.w, h.w);

        // Normalize gradients
        float4 norm = TaylorInvSqrt(new float4(Vector3.Dot(g0, g0), Vector3.Dot(g1, g1), Vector3.Dot(g2, g2), Vector3.Dot(g3, g3)));
        g0 *= norm.x;
        g1 *= norm.y;
        g2 *= norm.z;
        g3 *= norm.w;

        // Mix final noise value
        float4 m = Vector4.Max(0.6f - new float4(Vector3.Dot(x0, x0),Vector3.Dot(x1, x1), Vector3.Dot(x2, x2), Vector3.Dot(x3, x3)), float4.zero);
        m *= m;
        m *= m;

        float4 px = new (Vector3.Dot(x0, g0), Vector3.Dot(x1, g1), Vector3.Dot(x2, g2), Vector3.Dot(x3, g3));
        return 42.0f * Vector4.Dot(m, px);
    }

    //This is why I miss vector operations DX
    private static float3 Step(float3 edge, float3 x) {return new float3(x.x < edge.x ? 0.0f : 1.0f, x.y < edge.y ? 0.0f : 1.0f, x.z < edge.z ? 0.0f : 1.0f);}
    private static float4 Step(float4 edge, float4 x) { return new float4(x.x < edge.x ? 0.0f : 1.0f, x.y < edge.y ? 0.0f : 1.0f, x.z < edge.z ? 0.0f : 1.0f, x.w < edge.w ? 0.0f : 1.0f);}
    private static float3 Floor(float3 a) {return new float3(Mathf.Floor(a.x), Mathf.Floor(a.y), Mathf.Floor(a.z));}
    private static float4 Floor(float4 a) {return new float4(Mathf.Floor(a.x), Mathf.Floor(a.y), Mathf.Floor(a.z), Mathf.Floor(a.w));}
    private static float4 Abs(float4 a) {return new float4(Mathf.Abs(a.x), Mathf.Abs(a.y), Mathf.Abs(a.z), Mathf.Abs(a.w));}

    private static float3 yzx(float3 a) {return new float3(a.y, a.z, a.x);}
    private static float3 zxy(float3 a) {return new float3(a.z, a.x, a.y);}
    private static float4 xzyw(float4 a) {return new float4(a.x, a.z, a.y, a.w);}
    private static float4 xxyy(float4 a) {return new float4(a.x, a.x, a.y, a.y);}
    private static float4 zzww(float4 a) {return new float4(a.z, a.z, a.w, a.w);}
}
