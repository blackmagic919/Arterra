using UnityEngine;
using Unity.Mathematics;
using Unity.Burst;
using System;
using WorldConfig.Generation.Structure;
using MapStorage;
/*
y
^      0  5        z
|      | /        /\
|      |/         /
| 4 -- c -- 2    /
|     /|        /
|    / |       /
|   3  1      /
+----------->x
*/

namespace WorldConfig.Generation.Material{

/// <summary> A concrete material that will attempt to spread itself to neighboring entries 
/// when and only when  randomly updated. </summary>
[BurstCompile]
[CreateAssetMenu(menuName = "Generation/MaterialData/GrassMat")]
public class GrassMaterial : MaterialData
{
    /// <summary> The chance that grass will spread to a neighboring entry. </summary>
    [Range(0, 1)]
    public float SpreadChance;
    /// <summary>  The material that grass will spread onto  </summary>
    public string SpreadMaterial;
    /// <summary> The <see cref="MapData"/> requirements of the material that the grass can spread onto.  </summary>

    [Header("Spread Bounds")]
    public StructureData.CheckInfo SpreadBounds;
    /// <summary> The <see cref="MapData"/> requirements of at least one neighbor of the material that the grass can spread onto.  </summary>
    [Header("Neighbor Bounds")]
    public StructureData.CheckInfo NeighborBounds;
    readonly int3[] dP = new int3[6]{
        new (0,1,0),
        new (0,-1,0),
        new (1,0,0),
        new (0,0,-1),
        new (-1,0,0),
        new (0,0,1),
    };

    /// <summary> Random Material Update entry used to trigger grass growth. </summary>
    /// <param name="GCoord">The coordinate in grid space of a map entry that is this material</param>
    /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
    [BurstCompile]
    public override void RandomMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng){
        MapData cur = CPUMapManager.SampleMap(GCoord); //Current 
        if(!cur.IsSolid) return;
        SpreadGrass(cur, GCoord, prng);
    }
    
    /// <summary> Mandatory callback for when grass is forcibly updated. Do nothing here. </summary>
    /// <param name="GCoord">The coordinate in grid space of a map entry that is this material</param>
    /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
    public override void PropogateMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng = default) {
        //nothing to do here
    }
    

    internal bool SpreadGrass(MapData cur, int3 GCoord, Unity.Mathematics.Random prng) {
            var matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
            int spreadMat = matInfo.RetrieveIndex(SpreadMaterial);

            bool spread = false;
            for (int i = 0; i < 6; i++) {
                if (prng.NextFloat() > SpreadChance) continue;
                int3 NCoord = GCoord + dP[i];
                MapData neighbor = CPUMapManager.SampleMap(NCoord);
                if (!CanSpread(neighbor, NCoord, spreadMat)) continue;
                neighbor.material = cur.material;
                CPUMapManager.SetMap(neighbor, NCoord);
                spread = true;
            }
            return spread;
        }

    private bool CanSpread(MapData neighbor, int3 GCoord, float spreadMat){
        if(neighbor.material != spreadMat) return false;
        if(!SpreadBounds.Contains(neighbor)) return false;
        for(int i = 0; i < 6; i++){
            MapData nNeighbor = CPUMapManager.SampleMap(GCoord + dP[i]);
            if(!NeighborBounds.Contains(nNeighbor)) continue;
            return true;
        }
        return false;
    }
}}
