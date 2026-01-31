using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using Arterra.Utils;
using Arterra.Configuration;
using Arterra.Configuration.Gameplay;

/*
1.0::Menu
2.0::Subscene
3.0::Window
4.0::Movement
5.0::Gameplay
6.0::Process

Design Rule: If a system adds keybinds on a listener input, 
it should add those keybinds to the layer the original listener 
was added(i.e. if an inventory pop-up is bound to tab on “Window” layer, 
inputs related to that should be added to the pop-up “Window” layer as well). 
Otherwise things can get very confusing and messy.
*/
namespace Arterra.Configuration.Gameplay {
    /// <summary> A list of conditions that is assigned a name. During gameplay, a system
    /// may bind an action to this list through its name which will be triggered
    /// when the conditions are met. </summary>
    [Serializable]
    [CreateAssetMenu(menuName = "GamePlay/Input/KeyBind")]
    public class KeyBind : Category<KeyBind> {
        /// <summary> A list of conditions that must be met for the action to be triggered.
        /// See <see cref="Binding"/> for more information. </summary>
        public Option<List<Binding>> bindings;
        /// <summary> Accessor to unwrap the binding from the option  </summary>
        [JsonIgnore]
        public List<Binding> Bindings => bindings.value;
        /// <summary> An additional number of times the keybind must be triggered to activate. (Ex. double click) </summary>
        public uint AdditionalTriggerCount = 0;
        private static readonly Func<KeyCode, bool>[] PollTypes = {
            null,
            Input.GetKey,
            Input.GetKey,
            Input.GetKeyDown,
            Input.GetKeyUp,
        };
        /// <summary> If <see cref="BindPoll"/> is <see cref="BindPoll.Axis"/>, the 
        /// translation of <see cref="KeyCode"/> to the axis name that is being polled. </summary>
        private static readonly Dictionary<KeyCode, string> AxisMappings = new Dictionary<KeyCode, string>{
            {KeyCode.Alpha0, "Mouse X"},
            {KeyCode.Alpha1, "Mouse Y"},
            {KeyCode.Alpha2, "Mouse ScrollWheel"},
            {KeyCode.Alpha3, "Horizontal"},
            {KeyCode.Alpha4, "Vertical"},
        };
        /// <summary> A single condition polling a singular type of input from the user. </summary>
        [Serializable]
        public struct Binding {
            /// <summary> The keycode of the input that is being polled. If <see cref="BindPoll"/> is <see cref="BindPoll.Axis"/>,
            /// this is the alias for the axis that is being polled. </summary>
            public KeyCode Key;
            /// <summary> The type of polling that is being done on the input. See <see cref="BindPoll"/> for more information. </summary>
            public BindPoll PollType;
            /// <summary> Whether the binding is an alias for another binding. If true, the binding is an alias for the 
            /// first binding before it in the <see cref="bindings"/> list that isn't an alias. All alias directly 
            /// proceeding a non-alias binding will allow the binding to evaluate as true if any of the aliases are true. </summary>
            public bool IsAlias;
        }
        /// <summary> The type of polling that is being done on the input. </summary>
        public enum BindPoll {
            /// <summary>
            /// If the poll type is axis, uses unity's <see cref="UnityEngine.Input.GetAxis(string)"/> which returns an analog-like
            /// value between -1 and 1 for the requested axis. The axis is determined by the <see cref="KeyCode"/> of the binding
            /// transformed through <see cref="AxisMappings"/>.
            /// </summary>
            Axis = 0,
            /// <summary> If the poll type is exclude, the binding will only evaluate as true if the input is not being being held. </summary>
            Exclude = 1,
            /// <summary> If the poll type is hold, the binding will only evaluate as true if the input is being held. </summary>
            Hold = 2,
            /// <summary> If the poll type is down, the binding will only evaluate as true on the frame the input is pressed. 
            /// That is the frame where the input is held where it wasn't held in the previous frame. </summary>
            Down = 3,
            /// <summary> If the poll type is up, the binding will only evaluate as true on the frame the input is released.
            /// That is the frame where the input is not held where it was held in the previous frame. </summary>
            Up = 4,
        }

