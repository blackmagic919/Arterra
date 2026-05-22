using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using Arterra.Configuration;
using Arterra.GamePlay;
using Arterra.Data.Entity.Behavior;

namespace Arterra.GamePlay.UI {
    public static class PlayerStatDisplay {
        private static GameObject HealthBar;
        private static Image breathStat;
        private static Image healthStat;
        private static Image damageStat;
        public static void Initialize() {
            HealthBar = GameObject.Instantiate(Indicators.BarIndicator, GameUIManager.UIHandle.transform);
            RectTransform rect = HealthBar.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(-0.025f, 0); rect.anchorMax = new Vector2(0.4f, 0.075f);
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            healthStat = HealthBar.transform.Find("HealthBar").GetComponent<Image>();
            damageStat = HealthBar.transform.Find("DamageBar").GetComponent<Image>();
            breathStat = HealthBar.transform.Find("BreathBar").GetComponent<Image>();

            Config.CURRENT.System.AddHook("Gamemode:Invulnerability", ToggleInvulnerability);
            object vulnerability = Config.CURRENT.GamePlay.Gamemodes.value.Invulnerability;
            ToggleInvulnerability(ref vulnerability);
        }

        public static void ToggleInvulnerability(ref object vulnerability) {
            bool isVulnerable = (bool)vulnerability;
            if ((!isVulnerable) == HealthBar.activeSelf)
                return;
            HealthBar.SetActive(!isVulnerable);
        }

        public static void UpdateIndicator(BehaviorEntity.Animal data) {
            if (!HealthBar.activeSelf) return;
            if (!data.Is(out VitalityBehavior vit)) return;
            healthStat.fillAmount = vit.healthPercent;
            if (!data.Is(out MapInteractBehavior map)) return;
            breathStat.fillAmount = math.fmod(map.breathPercent, 1);
            if (vit.invincibility <= 0) damageStat.fillAmount = math.max(vit.healthPercent, damageStat.fillAmount - 0.01f);
        }
    }
}
