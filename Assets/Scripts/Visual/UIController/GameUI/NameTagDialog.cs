using System;
using Arterra.GamePlay.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Arterra.GamePlay.UI {
    /// <summary>
    /// A screen-space modal dialog that prompts the player to enter or change an entity's name.
    /// Shown when the player interacts bare-handed with an entity that has a NameTagBehavior.
    /// </summary>
    public static class NameTagDialog {
        private static GameObject dialogRoot;
        private static TMP_InputField inputField;
        private static Action<string> onConfirm;
        private static bool initialized = false;

        /// <summary>
        /// Lazily builds the dialog UI from code. Called automatically on first Show().
        /// Requires GameUIManager.UIHandle to already be set (guaranteed during gameplay).
        /// </summary>
        public static void Initialize() {
            if (initialized) return;

            // Separate Canvas so we can pin sortingOrder on top of everything
            dialogRoot = new GameObject("NameTagDialog");
            dialogRoot.transform.SetParent(GameUIManager.UIHandle.transform, false);

            Canvas canvas = dialogRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            dialogRoot.AddComponent<GraphicRaycaster>();

            RectTransform rootRect = dialogRoot.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            // Full-screen dimming overlay that also eats stray clicks
            GameObject overlay = new GameObject("Overlay");
            overlay.transform.SetParent(dialogRoot.transform, false);
            RectTransform overlayRect = overlay.AddComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlay.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.40f);

            // Centred dialog panel
            GameObject panel = new GameObject("Panel");
            panel.transform.SetParent(dialogRoot.transform, false);
            RectTransform panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.35f, 0.40f);
            panelRect.anchorMax = new Vector2(0.65f, 0.60f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            panel.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.12f, 0.96f);

            // Title label
            CreateLabel(panel, "Title",
                anchorMin: new Vector2(0f, 0.68f), anchorMax: new Vector2(1f, 1f),
                offset: new Vector4(8f, 0f, -8f, -4f),
                text: "Name this creature", size: 18f, color: Color.white);

            // Input field – reuse the existing SegmentedUI text-input prefab
            GameObject inputPrefab = Resources.Load<GameObject>("Prefabs/SegmentedUI/Text_Input")
                                  ?? Resources.Load<GameObject>("Prefabs/PaginatedUI/Text_Input");
            if (inputPrefab != null) {
                GameObject inputObj = UnityEngine.Object.Instantiate(inputPrefab, panel.transform);
                RectTransform inputRect = inputObj.GetComponent<RectTransform>();
                inputRect.anchorMin = new Vector2(0.04f, 0.32f);
                inputRect.anchorMax = new Vector2(0.96f, 0.66f);
                inputRect.offsetMin = Vector2.zero;
                inputRect.offsetMax = Vector2.zero;
                inputField = inputObj.GetComponent<TMP_InputField>();
            }

            if (inputField != null) {
                inputField.characterLimit = 32;
                inputField.onSubmit.AddListener(_ => Confirm());
            }

            // Confirm / Cancel buttons
            CreateButton(panel, "Confirm",
                anchorMin: new Vector2(0.04f, 0.04f), anchorMax: new Vector2(0.48f, 0.30f),
                bgColor: new Color(0.18f, 0.55f, 0.18f, 1f), onClick: Confirm);

            CreateButton(panel, "Cancel",
                anchorMin: new Vector2(0.52f, 0.04f), anchorMax: new Vector2(0.96f, 0.30f),
                bgColor: new Color(0.55f, 0.18f, 0.18f, 1f), onClick: Close);

            dialogRoot.SetActive(false);
            initialized = true;
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Displays the naming dialog. Blocks all other input while open.
        /// <paramref name="currentName"/> pre-fills the input field.
        /// <paramref name="callback"/> is invoked with the submitted name on confirm,
        /// or with the original name when canceled (no-op from NameTagBehavior.SetName).
        /// </summary>
        public static void Show(string currentName, Action<string> callback) {
            if (!initialized) Initialize();

            onConfirm = callback;
            if (inputField != null) {
                inputField.text = currentName;
            }
            dialogRoot.SetActive(true);
            if (inputField != null) {
                inputField.Select();
                inputField.ActivateInputField();
            }

            // Unlock cursor so the player can click and type
            InputPoller.AddStackPoll(
                new ActionBind("Frame:NameTagDialog", _ => InputPoller.SetCursorLock(false)),
                "CursorLock");

            // Block all other keybinds; allow Escape/Pause to cancel
            InputPoller.AddKeyBindChange(() => {
                InputPoller.AddContextFence("NameTagDialog", "1.0::Menu", ActionBind.Exclusion.ExcludeAll);
                InputPoller.AddBinding(new ActionBind("Pause", _ => Close()), "NameTagDialog:CLS", "1.0::Menu");
            });
        }

        /// <summary> Closes the dialog without saving any changes. </summary>
        public static void Close() {
            if (!initialized || !dialogRoot.activeSelf) return;

            dialogRoot.SetActive(false);
            onConfirm = null;

            InputPoller.RemoveStackPoll("Frame:NameTagDialog", "CursorLock");
            InputPoller.AddKeyBindChange(() => InputPoller.RemoveContextFence("NameTagDialog", "1.0::Menu"));
        }

        // ── Internals ─────────────────────────────────────────────────────────────

        private static void Confirm() {
            string name = inputField != null ? inputField.text.Trim() : "";
            onConfirm?.Invoke(name);
            Close();
        }

        private static void CreateButton(GameObject parent, string label,
                Vector2 anchorMin, Vector2 anchorMax, Color bgColor, Action onClick) {
            GameObject btn = new GameObject(label + "Btn");
            btn.transform.SetParent(parent.transform, false);

            RectTransform rect = btn.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            btn.AddComponent<Image>().color = bgColor;
            btn.AddComponent<Button>().onClick.AddListener(() => onClick());

            CreateLabel(btn, "Label",
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                offset: Vector4.zero,
                text: label, size: 14f, color: Color.white);
        }

        /// <param name="offset"> (left, bottom, right, top) offset from anchors in pixels. </param>
        private static void CreateLabel(GameObject parent, string name,
                Vector2 anchorMin, Vector2 anchorMax, Vector4 offset,
                string text, float size, Color color) {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent.transform, false);

            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(offset.x, offset.y);
            rect.offsetMax = new Vector2(offset.z, offset.w);

            TMP_Text tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = size;
            tmp.color = color;
        }
    }
}