        /// <summary>
        /// Determines whether or not the KeyBind is triggered. Evaluates all conditions
        /// in <see cref="bindings"/> to determine whether the associated action should be triggered.
        /// </summary>
        /// <param name="axis">If the <see cref="bindings"/> contains an <see cref="BindPoll.Axis">axis</see>, the axis
        /// value of the last axis poll in the list. </param>
        /// <returns>Whether or not the KeyBind has been triggered. </returns>
        public bool IsTriggered(Func<KeyCode, bool> IsBlocked, out float axis) {
            axis = 0;
            bool Pressed = false;
            Truthy ConsumedBind = Truthy.Unset;
            if (Bindings == null || Bindings.Count == 0) return false;
            for (int i = Bindings.Count - 1; i >= 0; i--) {
                Binding bind = Bindings[i];
                if (bind.PollType == BindPoll.Exclude) {
                    Pressed |= !PollTypes[(int)bind.PollType](bind.Key);
                } else if (bind.PollType == BindPoll.Axis) {
                    if (AxisMappings.ContainsKey(bind.Key))
                        axis = Input.GetAxis(AxisMappings[bind.Key]);
                    Pressed = true;
                } else {
                    if (ConsumedBind == Truthy.Unset) ConsumedBind = Truthy.False;
                    if (!IsBlocked(bind.Key)) ConsumedBind = Truthy.True;
                    Pressed |= PollTypes[(int)bind.PollType](bind.Key);
                }
                if (!bind.IsAlias && !Pressed) return false;
                if (!bind.IsAlias) Pressed = false;
            }
            return ConsumedBind != Truthy.False;
        }

        /// <summary> Consumes all pressed keys by adding them to the exclusion set. </summary>
        /// <param name="BlockedExclusion">The exclusion set pressed keys will be added to</param>
        public void ConsumeKeys(HashSet<KeyCode> BlockedExclusion) {
            if (Bindings == null) return;
            if (BlockedExclusion == null) return;
            for (int i = Bindings.Count - 1; i >= 0; i--) {
                Binding bind = Bindings[i];
                if (bind.PollType != BindPoll.Down && bind.PollType != BindPoll.Hold)
                    continue;
                BlockedExclusion.Add(bind.Key);
            }
        }

        private enum Truthy {
            True = 2,
            Unset = 1,
            False = 0,
        }
    }
}

namespace Arterra.GamePlay.Interaction {
    public static class InputPoller {
        private class KeyBinder {
            public SharedLinkedList<ActionBind> KeyBinds;
            public Registry<uint> LayerHeads;
            public ref Catalogue<KeyBind> KeyMappings => ref Config.CURRENT.GamePlay.Input;
            public ref Catalogue<KeyBind> DefaultMappings => ref Config.TEMPLATE.GamePlay.Input;

            public KeyBinder() {
                Config.CURRENT.System.GameplayModifyHooks.Add("KeyBindReconstruct", ReconstructMappings);
                KeyBinds = new SharedLinkedList<ActionBind>(MaxActionBinds);
                LayerHeads = new Registry<uint>();
                LayerHeads.Construct();
                DefaultMappings.Construct();
                object Mappings = KeyMappings;
                ReconstructMappings(ref Mappings);
                KeyMappings = (Catalogue<KeyBind>)Mappings;
            }
            private void AssertMapping(Catalogue<KeyBind> NewMappings, string name) {
                if (NewMappings.Contains(name)) return; //We are missing the binding
                KeyBind binding = DefaultMappings.Retrieve(name);
                if (binding.Equals(default)) throw new Exception("Cannot Find Keybind Name"); //There is no such binding
                binding.bindings.Clone(); // We need to clone the bindings
                NewMappings.Add(name, binding);
            }


            //We need to check if the mappings have been changed
            //Because mappings can change at any time during runtime
            private void ReconstructMappings(ref object nMaps) {
                Catalogue<KeyBind> NewMappings = (Catalogue<KeyBind>)nMaps;
                NewMappings.Construct();
                //Rebind all currently bound actions
                foreach (Registry<uint>.Pair head in LayerHeads.Reg) {
                    uint current = head.Value;
                    do {
                        ref ActionBind BoundAction = ref KeyBinds.RefVal(current);
                        current = KeyBinds.Next(current);
                        if (BoundAction.Binding == null) continue; //Context Fence/Barrier
                        AssertMapping(NewMappings, BoundAction.Binding);
                    } while (current != head.Value);
                }
                nMaps = NewMappings;
            }

            public KeyBind Retrieve(string name) {
                if (!KeyMappings.Contains(name))
                    AssertMapping(KeyMappings, name);
                return KeyMappings.Retrieve(name);
            }
        }

