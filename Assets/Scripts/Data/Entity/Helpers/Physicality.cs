using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using WorldConfig;
using WorldConfig.Generation.Entity;

/// <summary> An interface for all object that can be attacked and take damage. It is up to the 
/// implementer to decide how the request to take damage is handled. </summary>
public interface IAttackable {
    public void Interact(Entity caller);
    public WorldConfig.Generation.Item.IItem Collect(float collectRate);
    public void TakeDamage(float damage, float3 knockback, Entity attacker = null);
    public bool IsDead { get; }
}

public interface ICollidable {
    public float Weight { get; }
    public float3 Velocity { get; }
    public Bounds Bounds { get; }
}

public class MinimalVitality {
    [Serializable]
    public class Stats {
        [Range(0, 1)]
        public float weight;
        public Genetics.GeneFeature MaxHealth;
        public Genetics.GeneFeature NaturalRegen;
        public Genetics.GeneFeature InvincTime;
        public Genetics.GeneFeature HoldBreathTime;

        public virtual void InitGenome(uint entityType) {
            Genetics.AddGene(entityType, ref MaxHealth);
            Genetics.AddGene(entityType, ref NaturalRegen);
            Genetics.AddGene(entityType, ref InvincTime);
            Genetics.AddGene(entityType, ref HoldBreathTime);
        }
    }


    [JsonIgnore]
    protected Stats stats;
    protected Genetics genetics;
    public float health;
    public float invincibility;
    public float healthPercent => health / genetics.Get(stats.MaxHealth);
    public float breath;
    public float breathPercent => breath / genetics.Get(stats.HoldBreathTime);
    public bool IsDead => health <= 0;
    public const float FallDmgThresh = 10;
    public MinimalVitality(Stats stats, Genetics genetics = null) {
        this.genetics = genetics ?? new Genetics();
        this.stats = stats;
        invincibility = 0;
        health = this.genetics.Get(stats.MaxHealth);
        breath = this.genetics.Get(stats.HoldBreathTime);
    }

    public MinimalVitality() { }

    public virtual void Deserialize(Stats stats, Genetics genetics = null) {
        this.genetics = genetics ?? new Genetics();
        this.stats = stats;
        invincibility = 0;
    }

    public virtual void Update() {
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

    public void ProcessSuffocation(Entity self, float density) {
        if (density <= 0) return;
        if (self is not IAttackable) return;
        IAttackable target = (IAttackable)self;
        if (target.IsDead) return;
        EntityManager.AddHandlerEvent(() => target.TakeDamage(density / 255.0f, 0, null));
    }

    public void ProcessInGas(float density) {
        breath = genetics.Get(stats.HoldBreathTime);
    }

    public void ProcessInLiquid(Entity self, ref TerrainCollider tCollider, float density) {
        breath = math.max(breath - EntityJob.cxt.deltaTime, 0);
        tCollider.transform.velocity += EntityJob.cxt.deltaTime * -EntityJob.cxt.gravity;
        tCollider.useGravity = false;
        if (breath > 0) return;
        //If dead don't process suffocation
        if (self is IAttackable target && target.IsDead) return;
        ProcessSuffocation(self, density);
    }

    public void ProcessInLiquidAquatic(Entity self, ref TerrainCollider tCollider, float density, float drownTime) {
        if (breath >= 0) breath = -drownTime;

        const float Epsilon = 0.001f;
        breath = math.min(breath + EntityJob.cxt.deltaTime, -Epsilon);
        tCollider.useGravity = false;

        if (self is IAttackable target && target.IsDead) { //If dead float to the surface
            tCollider.transform.velocity += EntityJob.cxt.deltaTime * -EntityJob.cxt.gravity;
            return; //don't process suffocation
        }

        if (breath >= -Epsilon) ProcessSuffocation(self, density);
    }

    public void ProcessInGasAquatic(Entity self, ref TerrainCollider tCollider, float density) {
        if (breath < 0) breath = genetics.Get(stats.HoldBreathTime);
        breath = math.max(breath - EntityJob.cxt.deltaTime, 0);
        tCollider.useGravity = true;

        if (self is IAttackable target && target.IsDead) return; //If dead don't process suffocation
        if (breath <= 0) ProcessSuffocation(self, density);
    }
}

public class Vitality : MinimalVitality {
    [Serializable]
    public class Stats : MinimalVitality.Stats {
        public Genetics.GeneFeature AttackDistance;
        public Genetics.GeneFeature AttackDamage;
        public Genetics.GeneFeature AttackCooldown;
        public Genetics.GeneFeature KBStrength;
        public Genetics.GeneFeature HuntThreshold;
        public Genetics.GeneFeature MateThreshold;
        public Genetics.GeneFeature MateCost; //Everything );
        public float PregnacyLength;
        public float ConsumptionRate;

