using System;
using System.Collections.Generic;
using Arterra.Core.Events;
using Arterra.GamePlay.UI;
using Newtonsoft.Json;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class NameTagSettings : IBehaviorSetting {
        /// <summary> How far above the entity's origin the name tag is displayed, in game units. </summary>
        public float VerticalOffset = 2.5f;

        public object Clone() {
            return new NameTagSettings {
                VerticalOffset = VerticalOffset
            };
        }
    }

    /// <summary>
    /// Gives an entity a player-assignable name tag. The name is displayed as a world-space
    /// billboard above the entity. The player can rename the entity by interacting bare-handed
    /// (no item held) with it.
    /// </summary>
    public class NameTagBehavior : IBehavior {
        [JsonIgnore] private NameTagSettings settings;

        /// <summary> The current name assigned to this entity. Persisted via JSON serialization. </summary>
        [JsonProperty] public string EntityName = "";

        private BehaviorEntity.Animal self;
        private GameObject nameTagObject;
        private TMP_Text nameText;

        /// <summary> Sets the entity's display name and updates the world-space name tag. </summary>
        public void SetName(string name) {
            EntityName = name ?? "";
            if (nameText != null) nameText.text = EntityName;
            if (nameTagObject != null) nameTagObject.SetActive(!string.IsNullOrEmpty(EntityName));
        }

        private void OnInteract(object actor, object target, object cxt) {
            // Only respond to bare-hand interaction (cxt is the held item, null means no item)
            if (cxt != null) return;
            if (self == null) return;
            NameTagBehavior captured = this;
            EntityManager.AddHandlerEvent(() => NameTagDialog.Show(captured.EntityName, captured.SetName));
        }

        private void SetupDisplay(BehaviorEntity.Animal self) {
            // World-space canvas parented to the entity controller's GameObject
            nameTagObject = new GameObject("NameTag");
            nameTagObject.transform.SetParent(self.controller.gameObject.transform, false);
            nameTagObject.transform.localPosition = new Vector3(0f, settings.VerticalOffset, 0f);

            Canvas canvas = nameTagObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            // Canvas is 200x50 units scaled to 0.005 → 1m x 0.25m in world space
            RectTransform rect = nameTagObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(200f, 50f);
            nameTagObject.transform.localScale = new Vector3(0.005f, 0.005f, 0.005f);

            // Semi-transparent backing panel
            Image bg = nameTagObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);

            // Name label
            GameObject textObj = new GameObject("NameText");
            textObj.transform.SetParent(nameTagObject.transform, false);
            nameText = textObj.AddComponent<TextMeshProUGUI>();
            nameText.text = EntityName;
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.fontSize = 24f;
            nameText.color = Color.white;
            nameText.fontStyle = FontStyles.Bold;

            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(4f, 2f);
            textRect.offsetMax = new Vector2(-4f, -2f);

            // Only show when a name has been assigned
            nameTagObject.SetActive(!string.IsNullOrEmpty(EntityName));
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: NameTag Behavior requires AnimalSettings to have NameTagSettings");
            this.self = self;
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Interact, OnInteract);
            SetupDisplay(self);
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: NameTag Behavior requires AnimalSettings to have NameTagSettings");
            this.self = self;
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Interact, OnInteract);
            // EntityName is already restored from JSON before Deserialize is called
            SetupDisplay(self);
        }

        public void UpdateController(BehaviorEntity.Animal self, BehaviorEntity.AnimalController controller) {
            if (nameTagObject == null || !nameTagObject.activeSelf) return;
            if (Camera.main == null) return;
            // Billboard: keep facing the camera regardless of entity rotation
            nameTagObject.transform.rotation = Camera.main.transform.rotation;
        }

        public void Disable(BehaviorEntity.Animal self) {
            // Actual GameObject cleanup is handled by AnimalController.Dispose()
            this.self = null;
            nameTagObject = null;
            nameText = null;
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) { }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(NameTagSettings), new NameTagSettings());
        }
    }
}