        private static KeyBinder Binder;
        private static StateStack SStack;
        //Getter is ref otherwise modification would be impossible as we have copy
        private static TwoWayDict<string, uint> TaskBindingDict;
        private static Dictionary<string, (float trigTime, uint trigCount)> LastTrigger;
        private static ref SharedLinkedList<ActionBind> KeyBinds => ref Binder.KeyBinds;
        private static ref Registry<uint> LayerHeads => ref Binder.LayerHeads;
        private static Queue<Action> KeyBindChanges;
        private static IUpdateSubscriber eventTask;
        private static HashSet<KeyCode> GlobalKeyExclusion;
        private static HashSet<KeyCode> LayerKeyExclusion;
        private static HashSet<string> GlobalBindExclusion;
        private static HashSet<string> LayerBindExclusion;

        private static bool CursorLock = false;
        private const int MaxActionBinds = 10000;
        private const float TriggerResetTime = 0.25f;

        public static void Initialize() {
            Binder = new KeyBinder();
            SStack = new StateStack();
            KeyBindChanges = new Queue<Action>();
            AddStackPoll(new ActionBind("BASE", (float _) => SetCursorLock(true)), "CursorLock");
            eventTask = new IndirectUpdate(Update);
            Arterra.Engine.Terrain.OctreeTerrain.MainLoopUpdateTasks.Enqueue(eventTask);
            GlobalKeyExclusion = new HashSet<KeyCode>();
            LayerKeyExclusion = new HashSet<KeyCode>();
            GlobalBindExclusion = new HashSet<string>();
            LayerBindExclusion = new HashSet<string>();
            LastTrigger = new Dictionary<string, (float trigTime, uint trigCount)>();
            TaskBindingDict = new TwoWayDict<string, uint>();
        }

