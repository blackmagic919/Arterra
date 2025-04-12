using System;
using Unity.Mathematics;
using static CPUMapManager;
using WorldConfig.Gameplay.Player;
using WorldConfig;
using UnityEngine;
using Newtonsoft.Json;

namespace WorldConfig.Gameplay.Player {
    /// <summary> Settings describing the player's vitaltiy, or the 
    /// combat ability and/or physical statistics of the player. </summary>
    [Serializable]
    public class Physicality{
        /// <summary>The base damage the player deals when attacking entities without using any items. </summary>
        public float AttackDamage = 2;
        /// <summary>The amount of time in seconds between successive attacks from the player that will be processed.</summary>
        public float AttackFrequency = 0.25f;
        public float KnockBackStrength = 5f;
        public float InvincTime;
        public float MaxHealth;
        public float NaturalRegen;
        public float DecompositionTime;

        [Range(0, 1)]
        public float weight;
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
    [JsonIgnore]
    public bool IsDead{get => health <= 0; }
    public PlayerVitality(){
        InputPoller.AddBinding(new InputPoller.ActionBind("Attack", AttackEntity), "5.0::GamePlay");
        AttackCooldown = 0;
        Invincibility = 0;
        health = settings.MaxHealth;
    }

    public void Update(){
        Invincibility = math.max(Invincibility - EntityJob.cxt.deltaTime, 0);
        AttackCooldown = math.max(AttackCooldown - EntityJob.cxt.deltaTime, 0);
        if(IsDead) return;
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

    private void AttackEntity(float _){
        if(AttackCooldown > 0) return;
        AttackCooldown = settings.AttackFrequency;
        float3 hitPt = PlayerHandler.data.position + (float3)PlayerHandler.camera.forward * interact.ReachDistance;
        if(PlayerInteraction.RayTestSolid(PlayerHandler.data, out float3 terrHit)) hitPt = terrHit;
        if(!EntityManager.ESTree.FindClosestAlongRay(PlayerHandler.data.position, hitPt, PlayerHandler.data.info.entityId, out WorldConfig.Generation.Entity.Entity entity))
            return;
        static void PlayerDamageEntity(WorldConfig.Generation.Entity.Entity target){
            if(!target.active) return;
            if(target is not IAttackable) return;
            IAttackable atkEntity = target as IAttackable;
            float3 knockback = math.normalize(target.position - PlayerHandler.data.position) * settings.KnockBackStrength;
            atkEntity.TakeDamage(settings.AttackDamage, knockback, PlayerHandler.data);

            string anim = UnityEngine.Random.Range(0, 2) == 0 ? "PunchR" : "PunchL";
            PlayerHandler.data.animator.SetTrigger(anim);
        }
        EntityManager.AddHandlerEvent(() => PlayerDamageEntity(entity));
    }
}
