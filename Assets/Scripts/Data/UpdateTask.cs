using System;
using UnityEngine;

public interface IUpdateSubscriber
{
    public bool Active{ get; set; }
    public void Update(MonoBehaviour mono = null);
}

public class IndirectUpdate : IUpdateSubscriber{
    private bool active = false;
    public bool Active {
        get => active;
        set => active = value;
    }
    Action<MonoBehaviour> callback;
    public IndirectUpdate(Action<MonoBehaviour> callback){
        this.active = true;
        this.callback = callback;
    }
    public void Update(MonoBehaviour mono = null)
    {
        callback.Invoke(mono);
    }
}
