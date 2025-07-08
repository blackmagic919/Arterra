using UnityEngine;
using UnityEngine.Pool;
using static EntityManager;
using WorldConfig.Generation.Entity;
using WorldConfig;
using Unity.Mathematics;
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
    public static ObjectPool<GameObject> ItemSlots;
    public static ObjectPool<GameObject> StackableItems;
    public static ObjectPool<GameObject> HolderItems;
    public static GameObject SelectionIndicator;
    public static GameObject DamageIndicator;
    public static GameObject BarIndicator;
    public static void Initialize()
    {
        static void OnActivate(GameObject indicator) => indicator.SetActive(true);
        static void OnDeactivate(GameObject indicator){
            indicator.transform.SetParent(null, false);
            indicator.SetActive(false);
        }
        static void OnDestroy(GameObject indicator) {
#if UNITY_EDITOR
            GameObject.DestroyImmediate(indicator);
#else
            GameObject.Destroy(indicator);
#endif
        }

        SelectionIndicator = Object.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Selection"));
        DamageIndicator = Resources.Load<GameObject>("Prefabs/GameUI/DamageEffect");
        BarIndicator = Resources.Load<GameObject>("Prefabs/GameUI/EntityStats");
        SelectionIndicator.SetActive(false);

        GameObject ItemSlot = Resources.Load<GameObject>("Prefabs/GameUI/Inventory/Slot");
        GameObject StackableItem = Resources.Load<GameObject>("Prefabs/GameUI/Inventory/StackableItem");
        GameObject HolderItem = Resources.Load<GameObject>("Prefabs/GameUI/Inventory/HolderItem");

        ItemSlots = new ObjectPool<GameObject>(() =>
        {
            return GameObject.Instantiate(ItemSlot);
        }, OnActivate, OnDeactivate, OnDestroy, true, 25, 100);
        StackableItems = new ObjectPool<GameObject>(() =>
        {
            return GameObject.Instantiate(StackableItem);
        }, OnActivate, OnDeactivate, OnDestroy, true, 5, 40);
        HolderItems = new ObjectPool<GameObject>(() => {
            return GameObject.Instantiate(HolderItem);
        }, OnActivate, OnDeactivate, OnDestroy, true, 5, 20);
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
            Vitality vitals = (Vitality)vitality;
            Image healthSlider = stats?.transform.Find("HealthBar").GetComponent<Image>();
            if(healthSlider != null) healthSlider.fillAmount = vitals.healthPercent;
            Image breathSlider = stats?.transform.Find("BreathBar").GetComponent<Image>();
            if(breathSlider != null) breathSlider.fillAmount = math.fmod(vitals.breathPercent, 1);;
            if(vitals.invincibility > 0) return;
            Image damageSlider = stats?.transform.Find("DamageBar").GetComponent<Image>();
            damageSlider.fillAmount = math.max(vitals.healthPercent, damageSlider.fillAmount - 0.01f);
        } if(path != null) {
            //Maybe Implement path indicator(s) ?
        }
    }

    public static GameObject DisplayDamageParticle(float3 posGS, float3 eulerDir = default){
        if(!Stats.DisplayEntityDamage) return null;
        GameObject indicator = GameObject.Instantiate(DamageIndicator);

        Quaternion rot;
        if(math.all(eulerDir == default)) rot = UnityEngine.Random.rotation;
        else rot = Quaternion.LookRotation(eulerDir);
        indicator.transform.SetPositionAndRotation(MapStorage.CPUMapManager.GSToWS(posGS), rot);
        return indicator;
    }


    public static void OnDrawGizmos(){
        if(ESTree.Length == 0) return;
        /*for(int i = 1; i < ESTree.Length; i++){
            STree.TreeNode region = ESTree.tree[i];
            Vector3 center = CPUMapManager.GSToWS((float3)(region.bounds.min + region.bounds.max) / 2);
            Vector3 size = (float3)(region.bounds.max - region.bounds.min);
            Gizmos.DrawWireCube(center, size);
        }*/
        int[] entities = new int[Config.CURRENT.Generation.Entities.Reg.Count];
        foreach(Entity entity in EntityHandler){
            entities[entity.info.entityType]++;
            entity.OnDrawGizmos();
        } for(int i = 0; i < entities.Length; i++){
            if(entities[i] == 0) continue;
            //Debug.Log($"Entity Type {i}: {entities[i]}");
        }
    }
}
