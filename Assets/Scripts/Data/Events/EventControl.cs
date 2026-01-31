using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime;
using Arterra.Data.Entity;
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
        Entity_Collect = GameEventBases.Entity_Base + 18,
        Entity_RemoveMaterial = GameEventBases.Entity_Base + 19,
        Entity_PlaceMaterial = GameEventBases.Entity_Base + 20,
        
        
        Item_ConsumeFood = GameEventBases.Item_Base + 0,
        Item_HoldTool = GameEventBases.Item_Base + 1,
        Item_UnholdTool = GameEventBases.Item_Base + 2,
        Item_UseTool = GameEventBases.Item_Base + 3,
        Item_DrawBow = GameEventBases.Item_Base + 4,
        Item_ReleaseBow = GameEventBases.Item_Base + 5,
        Item_DrawRod = GameEventBases.Item_Base + 6,
        Item_ReleaseRod = GameEventBases.Item_Base + 7,


        Action_Jump = GameEventBases.Action_Base + 0,
        Action_RemoveTerrain = GameEventBases.Action_Base + 1,
        Action_PlaceTerrain = GameEventBases.Action_Base + 2,
        Action_LookGradual = GameEventBases.Action_Base + 3,
        Action_LookDirect = GameEventBases.Action_Base + 4,
        Action_MountRideable = GameEventBases.Action_Base + 5,
        Action_DismountRideable = GameEventBases.Action_Base + 6,
        Action_CraftItem = GameEventBases.Action_Base + 7,
        Action_OpenInventory = GameEventBases.Action_Base + 8,
        Action_OpenCrafting = GameEventBases.Action_Base + 9,
        Action_OpenArmor = GameEventBases.Action_Base + 10,
        Action_OpenChest = GameEventBases.Action_Base + 11,
        Action_OpenFurnace = GameEventBases.Action_Base + 12,
        Action_OpenMortar = GameEventBases.Action_Base + 13,

        System_Deserialize = GameEventBases.System_Base + 0,
    }

    //This is the visual layer, in the future this information would need to be
    //sent over to the server so other clients can see your animations
    public interface IEventControlled {
        public void AddEventHandler<T>(GameEvent type, RefEventHandler handler) => Events.AddEventHandler(type, handler);
        public void RemoveEventHandler<T>(GameEvent type, RefEventHandler handler) => Events.RemoveEventHandler(type, handler);
        public void AddContextlessEventHandler<T>(GameEvent type, Action<object, object> handler) => Events.AddContextlessEventHandler(type, handler);
        public void RaiseEvent<T>(GameEvent type, object actor, object target, ref T ctx) => Events.RaiseEvent(type, actor, target, ctx);
        public void RaiseEvent(GameEvent type, object actor, object target) => Events.RaiseEvent(type, actor, target);
        public EventControl Events {get;}
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

    public sealed class RefTuple<TTuple> where TTuple : struct {
        public TTuple Value;

        public RefTuple(TTuple value) {
            Value = value;
        }

        public static implicit operator RefTuple<TTuple>(TTuple value)
            => new RefTuple<TTuple>(value);

        public static implicit operator TTuple(RefTuple<TTuple> wrapper)
            => wrapper.Value;
        
    }

    public delegate void RefEventHandler(object actor, object target, object cxt);

    // Event control class implement a common control using EventHandlerList
    public class EventControl {
        private Dictionary<int, Delegate> events = new Dictionary<int, Delegate>();


        // Methods to add, remove, and raise events
        public void AddEventHandler(GameEvent type, RefEventHandler handler) {
            if (events.ContainsKey((int)type)) {
                events[(int)type] = Delegate.Combine(events[(int)type], handler);
            } else {
                events[(int)type] = handler;
            }
        }
        
        public void RemoveEventHandler(GameEvent type, RefEventHandler handler) {
            if (events.ContainsKey((int)type)) {
                events[(int)type] = Delegate.Remove(events[(int)type], handler);
            }
        }

        /// <summary>
        /// Adds an event handler for the specified GameEvent without context parameter.
        /// </summary>
        /// <param name="evt"></param>
        /// <param name="handler"></param>
        /// <returns> The lambda wrapper created so handler may use to remove </returns>/// 
        public RefEventHandler AddContextlessEventHandler(GameEvent evt, Action<object, object> handler)
        {
            RefEventHandler lambda = (object actor, object target, object ctx) => {
                handler(actor, target);
            };
            AddEventHandler(evt, lambda);
            return lambda;
        }            

        public void RaiseEvent(GameEvent type, object actor, object target, object ctx = null) {
            // this  could be entity / player/ item.... 
            // Eventqueu.
            if (events.ContainsKey((int)type)) {
                var handler = (RefEventHandler)events[(int)type];
                handler?.Invoke(actor, target, ctx);
            }
        }
    }

}