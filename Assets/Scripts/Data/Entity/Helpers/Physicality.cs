using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using WorldConfig;
using WorldConfig.Generation.Entity;

/// <summary> An interface for all object that can be attacked and take damage. It is up to the 
/// implementer to decide how the request to take damage is handled. </summary>
public interface IAttackable{
    public void Interact(Entity caller);
    public WorldConfig.Generation.Item.IItem Collect(float collectRate);
    public void TakeDamage(float damage, float3 knockback, Entity attacker = null);
    public bool IsDead{get;}
}

public struct Vitality{
    [Serializable]
    public class Stats{
        public float MaxHealth;
        public float NaturalRegen;
        public float AttackDistance;
        public float AttackDamage;
        public float AttackCooldown;
        public float KBStrength;
        public float InvincTime;
        public float HoldBreathTime;
        [Range(0, 1)]
        public float HuntThreshold;
        [Range(0, 1)]
        public float MateThreshold;
        public float PregnacyLength;
        public float ConsumptionRate;
        public float MateCost; //Everything );

        [Range(0,1)]
        public float weight;
    }

    private Stats stats;
    public float health;
    public float invincibility;
    public float attackCooldown;
    public float healthPercent => health / stats.MaxHealth;
    public float breath;
    public float breathPercent => breath / stats.HoldBreathTime;
    public bool IsDead => health <= 0;
    public const float FallDmgThresh = 10;
    public Vitality(Stats stats, ref Unity.Mathematics.Random random){
        this.stats = stats;
        invincibility = 0;
        attackCooldown = 0;
        float initHealth = math.clamp(stats.HuntThreshold + (stats.MateThreshold - stats.HuntThreshold) * random.NextFloat(), 0, 1);
        health = stats.MaxHealth * initHealth;
        breath = stats.HoldBreathTime;
    }

    public void Deserialize(Stats stats){
        this.stats = stats;
        invincibility = 0;
        attackCooldown = 0;
    }

    public void Update(){
        invincibility = math.max(invincibility - EntityJob.cxt.deltaTime, 0);
        attackCooldown = math.max(attackCooldown - EntityJob.cxt.deltaTime, 0);
        if(IsDead) return;
        float delta = math.min(health + stats.NaturalRegen * EntityJob.cxt.deltaTime, 
                      stats.MaxHealth) - health;
        health += delta;
    }
    public bool Damage(float delta){
        if(invincibility > 0) return false;
        invincibility = stats.InvincTime;
        delta = health - math.max(health - delta, 0);
        health -= delta;
        return true;
    }

    public bool Attack(Entity target, Entity self){
        if(attackCooldown > 0) return false;
        if(target is not IAttackable) return false;
        attackCooldown = stats.AttackCooldown;
        float damage = stats.AttackDamage;
        float3 knockback = math.normalize(target.position - self.position) * stats.KBStrength;
        EntityManager.AddHandlerEvent(() => (target as IAttackable).TakeDamage(damage, knockback, self));
        return true;
    }

    public void Heal(float delta, bool force = false){
        if(force) {health += delta; return;}
        if(IsDead) return;
        health = math.min(health + delta, stats.MaxHealth);
    }

    public void ProcessSuffocation(Entity self, float density){
        if(density <= 0) return;
        if(self is not IAttackable) return;
        IAttackable target = (IAttackable)self;
        if(target.IsDead) return;
        EntityManager.AddHandlerEvent(() => target.TakeDamage(density/255.0f, 0, null));
    }

    public void ProcessInGas(float density){
        breath = stats.HoldBreathTime;
    }

    public void ProcessInLiquid(Entity self, ref TerrainColliderJob tCollider, float density){
        breath = math.max(breath - EntityJob.cxt.deltaTime, 0);
        tCollider.velocity += EntityJob.cxt.deltaTime * -EntityJob.cxt.gravity;
        tCollider.useGravity = false;
        if(breath > 0) return;
        //If dead don't process suffocation
        if(self is IAttackable target && target.IsDead) return; 
        ProcessSuffocation(self, density);
    }

    public void ProcessInLiquidAquatic(Entity self, ref TerrainColliderJob tCollider, float density, float drownTime){
        if(breath >= 0) breath = -drownTime;

        const float Epsilon = 0.001f;
        breath = math.min(breath + EntityJob.cxt.deltaTime, -Epsilon);
        tCollider.useGravity = false;

        if (self is IAttackable target && target.IsDead){ //If dead float to the surface
            tCollider.velocity += EntityJob.cxt.deltaTime * -EntityJob.cxt.gravity;
            return; //don't process suffocation
        }

        if(breath >= -Epsilon) ProcessSuffocation(self, density);
    }

    public void ProcessInGasAquatic(Entity self, ref TerrainColliderJob tCollider, float density){
        if(breath < 0) breath = stats.HoldBreathTime;
        breath = math.max(breath - EntityJob.cxt.deltaTime, 0);
        tCollider.useGravity = true;

        if(self is IAttackable target && target.IsDead) return; //If dead don't process suffocation
        if(breath <= 0) ProcessSuffocation(self, density);
    }

    [Serializable]
    public struct Decomposition{
        public Option<List<LootInfo>> LootTable;
        public float DecompositionTime; //~300 seconds
        public WorldConfig.Generation.Item.IItem LootItem(float collectRate, ref Unity.Mathematics.Random random){
            if(LootTable.value == null || LootTable.value.Count == 0) return null;
            int index = random.NextInt(LootTable.value.Count);
            
            float delta = LootTable.value[index].DropAmount * collectRate;
            int amount = Mathf.FloorToInt(delta) + (random.NextFloat() < math.frac(delta) ? 1 : 0);
            if(amount == 0) return null;
    
            Registry<WorldConfig.Generation.Item.Authoring> registry = Config.CURRENT.Generation.Items;
            int itemindex = registry.RetrieveIndex(LootTable.value[index].ItemName);
            WorldConfig.Generation.Item.IItem item = registry.Retrieve(itemindex).Item;
            item.Create(itemindex, amount);
            return item;
        }
        [Serializable]
        public struct LootInfo{
            public string ItemName;
            public float DropAmount;
        }
    }

    [Serializable]
    public struct Aquatic{
        public float DrownTime;
        [Range(0, 1)]
        //Threshold at which the entity will try to swim to the surface
        public float SurfaceThreshold; 
        public float JumpStickDistance;
        public float JumpStrength;
        public EntitySetting.ProfileInfo SurfaceProfile;
    }
}