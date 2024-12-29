using UnityEngine;
using static CPUDensityManager;
using Unity.Mathematics;
using Unity.Burst;
using System;
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
[BurstCompile]
[CreateAssetMenu(menuName = "Generation/MaterialData/LiquidMat")]
public class LiquidMaterial : MaterialData
{
    readonly int3[] dP = new int3[6]{
        new (0,1,0),
        new (0,-1,0),
        new (1,0,0),
        new (0,0,-1),
        new (-1,0,0),
        new (0,0,1),
    };

    [BurstCompile]
    private void TransferLiquid(ref MapData a1, ref MapData b1){
        int amount = a1.LiquidDensity; //Amount that's transferred
        amount = math.min(b1.density + amount, 255) - b1.density;
        b1.density += amount;
        a1.density -= amount;
    }
    
    [BurstCompile]
    private void AverageLiquid(ref MapData min1, ref MapData max1){
        //make sure max is max liquid and min is min liquid
        if(min1.LiquidDensity >= max1.LiquidDensity){
            ref MapData temp = ref min1;
            min1 = ref max1;
            max1 = ref temp;
        }

        //Rounds down to the nearest integer
        int amount = (max1.LiquidDensity - min1.LiquidDensity) >> 1;
        amount = math.min(min1.density + amount, 255) - min1.density;
        amount = max1.density - math.max(max1.density - amount, 0);
        min1.density += amount;
        max1.density -= amount;
    }

    [BurstCompile]
    public override void UpdateMat(int3 GCoord){
        byte ChangeState = (byte)0;
        MapData cur = SampleMap(GCoord); //Current 
        if(cur.IsSolid) return;

        int material = cur.material;
        MapData[] map = {SampleMap(GCoord + dP[0]), SampleMap(GCoord + dP[1]), SampleMap(GCoord + dP[2]), 
                         SampleMap(GCoord + dP[3]), SampleMap(GCoord + dP[4]), SampleMap(GCoord + dP[5])};
        
        for(int i = 0; i < 6; i++){
            ChangeState ^= (byte)((map[i].IsLiquid ? 1 : 0) << i);
            if(map[i].IsSolid) return;
        }
        
        if(map[1].material == material || map[1].IsGaseous) {
            TransferLiquid(ref cur, ref map[1]);
            if(map[1].IsLiquid) map[1].material = material;
        } 
        if(map[0].material == material){
            TransferLiquid(ref map[0], ref cur);
        }
        
        for(int i = 2; i < 6; i++){
            if(map[i].material == material || map[i].IsGaseous){
                AverageLiquid(ref cur, ref map[i]);
                //If the point became liquid, make it the same material
                if(map[i].IsLiquid) map[i].material = material;
            }
        }
        
        SetMap(cur, GCoord);
        for(int i = 0; i < 6; i++){
            //Update the map
            SetMap(map[i], GCoord + dP[i]);
            //If state changed, add it to be updated
            if((((ChangeState >> i) & 0x1) ^ (map[i].IsLiquid ? 1 : 0)) != 0)
                TerrainUpdateManager.AddUpdate(GCoord + dP[i]); 
        }
        
    }
}
