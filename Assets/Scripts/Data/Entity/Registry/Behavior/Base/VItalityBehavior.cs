using UnityEngine;
using Unity.Mathematics;
using Arterra.Data.Entity;
using Newtonsoft.Json;
using Arterra.Data.Item;
using Arterra.Core.Events;
using TerrainCollider = Arterra.GamePlay.Interaction.TerrainCollider;
using System.Collections.Generic;
using System;
using Arterra.Utils;
using Arterra.Configuration;
using Arterra.Editor;


namespace Arterra.Data.Entity.Behavior {
    /// <summary> An interface for all object that can be attacked and take damage. It is up to the 
    /// implementer to decide how the request to take damage is handled. </summary>
    public interface IAttackable {
        public void Interact(Entity caller, IItem item = null);
        public void Collect(Entity caller, Action<IItem> Collect, float collectRate);
        public bool TakeDamage(float damage, float3 knockback, Entity attacker = null);
        public bool IsDead { get; }
    }
    
    [Serializable]
    public class PhysicalitySetting : IBehaviorSetting {
        ///<summary>Name of settings object in UI generation</summary>
        [JsonIgnore] public static string Name => "Vitality";
        public float weight;
        public float StartHealthPercent = 0.7f;
        public float StartHealthVariance = 0.2f;
        public float MaxHealth;
        public float NaturalRegen;
        public float InvincTime;

        public object Clone() {
            return new PhysicalitySetting {
                weight = weight,
                MaxHealth = MaxHealth,
                NaturalRegen = NaturalRegen,
                InvincTime = InvincTime,
                StartHealthPercent = StartHealthPercent,
                StartHealthVariance = StartHealthVariance,
            };
        }
    }

    [Serializable]
    public class Decomposition : IBehaviorSetting {
        ///<summary>Name of settings object in UI generation</summary>
        [JsonIgnore] public static string Name => "Decomposition";
        public Option<List<LootInfo>> LootTable;

        public object Clone() {
            return new Decomposition {
                LootTable = LootTable
            };
        }

        public IItem LootItem(Modifier mod, float collectRate, ref Unity.Mathematics.Random random) {
            if (LootTable.value == null || LootTable.value.Count == 0) return null;
            int index = random.NextInt(LootTable.value.Count);

            float amount = Modifier.Get(mod, MSettings.DropAmount, LootTable.value[index].DropAmount) * collectRate;
            Catalogue<Arterra.Data.Item.Authoring> registry = Config.CURRENT.Generation.Items;
            int itemindex = registry.RetrieveIndex(LootTable.value[index].ItemName);
            IItem item = registry.Retrieve(itemindex).Item;

            amount *= item.UnitSize;
            int delta = Mathf.FloorToInt(amount) + (random.NextFloat() < math.frac(amount) ? 1 : 0);
            if (delta == 0) return null;
            delta = math.min(delta, item.StackLimit);
            item.Create(itemindex, delta);
            return item;
        }
        [Serializable]
        public struct LootInfo {
            [RegistryReference("Items")]
            public string ItemName;
            //The unit amount given per second of decomposition
            public float DropAmount;
        }
    }

    public class VitalityBehavior : ISpeciesBehavior, IAttackable {
        [JsonIgnore] public Decomposition Decomposition;
        [JsonIgnore] public PhysicalitySetting stats;

        private BehaviorEntity.Animal self;
        private Modifier mod;

        private float MaxHealth => Modifier.Get(mod, MSettings.MaxHealth, stats.MaxHealth);
        private float NaturalRegen => Modifier.Get(mod, MSettings.NaturalRegen, stats.NaturalRegen);
        private float InvincTime => Modifier.Get(mod, MSettings.InvincTime, stats.InvincTime);

        [JsonIgnore] public float weight => stats.weight;
        [JsonIgnore] public float healthPercent => health / MaxHealth;
        [JsonIgnore] public bool IsDead => health <= 0;

        public float health;
        public float invincibility;
        public bool TriggeredDeath;
        private float MaxAccDamage;

        public const float FallDmgThresh = 10;

        public void Interact(Entity caller, IItem item) => self.eventCtrl.RaiseEvent(GameEvent.Entity_Interact, self, caller, item);

        public void Collect(Entity caller, Action<IItem> collect, float amount) {
            if (IsDead && Decomposition != null) collect(Decomposition.LootItem(mod, amount, ref self.random));
            self.eventCtrl.RaiseEvent(GameEvent.Entity_Collect, self, caller, (collect, amount));
        }

        public bool IsKillingBlow(float damage) => MaxAccDamage < health && damage > health;

        public bool TakeDamage(float damage, float3 knockback, Entity attacker = null) {
            if (damage < MaxAccDamage) return false;
            RefTuple<(float, float3)> cxt = (damage - MaxAccDamage, knockback);
            self.eventCtrl.RaiseEvent(GameEvent.Entity_Damaged, self, attacker, cxt);
            (damage, knockback) = cxt.Value;
            if (damage == 0) return false;
            
            MaxAccDamage += damage;
            self.velocity += knockback;
            return true;
        }


        public void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            if (self.context == BehaviorEntity.UpdateContext.Main) return;
            TryApplyAccDamage();

            if (IsDead) {
                if (TriggeredDeath) return;
                self.eventCtrl.RaiseEvent(GameEvent.Entity_Death, self, null);
                TriggeredDeath = true;
                return;
            }

            float delta = math.min(health + NaturalRegen
                        * self.DeltaTime, MaxHealth)
                        - health;
            health += delta;
        }

        public bool TryApplyAccDamage() {
            invincibility = math.max(invincibility - self.DeltaTime, 0);
            if (invincibility > 0 || MaxAccDamage == 0) return false;

            invincibility = InvincTime;
            float damage = health - math.max(health - MaxAccDamage, 0);
            health -= damage;
            MaxAccDamage = 0;
            return true;
        }

        public void Heal(float delta, bool force = false) {
            if (force) { health += delta; return; }
            if (IsDead) return;
            health = math.min(health + delta, MaxHealth);
        }


        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {}

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(PhysicalitySetting), new PhysicalitySetting());
            heirarchy.TryAdd(typeof(Decomposition), new Decomposition());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out stats))
                throw new System.Exception("Entity: Vitality Behavior Requires AnimalSettings to have PhysicalitySettings");
            if (!setting.Is(out Decomposition)) Decomposition = null;
            if (!self.Is(out mod)) mod = null;

            this.self = self;
            invincibility = 0;
            MaxAccDamage = 0;
            health = (float)CustomUtility.Sample(self.random, stats.StartHealthPercent, stats.StartHealthVariance);
            health = math.clamp(health, 0, 1);
            health *= MaxHealth;
            TriggeredDeath = false;
            self.weight = this.weight;

            self.Register<IAttackable>(this);
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out stats))
                throw new System.Exception("Entity: Vitality Behavior Requires Animal to have PhysicalitySettings");
            if (!setting.Is(out Decomposition)) Decomposition = null;
            if (!self.Is(out mod)) mod = null;

            this.self = self;
            invincibility = 0;
            self.weight = this.weight;

            self.Register<IAttackable>(this);
        }

        public void Disable() {
            this.self = null;
        }
    }
}