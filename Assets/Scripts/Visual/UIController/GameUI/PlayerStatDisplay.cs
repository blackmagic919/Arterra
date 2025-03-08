using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

public static class PlayerStatDisplay
{
    private static GameObject HealthBar;
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
    }

    public static void UpdateIndicator(PlayerVitality vitality){
        healthStat.fillAmount = vitality.healthPercent;
        if(vitality.Invincibility > 0) return;
        damageStat.fillAmount = math.max(vitality.healthPercent, damageStat.fillAmount - 0.01f);
    }
}
