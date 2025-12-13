using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Arterra.Core.Events {

    // Event type categories
    public enum EventCategory {
        Entity = 0,
        Material = 1,
        Item = 2
    }

    // Entity event types
    public enum EntityEventType {
        OnDeath,
        OnRespawn,
        OnDamaged,
        OnAttack,
        OnJump,
        OnMate,
        OnMount,
        OnDismount,
        OnHitGround
    }

    // Material event types
    public enum MaterialEventType {

    }

    // Item event types
    public enum ItemEventType {

    }

    public struct EventType
    {
        public EventCategory Category { get; }
        public int Type { get; }
        
        public EventType(EventCategory category, int type)
        {
            Category = category;
            Type = type;
        }
        
        // Implicit conversion FROM EntityEventType TO EventType
        public static implicit operator EventType(EntityEventType type)
        {
            return new EventType(EventCategory.Entity, (int)type);
        }
        
        // Implicit conversion FROM MaterialEventType TO EventType
        public static implicit operator EventType(MaterialEventType type)
        {
            return new EventType(EventCategory.Material, (int)type);
        }
        
        // Use EXPLICIT for EventType → enum (can fail)
        public static explicit operator EntityEventType(EventType evt)
        {
            if (evt.Category != EventCategory.Entity)
                throw new InvalidCastException("EventType is not an EntityEventType");
            return (EntityEventType)evt.Type;
        }
        
        public static explicit operator MaterialEventType(EventType evt)
        {
            if (evt.Category != EventCategory.Material)
                throw new InvalidCastException("EventType is not a MaterialEventType");
            return (MaterialEventType)evt.Type;
        }
    }

    public delegate void RefEventHandler<T>(object sender, ref T cxt);

    // Event control class implement a common control using EventHandlerList
    public class EventControl {
        private Dictionary<object, Delegate> events = new Dictionary<object, Delegate>();


        // Methods to add, remove, and raise events
        public void AddEventHandler<T>(EventType type, RefEventHandler<T> handler) {
            if (events.ContainsKey(type)) {
                events[type] = Delegate.Combine(events[type], handler);
            } else {
                events[type] = handler;
            }
        }

        public void RemoveEventHandler<T>(EventType type, RefEventHandler<T> handler) {
            if (events.ContainsKey(type)) {
                events[type] = Delegate.Remove(events[type], handler);
            }
        }

        public void RaiseEvent<T>(EventType type, ref T ctx) {
            if (events.ContainsKey(type)) {
                var handler = (RefEventHandler<T>)events[type];
                handler?.Invoke(this, ref ctx);
            }
        }
    }
}