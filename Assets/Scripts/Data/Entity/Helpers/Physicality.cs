using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using WorldConfig;
using WorldConfig.Generation.Entity;

// <summary> An interface for all object that can be attacked and take damage. It is up to the 
/// implementer to decide how the request to take damage is handled. </summary>
public interface IAttackable{
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
    public bool IsDead => health <= 0;
    public const float FallDmgThresh = 10;
    public Vitality(Stats stats, ref Unity.Mathematics.Random random){
        this.stats = stats;
        invincibility = 0;
        attackCooldown = 0;
        float initHealth = math.clamp(stats.HuntThreshold + (stats.MateThreshold - stats.HuntThreshold) * random.NextFloat(), 0, 1);
        health = stats.MaxHealth * initHealth;
    }

    public void Deserialize(Stats stats){
        this.stats = stats;
        invincibility = 0;
        attackCooldown = 0;
    }

    public void Update(){
        if(IsDead) return;
        invincibility = math.max(invincibility - EntityJob.cxt.deltaTime, 0);
        attackCooldown = math.max(attackCooldown - EntityJob.cxt.deltaTime, 0);
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

    [Serializable]
    public struct Decomposition{
        public Option<List<LootInfo>> LootTable;
        public float DecompositionTime; //~300 seconds
        public float DecompPerLoot;
        public WorldConfig.Generation.Item.IItem LootItem(float collectRate, ref Unity.Mathematics.Random random){
            if(LootTable.value == null || LootTable.value.Count == 0) return null;
            int index = random.NextInt() % LootTable.value.Count;

            float delta = LootTable.value[index].DropAmount * collectRate;
            int amount = Mathf.FloorToInt(delta) + (random.NextFloat() < math.frac(delta) ? 1 : 0);
            if(amount == 0) return null;
    
            Registry<WorldConfig.Generation.Item.Authoring> registry = Config.CURRENT.Generation.Items;
            int itemindex = registry.RetrieveIndex(LootTable.value[index].ItemName);
            WorldConfig.Generation.Item.IItem item = registry.Retrieve(itemindex).Item;
            item.Index = itemindex;
            item.AmountRaw = amount;
            return item;
        }
        [Serializable]
        public struct LootInfo{
            public string ItemName;
            public float DropAmount;
        }
    }
}