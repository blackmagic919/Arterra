using System;
using System.Collections;
using System.Collections.Generic;
using Arterra.Core.Terrain;
using UnityEngine;
using Utils;

#nullable enable

namespace Arterra.UI.ToolTips {
    public enum TooltipPriority {
        Low = 0,
        Medium = 1,
        High = 2,
        Critical = 3
    }


    public class TooltipData {
        public TooltipConfig Config; // Tooltip config data
        public int VirtualRunTime; // Simulated virtual runtime value for scheduling.

        public DateTime EnqueuedTime; // Time when the tooltip was enqueued.

        public bool IsAcknowledged {
            get {
                return (DateTime.Now - EnqueuedTime).TotalSeconds >= Config.AcknowledgeTime;
            }
        }

        public void Acknowlege() {
            EnqueuedTime = DateTime.Now.AddSeconds(-Config.AcknowledgeTime - 1); // Manipulate enqueued time to mark as acknowledged.
        }

        public TooltipData(TooltipConfig config) {
            Config = config;
            EnqueuedTime = DateTime.Now;
            VirtualRunTime = 0;
        }        
    }

    class TooltipSlot {
        public TooltipData? ActiveTooltip;
        public DateTime DisplayStartTime;

        public int Index; //TEST only

        private float MinDisplayTimeInSeconds;

        public TooltipSlot(float minDisplayTimeInSeconds, int index) {
            ActiveTooltip = null;
            MinDisplayTimeInSeconds = minDisplayTimeInSeconds;
            Index = index;
        }

        public bool IsAvailable() {
            return ActiveTooltip == null;
        }

        public bool CanClear() {
            if (ActiveTooltip == null) return true;

            // Check if minimum display time has passed
            var elapsed = (DateTime.Now - DisplayStartTime).TotalSeconds;
            return elapsed >= MinDisplayTimeInSeconds;
        }

        public void Clear() {
            if (ActiveTooltip == null) return;
            // TODO: clear UI elements
            ActiveTooltip = null;
            ADebug.LogInfo($"TooltipPipeline: Cleared tooltip in slot {Index}");
        }


        public void ShowTooltip(TooltipData? tooltip) {
            if (tooltip == null) return;
            ActiveTooltip = tooltip;
            DisplayStartTime = DateTime.Now;
            // TODO: render UI elements
            ADebug.LogInfo($"TooltipPipeline: Showing tooltip {tooltip.Config.PrefabPath} in slot {Index}");
        }
    }

    /// <summary>
    /// Tooltip Pipeline handling the flow of tooltip data and rendering.
    /// </summary>
    public class TooltipPipeline {

        // Simulate CFS weights for tooltip priority handling.
        const int WEIGHT_LOW = 335;
        const int WEIGHT_MEDIUM = 1024;
        const int WEIGHT_HIGH = 3121;
        const int WEIGHT_CRITICAL = 88761;

        Dictionary<TooltipPriority, int> priorityWeights = new Dictionary<TooltipPriority, int>() {
            { TooltipPriority.Low, WEIGHT_LOW },
            { TooltipPriority.Medium, WEIGHT_MEDIUM },
            { TooltipPriority.High, WEIGHT_HIGH },
            { TooltipPriority.Critical, WEIGHT_CRITICAL }
        };

        private float MinDisplayTimeInSeconds = 2.0f;

        private List<TooltipSlot>? slots;

        private HashSet<string> blacklist = new HashSet<string>();

        private PriorityQueue<TooltipData, int> tooltipQueue = new PriorityQueue<TooltipData, int>();

        // To support delayed tooltips. The tooltip will be delayed to be enqueued until the TriggerTime is reached.
        private List<TooltipData> delayedTooltips = new List<TooltipData>();

        // Makde singleton instance
        private static TooltipPipeline? _instance;
        public static TooltipPipeline? Instance {
            get {
                if (_instance == null) {
                    _instance = new TooltipPipeline();
                }
                return _instance;
            }
        }

        // Make constructor private
        private TooltipPipeline() { }

        // Initialize the tooltip pipeline with settings.
        public  void Initialize(float minDisplayTimeInSeconds, int slotCount) {
            MinDisplayTimeInSeconds = minDisplayTimeInSeconds;

            slots = new List<TooltipSlot>(slotCount);
            for (int i = 0; i < slotCount; i++) {
                slots.Add(new TooltipSlot(MinDisplayTimeInSeconds, i));
            }

            // Set timer to check tooltipQueue and display tooltips.
            // Need slot count timers, staggered by MinDisplayTimeInSeconds / SlotCount.
            OctreeTerrain.MainCoroutines.Enqueue(ProcessTooltipQueue());
        }

