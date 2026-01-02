using UnityEngine;
using UnityEngine.Pool;
using static EntityManager;
using Arterra.Configuration.Generation.Entity;
using Arterra.Configuration;
using Unity.Mathematics;
using UnityEngine.UI;

namespace Arterra.Configuration.Gameplay{
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
public class Indicators
{
    public static Arterra.Configuration.Gameplay.Statistics Stats => Config.CURRENT.GamePlay.Statistics;
    public static ObjectPool<GameObject> ItemSlots;
    public static ObjectPool<GameObject> TransparentSlots;
    public static ObjectPool<GameObject> StackableItems;
    public static ObjectPool<GameObject> HolderItems;
    public static ObjectPool<GameObject> ToolItems;
    public static ObjectPool<GameObject> RecipeSelections;
    public static GameObject SelectionIndicator;
    public static GameObject DamageIndicator;
    public static GameObject BarIndicator;
    public static void Initialize() {
        static void OnActivate(GameObject indicator) => indicator.SetActive(true);
        static void OnDeactivate(GameObject indicator) {
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
        GameObject ToolItem = Resources.Load<GameObject>("Prefabs/GameUI/Inventory/ToolItem");
        GameObject RecipeSelection = Resources.Load<GameObject>("Prefabs/GameUI/Crafting/RecipeSelection");
        GameObject TransparentSlot = Resources.Load<GameObject>("Prefabs/GameUI/Crafting/TransparentSlot");

        ItemSlots = new ObjectPool<GameObject>(() => {
            return GameObject.Instantiate(ItemSlot);
        }, OnActivate, OnDeactivate, OnDestroy, true, 25, 100);
        StackableItems = new ObjectPool<GameObject>(() => {
            return GameObject.Instantiate(StackableItem);
        }, OnActivate, OnDeactivate, OnDestroy, true, 5, 40);
        HolderItems = new ObjectPool<GameObject>(() => {
            return GameObject.Instantiate(HolderItem);
        }, OnActivate, OnDeactivate, OnDestroy, true, 5, 20);
        ToolItems = new ObjectPool<GameObject>(() => {
            return GameObject.Instantiate(ToolItem);
        }, OnActivate, OnDeactivate, OnDestroy, true, 5, 20);
        RecipeSelections = new ObjectPool<GameObject>(() => {
            return GameObject.Instantiate(RecipeSelection);
        }, OnActivate, OnDeactivate, OnDestroy, true, 5, 20);
        TransparentSlots = new ObjectPool<GameObject>(() => {
            return GameObject.Instantiate(TransparentSlot);
        }, OnActivate, OnDeactivate, OnDestroy, true, 5, 16);
    }

    public static GameObject DisplayDamageParticle(float3 posGS, float3 eulerDir = default){
        if(!Stats.DisplayEntityDamage) return null;
        GameObject indicator = GameObject.Instantiate(DamageIndicator);

        Quaternion rot;
        if(math.all(eulerDir == default)) rot = UnityEngine.Random.rotation;
        else rot = Quaternion.LookRotation(eulerDir);
        indicator.transform.SetPositionAndRotation(Arterra.Core.Storage.CPUMapManager.GSToWS(posGS), rot);
        return indicator;
    }

    //Audio
    public static void PlayWaterSplash(Entity entity, float weight = 1) {
        const int small = 0; const int medium = 1;  const int large = 2; 
        float strength = math.length(entity.velocity);
        if (strength <= 4) return;
        strength *= weight;

        int type = strength < 7.5 ? small : strength < 20 ? medium : large;
        FMOD.Studio.EventInstance evnt = AudioManager.CreateEvent(AudioEvents.Action_WaterSplash, entity.position);
        evnt.setParameterByName("Splash Strength", (float)type);
    }

    private GameObject controller;
    private Entity entity;
    private MinimalVitality vitality;
    private bool InWater;

    public Indicators(GameObject controller, Entity entity, MinimalVitality vitality = null){
       this.entity = entity;
       this.vitality = vitality;
       this.controller = controller;
       this.InWater = false;
       GameObject.Instantiate(BarIndicator, controller.transform);
       entity.eventCtrl.AddEventHandler<float>(Arterra.Core.Events.GameEvent.Entity_InLiquid, OnEnterWater);
       entity.eventCtrl.AddEventHandler<float>(Arterra.Core.Events.GameEvent.Entity_InGas, OnEnterGas);
    }

    public void Update(){
        GameObject stats = controller.transform.Find("EntityStats(Clone)")?.gameObject;
        if(stats == null) return;
        if(Stats.DisplayEntityStats != stats.activeSelf) 
            stats.SetActive(Stats.DisplayEntityStats);
        if(!stats.activeSelf) return;

        if(vitality != null) {
            Image healthSlider = stats?.transform.Find("HealthBar").GetComponent<Image>();
            if(healthSlider != null) healthSlider.fillAmount = vitality.healthPercent;
            Image breathSlider = stats?.transform.Find("BreathBar").GetComponent<Image>();
            if(breathSlider != null) breathSlider.fillAmount = math.fmod(vitality.breathPercent, 1);;
            if(vitality.invincibility > 0) return;
            Image damageSlider = stats?.transform.Find("DamageBar").GetComponent<Image>();
            damageSlider.fillAmount = math.max(vitality.healthPercent, damageSlider.fillAmount - 0.01f);
        }
    }

    private void OnEnterWater(object source, object target, ref float density) {
        if (InWater) return;
        InWater = true;
        float weight = vitality != null ? vitality.weight : 1;
        AddHandlerEvent(() => PlayWaterSplash(entity, weight));
    }


    private void OnEnterGas(object source, object target, ref float density) {
        if (!InWater) return;
        InWater = false;
        float weight = vitality != null ? vitality.weight : 1;
        AddHandlerEvent(() => PlayWaterSplash(entity, weight));
    }

    public void Release() {
        //Release circular references
        controller = null;
        vitality = null;
        entity = null;
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
        foreach(Entity entity in EntityIndex.Values){
            entities[entity.info.entityType]++;
            entity.OnDrawGizmos();//
        } for(int i = 0; i < entities.Length; i++){
            if(entities[i] == 0) continue;
            //Debug.Log($"Entity Type {i}: {entities[i]}");
        }
    }
}