        public override void InitGenome(uint entityType) {
            base.InitGenome(entityType);

            Genetics.AddGene(entityType, ref AttackDistance);
            Genetics.AddGene(entityType, ref AttackDamage);
            Genetics.AddGene(entityType, ref AttackCooldown);
            Genetics.AddGene(entityType, ref KBStrength);
            Genetics.AddGene(entityType, ref HuntThreshold);
            Genetics.AddGene(entityType, ref MateThreshold);
            Genetics.AddGene(entityType, ref MateCost);
        }
    }
    [JsonIgnore]
    private Stats CStats => stats as Stats;
    [JsonProperty]
    private float attackCooldown;
    [JsonProperty]
    private bool IsHunting;
    public Vitality(Stats stats, Genetics genetics = null) : base(stats, genetics) {
        this.stats = stats;
        invincibility = 0;
        attackCooldown = 0;
        breath = genetics.Get(stats.HoldBreathTime);
        IsHunting = false;
        //Higher mate cost => higher starting health for children
        health = genetics.Get(stats.MaxHealth) * Mathf.Lerp(
            math.min(genetics.Get(stats.HuntThreshold), genetics.Get(stats.MateThreshold)),
            genetics.Get(stats.MateThreshold),
            (genetics.GetRawGene(stats.MateCost) + 1) / 2
        );
    }

    public Vitality() { }

    public override void Deserialize(MinimalVitality.Stats stats, Genetics genetics = null) {
        this.genetics = genetics ?? new Genetics();
        this.stats = stats;
        invincibility = 0;
        attackCooldown = 0;
    }

    public override void Update() {
        attackCooldown = math.max(attackCooldown - EntityJob.cxt.deltaTime, 0);
        base.Update();
    }

    public bool Attack(Entity target, Entity self) {
        if (attackCooldown > 0) return false;
        if (target is not IAttackable) return false;
        attackCooldown = genetics.Get(CStats.AttackCooldown);
        float damage = genetics.Get(CStats.AttackDamage);
        float3 knockback = math.normalize(target.position - self.position) * genetics.Get(CStats.KBStrength);
        EntityManager.AddHandlerEvent(() => (target as IAttackable).TakeDamage(damage, knockback, self));
        return true;
    }

    public bool BeginHunting() => IsHunting || (IsHunting = healthPercent < genetics.Get(CStats.HuntThreshold));
    public bool StopHunting() => !IsHunting || !(IsHunting = healthPercent < math.lerp(genetics.Get(CStats.MateThreshold), 1, 0.5f));
    public bool BeginMating() => healthPercent > genetics.Get(CStats.MateThreshold);
    public bool StopMating() => healthPercent < genetics.Get(CStats.MateThreshold);
    [Serializable]
    public struct Decomposition {
        public Option<List<LootInfo>> LootTable;
        public Genetics.GeneFeature DecompositionTime; //~300 seconds
        public void InitGenome(uint entityType) {
            Genetics.AddGene(entityType, ref DecompositionTime);
            ref List<LootInfo> table = ref LootTable.value;
            for (int i = 0; i < table.Count; i++) {
                LootInfo loot = table[i];
                Genetics.AddGene(entityType, ref loot.DropAmount);
                table[i] = loot;
            }
        }
        public WorldConfig.Generation.Item.IItem LootItem(Genetics genetics, float collectRate, ref Unity.Mathematics.Random random) {
            if (LootTable.value == null || LootTable.value.Count == 0) return null;
            int index = random.NextInt(LootTable.value.Count);

            float amount = genetics.Get(LootTable.value[index].DropAmount) * collectRate;
            Catalogue<WorldConfig.Generation.Item.Authoring> registry = Config.CURRENT.Generation.Items;
            int itemindex = registry.RetrieveIndex(LootTable.value[index].ItemName);
            WorldConfig.Generation.Item.IItem item = registry.Retrieve(itemindex).Item;

            amount *= item.UnitSize;
            int delta = Mathf.FloorToInt(amount)
                + (random.NextFloat() < math.frac(amount) ? 1 : 0);
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
}