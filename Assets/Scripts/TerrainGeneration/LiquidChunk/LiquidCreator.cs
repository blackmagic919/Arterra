using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using static EndlessTerrain;

[CreateAssetMenu(menuName = "Containers/WaterGen Settings")]
public class LiquidCreatorSettings : ScriptableObject{
    public float WaterTerrainHeight;
}
public class LiquidCreator
{
    LiquidCreatorSettings settings;
    Queue<ComputeBuffer> tempBuffers = new Queue<ComputeBuffer>();

    public LiquidCreator(LiquidCreatorSettings settings){
        this.settings = settings;
    }

    
}
