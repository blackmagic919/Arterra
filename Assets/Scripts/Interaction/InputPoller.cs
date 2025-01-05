using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

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
public class InputPoller : UpdateTask
{
    private class KeyBinder{
        public SharedLinkedList<ActionBind> KeyBinds;
        public Registry<uint> LayerHeads;
        public ref Registry<KeyBind> KeyMappings => ref WorldOptions.CURRENT.GamePlay.Input;
        public ref Registry<KeyBind> DefaultMappings => ref WorldOptions.TEMPLATE.GamePlay.Input;

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

    [Serializable]
    public struct KeyBind{
        public Option<List<Binding> > bindings;
        [JsonIgnore]
        public readonly List<Binding> Bindings => bindings.value;
        private static readonly Func<KeyCode, bool>[] PollTypes = {
            null,
            Input.GetKey, 
            Input.GetKey,
            Input.GetKeyDown,
            Input.GetKeyUp,
        };
        private static readonly Dictionary<KeyCode, string> AxisMappings = new Dictionary<KeyCode, string>{
            {KeyCode.Alpha0, "Mouse X"},
            {KeyCode.Alpha1, "Mouse Y"},
            {KeyCode.Alpha2, "Mouse ScrollWheel"},
            {KeyCode.Alpha3, "Horizontal"},
            {KeyCode.Alpha4, "Vertical"},
        };
        [Serializable]
        public struct Binding
        {
            public KeyCode Key;
            public BindPoll PollType;
            public bool IsAlias; 
        }
        public enum BindPoll{
            Axis = 0,
            Exclude = 1,
            Hold = 2,
            Down = 3,
            Up = 4,
        }
        public readonly bool IsTriggered(out float axis){
            axis = 0; bool Pressed = false;
            if(Bindings == null || Bindings.Count == 0) return false;
            for(int i = Bindings.Count-1; i >= 0; i--){
                Binding bind = Bindings[i];
                if(bind.PollType == BindPoll.Exclude) {
                    Pressed |= !PollTypes[(int)bind.PollType](bind.Key);
                } else if(bind.PollType == BindPoll.Axis){
                    if(AxisMappings.ContainsKey(bind.Key)) 
                        axis = Input.GetAxis(AxisMappings[bind.Key]);
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
    