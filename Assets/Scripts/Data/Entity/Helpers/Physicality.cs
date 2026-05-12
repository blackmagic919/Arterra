using System;
using System.Collections.Generic;
using Arterra.Core.Storage;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Data.Entity;
using Arterra.Data.Item;
using Arterra.Core.Events;
using TerrainCollider = Arterra.GamePlay.Interaction.TerrainCollider;
using Arterra.Editor;
using Arterra.Data.Entity.Behavior;

public class MinimalVitality {
    [Serializable]
    public class Stats {
        [Range(0, 1)]
        public float weight;
        public float MaxHealth;
        public float NaturalRegen;
        public float InvincTime;
        public float HoldBreathTime;
    }


    [JsonIgnore]
    protected Stats stats;
    [JsonIgnore]
    protected Modifier mod;
    [JsonIgnore]
    public float weight => stats.weight;
    public float health;
    public float invincibility;
    public float healthPercent => health / MaxHealth;
    public float breath;
    public float breathPercent => breath / HoldBreathTime;
    public bool IsDead => health <= 0;
    public const float FallDmgThresh = 10;

    private float MaxHealth => Modifier.Get(mod, MSettings.MaxHealth, stats.MaxHealth);
    private float HoldBreathTime => Modifier.Get(mod, MSettings.HoldBreathTime, stats.HoldBreathTime);
    private float NaturalRegen => Modifier.Get(mod, MSettings.NaturalRegen, stats.NaturalRegen);
    private float InvincTime => Modifier.Get(mod, MSettings.NaturalRegen, stats.InvincTime);
    //Do not name gs genetics or Newtonsoft will try to use this path
    public MinimalVitality(Stats stats, Modifier mod = null) {
        invincibility = 0;
        if (stats == null) return;
        this.stats = stats;
        health = MaxHealth;
        breath = HoldBreathTime;
    }

    public MinimalVitality() { }

    public virtual void Deserialize(Stats stats, Modifier mod = null) {
        this.mod = mod;
        this.stats = stats;
        invincibility = 0;
    }

    public virtual void Update(Entity self) {
        invincibility = math.max(invincibility - EntityJob.cxt.deltaTime, 0);
        if (IsDead) return;
        float delta = math.min(health + NaturalRegen
                    * EntityJob.cxt.deltaTime, MaxHealth)
                    - health;
        health += delta;
    }

    public bool Damage(float delta) {
        if (invincibility > 0) return false;
        invincibility = InvincTime;
        delta = health - math.max(health - delta, 0);
        health -= delta;
        return true;
    }

    public void Heal(float delta, bool force = false) {
        if (force) { health += delta; return; }
        if (IsDead) return;
        health = math.min(health + delta, MaxHealth);
    }

    public void ProcessInSolid(Entity self, float density) {
        self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InSolid, self, null, density);
        ProcessSuffocation(self, density);
    }

    private void ProcessSuffocation(Entity self, float density) {
        if (density <= 0) return;
        if (!self.Is<IAttackable>()) return;
        IAttackable target = (IAttackable)self;
        if (target.IsDead) return;
        EntityManager.AddHandlerEvent(() => target.TakeDamage(density / 255.0f, 0, null));
    }

    public void ProcessInGas(Entity self, float density) {
        self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InGas, self, null, density);
        breath = HoldBreathTime;
    }

    public void ProcessInLiquid(Entity self, ref TerrainCollider tCollider, float density) {
        self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InLiquid, self, null, density);
        breath = math.max(breath - EntityJob.cxt.deltaTime, 0);
        tCollider.transform.velocity += EntityJob.cxt.deltaTime * -EntityJob.cxt.gravity;
        tCollider.useGravity = false;
        if (breath > 0) return;
        //If dead don't process suffocation
        if (self.Is(out IAttackable target) && target.IsDead) return;
        ProcessSuffocation(self, density);
    }

    public void ProcessInLiquidAquatic(Entity self, ref TerrainCollider tCollider, float density, float drownTime) {
        if (breath >= 0) breath = -drownTime;

        const float Epsilon = 0.001f;
        breath = math.min(breath + EntityJob.cxt.deltaTime, -Epsilon);
        tCollider.useGravity = false;

        if (self.Is(out IAttackable target) && target.IsDead) { //If dead float to the surface
            tCollider.transform.velocity += EntityJob.cxt.deltaTime * -EntityJob.cxt.gravity;
            return; //don't process suffocation
        }

        if (breath >= -Epsilon) ProcessSuffocation(self, density);
    }

    public void ProcessInGasAquatic(Entity self, ref TerrainCollider tCollider, float density) {
        if (breath < 0) breath = HoldBreathTime;
        breath = math.max(breath - EntityJob.cxt.deltaTime, 0);
        tCollider.useGravity = true;

        if (self.Is(out IAttackable target) && target.IsDead) return; //If dead don't process suffocation
        if (breath <= 0) ProcessSuffocation(self, density);
    }
}

