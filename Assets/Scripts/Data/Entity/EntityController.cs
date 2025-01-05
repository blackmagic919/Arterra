using System;
using UnityEngine;

public abstract class EntityController : MonoBehaviour
{
    // Start is called before the first frame update
    public virtual void Initialize(IntPtr entity){
        TerrainGeneration.OctreeTerrain.OrderedDisable.AddListener(Disable);
    }
    // Update is called once per frame
    public abstract void Update();
    public virtual void Disable(){
        TerrainGeneration.OctreeTerrain.OrderedDisable.RemoveListener(Disable);
    }
}
