using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Arterra.Configuration;
using Arterra.Engine.Terrain;
using UnityEngine;
using UnityEngine.Profiling;

namespace Arterra.Core {
public class ArterraRuntime : MonoBehaviour {
    public static ArterraRuntime instance { get; private set; }
    /// <summary>
    /// The load for each task as ordered in <see cref="Utils.priorities.planning"/>.
    /// Each task's load is cumilated until the frame's load is exceeded at which point generation stops.
    /// </summary>
    public static readonly int[] taskLoadTable = { 4, 3, 3, 1, 4, 0, 3 };
    /// <summary>
    /// A queue containing subscribed tasks that are executed
    /// once every update loop. The update loop occurs
    /// once every frame before the late update loop.
    /// </summary>
    public static Queue<IUpdateSubscriber> MainLoopUpdateTasks;
    /// <summary>
    /// A queue containing subscribed tasks that are executed
    /// once every late update loop. The late update loop
    /// occurs once every frame after the update loop.
    /// </summary>
    public static Queue<IUpdateSubscriber> MainLateUpdateTasks;
    /// <summary>
    /// A queue containing subscribed tasks that are executed
    /// once every fixed update loop. The fixed update loop is
    /// akin to a game-tick and is frame-independent. 
    /// </summary>
    public static Queue<IUpdateSubscriber> MainFixedUpdateTasks;
    /// <summary>
    /// A queue containing coroutines which will be synchronized and updated
    /// by Unity's main update loop. Unity does not allow injection into its synchronization
    /// outside monobehavior, so OctreeTerrain has to manage this.
    /// </summary>
    public static Queue<IEnumerator> MainCoroutines;
    /// <summary>
    /// A queue of generation actions which are processed
    /// sequentially and discarded once they are called. All tasks 
    /// are channeled through this queue to manage the resource load
    /// and facilitate expensive operations. 
    /// </summary>
    /// <remarks>
    /// The concurrent queue may also be used to reinject tasks
    /// on different threads back into the main thread.
    /// </remarks>
    public static LinkedList<RuntimeTaskEl> EventTaskQueue; 
    private static LinkedListNode<RuntimeTaskEl> _nextNode;
    
    private void OnEnable() {
        instance = this;
        MainLoopUpdateTasks = new Queue<IUpdateSubscriber>();
        MainLateUpdateTasks = new Queue<IUpdateSubscriber>();
        MainFixedUpdateTasks = new Queue<IUpdateSubscriber>();
        MainCoroutines = new Queue<System.Collections.IEnumerator>();
        EventTaskQueue = new LinkedList<RuntimeTaskEl>();
        _nextNode = null;
        SystemProtocol.Startup();
    }

    private void OnDisable() {
        if (instance == this)
            instance = null;
        SystemProtocol.Shutdown();
    }

#if UNITY_EDITOR
    private void OnDrawGizmos() => OctreeTerrain.OnDrawGizmos();
#endif

    private void Update() {
        ProcessUpdateTasks(MainLoopUpdateTasks);
        ProcessCoroutines(MainCoroutines);
        ProcessEventTasks();
    }
    private void LateUpdate() { ProcessUpdateTasks(MainLateUpdateTasks); }
    private void FixedUpdate() { ProcessUpdateTasks(MainFixedUpdateTasks); }
    private void ProcessUpdateTasks(Queue<IUpdateSubscriber> taskQueue) {
        int UpdateTaskCount = taskQueue.Count;
        for (int i = 0; i < UpdateTaskCount; i++) {
            IUpdateSubscriber task = taskQueue.Dequeue();
            if (!task.Active)
                continue;
            task.Update(this);
            taskQueue.Enqueue(task);
        }
    }

    private void ProcessCoroutines(Queue<IEnumerator> taskQueue) {
        int UpdateTaskCount = taskQueue.Count;
        for (int i = 0; i < UpdateTaskCount; i++) {
            var task = taskQueue.Dequeue();
            if (task != null) StartCoroutine(task);
        }
    }

    public static void Register(EventTask config) {
        EventTaskQueue.AddLast(new RuntimeTaskEl {
            type = RuntimeTaskEl.ElType.Task,
            task = config,
        }); 
    }

    public static void RegisterFence(TaskFence config) {
        EventTaskQueue.AddLast(new RuntimeTaskEl {
            type = RuntimeTaskEl.ElType.Fence,
            fence = config,
        }); 
    }

    private void ProcessEventTasks() {
        if (EventTaskQueue.Count == 0)
            return;

        int maxFrameLoad = Config.CURRENT.Quality.Terrain.value.maxFrameLoad;
        
        int frameLoad = 0;
        int iterations = EventTaskQueue.Count;
        if (_nextNode == null || _nextNode.List != EventTaskQueue)
            _nextNode = EventTaskQueue.First;
        
        //Round robin event thingy
        while (iterations-- > 0 && _nextNode != null && EventTaskQueue.Count > 0) {
            var current = _nextNode;
            _nextNode = current.Next ?? EventTaskQueue.First;

            RuntimeTaskEl currentEl = current.Value;
            if (currentEl.type == RuntimeTaskEl.ElType.Fence) {
                if (EventTaskQueue.First.Value == currentEl) {
                    EventTaskQueue.Remove(current);
                    currentEl.fence.OnReached?.Invoke();
                } else if (currentEl.fence.IsBlocking)
                    _nextNode = EventTaskQueue.First;
                continue;
            }

            EventTask task = currentEl.task;
            if (task.cancelToken?.Invoke() == true) {
                EventTaskQueue.Remove(current);
                continue;
            }

            if (task.processToken?.Invoke() == false)
                continue;
            

            Profiler.BeginSample("Task Number: " + task.id);
            task.task?.Invoke();
            Profiler.EndSample();

            EventTaskQueue.Remove(current);
            frameLoad += taskLoadTable[task.id];
            if (frameLoad > maxFrameLoad)
                break;
        }

        if (EventTaskQueue.Count == 0)
            _nextNode = null;
    }

    public class RuntimeTaskEl {
        public enum ElType {
            Task, Fence
        }
        public ElType type;
        public TaskFence fence;
        public EventTask task;
    }

    public struct EventTask {
        /// <summary> The action that is executed when the task is processed.</summary>
        public Action task;
        /// <summary>A callback that returns whether or not the job can be performed</summary>
        public Func<bool> processToken;
        /// <summary>
        /// A callback that returns whether or not the job has been cancelled and can be discarded.
        /// </summary>
        public Func<bool> cancelToken;
        /// <summary> 
        /// The priority of the task as defined in <see cref="Utils.priorities.planning"/>. 
        /// Used to identify the load and loading message of the task.
        /// </summary>
        public int id;
    }

    public struct TaskFence {
        public bool IsBlocking;
        public Action OnReached;
    }

    public interface IUpdateSubscriber
    {
        public bool Active{ get; set; }
        public void Update(MonoBehaviour mono = null);
    }

    public class IndirectUpdate : IUpdateSubscriber {
        private bool active = false;
        public bool Active {
            get => active;
            set => active = value;
        }
        Action<MonoBehaviour> callback;
        public IndirectUpdate(Action<MonoBehaviour> callback) {
            this.active = true;
            this.callback = callback;
        }
        public void Update(MonoBehaviour mono = null) {
            callback.Invoke(mono);
        }
    }
}
}