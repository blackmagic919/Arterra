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

    /// <summary> A generic event context wrapper </summary>
    /// <typeparam name="T"> The type of data contained in the context. 
    /// If there're multiple data in the context, first wrap them in a tuple or a custom struct. </typeparam>
    public class EventContext<T> {
        public T Data;

        public EventContext(ref T data) {
            Data = data;
        }
    }

    public delegate void RefEventHandler(object actor, object target, object cxt);

    // Event control class implement a common control using EventHandlerList
    public class EventControl {
        private Dictionary<object, Delegate> events = new Dictionary<object, Delegate>();


        // Methods to add, remove, and raise events
        public void AddEventHandler(GameEvent type, RefEventHandler handler) {
            if (events.ContainsKey(type)) {
                events[type] = Delegate.Combine(events[type], handler);
            } else {
                events[type] = handler;
            }
        }
        
        public void RemoveEventHandler(GameEvent type, RefEventHandler handler) {
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
            AddEventHandler(evt, (object actor, object target, object ctx) =>
            {
                handler(actor, target);
            });
        }            

        public void RaiseEvent(GameEvent type, object actor, object target, object ctx) {
            // this  could be entity / player/ item.... 
            // Eventqueu.
            if (events.ContainsKey(type)) {
                var handler = (RefEventHandler)events[type];
                handler?.Invoke(actor, target, ctx);
            }
        }
    }

}