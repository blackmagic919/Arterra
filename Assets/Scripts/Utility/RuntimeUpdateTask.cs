using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arterra.Core;
using UnityEngine;

namespace Arterra.Utils {
    /// <summary>
    /// Main-thread update task routed explicitly by Initialize and Release.
    /// Outside gameplay it yields like AnimatorAwaitTask; no helper GameObject.
    /// Invoke on Unity's main thread and Disable when the owner shuts down.
    /// </summary>
    public sealed class RuntimeUpdateTask {
        private static readonly HashSet<RuntimeUpdateTask> tasks = new();
        /// <summary>Whether updates should be routed through ArterraRuntime's queue.</summary>
        public static bool Active { get; private set; }

        // Call after the runtime update queue has been created.
        public static void Initialize() {
            if (Active) return;
            Active = true;
            foreach (var task in new List<RuntimeUpdateTask>(tasks)) task.Route();
        }

        // Switch back to yielding before awaiting network shutdown.
        public static void Release() {
            if (!Active) return;
            Active = false;
            foreach (var task in new List<RuntimeUpdateTask>(tasks)) task.Route();
        }

        private readonly Action update;
        private readonly Action<Exception> onError;
        private bool running;
        private int generation;
        private Subscription subscription;

        public RuntimeUpdateTask(Action update, Action<Exception> onError) {
            this.update = update;
            this.onError = onError;
        }

        public void Disable() {
            running = false;
            tasks.Remove(this);
            ++generation;
            if (subscription != null) subscription.Active = false;
            subscription = null;
        }

        public void Invoke() {
            if (running) return;
            running = true;
            tasks.Add(this);
            Route();
        }

        private void Route() {
            if (!running) return;
            int epoch = ++generation;
            if (subscription != null) subscription.Active = false;
            subscription = null;
            if (Active) {
                subscription = new Subscription(this);
                ArterraRuntime.MainLoopUpdateTasks.Enqueue(subscription);
            } else InvokeFallback(epoch);
        }

        private async void InvokeFallback(int epoch) {
            try {
                // A lifecycle transition invalidates this loop, even if another loop
                // starts before its pending yield resumes.
                while (running && generation == epoch && Application.isPlaying) {
                    update();
                    await Task.Yield();
                }
            } catch (Exception exception) {
                if (generation == epoch) Disable();
                onError?.Invoke(exception);
            } finally {
                if (generation == epoch) Disable();
            }
        }

        private sealed class Subscription : ArterraRuntime.IUpdateSubscriber {
            private readonly RuntimeUpdateTask owner;
            public bool Active { get; set; } = true;
            public Subscription(RuntimeUpdateTask owner) => this.owner = owner;
            public void Update(MonoBehaviour mono = null) {
                if (!Active) return;
                try { owner.update(); }
                catch (Exception exception) {
                    owner.Disable();
                    owner.onError?.Invoke(exception);
                }
            }
        }
    }
}