        // Enqueue a tooltip into the pipeline.
        public bool EnqueueTooltip(TooltipData? tooltip) {
            // TODO: if there's already the same tooltip in the queue or active, ignore.
            if (tooltip == null) return false;

            if (blacklist.Contains(tooltip.Config.PrefabPath)) {
                ADebug.LogInfo($"TooltipPipeline: Tooltip {tooltip.Config.PrefabPath} is blacklisted, not enqueuing.");
                return false;
            }

            // If TriggerTime is set, put into delayed tooltips.
            if (tooltip.Config.TriggerTime > 0) {
                delayedTooltips.Add(tooltip);
                ADebug.LogInfo($"TooltipPipeline: Tooltip {tooltip.Config.PrefabPath} will be enqueued after delay of {tooltip.Config.TriggerTime} seconds.");
                return true;
            }

            int weight = priorityWeights[tooltip.Config.Priority];
            tooltipQueue.Enqueue(tooltip, weight);
            return true;
        }

        public TooltipData? DequeueTooltip() {
            // Check delayed tooltips first, if any reached trigger time, enqueue them.
            foreach (var delayedTooltip in delayedTooltips.ToArray()) {
                var elapsed = (DateTime.Now - delayedTooltip.EnqueuedTime).TotalSeconds;
                if (elapsed >= delayedTooltip.Config.TriggerTime) {
                    int weight = priorityWeights[delayedTooltip.Config.Priority];
                    delayedTooltip.EnqueuedTime = DateTime.Now; // Reset enqueued time
                    tooltipQueue.Enqueue(delayedTooltip, weight);
                    delayedTooltips.Remove(delayedTooltip);
                    ADebug.LogInfo($"TooltipPipeline: Delayed tooltip {delayedTooltip.Config.PrefabPath} enqueued after delay.");
                }
            }

            while (tooltipQueue.Count > 0) {
                TooltipData nextTooltip = tooltipQueue.Dequeue();
                if (nextTooltip.IsAcknowledged) {
                    ADebug.LogDebug($"TooltipPipeline: Tooltip {nextTooltip.Config.PrefabPath} is acknowledged upon dequeue.");
                    if (nextTooltip.Config.BlockingTooltips) {
                        blacklist.Add(nextTooltip.Config.PrefabPath);
                        ADebug.LogDebug($"TooltipPipeline: Tooltip {nextTooltip.Config.PrefabPath} added to blacklist.");
                    }
                    continue; // Skip acknowledged tooltips
                    
                } 
                if (blacklist.Contains(nextTooltip.Config.PrefabPath)) {
                    ADebug.LogDebug($"TooltipPipeline: Tooltip {nextTooltip.Config.PrefabPath} is blacklisted upon dequeue.");
                    continue; // Skip acknowledged or blacklisted tooltips
                } else {
                    return nextTooltip;
                }
            }
            return null;
        }

        public void DismissTooltip(string prefabPath) {
            foreach (var tooltip in tooltipQueue.UnorderedItems) {
                if (tooltip.Element.Config.PrefabPath == prefabPath) {
                    tooltip.Element.Acknowlege();
                    ADebug.LogInfo($"TooltipPipeline: Tooltip {prefabPath} acknowledged and will be dismissed.");
                }
            }
        }

        private void UpdateVirtualRuntime(TooltipData? tooltip) {
            // Update virtual runtimes of all tooltips in the queue.
            if (tooltip == null) return;
            int weight = priorityWeights[tooltip.Config.Priority];
            tooltip.VirtualRunTime += (int)(MinDisplayTimeInSeconds * (WEIGHT_MEDIUM / weight));
        }
        

        // Prepare clear tooltip in the given slot.
        // If the tootip not acknowledged, re-enqueue it, but do not clear the slot. 
        //   This is to ensure the tooltip remains in the slot if it's on its turn next.
        // If acknowledged, clear the slot.
        // Returns the to be cleared tooltip data if any.
        private TooltipData? PrepareClearTooltip(TooltipSlot slot) {
            if (!slot.IsAvailable()) {
                // Re-enqueue the current tooltip
                TooltipData? currentTooltip = slot.ActiveTooltip;
                UpdateVirtualRuntime(currentTooltip);
                // TODO: Check aknowlege time
                if (currentTooltip != null) {
                    tooltipQueue.Enqueue(currentTooltip, currentTooltip.VirtualRunTime);
                } else {
                    slot.Clear();
                }

                return currentTooltip;
            } else {
                return null;
            }
        }

        private void ClearAndShowTooltip(TooltipData? tooltip, TooltipSlot slot) {
            slot.Clear();
            slot.ShowTooltip(tooltip);
        }


        private IEnumerator ProcessTooltipQueue() {
            while (true) {
                if (slots != null) {
                    foreach (var slot in slots) {
                        // Process each slot
                        // If the slot can be cleared
                        if (slot.CanClear()) {
                            TooltipData? oldTooltip = PrepareClearTooltip(slot);
                            
                            // Dequeue next tooltip to the slot
                            TooltipData? nextTooltip = DequeueTooltip();

                            if (oldTooltip != nextTooltip)
                                ClearAndShowTooltip(nextTooltip, slot);
                        }
                    }
                }
                yield return new WaitForSeconds(1.0f); // Check every second
            }
        }

    }
}