using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Configuration.Gameplay;
using Arterra.Core.Events;
using Arterra.Core.Storage;
using Arterra.Data.Entity;
using Arterra.Engine.Audio;
using Newtonsoft.Json;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;
using static EntityManager;

namespace Arterra.Configuration.Gameplay{
    /// <summary> A collection of settings describing optional statistics that can be displayed in the game.
    /// These statistics are optional and can be toggled on or off; viewing certain statistics may
    /// break immersion or provide an unfair advantage. </summary>
    [System.Serializable]
    public struct Statistics{
        /// <summary> Whether to display the entity's statistics like health, hunger, etc. in the game.
        /// Whether an entity displays is able to display its statistics is up to the entity's implementation. </summary>
        [UIModifiable(CallbackName = "Statistics:Display")]
        public bool DisplayEntityStats; //false
        /// <summary> Whether to display the pop-up damage dealt to an entity in the game. </summary>
        public bool DisplayEntityDamage; //true
    }

    public interface IEffect {
        public string Icon {get;}
    }
}

public static class Indicators {
    private static Statistics Stats => Config.CURRENT.GamePlay.Statistics;
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
        static void OnDeactivateStackable(GameObject indicator) {
            indicator.transform.GetComponentInChildren<TMPro.TextMeshProUGUI>().text = "";
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
        BarIndicator = Resources.Load<GameObject>("Prefabs/GameUI/Stats/EntityStats");
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

    public static GameObject DisplayDamageParticle(float3 posGS, float3 eulerDir = default){
        if(!Stats.DisplayEntityDamage) return null;
        GameObject indicator = GameObject.Instantiate(Indicators.DamageIndicator);

        Quaternion rot;
        if(math.all(eulerDir == default)) rot = UnityEngine.Random.rotation;
        else rot = Quaternion.LookRotation(eulerDir);
        indicator.transform.SetPositionAndRotation(Arterra.Core.Storage.CPUMapManager.GSToWS(posGS), rot);
        return indicator;
    }

    //Audio
    public static void PlayWaterSplash(Entity entity, float weight = 1) {
        const int small = 0; const int medium = 1;  const int large = 2; 
        if (entity == null || !entity.active) return;
        float strength = math.length(entity.velocity);
        if (strength <= 4) return;
        strength *= weight;

        int type = strength < 7.5 ? small : strength < 20 ? medium : large;
        FMOD.Studio.EventInstance evnt = AudioManager.CreateEvent(AudioEvents.Action_WaterSplash, entity.position);
        evnt.setParameterByName("Splash Strength", (float)type);
    }
}

namespace Arterra.Data.Entity.Behavior {
//ToDo: Support multiple paths for animator
public class InidcatorsBehavior : ISpeciesBehavior {
    private static Statistics Stats => Config.CURRENT.GamePlay.Statistics;
    private VitalityBehavior vit;
    private MapInteractBehavior map;
    [JsonIgnore] public GameObject stats;
    [JsonIgnore] public Image breathStat;
    [JsonIgnore] public Image healthStat;
    [JsonIgnore] public Image damageStat;
    [JsonIgnore] public Transform effectWindow;
    private Dictionary<string, (int, Transform)> Effects;
    private bool active;

    private GameObject controller;
    private BehaviorEntity.Animal self;
    private bool InWater;
    private void OnEnterWater(object source, object target, object density) {
        if (InWater) return;
        InWater = true;
        float weight = vit != null ? vit.weight : 1;
        AddHandlerEvent(() => Indicators.PlayWaterSplash(self, weight));
    }


    private void OnEnterGas(object source, object target, object density) {
        if (!InWater) return;
        InWater = false;
        float weight = vit != null ? vit.weight : 1;
        AddHandlerEvent(() => Indicators.PlayWaterSplash(self, weight));
    }

    private void OnDamaged(object self, object attacker, object cxt) {
        RefTuple<(float damage, float3 kb)> data = cxt as RefTuple<(float damage, float3 kb)>;
        if (data.Value.damage == 0) return;
        Indicators.DisplayDamageParticle((self as Entity).position, data.Value.kb);
    }

    private void SetupEffectWindow() {
        if (effectWindow == null) return;
        Effects ??= new Dictionary<string, (int, Transform)>();
        if (Effects.Count > 0) {
            foreach (var effect in Effects.Values)
                GameObject.Destroy(effect.Item2.gameObject);
            Effects.Clear();
        }
        foreach(var behavior in self.Behaviors) {
            if (behavior is not IEffect behaviorEffect) continue;
            AddEffect(behaviorEffect);
        }
    }

