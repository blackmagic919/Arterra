using UnityEngine;
using static CPUMapManager;
using Unity.Mathematics;
using Unity.Burst;
using System;
using WorldConfig.Generation.Structure;
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
/// <summary>
/// A concrete material that will attempt to perform liquid physics when updated. Liquid physics
/// simulate how liquids flow using a small set of specific rules.
/// <list type="number">
/// <item> A liquid will try to move from the update entry to the entry below it if it can. </item>
/// <item> Liquid above the update entry will try to move to the update entry if it can. </item>
/// <item> Liquid around the update entry will try to average out the liquid levels with the update entry if it can </item>
/// <item> A neighboring entry will only be updated if the state of the entry changes </item>
/// <item> A liquid can move between two entries if the entry it is moving to is gaseous, or if both entries are the same material </item>
/// </list>
/// This is the default behavior liquids use to emulate liquid physics. If left unchecked, the propogation of liquid
/// physics will eventually be curtailed by <see cref="TerrainGeneration.TerrainUpdate.ConstrainedQueue{T}"> the maximum 
/// amount of updates </see> defined by the system at which point liquid physics may prevent other terrain updates from occuring.
/// </summary>
[BurstCompile]
[CreateAssetMenu(menuName = "Generation/MaterialData/GrassMat")]
public class GrassMaterial : MaterialData
{
    [Range(0, 1)]
    public float SpreadChance;
    public string SpreadMaterial;
    public StructureData.CheckInfo SpreadBounds;
    readonly int3[] dP = new int3[6]{
        new (0,1,0),
        new (0,-1,0),
        new (1,0,0),
        new (0,0,-1),
        new (-1,0,0),
        new (0,0,1),
    };

    /// <summary> Updates the liquid material to perform liquid physics. </summary>
    /// <param name="GCoord">The coordinate in grid space of a map entry that is this material (a liquid material)</param>
    /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
    [BurstCompile]
    public override void UpdateMat(int3 GCoord, Unity.Mathematics.Random prng){
        MapData cur = SampleMap(GCoord); //Current 
        if(!cur.IsSolid) return;
        var matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        int spreadMat = matInfo.RetrieveIndex(SpreadMaterial);

        for(int i = 0; i < 6; i++){
            MapData neighbor = SampleMap(GCoord + dP[i]);
            if(neighbor.material != spreadMat) continue;
            if(!SpreadBounds.Contains(neighbor)) continue;
            if(prng.NextFloat() > SpreadChance) continue;
            neighbor.material = cur.material;
            SetMap(neighbor, GCoord + dP[i]);
        }
    }
}}
