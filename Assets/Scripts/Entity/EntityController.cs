using System;
using UnityEngine;

public abstract class EntityController : MonoBehaviour
{
    // Start is called before the first frame update
    public virtual void Initialize(IntPtr entity){
        OctreeTerrain.OrderedDisable.AddListener(Disable);
    }
    // Update is called once per frame
    public abstract void Update();
    public abstract void Disable();
}
