using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InputPoller : UpdateTask
{
    private static Dictionary<string, KeyCode> KeyMappings;
    private static Dictionary<KeyCode, string> AxisMappings;

    private static Dictionary<string, uint> LayerHeads;
    private static SharedLinkedList<KeyBind> KeyBinds;
    private static Queue<Action> KeyBindChanges;
    private static bool CursorLock = false;

    private static Func<KeyCode, bool>[] PollTypes = {
        null,
        null,
        Input.GetKey,
        Input.GetKeyDown,
        Input.GetKeyUp,
    };

    public static void Initialize()
    {
        KeyMappings = new ()
        {
            {"Jump", KeyCode.Space},
            {"Remove Terrain", KeyCode.Mouse0},
            {"Place Terrain", KeyCode.Mouse1},
            {"Place Liquid", KeyCode.LeftShift},
            {"Previous Material", KeyCode.Alpha1},
            {"Next Material", KeyCode.Alpha2},
            {"Toggle Crafting", KeyCode.C},
            {"Pause", KeyCode.Escape},
            {"Quit", KeyCode.Return},

            {"Move Horizontal", KeyCode.Alpha3},
            {"Move Vertical", KeyCode.Alpha4},
            {"Mouse Horizontal", KeyCode.Alpha0},
            {"Mouse Vertical", KeyCode.Alpha1},
            
        };

        AxisMappings = new ()
        {
            {KeyCode.Alpha0, "Mouse Y"},
            {KeyCode.Alpha1, "Mouse X"},
            {KeyCode.Alpha2, "Mouse ScrollWheel"},
            {KeyCode.Alpha3, "Horizontal"},
            {KeyCode.Alpha4, "Vertical"},
        };

        LayerHeads = new Dictionary<string, uint>();
        KeyBinds = new SharedLinkedList<KeyBind>(10000); 
        KeyBindChanges = new Queue<Action>();
        EndlessTerrain.MainLoopUpdateTasks.Enqueue(new InputPoller{active = true});
    }

    public static void SetCursorLock(bool value)
    {
        if(CursorLock == value) return;
        CursorLock = value;

        if(CursorLock)
        {//we force unlock the cursor if the user disable the cursor locking helper
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        } else {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    public override void Update(MonoBehaviour mono){
        foreach(uint head in LayerHeads.Values){
            uint current = head;
            do{
                KeyBind bind = KeyBinds.Value(current);
                if(bind.pollType == (byte)BindPoll.ContextFence) break; //Context Fence/Barrier
                if(bind.pollType == (byte)BindPoll.Axis){
                    bind.action.Invoke(Input.GetAxis(AxisMappings[bind.key]));
                } else if(PollTypes[bind.pollType](bind.key))
                    bind.action(float.NaN);
                current = KeyBinds.Next(current);
            } while(current != head);
        }
        while(KeyBindChanges.Count > 0)
            KeyBindChanges.Dequeue().Invoke();
    }
    
    public static void AddKeyBindChange(Action action){
        KeyBindChanges.Enqueue(action);
    }

    public static uint AddBinding(Binding binding){
        if(!KeyMappings.ContainsKey(binding.Key))
            return 0;
        return AddKeyBind(new KeyBind{
            key = KeyMappings[binding.Key],
            pollType = binding.PollType,
            action = binding.action
        }, binding.Layer);
    }

    public static uint AddContextFence(string Layer){
        return AddKeyBind(new KeyBind{
            key = KeyCode.None,
            pollType = (byte)BindPoll.ContextFence,
            action = null,
        }, Layer);
    }

    //Do not call this with an invalid bindIndex not in the layer, or else it could corrupt all keybinds
    public static void RemoveKeyBind(uint bindIndex, string layer){
        if(!LayerHeads.ContainsKey(layer))
            return;

        //Move head if removed, if empty remove layer
        if(LayerHeads[layer] == bindIndex)
            LayerHeads[layer] = KeyBinds.Next(bindIndex);
        if(LayerHeads[layer] == bindIndex)
            LayerHeads.Remove(layer);

        KeyBinds.Remove(bindIndex);
    }

    //Do not call this with an invalid bindIndex not in the layer, or else it could corrupt all keybinds
    public static void RemoveContextFence(uint bindIndex, string layer = "base"){
        if(!LayerHeads.ContainsKey(layer))
            return;

        uint current = LayerHeads[layer];
        while(current != bindIndex){
            uint next = KeyBinds.Next(current);
            RemoveKeyBind(current, layer);
            current = next;
        }
        LayerHeads[layer] = KeyBinds.Next(current);
        RemoveKeyBind(current, layer);
    }

    private static uint AddKeyBind(KeyBind keyBind, string layer){
        if(!LayerHeads.ContainsKey(layer))
            LayerHeads.Add(layer, KeyBinds.Enqueue(keyBind));
        else 
            LayerHeads[layer] = KeyBinds.Enqueue(keyBind, LayerHeads[layer]);
        return LayerHeads[layer];
    }

    public struct Binding{
        public string Key;
        public string Layer;
        public byte PollType;
        public Action<float> action;
        public Binding(string key, string layer, BindPoll pollType, Action<float> action){
            Key = key;
            Layer = layer;
            PollType = (byte)pollType;
            this.action = action;
        }
    }

    public enum BindPoll{
        ContextFence = 0,
        Axis = 1,
        Hold = 2,
        Down = 3,
        Up = 4,
    }

    private struct KeyBind
    {
        public KeyCode key;
        public byte pollType; // 0 = Hold, 1 = Down, 2 = Up, 3 = Context Fence/Barrier
        public Action<float> action;
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
}
