using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using WorldConfig;
using WorldConfig.Gameplay;

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
namespace WorldConfig.Gameplay{
    /// <summary> A list of conditions that is assigned a name. During gameplay, a system
    /// may bind an action to this list through its name which will be triggered
    /// when the conditions are met. </summary>
    [Serializable]
    public struct KeyBind{
        /// <summary> A list of conditions that must be met for the action to be triggered.
        /// See <see cref="Binding"/> for more information. </summary>
        public Option<List<Binding> > bindings;
        /// <summary> Accessor to unwrap the binding from the option  </summary>
        [JsonIgnore]
        public readonly List<Binding> Bindings => bindings.value;
        private static readonly Func<KeyCode, bool>[] PollTypes = {
            null,
            UnityEngine.Input.GetKey, 
            UnityEngine.Input.GetKey,
            UnityEngine.Input.GetKeyDown,
            UnityEngine.Input.GetKeyUp,
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
        public struct Binding
        {
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
        public enum BindPoll{
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
        public readonly bool IsTriggered(out float axis){
            axis = 0; bool Pressed = false;
            if(Bindings == null || Bindings.Count == 0) return false;
            for(int i = Bindings.Count-1; i >= 0; i--){
                Binding bind = Bindings[i];
                if(bind.PollType == BindPoll.Exclude) {
                    Pressed |= !PollTypes[(int)bind.PollType](bind.Key);
                } else if(bind.PollType == BindPoll.Axis){
                    if(AxisMappings.ContainsKey(bind.Key)) 
                        axis = UnityEngine.Input.GetAxis(AxisMappings[bind.Key]);
                    Pressed = true;
                } else {
                    Pressed |= PollTypes[(int)bind.PollType](bind.Key);
                }
                if(!bind.IsAlias && !Pressed) return false;
                if(!bind.IsAlias) Pressed = false;
            }
            return true;
        }
    }
}
public class InputPoller : UpdateTask
{
    private class KeyBinder{
        public SharedLinkedList<ActionBind> KeyBinds;
        public Registry<uint> LayerHeads;
        public ref Registry<KeyBind> KeyMappings => ref Config.CURRENT.GamePlay.Input;
        public ref Registry<KeyBind> DefaultMappings => ref Config.TEMPLATE.GamePlay.Input;

        public KeyBinder(){
            KeyBinds = new SharedLinkedList<ActionBind>(MaxActionBinds);
            LayerHeads = new Registry<uint>();
            LayerHeads.Construct();
            DefaultMappings.Construct();
            ReconstructMappings();
        }
        private void AssertMapping(string name){
            if(KeyMappings.Contains(name)) return; //We are missing the binding
            KeyBind binding = DefaultMappings.Retrieve(name);
            if(binding.Equals(default)) throw new Exception("Cannot Find Keybind Name"); //There is no such binding
            binding.bindings.Clone(); // We need to clone the bindings
            KeyMappings.Add(name, binding);
        }
        
        //We need to check if the mappings have been changed
        //Because mappings can change at any time during runtime
        private void ReconstructMappings(){
            KeyMappings.Construct();
            //Rebind all currently bound actions
            foreach(Registry<uint>.Pair head in LayerHeads.Reg.value){
                uint current = head.Value;
                do{
                    ref ActionBind BoundAction = ref KeyBinds.RefVal(current);
                    current = KeyBinds.Next(current);
                    if(BoundAction.Binding == null) continue; //Context Fence/Barrier
                    AssertMapping(BoundAction.Binding);
                } while(current != head.Value);
            } 
        }

        public KeyBind Retrieve(string name){
            if(!KeyMappings.Contains(name)) AssertMapping(name);
            int index = KeyMappings.RetrieveIndex(name);
            if(!KeyMappings.Contains(index)) ReconstructMappings();
            if(KeyMappings.RetrieveName(index).CompareTo(name) != 0) ReconstructMappings();

            return KeyMappings.Retrieve(name);
        }
    }

    private static KeyBinder Binder;
    private static StateStack SStack;
    //Getter is ref otherwise modification would be impossible as we have copy
    private static ref SharedLinkedList<ActionBind> KeyBinds => ref Binder.KeyBinds;
    private static ref Registry<uint> LayerHeads => ref Binder.LayerHeads;
    private static Queue<Action> KeyBindChanges;
    private static bool CursorLock = false;
    private const int MaxActionBinds = 10000;

    public static void Initialize()
    {
        Binder = new KeyBinder();
        SStack = new StateStack();
        KeyBindChanges = new Queue<Action>();
        AddStackPoll(new ActionBind("BASE", (float _) => SetCursorLock(true)), "CursorLock");
        TerrainGeneration.OctreeTerrain.MainLoopUpdateTasks.Enqueue(new InputPoller{active = true});
    }

    public static void SetCursorLock(bool value)
    {
        if(CursorLock == value) return;
        CursorLock = value;

        if(CursorLock){
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        } else {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void AnswerKeyBinds(){
        //Explicit lexicographic-name ordering
        HashSet<string> GlobalExclusion = new();
        HashSet<string> LayerExclusion = new();
        foreach(Registry<uint>.Pair head in LayerHeads.Reg.value){
            uint current = head.Value; LayerExclusion.Clear();
            do{
                ActionBind BoundAction = KeyBinds.Value(current);
                current = KeyBinds.Next(current);

                if(GlobalExclusion.Contains(BoundAction.Binding)) continue;
                if(LayerExclusion.Contains(BoundAction.Binding)) continue;

                if(BoundAction.Binding == null) { //Context Fence/Barrier
                    if(BoundAction.exclusion == ActionBind.Exclusion.ExcludeAll) return;
                    if(BoundAction.exclusion == ActionBind.Exclusion.ExcludeLayer) break;
                    continue; //otherwise just a marker
                } else if(BoundAction.exclusion != ActionBind.Exclusion.None){ 
                    if(BoundAction.exclusion == ActionBind.Exclusion.ExcludeLayer) 
                        LayerExclusion.Add(BoundAction.Binding);
                    else GlobalExclusion.Add(BoundAction.Binding);
                } 

                KeyBind KeyBind = Binder.Retrieve(BoundAction.Binding);
                if(KeyBind.IsTriggered(out float axis))
                    BoundAction.action.Invoke(axis);
            } while(current != head.Value);
        }
    }
    public override void Update(MonoBehaviour mono){
        AnswerKeyBinds();
        //Fulfill all keybind change requests
        while(KeyBindChanges.Count > 0) 
            KeyBindChanges.Dequeue().Invoke();
    }
    
    public static void AddKeyBindChange(Action action){ KeyBindChanges.Enqueue(action); }

    public static uint AddBinding(ActionBind bind, string Layer){
        return AddKeyBind(bind, Layer);
    }

    public static uint AddContextFence(string Layer, ActionBind.Exclusion exclusion = ActionBind.Exclusion.ExcludeLayer){
        return AddKeyBind(new ActionBind{
            Binding = null,
            action = null,
            exclusion = exclusion
        }, Layer);
    }

    //Do not call this with an invalid bindIndex not in the layer, or else it could corrupt all keybinds
    public static void RemoveContextFence(uint bindIndex, string layer = "base"){
        if(!LayerHeads.Contains(layer))
            return;

        uint current = LayerHeads.Retrieve(layer);
        while(current != bindIndex){
            uint next = KeyBinds.Next(current);
            RemoveKeyBind(current, layer);
            current = next;
        }
        LayerHeads.TrySet(layer, KeyBinds.Next(current));
        RemoveKeyBind(current, layer);
    }

    //Do not call this with an invalid bindIndex not in the layer, or else it could corrupt all keybinds
    public static void RemoveKeyBind(uint bindIndex, string layer = "base"){
        if(!LayerHeads.Contains(layer))
            return;

        //Move head if removed, if empty remove layer
        if(LayerHeads.Retrieve(layer) == bindIndex){
            LayerHeads.TrySet(layer, KeyBinds.Next(bindIndex));
        } if(LayerHeads.Retrieve(layer) == bindIndex){
            LayerHeads.TryRemove(layer);
        } KeyBinds.Remove(bindIndex);
    }

    private static uint AddKeyBind(ActionBind keyBind, string layer){
        if(!LayerHeads.Contains(layer)){
            LayerHeads.Add(layer, KeyBinds.Enqueue(keyBind));
            LayerHeads.Reg.value.Sort((a, b) => a.Name.CompareTo(b.Name));
            LayerHeads.Construct(); //reconstruct because sorting breaks the dictionary's values
        } else LayerHeads.TrySet(layer, KeyBinds.Enqueue(keyBind, LayerHeads.Retrieve(layer)));
        return LayerHeads.Retrieve(layer);
    }

    public struct ActionBind
    {
        public string Binding;
        public Action<float> action;
        public Exclusion exclusion;
        public ActionBind(string Binding, Action<float> action, Exclusion exclusion = Exclusion.None){
            this.Binding = Binding;
            this.action = action;
            this.exclusion = exclusion;
        }
        public enum Exclusion{
            None = 0,
            ExcludeLayer = 1,
            ExcludeAll = 2,
        }
    }

    
    

    public struct SharedLinkedList<T>{
        /*
        Multiple linked list contained in one array
        The caller provides the head of the array when enqueueing
        */

        public LListNode[] array;
        private int _length;
        public readonly int Length{get{return _length;}}
        public SharedLinkedList(int length){
            //We Need Clear Memory Here
            array = new LListNode[length + 1];
            array[0].next = 1;

            _length = 0;
        }

        public uint Enqueue(T node, uint head = 0){
            if(_length >= array.Length - 2)
                return head; //Just Ignore it
            
            uint freeNode = array[0].next; //Free Head Node
            uint nextNode = array[freeNode].next == 0 ? freeNode + 1 : array[freeNode].next;
            array[0].next = nextNode;

            array[freeNode].value = node;
            if(head == 0){
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

        public void Remove(uint index){
            uint nextNode = array[index].next;
            uint prevNode = array[index].previous;
            array[prevNode].next = nextNode;
            array[nextNode].previous = prevNode;

            array[index].next = array[0].next;
            array[0].next = index;
            _length--;
        }

        public readonly T Value(uint index){
            return array[index].value;
        }

        public readonly ref T RefVal(uint index){
            return ref array[index].value;
        }

        public readonly uint Next(uint index){
            return array[index].next;
        }

        public readonly uint Previous(uint index){
            return array[index].previous;
        }
        

        public struct LListNode{
            public uint previous;
            public uint next;
            public T value;
        }
    }

    public class StateStack{
        private SharedLinkedList<ActionBind> StackBinds;
        private Registry<uint> LayerHeads;
        private Registry<uint> StackEntries;
        private const int MaxStackBinds = 5000;

        public StateStack(){
            StackBinds = new (MaxStackBinds);
            LayerHeads = new Registry<uint>();
            StackEntries = new Registry<uint>();
            LayerHeads.Construct();
            StackEntries.Construct();
        }
        
        public void AddStackPoll(ActionBind bind, string layer, string KeyName){
            if(!LayerHeads.Contains(layer)) LayerHeads.Add(layer, StackBinds.Enqueue(bind));
            else LayerHeads.TrySet(layer, StackBinds.Enqueue(bind, LayerHeads.Retrieve(layer)));

            if(StackEntries.Contains(KeyName)) StackEntries.TryRemove(KeyName);
            StackEntries.Add(KeyName, LayerHeads.Retrieve(layer));
            bind.action.Invoke(0);
        }
        
        public void RemoveStackPoll(string layer, string KeyName){
            if(!StackEntries.Contains(KeyName)) return;
            if(!LayerHeads.Contains(layer)) return;

            uint bindIndex = StackEntries.Retrieve(KeyName);
            //Move head if removed, if empty remove layer
            if(LayerHeads.Retrieve(layer) == bindIndex){
                LayerHeads.TrySet(layer, StackBinds.Next(bindIndex));
            } if(LayerHeads.Retrieve(layer) == bindIndex){
                LayerHeads.TryRemove(layer);
            } 
            StackBinds.Remove(bindIndex);
            StackEntries.TryRemove(KeyName);
            if(!LayerHeads.Contains(layer)) return;
            //Invoke the top bind in the stack
            bindIndex = LayerHeads.Retrieve(layer);
            StackBinds.Value(bindIndex).action.Invoke(0);
        }
    }
    public static void AddStackPoll(ActionBind bind, string Layer) => SStack.AddStackPoll(bind, Layer, bind.Binding);
    public static void RemoveStackPoll(string BindName, string Layer) => SStack.RemoveStackPoll(Layer, BindName);
}
    