using UnityEngine;
using Unity.Mathematics;
using Arterra.Data.Entity;
using Newtonsoft.Json;
using Arterra.Data.Item;
using Arterra.Core.Events;
using Arterra.Core.Network;
using TerrainCollider = Arterra.GamePlay.Interaction.TerrainCollider;
using System.Collections.Generic;
using System;
using Arterra.Utils;
using Arterra.Configuration;
using Arterra.Editor;
using Arterra.Engine.Audio;


namespace Arterra.Data.Entity.Behavior {
    /// <summary> An interface for all object that can be attacked and take damage. It is up to the 
    /// implementer to decide how the request to take damage is handled. </summary>
    public interface IVitality {
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

        public List<IItem> CreateLoot(Modifier mod, ref Unity.Mathematics.Random random) {
            List<IItem> loot = new();
            if (LootTable.value == null || LootTable.value.Count == 0) return loot;
            Catalogue<Item.Authoring> registry = Config.CURRENT.Generation.Items;
            foreach (LootInfo drop in LootTable.value) {
                float mean = Modifier.Get(mod, MSettings.DropAmount, drop.DropAmount);
                float amount = math.max((float)CustomUtility.Sample(ref random, mean, drop.DropStdDev * drop.DropStdDev), 0);
                int index = registry.RetrieveIndex(drop.ItemName);
                IItem item = registry.Retrieve(index).Item;
                amount *= item.UnitSize;
                int remaining = (int)math.floor(amount) + (random.NextFloat() < math.frac(amount) ? 1 : 0);
                while (remaining > 0) {
                    IItem stack = (IItem)item.Clone();
                    int count = math.min(remaining, item.StackLimit);
                    stack.Create(index, count);
                    loot.Add(stack);
                    remaining -= count;
                }
            }
            //Randomize order
            for (int i = loot.Count - 1; i > 0; i--) {
                int index = random.NextInt(i + 1);
                (loot[i], loot[index]) = (loot[index], loot[i]);
            }
            return loot;
        }
        [Serializable]
        public struct LootInfo {
            [RegistryReference("Items")]
            public string ItemName;
            //The total unit amount available from the corpse.
            public float DropAmount;
            public float DropStdDev;
        }
    }

    public class VitalityBehavior : SpeciesBehavior, IVitality {
        [JsonIgnore] public Decomposition Decomposition;
        [JsonIgnore] public PhysicalitySetting stats;

        private BehaviorEntity.Animal self;
        private Modifier mod;
        private TimedDecayBehavior decay;
        [JsonProperty] private List<IItem> loot;

        [JsonProperty] private NetworkVariable<float> netHealth = new("Health", NetworkPermission.Server);
        [JsonIgnore] public float MaxHealth => Modifier.Get(mod, MSettings.MaxHealth, stats.MaxHealth);
        private float NaturalRegen => Modifier.Get(mod, MSettings.NaturalRegen, stats.NaturalRegen);
        private float InvincTime => Modifier.Get(mod, MSettings.InvincTime, stats.InvincTime);

        [JsonIgnore] public float weight => stats.weight;
        [JsonIgnore] public float healthPercent => health / MaxHealth;
        [JsonIgnore] public bool IsDead => health <= 0;
        [JsonIgnore] public bool UseDefaultLoot = true;

        [JsonIgnore] public float health { get => netHealth.Value; set => netHealth.Value = value; }
        public float invincibility;
        public bool TriggeredDeath;
        private float MaxAccDamage;
        public const float FallDmgThresh = 10;


        private void BindDecay() {
            if (!self.Is(out decay)) decay = null;
            if (decay != null) decay.IsDepleted = IsLootDepleted;
        }

        private bool IsLootDepleted() => UseDefaultLoot && loot != null && loot.Count == 0;

        private void EnsureLoot() {
            if (loot != null || !UseDefaultLoot) return;
            loot = Decomposition?.CreateLoot(mod, ref self.random) ?? new List<IItem>();
        }

        public void Collect(object recieverId, object selfId, object ctx) {
            if (!self.IsAuthority || !IsDead || ctx is not float amount || amount <= 0) return;
            if (recieverId is not Guid entityId || !EntityManager.TryGetEntity(entityId, out Entity reciever)
                || !reciever.active) return;

            EnsureLoot();
            if (UseDefaultLoot && loot.Count > 0) {
                IItem item = loot[0];
                float rawAmount = amount * item.UnitSize;
                int count = math.min(item.AmountRaw, (int)math.floor(rawAmount)
                    + (self.random.NextFloat() < math.frac(rawAmount) ? 1 : 0));
                if (count > 0) {
                    IItem collected = (IItem)item.Clone();
                    collected.AmountRaw = count;
                    item.AmountRaw -= count;
                    if (item.AmountRaw == 0) loot.RemoveAt(0);
                    reciever.eventCtrl.EnqueueEvent(GameEvent.Entity_RecieveItem, selfId, recieverId, collected);
                }
            }
            self.eventCtrl.RaiseEvent(GameEvent.Entity_Collect, selfId, recieverId, amount);
        }

