using System;
using System.Collections.Generic;
using Arterra.Core.Storage;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Config;
using Arterra.Config.Generation.Entity;

/// <summary> An interface for all object that can be attacked and take damage. It is up to the 
/// implementer to decide how the request to take damage is handled. </summary>
public interface IAttackable {
    public void Interact(Entity caller);
    public Arterra.Config.Generation.Item.IItem Collect(float collectRate);
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
    [JsonIgnore]
    protected Genetics genetics;
    [JsonIgnore]
    public float weight => stats.weight;
    public float health;
    public float invincibility;
    public float healthPercent => health / genetics.Get(stats.MaxHealth);
    public float breath;
    public float breathPercent => breath / genetics.Get(stats.HoldBreathTime);
    public bool IsDead => health <= 0;
    public const float FallDmgThresh = 10;
    //Do not name gs genetics or Newtonsoft will try to use this path
    public MinimalVitality(Stats stats, Genetics gs = null) {
        this.genetics = gs ?? new Genetics();
        invincibility = 0;
        if (stats == null) return;
        this.stats = stats;
        health = this.genetics.Get(stats.MaxHealth);
        breath = this.genetics.Get(stats.HoldBreathTime);
    }

    public MinimalVitality() { }

    public virtual void Deserialize(Stats stats, Genetics genetics = null) {
        this.genetics = genetics ?? new Genetics();
        this.stats = stats;
        invincibility = 0;
    }

    public virtual void Update(Entity self) {
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

    public void ProcessInSolid(Entity self, float density) {
        self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InSolid, self, null, ref density);
        ProcessSuffocation(self, density);
    }

    private void ProcessSuffocation(Entity self, float density) {
        if (density <= 0) return;
        if (self is not IAttackable) return;
        IAttackable target = (IAttackable)self;
        if (target.IsDead) return;
        EntityManager.AddHandlerEvent(() => target.TakeDamage(density / 255.0f, 0, null));
    }

    public void ProcessInGas(Entity self, float density) {
        self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InGas, self, null, ref density);
        breath = genetics.Get(stats.HoldBreathTime);
    }

    public void ProcessInLiquid(Entity self, ref TerrainCollider tCollider, float density) {
        self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InLiquid, self, null, ref density);
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

public class MediumVitality : MinimalVitality {
    [Serializable]
    public new class Stats : MinimalVitality.Stats {
        public Genetics.GeneFeature AttackDistance;
        public Genetics.GeneFeature AttackDamage;
        public Genetics.GeneFeature AttackCooldown;
        public Genetics.GeneFeature KBStrength;
        public float AttackDuration;

        public override void InitGenome(uint entityType) {
            base.InitGenome(entityType);

            Genetics.AddGene(entityType, ref AttackDistance);
            Genetics.AddGene(entityType, ref AttackDamage);
            Genetics.AddGene(entityType, ref AttackCooldown);
            Genetics.AddGene(entityType, ref KBStrength);
        }
    }

    private float attackProgress;
    public float attackCooldown;
    //So an entity cannot attack immediately
    public bool AttackInProgress;
    [JsonIgnore]
    private Stats AStats => stats as Stats;
    private Guid AttackTarget;

    public MediumVitality(Stats stats, Genetics gs = null) : base(stats, gs) {
        if (stats == null) return;
        attackProgress = AStats.AttackDuration;
        attackCooldown = 0;
    }

    public override void Deserialize(MinimalVitality.Stats stats, Genetics gs = null) {
        base.Deserialize(stats, gs);
    }

    public bool Attack(Entity target) {
        if (AttackInProgress) return false;
        if (attackCooldown > 0) return false;
        if (target is not IAttackable) return false;
        attackProgress = AStats.AttackDuration;
        AttackTarget = target.info.entityId;
        AttackInProgress = true;
        return true;
    }

    public override void Update(Entity self) {
        base.Update(self);
        attackCooldown = math.max(attackCooldown - EntityJob.cxt.deltaTime, 0);
        if (!AttackInProgress) return;
        attackProgress = math.max(attackProgress - EntityJob.cxt.deltaTime, 0);
        if (attackProgress > 0) return;
        
        AttackInProgress = false;
        attackCooldown = genetics.Get(AStats.AttackCooldown);
        FlushAttack(self);
    }

    private void FlushAttack(Entity self) {
        if (!EntityManager.TryGetEntity(AttackTarget, out Entity target))
            return;
        if (target is not IAttackable) return;
        if (Recognition.GetColliderDist(target, self) > genetics.Get(AStats.AttackDistance))
            return;
        float damage = genetics.Get(AStats.AttackDamage);
        float3 knockback = math.normalize(target.position - self.position) * genetics.Get(AStats.KBStrength);
        RealAttack(self, target, damage, knockback);
    }

