using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Arterra.GamePlay.UI {
    public class PopupHandler : MonoBehaviour {
        public static PopupHandler Instance { get; private set; }
        public const int WEIGHT_LOW = 335;
        public const int WEIGHT_MEDIUM = 1024;
        public const int WEIGHT_HIGH = 3121;
        public const int WEIGHT_CRITICAL = 88761;
        public static readonly Dictionary<PopupPriority, int> priorityWeights = new() {
            { PopupPriority.Low, WEIGHT_LOW },
            { PopupPriority.Medium, WEIGHT_MEDIUM },
            { PopupPriority.High, WEIGHT_HIGH },
            { PopupPriority.Critical, WEIGHT_CRITICAL }
        };
        internal const string SlotPrefab = "Prefabs/GameUI/Popups/Slot/Slot";
        internal const string MessagePrefab = "Prefabs/GameUI/Popups/MessageDisplay";

        [SerializeField, Min(1)] private int maxPopups = 3;
        [SerializeField, Min(0.01f)] private float minDisplayTimeInSeconds = 2f;
        private Transform content;
        private List<PopupSlot<PopupConfig>> slots;
        private readonly PopupScheduler<PopupConfig> scheduler = new();
        private Coroutine processing;

        private void Awake() {
            if (Instance != null && Instance != this) {
                enabled = false;
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnEnable() {
            if (Instance != this) return;
            InitializeSlots();
            processing = StartCoroutine(ProcessQueue());
        }

        private void OnDisable() {
            if (processing != null) StopCoroutine(processing);
            processing = null;
        }

        private void OnDestroy() {
            if (Instance == this) Instance = null;
            if (slots == null) return;
            foreach (var slot in slots) slot.Release();
        }

        private void InitializeSlots() {
            if (slots != null) return;
            content = transform.GetChild(0).GetChild(0);
            slots = new List<PopupSlot<PopupConfig>>();
            for (int i = 0; i < Mathf.Max(1, maxPopups); i++) {
                var slot = Instantiate(Resources.Load<GameObject>(SlotPrefab), content);
                slots.Add(new PopupSlot<PopupConfig>(slot, minDisplayTimeInSeconds, i, DismissPopup));
            }
        }

        public void CreatePopup(PopupConfig config) {
            if (config == null) throw new System.ArgumentNullException(nameof(config));
            scheduler.Enqueue(new PopupData(config));
        }

        private void DismissPopup(PopupData<PopupConfig> popup) {
            popup.Acknowlege();
            if (popup.SlotIndex >= 0) slots[popup.SlotIndex].Clear();
        }

        private IEnumerator ProcessQueue() {
            while (true) {
                scheduler.ProcessSlots(slots, minDisplayTimeInSeconds, scheduler.Dequeue);
                yield return new WaitForSecondsRealtime(1f);
            }
        }

        [Serializable]
        public class PopupConfig {
            public string Text;
            // Seconds since enqueue; infinity requires explicit acknowledgment.
            public float AcknowledgeTime = float.PositiveInfinity;
            public PopupPriority Priority = PopupPriority.Medium;
        }

        public class PopupData<TConfig> where TConfig : PopupConfig {
            public TConfig Config;
            public double VirtualRunTime;
            public DateTime EnqueuedTime;
            public int SlotIndex = -1;
            [JsonProperty] private bool acknowledged;
            [JsonIgnore] public bool IsAcknowledged => acknowledged
                || (DateTime.Now - EnqueuedTime).TotalSeconds >= Config.AcknowledgeTime;

            public PopupData(TConfig config) {
                Config = config;
                EnqueuedTime = DateTime.Now;
            }

            public void Acknowlege() => acknowledged = true;
        }

        public class PopupData : PopupData<PopupConfig> {
            public PopupData(PopupConfig config) : base(config) { }
        }

        public class PopupSlot<TConfig> where TConfig : PopupConfig {
            public PopupData<TConfig> ActivePopup { get; private set; }
            public readonly int Index;
            protected readonly GameObject Slot;
            protected readonly Transform content;
            private readonly Animator animator;
            private readonly float minDisplayTime;
            private readonly bool trackSlotIndex;
            private DateTime displayStartTime;
            private AnimatorAwaitTask transition;

            public PopupSlot(GameObject slot, float minDisplayTime, int index,
                Action<PopupData<TConfig>> acknowledge, bool trackSlotIndex = true) {
                Slot = slot;
                Index = index;
                this.minDisplayTime = minDisplayTime;
                this.trackSlotIndex = trackSlotIndex;
                content = slot.transform.Find("Content");
                animator = slot.GetComponent<Animator>();
                var close = slot.transform.Find("Close")?.GetComponent<Button>();
                close?.onClick.AddListener(() => {
                    if (ActivePopup != null) acknowledge?.Invoke(ActivePopup);
                });
            }

            public void Release() {
                transition?.Disable();
                if (trackSlotIndex && ActivePopup != null) ActivePopup.SlotIndex = -1;
                ActivePopup = null;
                UnityEngine.Object.Destroy(Slot);
            }

            public bool CanClear() => ActivePopup == null
                || (DateTime.Now - displayStartTime).TotalSeconds >= minDisplayTime;

            internal void RestartTimeSlice() => displayStartTime = DateTime.Now;

            public void Clear() => Show(null);

            public void Show(PopupData<TConfig> popup, bool immediate = false) {
                if (trackSlotIndex) {
                    if (ActivePopup != null) ActivePopup.SlotIndex = -1;
                    if (popup != null) popup.SlotIndex = Index;
                }
                ActivePopup = popup;
                displayStartTime = DateTime.Now;
                transition?.Disable();
                animator.SetBool("IsOpen", false);
                if (immediate) SwitchDisplay();
                else {
                    transition = new AnimatorAwaitTask(animator, "Closed", SwitchDisplay);
                    transition.Invoke();
                }
            }

            private void SwitchDisplay() {
                ClearContent();
                if (ActivePopup != null) CreateContent(ActivePopup.Config);
                SegmentedUIEditor.ForceLayoutRefresh(Slot.transform);
                animator.SetBool("IsOpen", ActivePopup != null);
            }

            protected virtual void ClearContent() {
                foreach (Transform child in content) {
                    child.gameObject.SetActive(false);
                    GameObject.Destroy(child.gameObject);
                }
            }

            protected virtual void CreateContent(TConfig config){
                    var message = Resources.Load<GameObject>(MessagePrefab);
                    message = GameObject.Instantiate(message, content);
                    var text = message.GetComponentInChildren<TextMeshProUGUI>();
                    if (text != null) text.text = config.Text;
            }
        }

        // Both popup and tooltip slots use the same weighted virtual-runtime scheduling.
        public class PopupScheduler<TConfig> where TConfig : PopupConfig {
            private readonly Arterra.Utils.PriorityQueue<PopupData<TConfig>, double> queue = new();

            public void Enqueue(PopupData<TConfig> popup) => queue.Enqueue(popup, popup.VirtualRunTime);
            public bool TryDequeue(out PopupData<TConfig> popup) => queue.TryDequeue(out popup, out _);

            public PopupData<TConfig> Dequeue() {
                while (TryDequeue(out var popup))
                    if (!popup.IsAcknowledged) return popup;
                return null;
            }

            public void ProcessSlots(IEnumerable<PopupSlot<TConfig>> slots, float displayTime,
                Func<PopupData<TConfig>> dequeue) {
                foreach (var slot in slots) {
                    if (!slot.CanClear()) continue;
                    var previous = slot.ActivePopup;
                    if (previous != null) {
                        previous.VirtualRunTime += displayTime * WEIGHT_MEDIUM / (double)priorityWeights[previous.Config.Priority];
                        Enqueue(previous);
                    }
                    var next = dequeue();
                    if (previous != next) slot.Show(next);
                    else slot.RestartTimeSlice();
                }
            }
        }
    }

    public enum PopupPriority { Low, Medium, High, Critical }
}