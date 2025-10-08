using System.Collections.Generic;
using UnityEngine;

public class EntityEvents {
    public Dictionary<EventType, ExecutionContext> ActionEvents = new();
    public bool HasEvent(EventType type) => ActionEvents.ContainsKey(type);
    public void AddEvent<T>(EventType type, ExecutionContext.EventCallback<T> callback) {
        if (!ActionEvents.ContainsKey(type))
            ActionEvents[type] = new ExecutionContext { callbacks = new LinkedList<object>() };
        ActionEvents[type].AddCallback(callback);
    }
    public bool Invoke<T>(EventType type, ref T cxt) {
        if (!ActionEvents.ContainsKey(type)) return false;
        return ActionEvents[type].Invoke(ref cxt);
    }

    public class ExecutionContext {
        public LinkedList<object> callbacks;
        public delegate bool EventCallback<T>(ref T cxt);
        public void AddCallback<T>(EventCallback<T> callback) {
            callbacks.AddLast(callback);
        }
        public bool Invoke<T>(ref T cxt) {
            foreach (var cb in callbacks) {
                if (cb is EventCallback<T> tcb){
                    if (tcb.Invoke(ref cxt))
                        return true;
                }
            }
            return false;
        }
    }
    public enum EventType {
        OnDeath,
        OnRespawn,
        OnDamaged,
        OnAttack,
        OnJump,
        OnMate,
        OnMount,
        OnDismount,
        OnHitGround,
    }
}
