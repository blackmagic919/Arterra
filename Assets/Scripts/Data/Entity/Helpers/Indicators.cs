using UnityEngine;
using UnityEngine.Pool;
using static EntityManager;
using WorldConfig.Generation.Entity;
using WorldConfig;
using Unity.Mathematics;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine.UI;

public static class Indicators
{
    private static ObjectPool<GameObject> TextIndicators;
    private static GameObject TextIndicator;
    private static GameObject BarIndicator;
    private static float decayTime = 2f;
    public static void Initialize(){
        TextIndicator = Resources.Load<GameObject>("Prefabs/GameUI/IndicatorText");
        BarIndicator = Resources.Load<GameObject>("Prefabs/GameUI/IndicatorBar");
        TextIndicators = new ObjectPool<GameObject>(() => {
            return GameObject.Instantiate(TextIndicator);
        }, indicator => {
            indicator.SetActive(true);
        }, indicator => {
            indicator.SetActive(false);
        }, indicator => {
            GameObject.Destroy(indicator);
        }, true, 25, 100);
    }

    public static GameObject DisplayPopupText(string text, float3 posGS){
        GameObject indicator = TextIndicators.Get();
        Quaternion rot = Quaternion.Euler(0f, UnityEngine.Random.Range(0, 360f), 0f);
        indicator.transform.SetPositionAndRotation(CPUMapManager.GSToWS(posGS), rot);
        indicator.GetComponent<TextMesh>().text = text.ToString();
        indicator.GetComponent<GravitySimulator>().velocity = 
        new Vector3(UnityEngine.Random.Range(-1, 1), 1, UnityEngine.Random.Range(-1, 1)) * 5;
        ReleaseAfterDelay(indicator, decayTime);
        return indicator;
    }

    public static void SetupIndicators(GameObject entity){
        GameObject.Instantiate(BarIndicator, entity.transform);
    }

    public static void UpdateIndicators(GameObject entity, object vitality = null, object path = null){
        if(vitality != null) {
            Image healthSlider = entity.transform.Find("IndicatorBar(Clone)")?.Find("HealthBar")?.GetComponent<Image>();
            if(healthSlider != null) healthSlider.fillAmount = ((Vitality)vitality).healthPercent;
        } if(path != null) {
            //Maybe Implement path indicator(s) ?
        }
    }

    public static async void ReleaseAfterDelay(GameObject obj, float delay)
    {
        await Task.Delay((int)(delay * 1000)); // Convert seconds to milliseconds
        if(obj != null) TextIndicators.Release(obj);
    }

    public static void OnDrawGizmos(){
        if(ESTree.Length == 0) return;
        
        int[] entities = new int[Config.CURRENT.Generation.Entities.Reg.value.Count];
        foreach(Entity entity in EntityHandler){
            entities[entity.info.entityType]++;
            entity.OnDrawGizmos();
        } for(int i = 0; i < entities.Length; i++){
            if(entities[i] == 0) continue;
            //Debug.Log($"Entity Type {i}: {entities[i]}");
        }
    }
}
