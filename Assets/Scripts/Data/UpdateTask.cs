using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class UpdateTask
{
    public bool initialized = false;
    public bool enqueued = false;
    public virtual void Update(){

    }
}
