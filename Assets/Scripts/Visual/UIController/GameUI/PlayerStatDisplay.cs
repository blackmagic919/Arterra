using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using WorldConfig;

public static class PlayerStatDisplay
{
    private static GameObject HealthBar;
    private static Image breathStat;
    private static Image healthStat;
    private static Image damageStat;
    public static void Initialize()
    {
        HealthBar = GameObject.Instantiate(Indicators.BarIndicator, GameUIManager.UIHandle.transform);
        RectTransform rect = HealthBar.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(-0.025f, 0); rect.anchorMax = new Vector2(0.4f, 0.075f);
        rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one; 
        healthStat = HealthBar.transform.Find("HealthBar").GetComponent<Image>();
        damageStat = HealthBar.transform.Find("DamageBar").GetComponent<Image>();
        breathStat = HealthBar.transform.Find("BreathBar").GetComponent<Image>();

        Config.CURRENT.System.GameplayModifyHooks.Add("Gamemode:Invulnerability", ToggleInvulnerability);
        ToggleInvulnerability();
    }

    public static void ToggleInvulnerability(){
        if((!Config.CURRENT.GamePlay.Gamemodes.value.Invulnerability) == HealthBar.activeSelf)
            return;
        HealthBar.SetActive(!Config.CURRENT.GamePlay.Gamemodes.value.Invulnerability);
    }

    public static void UpdateIndicator(PlayerVitality vitality){
        if(!HealthBar.activeSelf) return;
        healthStat.fillAmount = vitality.healthPercent;
        breathStat.fillAmount = math.fmod(vitality.breathPercent, 1);
        if(vitality.Invincibility <= 0) damageStat.fillAmount = math.max(vitality.healthPercent, damageStat.fillAmount - 0.01f);
    }
}
