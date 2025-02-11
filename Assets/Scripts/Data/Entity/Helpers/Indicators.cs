using UnityEngine;
using UnityEngine.Pool;
using static EntityManager;
using WorldConfig.Generation.Entity;
using WorldConfig;
using Unity.Mathematics;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine.UI;

namespace WorldConfig.Gameplay{
    /// <summary> A collection of settings describing optional statistics that can be displayed in the game.
    /// These statistics are optional and can be toggled on or off; viewing certain statistics may
    /// break immersion or provide an unfair advantage. </summary>
    [System.Serializable]
    public struct Statistics{
        /// <summary> Whether to display the entity's statistics like health, hunger, etc. in the game.
        /// Whether an entity displays is able to display its statistics is up to the entity's implementation. </summary>
        public bool DisplayEntityStats; //false
        /// <summary> Whether to display the pop-up damage dealt to an entity in the game. </summary>
        public bool DisplayEntityDamage; //true
    }
}
public static class Indicators
{
    public static WorldConfig.Gameplay.Statistics Stats => Config.CURRENT.GamePlay.Statistics;
    private static ObjectPool<GameObject> TextIndicators;
    private static GameObject TextIndicator;
    private static GameObject BarIndicator;
    private static float decayTime = 2f;
    public static void Initialize(){
        TextIndicator = Resources.Load<GameObject>("Prefabs/GameUI/IndicatorText");
        BarIndicator = Resources.Load<GameObject>("Prefabs/GameUI/EntityStats");
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

    public static void SetupIndicators(GameObject entity){
        GameObject.Instantiate(BarIndicator, entity.transform);
    }

    public static void UpdateIndicators(GameObject entity, object vitality = null, object path = null){
        GameObject stats = entity.transform.Find("EntityStats(Clone)")?.gameObject;
        if(stats == null) return;
        if(Stats.DisplayEntityStats != stats.activeSelf) 
            stats.SetActive(Stats.DisplayEntityStats);
        if(!stats.activeSelf) return;

        if(vitality != null) {
            Image healthSlider = stats?.transform.Find("HealthBar").GetComponent<Image>();
            if(healthSlider != null) healthSlider.fillAmount = ((Vitality)vitality).healthPercent;
        } if(path != null) {
            //Maybe Implement path indicator(s) ?
        }
    }

    public static GameObject DisplayPopupText(string text, float3 posGS){
        if(!Stats.DisplayEntityDamage) return null;
        GameObject indicator = TextIndicators.Get();
        Quaternion rot = Quaternion.Euler(0f, UnityEngine.Random.Range(0, 360f), 0f);
        indicator.transform.SetPositionAndRotation(CPUMapManager.GSToWS(posGS), rot);
        indicator.GetComponent<TextMesh>().text = text.ToString();
        indicator.GetComponent<GravitySimulator>().velocity = 
        new Vector3(UnityEngine.Random.Range(-1, 1), 1, UnityEngine.Random.Range(-1, 1)) * 5;
        ReleaseAfterDelay(indicator, decayTime);
        return indicator;
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