        public static void SetCursorLock(bool value) {
            if (CursorLock == value) return;
            CursorLock = value;

            if (CursorLock) {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            } else {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private static void AnswerKeyBinds() {
            //Explicit lexicographic-name ordering
            GlobalKeyExclusion.Clear(); GlobalBindExclusion.Clear();
            LayerKeyExclusion.Clear(); LayerBindExclusion.Clear();
            static bool IsKeyBlocked(KeyCode key) => GlobalKeyExclusion.Contains(key) || LayerKeyExclusion.Contains(key);
            foreach (Registry<uint>.Pair head in LayerHeads.Reg) {
                uint current = head.Value;
                LayerKeyExclusion.Clear();
                LayerBindExclusion.Clear();
                do {
                    ActionBind BoundAction = KeyBinds.Value(current);
                    current = KeyBinds.Next(current);

                    if (BoundAction.Binding == null) { //Context Fence/Barrier
                        if (BoundAction.exclusion == ActionBind.Exclusion.ExcludeAll) return;
                        if (BoundAction.exclusion == ActionBind.Exclusion.ExcludeLayer) break;
                        continue; //otherwise just a marker
                    }

                    if (LayerBindExclusion.Contains(BoundAction.Binding)
                        || GlobalBindExclusion.Contains(BoundAction.Binding))
                        continue;

                    KeyBind KeyBind = Binder.Retrieve(BoundAction.Binding);
                    if (KeyBind.IsTriggered(IsKeyBlocked, out float axis)) {
                        if (LastTrigger.TryGetValue(BoundAction.Binding, out var t)) {
                            if (Time.time - t.trigTime > TriggerResetTime) t.trigCount = 0;
                            //Avoid inc if binding multiple times on same frame
                            else if (Time.time - t.trigTime > 0) t.trigCount += 1;
                            t.trigTime = Time.time;
                        } else t = (Time.time, 0);
                        LastTrigger[BoundAction.Binding] = t;
                        if (t.trigCount < KeyBind.AdditionalTriggerCount) continue;

                        BoundAction.action.Invoke(axis);
                        if (BoundAction.exclusion == ActionBind.Exclusion.ExcludeLayer)
                            KeyBind.ConsumeKeys(LayerKeyExclusion);
                        else if (BoundAction.exclusion == ActionBind.Exclusion.ExcludeAll)
                            KeyBind.ConsumeKeys(GlobalKeyExclusion);
                        else if (BoundAction.exclusion == ActionBind.Exclusion.ExcludeLayerByName)
                            LayerBindExclusion.Add(BoundAction.Binding);
                        else if (BoundAction.exclusion == ActionBind.Exclusion.ExcludeAllByName)
                            GlobalBindExclusion.Add(BoundAction.Binding);

                    }
                } while (current != head.Value);
            }
        }
        private static void Update(MonoBehaviour mono) {
            AnswerKeyBinds();
            //Fulfill all keybind change requests
            while (KeyBindChanges.Count > 0)
                KeyBindChanges.Dequeue().Invoke();
        }

        public static void AddKeyBindChange(Action action) { KeyBindChanges.Enqueue(action); }

        public static void AddBinding(ActionBind bind, string TaskName, string Layer) {
            string fullTaskName = String.Join("::", Layer, TaskName);
            if (TaskBindingDict.TryGetByKey(fullTaskName, out uint bindIndex))
                RemoveKeyBind(bindIndex, Layer);
            TaskBindingDict[fullTaskName] = AddKeyBind(bind, Layer);
        }

        public static void AddContextFence(string TaskName, string Layer, ActionBind.Exclusion exclusion = ActionBind.Exclusion.ExcludeLayer) {
            string fullTaskName = String.Join("::", Layer, TaskName);
            if (TaskBindingDict.TryGetByKey(fullTaskName, out uint bindIndex))
                RemoveKeyBind(bindIndex, Layer);
            TaskBindingDict[fullTaskName] = AddKeyBind(new ActionBind {
                Binding = null,
                action = null,
                exclusion = exclusion
            }, Layer);
        }

        public static void RemoveBinding(string TaskName, string Layer) {
            string fullTaskName = String.Join("::", Layer, TaskName);
            if (!TaskBindingDict.TryGetByKey(fullTaskName, out uint bindIndex))
                return;
            RemoveKeyBind(bindIndex, Layer);
            TaskBindingDict.RemoveByKey(fullTaskName);
        }


        //Do not call this with an invalid bindIndex not in the layer, or else it could corrupt all keybinds
        public static void RemoveContextFence(string TaskName, string Layer) {
            if (!LayerHeads.Contains(Layer))
                return;

            string fullTaskName = String.Join("::", Layer, TaskName);
            if (!TaskBindingDict.TryGetByKey(fullTaskName, out uint bindIndex))
                return;
            uint current = LayerHeads.Retrieve(Layer);
            while (current != bindIndex) {
                uint next = KeyBinds.Next(current);
                TaskBindingDict.RemoveByValue(current);
                RemoveKeyBind(current, Layer);
                current = next;
            }
            LayerHeads.TrySet(Layer, KeyBinds.Next(current));
            RemoveKeyBind(current, Layer);
            TaskBindingDict.RemoveByKey(fullTaskName);
        }

        public static void SuspendKeybindPropogation(string name, ActionBind.Exclusion exclusion = ActionBind.Exclusion.ExcludeLayer) {
            if (exclusion.Equals(ActionBind.Exclusion.ExcludeLayer)) {
                Binder.Retrieve(name).ConsumeKeys(LayerKeyExclusion);
            } else if (exclusion.Equals(ActionBind.Exclusion.ExcludeAll)) {
                Binder.Retrieve(name).ConsumeKeys(GlobalKeyExclusion);
            } else if (exclusion.Equals(ActionBind.Exclusion.ExcludeLayerByName)) {
                LayerBindExclusion.Add(name);
            } else if (exclusion.Equals(ActionBind.Exclusion.ExcludeAllByName)) {
                GlobalBindExclusion.Add(name);
            }
        }

        //Do not call this with an invalid bindIndex not in the layer, or else it could corrupt all keybinds
        private static void RemoveKeyBind(uint bindIndex, string layer = "base") {
            if (!LayerHeads.Contains(layer))
                return;

            //Move head if removed, if empty remove layer
            if (LayerHeads.Retrieve(layer) == bindIndex) {
                LayerHeads.TrySet(layer, KeyBinds.Next(bindIndex));
            }
            if (LayerHeads.Retrieve(layer) == bindIndex) {
                LayerHeads.TryRemove(layer);
            }
            KeyBinds.Remove(bindIndex);
        }

        private static uint AddKeyBind(ActionBind keyBind, string layer) {
            if (!LayerHeads.Contains(layer)) {
                LayerHeads.Add(layer, KeyBinds.Enqueue(keyBind));
                LayerHeads.Reg.Sort((a, b) => a.Name.CompareTo(b.Name));
                LayerHeads.Construct(); //reconstruct because sorting breaks the dictionary's values
            } else LayerHeads.TrySet(layer, KeyBinds.Enqueue(keyBind, LayerHeads.Retrieve(layer)));
            return LayerHeads.Retrieve(layer);
        }

        public struct SharedLinkedList<T> {
            /*
            Multiple linked list contained in one array
            The caller provides the head of the array when enqueueing
            */

            public LListNode[] array;
            private int _length;
            public readonly int Length { get { return _length; } }
            public SharedLinkedList(int length) {
                //We Need Clear Memory Here
                array = new LListNode[length + 1];
                array[0].next = 1;

                _length = 0;
            }

            public uint Enqueue(T node, uint head = 0) {
                if (_length >= array.Length - 2)
                    return head; //Just Ignore it

                uint freeNode = array[0].next; //Free Head Node
                uint nextNode = array[freeNode].next == 0 ? freeNode + 1 : array[freeNode].next;
                array[0].next = nextNode;

                array[freeNode].value = node;
                if (head == 0) {
                    array[freeNode].next = freeNode;
                    array[freeNode].previous = freeNode;
                } else {
                    uint tailNode = array[head].previous; //Tail Node
                    array[tailNode].next = freeNode;
                    array[head].previous = freeNode;
                    array[freeNode].previous = tailNode;
                    array[freeNode].next = head;
                    _length++;
                }

                return (uint)freeNode;
            }

            public void Remove(uint index) {
                uint nextNode = array[index].next;
                uint prevNode = array[index].previous;
                array[prevNode].next = nextNode;
                array[nextNode].previous = prevNode;

                array[index].next = array[0].next;
                array[0].next = index;
                _length--;
            }

            public readonly T Value(uint index) {
                return array[index].value;
            }

            public readonly ref T RefVal(uint index) {
                return ref array[index].value;
            }

            public readonly uint Next(uint index) {
                return array[index].next;
            }

            public readonly uint Previous(uint index) {
                return array[index].previous;
            }


            public struct LListNode {
                public uint previous;
                public uint next;
                public T value;
            }
        }

        public class StateStack {
            private SharedLinkedList<ActionBind> StackBinds;
            private Registry<uint> LayerHeads;
            private Registry<uint> StackEntries;
            private const int MaxStackBinds = 5000;

            public StateStack() {
                StackBinds = new(MaxStackBinds);
                LayerHeads = new Registry<uint>();
                StackEntries = new Registry<uint>();
                LayerHeads.Construct();
                StackEntries.Construct();
            }

            public void AddStackPoll(ActionBind bind, string layer, string KeyName) {
                if (!LayerHeads.Contains(layer)) LayerHeads.Add(layer, StackBinds.Enqueue(bind));
                else LayerHeads.TrySet(layer, StackBinds.Enqueue(bind, LayerHeads.Retrieve(layer)));

                if (StackEntries.Contains(KeyName)) StackEntries.TryRemove(KeyName);
                StackEntries.Add(KeyName, LayerHeads.Retrieve(layer));
                bind.action.Invoke(0);
            }

            public void RemoveStackPoll(string layer, string KeyName) {
                if (!StackEntries.Contains(KeyName)) return;
                if (!LayerHeads.Contains(layer)) return;

                uint bindIndex = StackEntries.Retrieve(KeyName);
                //Move head if removed, if empty remove layer
                if (LayerHeads.Retrieve(layer) == bindIndex) {
                    LayerHeads.TrySet(layer, StackBinds.Next(bindIndex));
                }
                if (LayerHeads.Retrieve(layer) == bindIndex) {
                    LayerHeads.TryRemove(layer);
                }
                StackBinds.Remove(bindIndex);
                StackEntries.TryRemove(KeyName);
                if (!LayerHeads.Contains(layer)) return;
                //Invoke the top bind in the stack
                bindIndex = LayerHeads.Retrieve(layer);
                StackBinds.Value(bindIndex).action.Invoke(0);
            }

            public void InvokeTop(string layer) {
                if (!LayerHeads.Contains(layer)) return;
                uint bindIndex = LayerHeads.Retrieve(layer);
                StackBinds.Value(bindIndex).action.Invoke(0);
            }

            public string PeekTop(string layer) {
                if (!LayerHeads.Contains(layer)) return null;
                uint bindIndex = LayerHeads.Retrieve(layer);
                return StackBinds.Value(bindIndex).Binding;
            }
        }

        public static string PeekTop(string layer) => SStack.PeekTop(layer);
        public static void InvokeStackTop(string layer) => SStack.InvokeTop(layer);
        public static void AddStackPoll(ActionBind bind, string Layer) => SStack.AddStackPoll(bind, Layer, bind.Binding);
        public static void RemoveStackPoll(string BindName, string Layer) => SStack.RemoveStackPoll(Layer, BindName);
    }

    public struct ActionBind {
        public string Binding;
        public Action<float> action;
        public Exclusion exclusion;
        public ActionBind(string Binding, Action<float> action, Exclusion exclusion = Exclusion.None) {
            this.Binding = Binding;
            this.action = action;
            this.exclusion = exclusion;
        }
        public enum Exclusion {
            None = 0,
            ExcludeLayer = 1,
            ExcludeAll = 2,
            ExcludeLayerByName = 3,
            ExcludeAllByName = 4,
        }
    }
}