using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using WorldConfig;

public static class CPUNoiseSampler
{

    public static float SampleTerrainHeight(float3 pos){
        var NoiseDict = Config.CURRENT.Generation.Noise.Reg.value;
        WorldConfig.Generation.Surface surface = Config.CURRENT.Generation.Surface.value;

        float PVNoise = SampleNoise(NoiseDict[surface.PVIndex].Value, pos.xz) * 2 - 1;
        float continental = SampleNoise(NoiseDict[surface.ContinentalIndex].Value, pos.xz);
        float erosion = SampleNoise(NoiseDict[surface.ErosionIndex].Value, pos.xz);
        float terrainHeight = (continental + PVNoise * erosion) * surface.MaxTerrainHeight + surface.terrainOffset;
        
        return terrainHeight;
    }

    

    public static float SampleNoise(WorldConfig.Generation.Noise sampler, float2 pos){
        return InterpolateValue(GetRawNoise(sampler, pos), sampler);
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

    static float InterpolateValue(float value, WorldConfig.Generation.Noise sampler){
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

    public static float GetRawNoise(WorldConfig.Generation.Noise sampler, float2 pos){
        float3[] offsets = sampler.OctaveOffsets.Select((Vector3 a) => new float3(a.x, a.y, a.z)).ToArray();

        float amplitude = 1;
        float frequency = 1;
        float noiseHeight = 0;
        for(int i = 0; i < offsets.Length; i++){
            float2 sample = (pos + offsets[i].xy) / sampler.noiseScale * frequency;
            float perlinValue = (Snoise2D(sample) + 1.0f) / 2.0f;
            noiseHeight = math.lerp(noiseHeight, perlinValue, amplitude);

            amplitude *= sampler.persistance;
            frequency *= sampler.lacunarity;
        }

        return noiseHeight;
    }
    private static float2 Mod289(float2 x) {
        return x - math.floor(x / 289.0f) * 289.0f;
    }

    private static float3 Mod289(float3 x)
    {
        return x - math.floor(x / 289.0f) * 289.0f;
    }

    private static float4 Mod289(float4 x)
    {
        return x - math.floor(x / 289.0f) * 289.0f;
    }

    private static float3 Permute(float3 x) {
        return Mod289((x * 34.0f + 1.0f) * x);
    }
    public static float3 TaylorInvSqrt(float3 r) {
        return 1.79284291400159f - 0.85373472095314f * r;
    }

    private static float Snoise2D(float2 v) {
        float4 C = new (0.211324865405187f,  // (3.0-sqrt(3.0))/6.0
                        0.366025403784439f,  // 0.5*(sqrt(3.0)-1.0)
                        -0.577350269189626f, // -1.0 + 2.0 * C.x
                        0.024390243902439f); // 1.0 / 41.0

        // First corner
        float2 i  = math.floor(v + math.dot(v, C.yy));
        float2 x0 = v -   i + math.dot(i, C.xx);

        // Other corners
        float2 i1;
        i1.x = math.step(x0.y, x0.x);
        i1.y = 1.0f - i1.x;

        // x1 = x0 - i1  + 1.0 * C.xx;
        // x2 = x0 - 1.0 + 2.0 * C.xx;
        float2 x1 = x0 + C.xx - i1;
        float2 x2 = x0 + C.zz;

        // Permutations
        i = Mod289(i); // Avoid truncation effects in permutation
        float3 p =
        Permute(Permute(i.y + new float3(0.0f, i1.y, 1.0f))
                        + i.x + new float3(0.0f, i1.x, 1.0f));

        float3 m = math.max(0.5f - new float3(math.dot(x0, x0), math.dot(x1, x1), math.dot(x2, x2)), 0.0f);
        m *= m;
        m *= m;

        // Gradients: 41 points uniformly over a line, mapped onto a diamond.
        // The ring size 17*17 = 289 is close to a multiple of 41 (41*7 = 287)
        float3 x = 2.0f * math.frac(p * C.www) - 1.0f;
        float3 h = math.abs(x) - 0.5f;
        float3 ox = math.floor(x + 0.5f);
        float3 a0 = x - ox;

        // Normalise gradients implicitly by scaling m
        m *= TaylorInvSqrt(a0 * a0 + h * h);

        // Compute final noise value at P
        float3 g = new(
            a0.x * x0.x + h.x * x0.y,
            a0.y * x1.x + h.y * x1.y,
            g.z = a0.z * x2.x + h.z * x2.y
        );
        return 130.0f * math.dot(m, g);
    }
}
