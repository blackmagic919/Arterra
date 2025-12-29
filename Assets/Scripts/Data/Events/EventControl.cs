using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime;
using Arterra.Config.Generation.Entity;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Arterra.Core.Events {
    public static class GameEventBases {
        public const int Entity_Base = 0;
        public const int Item_Base = 1000;
        public const int Material_Base = 2000;
    }

    public enum GameEvent {
        None = -1, // Placeholder for no event

        General_Notifcation = -2, // General notification event

        Entity_Death = GameEventBases.Entity_Base + 0,
        Entity_Respawn = GameEventBases.Entity_Base + 1,
        Entity_Damaged = GameEventBases.Entity_Base + 2,
        Entity_Attack = GameEventBases.Entity_Base + 3,
        Entity_Jump = GameEventBases.Entity_Base + 4,
        Entity_Mate = GameEventBases.Entity_Base + 5,
        Entity_Mount = GameEventBases.Entity_Base + 6,
        Entity_Dismount = GameEventBases.Entity_Base + 7,
        Entity_HitGround = GameEventBases.Entity_Base + 8,

        Entity_ItemEnterPrimaryInventory = GameEventBases.Entity_Base + 9,
        Entity_ItemEnterSecondaryInventory = GameEventBases.Entity_Base + 10,

        Entity_ItemHeld = GameEventBases.Entity_Base + 11,
        Entity_TouchMaterial = GameEventBases.Entity_Base + 12,
        Entity_InteractMaterial = GameEventBases.Entity_Base + 13,
    }

    public static class GameEventTypeMap {
    public static readonly Dictionary<GameEvent, Type> EventArgTypes = new() {
        { GameEvent.Entity_Damaged, typeof((float, float3, Entity)) },
        { GameEvent.Entity_HitGround, typeof(float) },
        { GameEvent.Entity_Attack, typeof(int) },
        // Add other mappings here
    };
}

    public delegate void RefEventHandler<T>(object actor, object target, ref T cxt);

    // Event control class implement a common control using EventHandlerList
    public class EventControl {
        private Dictionary<object, Delegate> events = new Dictionary<object, Delegate>();


        // Methods to add, remove, and raise events
        public void AddEventHandler<T>(GameEvent type, RefEventHandler<T> handler) {
            if (events.ContainsKey(type)) {
                events[type] = Delegate.Combine(events[type], handler);
            } else {
                events[type] = handler;
            }
        }
        
        public void RemoveEventHandler<T>(GameEvent type, RefEventHandler<T> handler) {
            if (events.ContainsKey(type)) {
                events[type] = Delegate.Remove(events[type], handler);
            }
        }

        /// <summary>
        /// Adds an event handler for the specified GameEvent without context parameter.
        /// </summary>
        /// <param name="evt"></param>
        /// <param name="handler"></param>
        public void AddContextlessEventHandler(GameEvent evt, Action<object, object> handler)
        {
            // Get the EventControl instance (replace with your actual instance)
            var eventCtrl = this;

            Type ctxType = GameEventTypeMap.EventArgTypes.GetValueOrDefault(evt, null);

            if (ctxType == null)
            {
                // If no context type is defined, use a dummy type
                Debug.LogWarning($"EventControl: No context type defined for event {evt}. Using dummy context.");
                ctxType = typeof(object);
            }
            // Get the generic delegate type: RefEventHandler<T>
            var handlerType = typeof(RefEventHandler<>).MakeGenericType(ctxType);

            var method = typeof(EventControl)
                .GetMethod(nameof(AddContextlessEventHandlerGeneric), 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .MakeGenericMethod(ctxType);

            method.Invoke(null, new object[] { eventCtrl, evt, handler });    
        }

        // Helper method to create a strongly-typed wrapper
        private static void AddContextlessEventHandlerGeneric<T>(EventControl eventCtrl, GameEvent evt, Action<object, object> handler)
        {
            RefEventHandler<T> wrapper = (object actor, object target, ref T ctx) =>
            {
                handler(actor, target);
            };
            eventCtrl.AddEventHandler(evt, wrapper);
        }             

        public void RaiseEvent<T>(GameEvent type, object actor, object target, ref T ctx) {
            // this  could be entity / player/ item.... 
            // Eventqueu.
            if (events.ContainsKey(type)) {
                var handler = (RefEventHandler<T>)events[type];
                handler?.Invoke(actor, target, ref ctx);
            }
        }
    }

}