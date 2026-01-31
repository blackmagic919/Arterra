using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Arterra.GamePlay.UI {
    [RequireComponent(typeof(RectTransform))]
    public class HoverMoveButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
        public RectTransform buttonRect;
        public Button button;
        public Vector2 moveDelta = new Vector2(-75f, 0);
        public float moveSpeed = 5f;

        private Vector2 originalPos;
        private Vector2 targetPos;
        private bool isMoving;
        private bool isLocked;

        void Awake() {
            originalPos = buttonRect.anchoredPosition;
            targetPos = originalPos;
            isLocked = false;
        }

        public void AddClickListener(UnityEngine.Events.UnityAction cb) {
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(cb);
        }

        public void Lock() {
            isLocked = true;
            buttonRect.anchoredPosition = targetPos;
        }

        public void Unlock() => isLocked = false;

        void Update() {
            if (!isMoving) return;
            if (isLocked) return;

            buttonRect.anchoredPosition = Vector2.Lerp(
                buttonRect.anchoredPosition, targetPos,
                Time.deltaTime * moveSpeed
            );

            // stop when close enough
            if (Vector2.Distance(buttonRect.anchoredPosition, targetPos) < 0.1f) {
                buttonRect.anchoredPosition = targetPos;
                isMoving = false;
            }
        }

        public void OnPointerEnter(PointerEventData eventData) {
            targetPos = originalPos + moveDelta;
            isMoving = true;
        }

        public void OnPointerExit(PointerEventData eventData) {
            targetPos = originalPos;
            isMoving = true;
        }
    }
}