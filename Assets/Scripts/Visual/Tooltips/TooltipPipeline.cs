using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Arterra.Engine.Terrain;
using UnityEngine;
using Arterra.Utils;
using Arterra.Configuration;
using Unity.Mathematics;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Arterra.Core;

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


    public class TooltipData : PopupHandler.PopupData<TooltipConfig> {
        public TooltipData(TooltipConfig config) : base((TooltipConfig)config.Clone()) { }
    }

    class TooltipSlot : PopupHandler.PopupSlot<TooltipConfig> {
        public TooltipData? ActiveTooltip => (TooltipData?)ActivePopup;

        public TooltipSlot(GameObject slot, float minDisplayTime, int index, bool history = false)
            : base(slot, minDisplayTime, index,
                popup => TooltipPipeline.Instance?.DismissTooltip(popup.Config.PrefabPath), trackSlotIndex: !history) {
            var ignore = slot.transform.Find("Ignore")?.GetComponent<Button>();
            if (ignore == null) return;
            ignore.gameObject.SetActive(true);
            ignore.onClick.AddListener(() => TooltipPipeline.Instance?.IgnoreToolTip(ActiveTooltip?.Config.PrefabPath));
        }

        public void ShowToolTipImmediate(TooltipData tooltip) => Show(tooltip, immediate: true);

        protected override void CreateContent(TooltipConfig config) =>
            GameObject.Instantiate(Resources.Load<GameObject>(config.PrefabPath), content);
    }

    /// <summary>
    /// Tooltip Pipeline handling the flow of tooltip data and rendering.
    /// </summary>
    public class TooltipPipeline {

        // Simulate CFS weights for tooltip priority handling.
        private float MinDisplayTimeInSeconds = 2.0f;

        private List<TooltipSlot>? slots;
        private List<TooltipSlot> slotsPast;

        public ToolTipSystemState State => WorldDataHandler.WorldData.ToolTips;
        private PopupHandler.PopupScheduler<TooltipConfig> scheduler;

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
            ToolTipRoot = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Popups/Popups/Menu"), GameUIManager.UIHandle.transform);

            scheduler = new PopupHandler.PopupScheduler<TooltipConfig>();
            delayedTooltips = new List<TooltipData>();
            InitializeButtons();

            Transform ActiveTsf = ToolTipRoot.transform.Find("ActiveToolTips");
            Transform Content = ActiveTsf.GetChild(0).GetChild(0);

            slotsPast = new List<TooltipSlot>();
            slots = new List<TooltipSlot>(slotCount);
            for (int i = 0; i < slotCount; i++) {
                GameObject slot = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Popups/Slot/IgnorableSlot"), Content);
                slots.Add(new TooltipSlot(slot, MinDisplayTimeInSeconds, i));
            }
            

            // Rotate eligible slots through the shared scheduler.
            ArterraRuntime.MainCoroutines.Enqueue(ProcessTooltipQueue());
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
                    ? "Prefabs/GameUI/Popups/Icons/Minimize"
                    : "Prefabs/GameUI/Popups/Icons/Maximize"
                );

                EventSystem.current.SetSelectedGameObject(null);
            });
            
            SeeHidden.onClick.AddListener(() => {
                MinimizeButton.image.sprite = Resources.Load<Sprite>("Prefabs/GameUI/Popups/Icons/Minimize");
                ActiveMenuAnimator.SetBool("IsOpen", true);
                PastMenuAnimator.SetBool("IsOpen", true);
                ActiveMenu.SetActive(!ActiveMenu.activeSelf);
                PastMenu.SetActive(!PastMenu.activeSelf);
                SeeHidden.image.sprite = Resources.Load<Sprite>(PastMenu.activeSelf
                    ? "Prefabs/GameUI/Popups/Icons/Unhide"
                    : "Prefabs/GameUI/Popups/Icons/Hide"
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
                GameObject slot = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Popups/Slot/DisabledSlot"), Content);
                TooltipSlot newToolTip = new (slot, float.MaxValue, i, history: true);
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
                entry.Config.Priority = (PopupPriority)math.max((int)tooltip.Config.Priority, (int)entry.Config.Priority);
                entry.Config.TriggerTime = math.min(tooltip.Config.TriggerTime, entry.Config.TriggerTime);
                return false;
            } else State.activeToolTips.Add(tooltip.Config.PrefabPath, tooltip);

            tooltip.SlotIndex = -1;
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
                    delayedTooltip.EnqueuedTime = DateTime.Now; // Reset enqueued time
                    State.toolTipHistory[delayedTooltip.Config.PrefabPath] = delayedTooltip;
                    scheduler.Enqueue(delayedTooltip);
                    delayedTooltips.Remove(delayedTooltip);
                    ADebug.LogInfo($"TooltipPipeline: Delayed tooltip {delayedTooltip.Config.PrefabPath} enqueued after delay.");
                }
            }

            while (scheduler.TryDequeue(out var next)) {
                var nextTooltip = (TooltipData)next;
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

        private IEnumerator ProcessTooltipQueue() {
            while (true) {
                if (ToolTipRoot == null) yield break;
                if (slots != null)
                    scheduler.ProcessSlots(slots, MinDisplayTimeInSeconds, DequeueTooltip);
                yield return new WaitForSeconds(1.0f); // Check every second
            }
        }

    }
}