    private void AddEffect(IEffect behaviorEffect) {
        if (Effects == null) return;
        string iconName = behaviorEffect.Icon;
        if (Effects.TryGetValue(iconName, out var effectData)) {
            Effects[iconName] = (effectData.Item1 + 1, effectData.Item2);
        } else {
            var icon = Config.CURRENT.Generation.Textures.Retrieve(iconName);
            var slot = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Stats/EffectSlot"), effectWindow).transform;
            slot.Find("Item").GetComponent<Image>().sprite = icon.self;
            Effects[iconName] = (1, slot.transform);
        } SetCount(iconName);
    }

    private void SetCount(string iconName) {
        if (!Effects.TryGetValue(iconName, out (int count, Transform slot)effect))
            return;
        TextMeshProUGUI multiplier = effect.slot?.Find("Multiplier")?.GetComponent<TextMeshProUGUI>();
       if(multiplier != null) multiplier.text = effect.count <= 1 ? null : $"x{Effects[iconName].Item1}";
    }

    private void OnAddBehavior(object source, object behavior) {
        if (behavior is not IEffect behaviorEffect) return;
        AddEffect(behaviorEffect);
    }

    private void OnRemoveBehavior(object source, object behavior) {
        if (behavior is not IEffect behaviorEffect) return;
        if (Effects == null) return;

        string iconName = behaviorEffect.Icon;
        if (!Effects.TryGetValue(iconName, out var effectData))
            return;

        if (effectData.Item1 > 1) {
            Effects[iconName] = (effectData.Item1 - 1, effectData.Item2);
            SetCount(iconName);
            return;
        }

        GameObject.Destroy(effectData.Item2.gameObject);
        Effects.Remove(iconName);
    }

    public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
        if (!self.Is(out vit)) vit = null;
        if (!self.Is(out map)) map = null;
        this.controller = self.controller.gameObject;
        this.self = self;

        SetStatDisplay(Stats.DisplayEntityStats);
        HookEvents();
    }
    public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
        if (!self.Is(out vit)) vit = null;
        if (!self.Is(out map)) map = null;
        this.controller = self.controller.gameObject;
        this.self = self;

        SetStatDisplay(Stats.DisplayEntityStats);
        HookEvents();
    }

    public void SetStatDisplay(bool IsActive) {
        object display = IsActive;
        ToggleStatDisplay(ref display);
    }

    public void ToggleStatDisplay(ref object IsActive) {
        this.active = (bool)IsActive;
        if (stats != null) GameObject.Destroy(stats);
        if (!self.active) return;
        if (!active) return;
        
        stats = GameObject.Instantiate(Indicators.BarIndicator, controller.transform);
        healthStat = stats?.transform.Find("HealthBar").GetComponent<Image>();
        damageStat = stats?.transform.Find("DamageBar").GetComponent<Image>();
        breathStat = stats?.transform.Find("BreathBar").GetComponent<Image>();
        effectWindow = stats?.transform.Find("Effects").GetChild(0).GetChild(0);
        SetupEffectWindow();
    }

    private void HookEvents() {
        self.eventCtrl.AddEventHandler(GameEvent.Entity_InLiquid, OnEnterWater);
        self.eventCtrl.AddEventHandler(GameEvent.Entity_InGas, OnEnterGas);
        self.eventCtrl.AddContextlessEventHandler(GameEvent.Entity_AddBehavior, OnAddBehavior);
        self.eventCtrl.AddContextlessEventHandler(GameEvent.Entity_RemoveBehavior, OnRemoveBehavior);
        self.eventCtrl.AddEventHandler(GameEvent.Entity_Damaged, OnDamaged);
        Config.CURRENT.System.AddHook("Statistics:Display", ToggleStatDisplay);
    }
    
    public void Disable(BehaviorEntity.Animal self) {
        Config.CURRENT.System.RemoveHook("Statistics:Display", ToggleStatDisplay);
        this.self = null;
    }
    

    public void Update(BehaviorEntity.Animal self) {
        if (self.context == BehaviorEntity.UpdateContext.Job)
            return;
        if (self.context == BehaviorEntity.UpdateContext.Fixed)
            return;
        
        if(!active) return;
        if(healthStat != null && vit != null) healthStat.fillAmount = vit.healthPercent;
        if(breathStat != null && map != null) breathStat.fillAmount = math.fmod(map.breathPercent, 1);;
        if(vit.invincibility > 0) return;
        if(damageStat != null && vit != null) damageStat.fillAmount = math.max(vit.healthPercent, damageStat.fillAmount - 0.01f);
    }

    public void OnDrawGizmos(BehaviorEntity.Animal self) {
        // Use golden ratio to spread hues evenly across the spectrum
        float h = (self.info.entityType * 0.618033988749f) % 1f;
        float s = 0.85f;
        float v = 0.95f;
        Gizmos.color = Color.HSVToRGB(h, s, v);
        Gizmos.DrawWireCube(CPUMapManager.GSToWS(self.position), self.settings.collider.size * 2);
    }
}
}