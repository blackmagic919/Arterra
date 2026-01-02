using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime;
using Arterra.Configuration.Generation.Entity;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Arterra.Core.Events {
    public static class GameEventBases {
        public const int Entity_Base = 0;
        public const int Item_Base = 1000;
        public const int Material_Base = 2000;
        public const int Action_Base = 3000;
        public const int System_Base = 4000;
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
        Entity_InLiquid = GameEventBases.Entity_Base + 14,
        Entity_InSolid = GameEventBases.Entity_Base + 15,
        Entity_InGas = GameEventBases.Entity_Base + 16,
        Entity_ProjectileHit = GameEventBases.Entity_Base + 17,
        
        
        Item_ConsumeFood = GameEventBases.Item_Base + 0,
        Item_HoldTool = GameEventBases.Item_Base + 1,
        Item_UnholdTool = GameEventBases.Item_Base + 2,
        Item_UseTool = GameEventBases.Item_Base + 3,
        Item_DrawBow = GameEventBases.Item_Base + 4,
        Item_ReleaseBow = GameEventBases.Item_Base + 5,

        Action_Jump = GameEventBases.Action_Base + 0,
        Action_RemoveTerrain = GameEventBases.Action_Base + 1,
        Action_PlaceTerrain = GameEventBases.Action_Base + 2,
        Action_LookGradual = GameEventBases.Action_Base + 3,
        Action_LookDirect = GameEventBases.Action_Base + 4,
        Action_MountRideable = GameEventBases.Action_Base + 5,
        Action_DismountRideable = GameEventBases.Action_Base + 6,

        System_Deserialize = GameEventBases.System_Base + 0,
    }

    public static class GameEventTypeMap {
        public static readonly Dictionary<GameEvent, Type> EventArgTypes = new() {
            { GameEvent.Entity_Damaged, typeof((float, float3)) },
            { GameEvent.Entity_HitGround, typeof(float) },
            { GameEvent.Entity_Attack, typeof(int) },
            // Add other mappings here
        };
    }

    //This is the visual layer, in the future this information would need to be
    //sent over to the server so other clients can see your animations
    public interface IEventControlled {
        public void AddEventHandler<T>(GameEvent type, RefEventHandler<T> handler) => Events.AddEventHandler(type, handler);
        public void RemoveEventHandler<T>(GameEvent type, RefEventHandler<T> handler) => Events.RemoveEventHandler(type, handler);
        public void AddContextlessEventHandler<T>(GameEvent type, Action<object, object> handler) => Events.AddContextlessEventHandler(type, handler);
        public void RaiseEvent<T>(GameEvent type, object actor, object target, ref T ctx) => Events.RaiseEvent(type, actor, target, ref ctx);
        public void RaiseEvent(GameEvent type, object actor, object target) => Events.RaiseEvent(type, actor, target);
        public EventControl Events {get;}
    }

    public delegate void RefEventHandler<T>(object actor, object target, ref T cxt);

    // Event control class implement a common control using EventHandlerList
    public class EventControl {
        private Dictionary<int, Delegate> events = new Dictionary<int, Delegate>();


        // Methods to add, remove, and raise events
        public void AddEventHandler<T>(GameEvent type, RefEventHandler<T> handler) {
            if (events.ContainsKey((int)type)) {
                events[(int)type] = Delegate.Combine(events[(int)type], handler);
            } else {
                events[(int)type] = handler;
            }
        }
        
        public void RemoveEventHandler<T>(GameEvent type, RefEventHandler<T> handler) {
            if (events.ContainsKey((int)type)) {
                events[(int)type] = Delegate.Remove(events[(int)type], handler);
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
            if (events.ContainsKey((int)type)) {
                var handler = (RefEventHandler<T>)events[(int)type];
                handler?.Invoke(actor, target, ref ctx);
            }
        }

        public void RaiseEvent(GameEvent type, object actor, object target) {
            // this  could be entity / player/ item.... 
            // Eventqueu.
            object ctx = null;
            if (events.ContainsKey((int)type)) {
                var handler = (RefEventHandler<object>)events[(int)type];
                handler?.Invoke(actor, target, ref ctx);
            }
        }
    }

}