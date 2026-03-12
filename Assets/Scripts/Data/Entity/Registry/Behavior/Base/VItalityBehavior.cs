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
        public IItem Collect(Entity caller, float collectRate);
        public bool TakeDamage(float damage, float3 knockback, Entity attacker = null);
        public bool IsDead { get; }
    }
    
    [Serializable]
    public class PhysicalitySetting : IBehaviorSetting {
        public float weight;
        public float StartHealthPercent = 0.7f;
        public float StartHealthVariance = 0.2f;
        public Genetics.GeneFeature MaxHealth;
        public Genetics.GeneFeature NaturalRegen;
        public Genetics.GeneFeature InvincTime;

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            Genetics.AddGene(entityType, ref MaxHealth);
            Genetics.AddGene(entityType, ref NaturalRegen);
            Genetics.AddGene(entityType, ref InvincTime);
        }

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
        public Option<List<LootInfo>> LootTable;
        public void InitGenome(uint entityType) {
            ref List<LootInfo> table = ref LootTable.value;
            for (int i = 0; i < table.Count; i++) {
                LootInfo loot = table[i];
                Genetics.AddGene(entityType, ref loot.DropAmount);
                table[i] = loot;
            }
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            InitGenome(entityType);
        }

        public object Clone() {
            return new Decomposition {
                LootTable = LootTable
            };
        }
        public IItem LootItem(Genetics genetics, float collectRate, ref Unity.Mathematics.Random random) {
            if (LootTable.value == null || LootTable.value.Count == 0) return null;
            int index = random.NextInt(LootTable.value.Count);

            float amount = genetics.Get(LootTable.value[index].DropAmount) * collectRate;
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
            public Genetics.GeneFeature DropAmount;
        }
    }

    public class VitalityBehavior : IBehavior, IAttackable {
        [JsonIgnore] public Decomposition Decomposition;
        [JsonIgnore] public PhysicalitySetting stats;

        private BehaviorEntity.Animal self;
        private Genetics genetics;

        [JsonIgnore] public float weight => stats.weight;
        [JsonIgnore] public float healthPercent => health / genetics.Get(stats.MaxHealth);
        [JsonIgnore] public bool IsDead => health <= 0;

        public float health;
        public float invincibility;

        public const float FallDmgThresh = 10;

        public void Interact(Entity caller, IItem item) => self.eventCtrl.RaiseEvent(GameEvent.Entity_Interact, self, caller, item);

        public IItem Collect(Entity caller, float amount) {
            IItem item = null;
            if (IsDead && Decomposition != null) item = Decomposition.LootItem(genetics, amount, ref self.random);
            self.eventCtrl.RaiseEvent(GameEvent.Entity_Collect, self, caller, (item, amount));
            return item;
        }

        public bool CanDamage => invincibility <= 0;
        public bool IsKillingBlow(float damage) => CanDamage && damage > health;

        public bool TakeDamage(float damage, float3 knockback, Entity attacker = null) {
            if (!CanDamage) return false;
            RefTuple<(float, float3)> cxt = (damage, knockback);
            self.eventCtrl.RaiseEvent(GameEvent.Entity_Damaged, self, attacker, cxt);
            (damage, knockback) = cxt.Value;

            if (!Damage(damage)) return false;
            Indicators.DisplayDamageParticle(self.position, knockback);
            self.velocity += knockback;
            return true;
        }


        public void Update(BehaviorEntity.Animal self) {
            invincibility = math.max(invincibility - EntityJob.cxt.deltaTime, 0);
            if (IsDead) return;
            float delta = math.min(health + genetics.Get(stats.NaturalRegen)
                        * EntityJob.cxt.deltaTime, genetics.Get(stats.MaxHealth))
                        - health;
            health += delta;
        }

        public bool Damage(float delta) {
            if (invincibility > 0) return false;
            invincibility = genetics.Get(stats.InvincTime);
            delta = health - math.max(health - delta, 0);
            health -= delta;
            return true;
        }

        public void Heal(float delta, bool force = false) {
            if (force) { health += delta; return; }
            if (IsDead) return;
            health = math.min(health + delta, genetics.Get(stats.MaxHealth));
        }


        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {}

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(PhysicalitySetting), new PhysicalitySetting());
            heirarchy.TryAdd(typeof(Decomposition), new Decomposition());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out stats))
                throw new System.Exception("Entity: Vitality Behavior Requires AnimalSettings to have PhysicalitySettings");
            if (self.Is(out GeneticsBehavior geneticsBehavior))
                this.genetics = geneticsBehavior.Genes;
            else this.genetics = new Genetics();
            if (!setting.Is(out Decomposition)) Decomposition = null;

            this.self = self;
            invincibility = 0;
            health = (float)CustomUtility.Sample(self.random, stats.StartHealthPercent, stats.StartHealthVariance);
            health = math.clamp(health, 0, 1);
            health *= this.genetics.Get(stats.MaxHealth);
            self.weight = this.weight;

            self.Register<IAttackable>(this);
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out stats))
                throw new System.Exception("Entity: Vitality Behavior Requires Animal to have PhysicalitySettings");
            if (self.Is(out GeneticsBehavior geneticsBehavior))
                this.genetics = geneticsBehavior.Genes;
            else this.genetics = new Genetics();
            if (!setting.Is(out Decomposition)) Decomposition = null;

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