public class MediumVitality : MinimalVitality {
    [Serializable]
    public new class Stats : MinimalVitality.Stats {
        public float AttackDistance;
        public float AttackDamage;
        public float AttackCooldown;
        public float KBStrength;
        public float AttackDuration;
    }

    private float attackProgress;
    public float attackCooldown;
    //So an entity cannot attack immediately
    public bool AttackInProgress;
    [JsonIgnore]
    private Stats AStats => stats as Stats;
    private Guid AttackTarget;
    
    private float AttackCooldown => Modifier.Get(mod, MSettings.AttackCooldown, AStats.AttackCooldown);
    private float AttackDistance => Modifier.Get(mod, MSettings.AttackDistance, AStats.AttackDistance);
    private float AttackDamage => Modifier.Get(mod, MSettings.AttackDamage, AStats.AttackDamage);
    private float KBStrength => Modifier.Get(mod, MSettings.KBStrength, AStats.KBStrength);

    public MediumVitality(Stats stats, Modifier mod = null) : base(stats, mod) {
        if (stats == null) return;
        attackProgress = AStats.AttackDuration;
        attackCooldown = 0;
    }

    public override void Deserialize(MinimalVitality.Stats stats, Modifier mod = null) {
        base.Deserialize(stats, mod);
    }

    public bool Attack(Entity target) {
        if (AttackInProgress) return false;
        if (attackCooldown > 0) return false;
        if (!target.Is<IAttackable>()) return false;
        attackProgress = AStats.AttackDuration;
        AttackTarget = target.info.rtEntityId;
        AttackInProgress = true;
        return true;
    }

    public override void Update(Entity self) {
        base.Update(self);
        attackCooldown = math.max(attackCooldown - EntityJob.cxt.deltaTime, 0);
        if (!AttackInProgress) return;
        attackProgress = math.max(attackProgress - EntityJob.cxt.deltaTime, 0);
        if (attackProgress > 0) return;
        FlushAttack(self);
    }

    private void FlushAttack(Entity self) {
        AttackInProgress = false;
        attackCooldown = AttackCooldown;
        
        if (!EntityManager.TryGetEntity(AttackTarget, out Entity target))
            return;
        if (!target.Is<IAttackable>()) return;
        if (ColliderUpdateBehavior.GetColliderDist(target, self) > AttackDistance)
            return;
        float damage = AttackDamage;
        float3 knockback = math.normalize(target.position - self.position) * KBStrength;
        RealAttack(self, target, damage, knockback);
    }

    public static void RealAttack(Entity self, Entity target, float damage, float3 knockback) {
        RefTuple<(float, float3)> cxt = (damage, knockback);
        self.eventCtrl.RaiseEvent(
            GameEvent.Entity_Attack,
            self, target, cxt
        ); (damage, knockback) = cxt.Value;
        EntityManager.AddHandlerEvent(() => target.As<IAttackable>().TakeDamage(damage, knockback, self));
    }
}
