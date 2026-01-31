using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Arterra.Engine.Terrain;
using UnityEngine;
using Arterra.Utils;
using Arterra.Configuration;
using Mono.Cecil;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#nullable enable

namespace Arterra.GamePlay.UI.ToolTips {
    public class ToolTipSystemState {
        public HashSet<string> blacklist;
        public Dictionary<string, TooltipData> activeToolTips;
        public Dictionary<string, TooltipConfig> pendingToolTips;
        public Dictionary<string, TooltipData> toolTipHistory;
        public HashSet<TooltipDismissorConfig> dismissEvents;
        public static ToolTipSystemState Build() {
            ToolTipSystemState d = new ();
            d.blacklist = new HashSet<string>();
            d.activeToolTips = new Dictionary<string, TooltipData>();
            d.toolTipHistory = new Dictionary<string, TooltipData>();
            d.pendingToolTips = new Dictionary<string, TooltipConfig>();
            d.dismissEvents = new HashSet<TooltipDismissorConfig>();

            foreach(var toolTip in Config.CURRENT.System.Tooltips.GenericToolTipEvents.value) {
                d.pendingToolTips.Add(toolTip.PrefabPath, toolTip);
            }

            foreach(var dismissor in Config.CURRENT.System.Tooltips.GenericTooltipDismissorEvents.value) {
                d.dismissEvents.Add(dismissor);
            }
            
            return d;
        }
    }

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
        public int SlotIndex = -1;

        public bool IsAcknowledged => (DateTime.Now - EnqueuedTime).TotalSeconds >= Config.AcknowledgeTime;

        public void Acknowlege() {
            EnqueuedTime = DateTime.Now.AddSeconds(-Config.AcknowledgeTime - 1); // Manipulate enqueued time to mark as acknowledged.
        }

