using System;
using Unity.Mathematics;
using WorldConfig.Gameplay.Player;
using WorldConfig;
using UnityEngine;
using Newtonsoft.Json;
using WorldConfig.Generation.Entity;

namespace WorldConfig.Gameplay.Player {
    /// <summary> Settings describing the player's vitaltiy, or the 
    /// combat ability and/or physical statistics of the player. </summary>
    [Serializable]
    public class Physicality : ICloneable {
        /// <summary>The base damage the player deals when attacking entities without using any items. </summary>
        public float AttackDamage = 2;
        /// <summary>The amount of time in seconds between successive attacks from the player that will be processed.</summary>
        public float AttackFrequency = 0.25f;
        public float KnockBackStrength = 5f;
        public float InvincTime;
        public float HoldBreathTime;
        public float MaxHealth;
        public float NaturalRegen;
        public float DecompositionTime;

        [Range(0, 1)]
        public float weight;

        public object Clone() {
            return new Physicality {
                AttackDamage = AttackDamage,
                AttackFrequency = AttackFrequency,
                KnockBackStrength = KnockBackStrength,
                InvincTime = InvincTime,
                HoldBreathTime = HoldBreathTime,
                MaxHealth = MaxHealth,
                NaturalRegen = NaturalRegen,
                DecompositionTime = DecompositionTime,
                weight = weight
            };
        }
    }
}
public class PlayerVitality
{
    public static Interaction interact => Config.CURRENT.GamePlay.Player.value.Interaction;
    public static Physicality settings => Config.CURRENT.GamePlay.Player.value.Physicality;
    public float AttackCooldown;
    public float Invincibility;
    public float health;
    public float healthPercent => health / settings.MaxHealth;
    public float breath;
    public float breathPercent => breath / settings.HoldBreathTime;
    [JsonIgnore]
    public bool IsDead{get => health <= 0; }
    public PlayerVitality(){
        InputPoller.AddBinding(new ActionBind("Attack", AttackEntity), "PlayerVitality:ATK", "5.0::GamePlay");
        AttackCooldown = 0;
        Invincibility = 0;
        health = settings.MaxHealth;
        breath = settings.HoldBreathTime;
    }

    public void Update(){
        Invincibility = math.max(Invincibility - EntityJob.cxt.deltaTime, 0);
        AttackCooldown = math.max(AttackCooldown - EntityJob.cxt.deltaTime, 0);
        if (IsDead) return;
        float delta = math.min(health + settings.NaturalRegen * EntityJob.cxt.deltaTime, 
                      settings.MaxHealth) - health;
        health += delta;
    }

    public bool Damage(float delta){
        if(Invincibility > 0) return false;
        Invincibility = settings.InvincTime;
        delta = health - math.max(health - delta, 0);
        health -= delta;
        return true;
    }
    
    public void Heal(float delta, bool force = false){
        if(force) {health += delta; return;}
        if(IsDead) return;
        health = math.min(health + delta, settings.MaxHealth);
    }

    private void AttackEntity(float _)
    {
        if (AttackCooldown > 0) return;
        AttackCooldown = settings.AttackFrequency;
        float3 hitPt = PlayerHandler.data.position + PlayerHandler.data.Forward * interact.ReachDistance;
        if (PlayerInteraction.RayTestSolid(PlayerHandler.data, out float3 terrHit)) hitPt = terrHit;
        if (!EntityManager.ESTree.FindClosestAlongRay(PlayerHandler.data.position, hitPt, PlayerHandler.data.info.entityId, out WorldConfig.Generation.Entity.Entity entity))
            return;
        static void PlayerDamageEntity(Entity target)
        {
            if (!target.active) return;
            if (target is not IAttackable) return;
            IAttackable atkEntity = target as IAttackable;
            float3 knockback = math.normalize(target.position - PlayerHandler.data.position) * settings.KnockBackStrength;
            atkEntity.TakeDamage(settings.AttackDamage, knockback, PlayerHandler.data);
            PlayerHandler.data.Play("Punch");
        }
        EntityManager.AddHandlerEvent(() => PlayerDamageEntity(entity));
    }

     public void ProcessSuffocation(Entity self, float density){
        if(density <= 0) return;
        if(self is not IAttackable) return;
        IAttackable target = (IAttackable)self;
        if(target.IsDead) return;
        target.TakeDamage(density/255.0f, 0, null);
    }

    public void ProcessInGas(float density){
        breath = settings.HoldBreathTime;
        SwimMovement.StopSwim(density);
    }

    public void ProcessInLiquid(Entity self, float density){
        SwimMovement.StartSwim(density);
        breath = math.max(breath - Time.fixedDeltaTime, 0);
        if(breath > 0) return;
        ProcessSuffocation(self, density);
    }
}
