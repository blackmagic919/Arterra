using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InputPoller : UpdateTask
{
    private static Registry<KeyBind> KeyMappings;
    private static Dictionary<string, uint> LayerHeads;
    private static SharedLinkedList<ActionBind> KeyBinds;
    private static Queue<Action> KeyBindChanges;
    private static bool CursorLock = false;
    private const int MaxActionBinds = 10000;

    public static void Initialize()
    {
        /*KeyMappings = new ()
        {
            {"Place Terrain", KeyCode.Mouse1},
            {"Remove Terrain", KeyCode.Mouse0},
            {"Place Liquid", KeyCode.LeftShift},
            {"Pickup Item", KeyCode.Mouse0},
            {"Pause", KeyCode.Escape},
            {"Quit", KeyCode.Return},
            
            {"Select", KeyCode.Mouse0},
            {"Open Inventory", KeyCode.Tab},
            {"Craft", KeyCode.Return},

            {"Jump", KeyCode.Space},
            {"Move Horizontal", KeyCode.Alpha3},
            {"Move Vertical", KeyCode.Alpha4},
            {"Mouse Horizontal", KeyCode.Alpha0},
            {"Mouse Vertical", KeyCode.Alpha1},

            {"Hotbar1", KeyCode.Alpha1},
            {"Hotbar2", KeyCode.Alpha2},
            {"Hotbar3", KeyCode.Alpha3},
            {"Hotbar4", KeyCode.Alpha4},
            {"Hotbar5", KeyCode.Alpha5},
            {"Hotbar6", KeyCode.Alpha6},
            {"Hotbar7", KeyCode.Alpha7},
            {"Hotbar8", KeyCode.Alpha8},
            {"Hotbar9", KeyCode.Alpha9},
        };*/
        KeyMappings = WorldStorageHandler.WORLD_OPTIONS.System.Input;
        KeyMappings.Construct();
        
        LayerHeads = new Dictionary<string, uint>();
        KeyBinds = new SharedLinkedList<ActionBind>(MaxActionBinds); 
        KeyBindChanges = new Queue<Action>();
        OctreeTerrain.MainLoopUpdateTasks.Enqueue(new InputPoller{active = true});
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
                ActionBind BoundAction = KeyBinds.Value(current);
                if(BoundAction.BindIndex == -1) break; //Context Fence/Barrier

                KeyBind KeyBind = KeyMappings.Retrieve(BoundAction.BindIndex);
                if(KeyBind.IsTriggered(out float axis))
                    BoundAction.action.Invoke(axis);
                current = KeyBinds.Next(current);
            } while(current != head);
        }
        while(KeyBindChanges.Count > 0)
            KeyBindChanges.Dequeue().Invoke();
    }
    
    public static void AddKeyBindChange(Action action){
        KeyBindChanges.Enqueue(action);
    }

    public static uint AddBinding(string Binding, string Layer, Action<float> action){
        if(!KeyMappings.Contains(Binding))
            return 0;
        return AddKeyBind(new ActionBind{
            BindIndex = KeyMappings.RetrieveIndex(Binding),
            action = action
        }, Layer);
    }

    public static uint AddContextFence(string Layer){
        return AddKeyBind(new ActionBind{
            BindIndex = -1,
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

    private static uint AddKeyBind(ActionBind keyBind, string layer){
        if(!LayerHeads.ContainsKey(layer))
            LayerHeads.Add(layer, KeyBinds.Enqueue(keyBind));
        else 
            LayerHeads[layer] = KeyBinds.Enqueue(keyBind, LayerHeads[layer]);
        return LayerHeads[layer];
    }

    [Serializable]
    public struct KeyBind{
        public List<Binding> Bindings;
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
        public enum BindCond{
            And = 0,
            Or = 1,
            Exclude = 2,
        }
        public readonly bool IsTriggered(out float axis){
            axis = 0; bool Pressed = false;
            if(Bindings == null || Bindings.Count == 0) return false;
            for(int i = 0; i < Bindings.Count; i++){
                Binding bind = Bindings[i];
                if(bind.PollType == BindPoll.Exclude) {
                    Pressed |= !PollTypes[(int)bind.PollType](bind.Key);
                } else if(bind.PollType == BindPoll.Axis){
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

    private struct ActionBind
    {
        public int BindIndex;
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
