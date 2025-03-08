using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class UpdateTask
{
    public bool active = false;
    public virtual void Update(MonoBehaviour mono = null){
        //Do Something
    }
}

public class IndirectUpdate : UpdateTask{
    Action<MonoBehaviour> callback;
    public IndirectUpdate(Action<MonoBehaviour> callback){
        this.active = true;
        this.callback = callback;
    }
    public override void Update(MonoBehaviour mono = null)
    {
        callback.Invoke(mono);
        base.Update(mono);
    }
}