        public bool IsKillingBlow(float damage) => MaxAccDamage < health && damage > health;

        public void TakeDamage(object sourceId, object attackerId, object context) {
            if (self == null || !self.IsAuthority) return;
            if (context is not (float damage, float3 knockback)) return;
            if (damage < MaxAccDamage) return;

            RefTuple<(float, float3)> cxt = (damage - MaxAccDamage, knockback);
            self.eventCtrl.RaiseEvent(GameEvent.Entity_Damaged, sourceId, attackerId, cxt);
            (damage, knockback) = cxt.Value;
            if (damage == 0) return;
            
            MaxAccDamage += damage;
            self.velocity += knockback;
        }

        private void FlushDeltaHealth(float delta) {
            RefTuple<float> cxt = new RefTuple<float>(delta);
            self.eventCtrl.RaiseEvent(GameEvent.Entity_FlushHealthDelta,
                self.info.rtEntityId, null, cxt);
            delta = cxt.Value;
            health = math.clamp(health + delta, 0, MaxHealth);
        }


        public override void Update(BehaviorEntity.Animal self) {
            if (!self.IsAuthority || self.context != BehaviorEntity.UpdateContext.Simulation) return;
            TryApplyAccDamage();

            if (IsDead) {
                EnsureLoot();
                if (TriggeredDeath) return;
                TriggeredDeath = true;
                self.eventCtrl.RaiseEvent(GameEvent.Entity_Death, self.info.rtEntityId, null);
                return;
            }

            TriggeredDeath = false;
            loot = null;
            FlushDeltaHealth(NaturalRegen * self.DeltaTime);
        }

        public bool TryApplyAccDamage() {
            invincibility = math.max(invincibility - self.DeltaTime, 0);
            if (invincibility > 0 || MaxAccDamage == 0) return false;

            invincibility = InvincTime;
            FlushDeltaHealth(-MaxAccDamage);
            MaxAccDamage = 0;
            return true;
        }

        public void Heal(float delta, bool force = false) {
            if (force) { health += delta; return; }
            if (IsDead) return;
            health = math.min(health + delta, MaxHealth);
        }


        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {}

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(PhysicalitySetting), new PhysicalitySetting());
            heirarchy.TryAdd(typeof(Decomposition), new Decomposition());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out stats))
                throw new System.Exception("Entity: Vitality Behavior Requires AnimalSettings to have PhysicalitySettings");
            if (!setting.Is(out Decomposition)) Decomposition = null;
            if (!self.Is(out mod)) mod = null;
            if (self.IsAuthority) self.eventCtrl.AddEventHandler(GameEvent.Apply_Damage, TakeDamage);
            if (self.IsAuthority) self.eventCtrl.AddEventHandler(GameEvent.Apply_Collect, Collect);

            this.self = self;
            BindDecay();
            invincibility = 0;
            MaxAccDamage = 0;
            health = (float)CustomUtility.Sample(
                ref self.random, stats.StartHealthPercent,
                stats.StartHealthVariance);
            health = math.clamp(health, 0, 1);
            health *= MaxHealth;
            TriggeredDeath = false;
            loot = null;
            self.weight = this.weight;

            netHealth.Register(self);
            self.Register<IVitality>(this);
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out stats))
                throw new System.Exception("Entity: Vitality Behavior Requires Animal to have PhysicalitySettings");
            if (!setting.Is(out Decomposition)) Decomposition = null;
            if (!self.Is(out mod)) mod = null;
            if (self.IsAuthority) self.eventCtrl.AddEventHandler(GameEvent.Apply_Damage, TakeDamage);
            if (self.IsAuthority) self.eventCtrl.AddEventHandler(GameEvent.Apply_Collect, Collect);

            this.self = self;
            BindDecay();
            invincibility = 0;
            self.weight = this.weight;

            netHealth.Register(self);
            self.Register<IVitality>(this);
        }

        public override void Disable(BehaviorEntity.Animal self) {
            self.eventCtrl.RemoveEventHandler(GameEvent.Apply_Damage, TakeDamage);
            self.eventCtrl.RemoveEventHandler(GameEvent.Apply_Collect, Collect);
            if (decay != null && decay.IsDepleted == IsLootDepleted) decay.IsDepleted = null;
            netHealth?.Dispose();
            this.self = null;
        }
    }
}
