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

    public class VitalityBehavior : SpeciesBehavior, IVitality {
        [JsonIgnore] public Decomposition Decomposition;
        [JsonIgnore] public PhysicalitySetting stats;

        private BehaviorEntity.Animal self;
        private Modifier mod;

        [JsonProperty] private NetworkVariable<float> netHealth = new("Health", NetworkPermission.Server);
        [JsonIgnore] public float MaxHealth => Modifier.Get(mod, MSettings.MaxHealth, stats.MaxHealth);
        private float NaturalRegen => Modifier.Get(mod, MSettings.NaturalRegen, stats.NaturalRegen);
        private float InvincTime => Modifier.Get(mod, MSettings.InvincTime, stats.InvincTime);

        [JsonIgnore] public float weight => stats.weight;
        [JsonIgnore] public float healthPercent => health / MaxHealth;
        [JsonIgnore] public bool IsDead => health <= 0;

        [JsonIgnore] public float health { get => netHealth.Value; set => netHealth.Value = value; }
        public float invincibility;
        public bool TriggeredDeath;
        private float MaxAccDamage;
        public const float FallDmgThresh = 10;


        public void Collect(object recieverId, object selfId, object ctx) {
            if (ctx is not float amount) return;

            Entity reciever;
            if (recieverId is Guid entityId) 
                EntityManager.TryGetEntity(entityId, out reciever);
            else reciever = null;
            
            IItem collectedItem = null;
            if (IsDead && Decomposition != null) 
                collectedItem = Decomposition.LootItem(mod, amount, ref self.random);
            self.eventCtrl.RaiseEvent(GameEvent.Entity_Collect, selfId, recieverId, amount);
            reciever?.eventCtrl.EnqueueEvent(GameEvent.Entity_RecieveItem, selfId, recieverId, collectedItem);
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
            if (self.context != BehaviorEntity.UpdateContext.Simulation) return;
            TryApplyAccDamage();

            if (IsDead) {
                if (TriggeredDeath) return;
                self.eventCtrl.RaiseEvent(GameEvent.Entity_Death, self.info.rtEntityId, null);
                TriggeredDeath = true;
                return;
            }

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
            invincibility = 0;
            MaxAccDamage = 0;
            health = (float)CustomUtility.Sample(self.random, stats.StartHealthPercent, stats.StartHealthVariance);
            health = math.clamp(health, 0, 1);
            health *= MaxHealth;
            TriggeredDeath = false;
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
            invincibility = 0;
            self.weight = this.weight;

            netHealth.Register(self);
            self.Register<IVitality>(this);
        }

        public override void Disable(BehaviorEntity.Animal self) {
            self.eventCtrl.RemoveEventHandler(GameEvent.Apply_Damage, TakeDamage);
            self.eventCtrl.RemoveEventHandler(GameEvent.Apply_Collect, Collect);
            netHealth?.Dispose();
            this.self = null;
        }
    }
}