    public static void RealAttack(Entity self, Entity target, float damage, float3 knockback) {
        var cxt = (damage, knockback);
        self.eventCtrl.RaiseEvent(
            Arterra.Core.Events.GameEvent.Entity_Attack,
            self, target, ref cxt
        ); (damage, knockback) = cxt;
        EntityManager.AddHandlerEvent(() => (target as IAttackable).TakeDamage(damage, knockback, self));
    }
}

public class Vitality : MediumVitality {
    [Serializable]
    public new class Stats : MediumVitality.Stats {
        public Genetics.GeneFeature HuntThreshold;
        public Genetics.GeneFeature MateThreshold;
        public Genetics.GeneFeature MateCost; //Everything );
        public float PregnacyLength;
        public float ConsumptionRate;

        public override void InitGenome(uint entityType) {
            base.InitGenome(entityType);

            Genetics.AddGene(entityType, ref HuntThreshold);
            Genetics.AddGene(entityType, ref MateThreshold);
            Genetics.AddGene(entityType, ref MateCost);
        }
    }
    [JsonIgnore]
    private Stats CStats => stats as Stats;
    [JsonProperty]
    private bool IsHunting;
    public Vitality(Stats stats, Genetics gs = null) : base(stats, gs) {
        IsHunting = false;
        //Higher mate cost => higher starting health for children
        if (stats == null) return;
        health = genetics.Get(stats.MaxHealth) * Mathf.Lerp(
            math.min(genetics.Get(stats.HuntThreshold), genetics.Get(stats.MateThreshold)),
            genetics.Get(stats.MateThreshold),
            (genetics.GetRawGene(stats.MateCost) + 1) / 2
        );
    }
    


    public override void Deserialize(MinimalVitality.Stats stats, Genetics gs = null) {
        base.Deserialize(stats, gs);
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
        public Arterra.Config.Generation.Item.IItem LootItem(Genetics genetics, float collectRate, ref Unity.Mathematics.Random random) {
            if (LootTable.value == null || LootTable.value.Count == 0) return null;
            int index = random.NextInt(LootTable.value.Count);

            float amount = genetics.Get(LootTable.value[index].DropAmount) * collectRate;
            Catalogue<Arterra.Config.Generation.Item.Authoring> registry = Config.CURRENT.Generation.Items;
            int itemindex = registry.RetrieveIndex(LootTable.value[index].ItemName);
            Arterra.Config.Generation.Item.IItem item = registry.Retrieve(itemindex).Item;

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
}


public class ProjectileLauncher {
    [Serializable]
    public class Stats {
        public float ShotDelay;
        public bool CheckSightline;
        public Genetics.GeneFeature ChargeTime;
        public ProjectileTag Projectile;
        public void InitGenome(uint entityType) {
            Genetics.AddGene(entityType, ref ChargeTime);
        }
    }

    [JsonIgnore]
    private Stats stats;
    [JsonIgnore]
    private Genetics genetics;
    private float3 fireDirection;
    private float chargeCooldown;
    private float shotProgress;
    public bool ShotInProgress;
    
    public ProjectileLauncher(Stats stats, Genetics gs = null) {
        this.genetics = gs ?? new Genetics();
        if (stats == null) return;
        this.stats = stats;
        
        chargeCooldown = gs.Get(stats.ChargeTime);
        shotProgress = 0;
        ShotInProgress = false;
    }


    public void Deserialize(Stats stats, Genetics gs = null) {
        this.genetics = gs ?? new Genetics();
        this.stats = stats;
    }

    //This is some whirly logic where if you call fire on a loop, it will
    public bool Fire(float3 target, Entity self) {
        if (ShotInProgress) return false;
        if (chargeCooldown > 0) return false;
        fireDirection = target - self.position;
        if (stats.CheckSightline) {
            if (CPUMapManager.RayCastTerrain(self.head, math.normalizesafe(fireDirection), 
                math.length(fireDirection), CPUMapManager.RayTestSolid, out float3 hit))
                return false;
        } 
        ShotInProgress = true;
        shotProgress = stats.ShotDelay;
        return true;
    }

    public void Update(Entity parent) {
        chargeCooldown = math.max(chargeCooldown - EntityJob.cxt.deltaTime, 0);
        if (!ShotInProgress) return;
        shotProgress = math.max(shotProgress - EntityJob.cxt.deltaTime, 0);
        if (shotProgress > 0) return;
        stats.Projectile.LaunchProjectile(parent, fireDirection);
        chargeCooldown = genetics.Get(stats.ChargeTime);
        ShotInProgress = false;
    }
}