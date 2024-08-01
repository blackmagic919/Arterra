using System;
using UnityEngine;

public abstract class EntityController : MonoBehaviour
{
    // Start is called before the first frame update
    public abstract void Initialize(IntPtr entity);
    // Update is called once per frame
    public abstract void Update();
    public abstract void OnDisable();
}