        public TooltipData(TooltipConfig config) {
            Config = config;
            EnqueuedTime = DateTime.Now;
            VirtualRunTime = 0;
            SlotIndex = -1;
        }        
    }

    class TooltipSlot {
        public TooltipData? ActiveTooltip;
        public DateTime DisplayStartTime;
        private AnimatorAwaitTask transitionTask;
        private Animator SlotAnimator;
        private GameObject Slot;
        private GameObject Content;
        private Button? Acknowledge;
        private Button? Ignore;

        public int Index; //TEST only

        private float MinDisplayTimeInSeconds;
        public TooltipSlot(GameObject SlotTransform, float minDisplayTimeInSeconds, int index) {
            ActiveTooltip = null;
            MinDisplayTimeInSeconds = minDisplayTimeInSeconds;
            Index = index;
            Slot = SlotTransform;
            Acknowledge = Slot.transform.Find("Close")?.GetComponent<Button>();
            Ignore = Slot.transform.Find("Ignore")?.GetComponent<Button>();
            Content = Slot.transform.Find("Content").gameObject;

            Acknowledge?.onClick.AddListener(() => TooltipPipeline.Instance?.DismissTooltip(ActiveTooltip?.Config.PrefabPath));
            Ignore?.onClick.AddListener(() => TooltipPipeline.Instance?.IgnoreToolTip(ActiveTooltip?.Config.PrefabPath));
            SlotAnimator = Slot.GetComponent<Animator>();
        }

        public void Release() => GameObject.Destroy(Slot);

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
            ActiveTooltip.SlotIndex = -1;
            ActiveTooltip = null;
            ADebug.LogInfo($"TooltipPipeline: Cleared tooltip in slot {Index}");
            transitionTask?.Disable();
            SlotAnimator.SetBool("IsOpen", false);
            transitionTask = new AnimatorAwaitTask(SlotAnimator, "Closed", ClearDisplay);
            transitionTask.Invoke();
        }


        public void ShowTooltip(TooltipData? tooltip) {
            if (tooltip == null) return;
            tooltip.SlotIndex = Index;
            ActiveTooltip = tooltip;
            DisplayStartTime = DateTime.Now;

            ADebug.LogInfo($"TooltipPipeline: Showing tooltip {tooltip.Config.PrefabPath} in slot {Index}");
            transitionTask?.Disable();
            SlotAnimator.SetBool("IsOpen", false);
            transitionTask = new AnimatorAwaitTask(SlotAnimator, "Closed", SwitchToolTipDisplay);
            transitionTask.Invoke();
        }

        public void ShowToolTipImmediate(TooltipData? tooltip) {
            if (tooltip == null) return;
            tooltip.SlotIndex = Index;
            ActiveTooltip = tooltip;
            DisplayStartTime = DateTime.Now;
            ADebug.LogInfo($"TooltipPipeline: Showing tooltip {tooltip.Config.PrefabPath} in slot {Index}");
            transitionTask?.Disable();
            SwitchToolTipDisplay();
        }

        private void SwitchToolTipDisplay() {
            if (ActiveTooltip == null) return;
            foreach(Transform child in Content.transform) {
                GameObject.Destroy(child.gameObject);
            }
            GameObject.Instantiate(Resources.Load<GameObject>(ActiveTooltip.Config.PrefabPath), Content.transform);
            SegmentedUIEditor.ForceLayoutRefresh(Slot.transform);
            SlotAnimator.SetBool("IsOpen", true);
        }

        private void ClearDisplay() {
            foreach(Transform child in Content.transform) {
                GameObject.Destroy(child.gameObject);
            }
            SegmentedUIEditor.ForceLayoutRefresh(Slot.transform);
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
        private List<TooltipSlot> slotsPast;

        public ToolTipSystemState State => WorldDataHandler.WorldData.ToolTips;
        private PriorityQueue<TooltipData, int> tooltipQueue;

        // To support delayed tooltips. The tooltip will be delayed to be enqueued until the TriggerTime is reached.
        private List<TooltipData> delayedTooltips;
        private GameObject ToolTipRoot;

        private GameObject PastMenu;
        private GameObject ActiveMenu;
        private Button SeeHidden;
        private Button MinimizeButton;

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

        public bool IsBlacklisted(TooltipData tt) => State.blacklist.Contains(tt.Config.PrefabPath);

        // Make constructor private
        private TooltipPipeline() { }

        // Initialize the tooltip pipeline with settings.
        public void Initialize(float minDisplayTimeInSeconds, int slotCount) {
            MinDisplayTimeInSeconds = minDisplayTimeInSeconds;
            ToolTipRoot = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/ToolTips/Menu"), GameUIManager.UIHandle.transform);

            tooltipQueue = new PriorityQueue<TooltipData, int>();
            delayedTooltips = new List<TooltipData>();
            InitializeButtons();

            Transform ActiveTsf = ToolTipRoot.transform.Find("ActiveToolTips");
            Transform Content = ActiveTsf.GetChild(0).GetChild(0);

            slotsPast = new List<TooltipSlot>();
            slots = new List<TooltipSlot>(slotCount);
            for (int i = 0; i < slotCount; i++) {
                GameObject slot = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/ToolTips/Slot/Slot"), Content);
                slots.Add(new TooltipSlot(slot, MinDisplayTimeInSeconds, i));
            }
            

            // Set timer to check tooltipQueue and display tooltips.
            // Need slot count timers, staggered by MinDisplayTimeInSeconds / SlotCount.
            OctreeTerrain.MainCoroutines.Enqueue(ProcessTooltipQueue());
        }

        private void InitializeButtons() {
            Transform ActiveTsf = ToolTipRoot.transform.Find("ActiveToolTips");
            Transform PastTsf = ToolTipRoot.transform.Find("PastToolTips");
            ActiveMenu = ActiveTsf.gameObject;
            PastMenu = PastTsf.gameObject;

            Animator ActiveMenuAnimator = ActiveTsf.GetComponent<Animator>();
            Animator PastMenuAnimator = PastTsf.GetComponent<Animator>();
            MinimizeButton = ToolTipRoot.transform.Find("Minimize").GetComponent<Button>();
            SeeHidden = ToolTipRoot.transform.Find("SeeHidden").GetComponent<Button>();
            
            MinimizeButton.onClick.AddListener(() => {
                bool active = !ActiveMenuAnimator.GetBool("IsOpen");
                ActiveMenuAnimator.SetBool("IsOpen", active);
                PastMenuAnimator.SetBool("IsOpen", active);
                MinimizeButton.image.sprite = Resources.Load<Sprite>(active
                    ? "Prefabs/GameUI/ToolTips/Icons/Minimize"
                    : "Prefabs/GameUI/ToolTips/Icons/Maximize"
                );

                EventSystem.current.SetSelectedGameObject(null);
            });
            
            SeeHidden.onClick.AddListener(() => {
                MinimizeButton.image.sprite = Resources.Load<Sprite>("Prefabs/GameUI/ToolTips/Icons/Minimize");
                ActiveMenuAnimator.SetBool("IsOpen", true);
                PastMenuAnimator.SetBool("IsOpen", true);
                ActiveMenu.SetActive(!ActiveMenu.activeSelf);
                PastMenu.SetActive(!PastMenu.activeSelf);
                SeeHidden.image.sprite = Resources.Load<Sprite>(PastMenu.activeSelf
                    ? "Prefabs/GameUI/ToolTips/Icons/Unhide"
                    : "Prefabs/GameUI/ToolTips/Icons/Hide"
                );
                
                if(PastMenu.activeSelf) RefreshPastToolTips();
                EventSystem.current.SetSelectedGameObject(null);
            });
        }

        private void RefreshPastToolTips() {
            Transform PastTsf = ToolTipRoot.transform.Find("PastToolTips");
            Transform Content = PastTsf.GetChild(0).GetChild(0);
            var pastToolTips = State.toolTipHistory.ToList();
            pastToolTips.Sort((a, b) => a.Value.EnqueuedTime.CompareTo(b.Value.EnqueuedTime));
            var toolTips = pastToolTips.Select(s => s.Value).ToArray();
            
            int shared = math.min(slotsPast.Count(), toolTips.Count());
            for (int i = 0; i < shared; i++) {
                slotsPast[i].ShowToolTipImmediate(toolTips[i]);
            } 
            for (int i = shared; i < slotsPast.Count(); i++) {
                slotsPast[i].Release();
            }  slotsPast = slotsPast.Take(shared).ToList();
            for (int i = shared; i < toolTips.Count(); i++) {
                GameObject slot = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/ToolTips/Slot/DisabledSlot"), Content);
                TooltipSlot newToolTip = new (slot, float.MaxValue, i);
                newToolTip.ShowToolTipImmediate(toolTips[i]);
                slotsPast.Add(newToolTip);
            } 
        }

        // Enqueue a tooltip into the pipeline.
        public bool EnqueueTooltip(TooltipData? tooltip) {
            // TODO: if there's already the same tooltip in the queue or active, ignore.
            if (tooltip == null) return false;

            if (IsBlacklisted(tooltip)) {
                ADebug.LogInfo($"TooltipPipeline: Tooltip {tooltip.Config.PrefabPath} is blacklisted, not enqueuing.");
                return false;
            }

            if (State.activeToolTips.TryGetValue(tooltip.Config.PrefabPath, out TooltipData entry)) {
                ADebug.LogInfo($"TooltipPipeline: Tooltip {tooltip.Config.PrefabPath} is already present, not enqueuing.");
                entry.Config.Priority = (TooltipPriority)math.max((int)tooltip.Config.Priority, (int)entry.Config.Priority);
                entry.Config.TriggerTime = math.min(tooltip.Config.TriggerTime, entry.Config.TriggerTime);
                return false;
            } else State.activeToolTips.Add(tooltip.Config.PrefabPath, tooltip);

            // If TriggerTime is set, put into delayed tooltips.
            delayedTooltips.Add(tooltip);
            ADebug.LogInfo($"TooltipPipeline: Tooltip {tooltip.Config.PrefabPath} will be enqueued after delay of {tooltip.Config.TriggerTime} seconds.");
            return true;
        }

        public TooltipData? DequeueTooltip() {
            // Check delayed tooltips first, if any reached trigger time, enqueue them.
            foreach (var delayedTooltip in delayedTooltips.ToArray()) {
                var elapsed = (DateTime.Now - delayedTooltip.EnqueuedTime).TotalSeconds;
                if (elapsed >= delayedTooltip.Config.TriggerTime) {
                    int weight = priorityWeights[delayedTooltip.Config.Priority];
                    delayedTooltip.EnqueuedTime = DateTime.Now; // Reset enqueued time
                    State.toolTipHistory[delayedTooltip.Config.PrefabPath] = delayedTooltip;
                    tooltipQueue.Enqueue(delayedTooltip, weight);
                    delayedTooltips.Remove(delayedTooltip);
                    ADebug.LogInfo($"TooltipPipeline: Delayed tooltip {delayedTooltip.Config.PrefabPath} enqueued after delay.");
                }
            }

            while (tooltipQueue.Count > 0) {
                TooltipData nextTooltip = tooltipQueue.Dequeue();
                if (nextTooltip.IsAcknowledged) {//
                    ADebug.LogDebug($"TooltipPipeline: Tooltip {nextTooltip.Config.PrefabPath} is acknowledged upon dequeue.");
                    State.activeToolTips.Remove(nextTooltip.Config.PrefabPath); //Remove from process list
                    if (nextTooltip.Config.BlockingTooltips) {
                        State.blacklist.Add(nextTooltip.Config.PrefabPath);
                        ADebug.LogDebug($"TooltipPipeline: Tooltip {nextTooltip.Config.PrefabPath} added to blacklist.");
                    }
                    continue; // Skip acknowledged tooltips
                    
                } 
                if (IsBlacklisted(nextTooltip)) {
                    ADebug.LogDebug($"TooltipPipeline: Tooltip {nextTooltip.Config.PrefabPath} is blacklisted upon dequeue.");
                    continue; // Skip acknowledged or blacklisted tooltips
                } else {
                    return nextTooltip;
                }
            }
            return null;
        }

        //"x" Button On Slot
        public void DismissTooltip(string prefabPath) {
            if (String.IsNullOrEmpty(prefabPath)) return;
            if (!State.activeToolTips.TryGetValue(prefabPath, out TooltipData tooltip))
                return;
            tooltip.Acknowlege();
            if(slots == null || tooltip.SlotIndex == -1) return;
            slots[tooltip.SlotIndex].Clear();
            State.activeToolTips.Remove(prefabPath); 
        }

        //"-" Button On Slot
        public void IgnoreToolTip(string prefabPath) {
            if (String.IsNullOrEmpty(prefabPath)) return;
            if (!State.activeToolTips.TryGetValue(prefabPath, out TooltipData tooltip))
                return;
            State.blacklist.Add(prefabPath);
            tooltip.Acknowlege();
            if(slots == null || tooltip.SlotIndex == -1) return;
            slots[tooltip.SlotIndex].Clear();
            State.activeToolTips.Remove(prefabPath); 
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