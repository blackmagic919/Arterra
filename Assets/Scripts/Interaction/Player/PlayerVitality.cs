using System;
using Unity.Mathematics;
using Arterra.Config.Gameplay.Player;
using Arterra.Config;
using UnityEngine;
using Newtonsoft.Json;
using Arterra.Config.Generation.Entity;
using Arterra.Core.Player;
using Arterra.Core.Events;

namespace Arterra.Config.Gameplay.Player {
    /// <summary> Settings describing the player's vitaltiy, or the 
    /// combat ability and/or physical statistics of the player. </summary>
    [Serializable]
    public class Physicality : ICloneable {
        /// <summary>The base damage the player deals when attacking entities without using any items. </summary>
        public float AttackDamage = 2;
        /// <summary>The amount of time in seconds between successive attacks from the player that will be processed.</summary>
        public float AttackFrequency = 0.25f;
        /// <summary> The strength by entities are knocked back when hit by the player.  </summary>
        public float KnockBackStrength = 5f;
        /// <summary> The amount of time the player is invincible after taking damage. </summary>
        public float InvincTime;
        /// <summary> The amount of time the player can stay underwater before they start suffocating.  </summary>
        public float HoldBreathTime;
        /// <summary> The maximum health of the player.  </summary>
        public float MaxHealth;
        /// <summary> How much player's health will change per second. </summary>
        public float NaturalRegen;
        /// <summary> The amount of time after the player dies until its body dissapears.  </summary>
        public float DecompositionTime;
        /// <summary> The weight of the player </summary>

        [Range(0, 1)]
        public float weight;
        /// <summary> Clones the settings </summary>
        /// <returns>The cloned object</returns>
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

namespace Arterra.Core.Player {
    /// <summary> Manager controlling the player's health, regenertion
    /// and base attack functionalities.  </summary>
    public class PlayerVitality
    {
        private static Interaction interact => Config.Config.CURRENT.GamePlay.Player.value.Interaction;
        /// <summary> The player's physicality settings. </summary>
        public static Physicality settings => Config.Config.CURRENT.GamePlay.Player.value.Physicality;
        /// <summary>The time in seconds before the player can initiate attacking.</summary>
        public float AttackCooldown;
        /// <summary>The time in seconds before the player can recieve damage again. </summary>
        public float Invincibility;
        /// <summary> The player's current health.</summary>
        public float health;
        /// <summary>The player's health normalized over their <see cref="Physicality.MaxHealth">maximum health</see> </summary>
        public float healthPercent => health / settings.MaxHealth;
        /// <summary>The time in seconds before the player will start drowning.</summary>
        public float breath;
        /// <summary> The amount of remaining time the player can hold their breath 
        /// normalized over the <see cref="Physicality.HoldBreathTime">maximum time</see>
        /// the player can hold their breath. </summary>
        public float breathPercent => breath / settings.HoldBreathTime;
        /// <summary> Whether or not the player is dead. </summary>
        [JsonIgnore]
        public bool IsDead{get => health <= 0; }

        /// <summary>Creates a new instance of the player's
        /// vitality. Attaches keybinds for player attack. </summary>
        public PlayerVitality(){
            InputPoller.AddBinding(new ActionBind("Attack", AttackEntity), "PlayerVitality:ATK", "5.0::GamePlay");
            AttackCooldown = 0;
            Invincibility = 0;
            health = settings.MaxHealth;
            breath = settings.HoldBreathTime;
        }

        /// <summary> Updates the player's vitality controller including
        /// any relevant cooldown timers.  </summary>
        public void Update(){
            Invincibility = math.max(Invincibility - EntityJob.cxt.deltaTime, 0);
            AttackCooldown = math.max(AttackCooldown - EntityJob.cxt.deltaTime, 0);
            if (IsDead) return;
            float delta = math.min(health + settings.NaturalRegen * EntityJob.cxt.deltaTime, 
                        settings.MaxHealth) - health;
            health += delta;
        }

        /// <summary>Requests that the player recieves damage.</summary>
        /// <param name="delta">The amount of damage to recieve</param>
        /// <returns>Whether or not damage was dealt based on
        /// the player's defenses/invincibility.</returns>
        public bool Damage(float delta){
            if(Invincibility > 0) return false;
            Invincibility = settings.InvincTime;
            delta = health - math.max(health - delta, 0);
            health -= delta;
            return true;
        }
        
        /// <summary> Attempts to give the player health. </summary>
        /// <param name="delta">The amount of health to give the player </param>
        /// <param name="force">Whether to force giving the player the requested health,
        /// potentially exceeding their maximum health or reviving them. </param>
        public void Heal(float delta, bool force = false){
            if(force) {health += delta; return;}
            if(IsDead) return;
            health = math.min(health + delta, settings.MaxHealth);
        }

        private void AttackEntity(float _)
        {
            if (AttackCooldown > 0) return;
            AttackCooldown = settings.AttackFrequency;
            float3 hitPt = PlayerHandler.data.head + PlayerHandler.data.Forward * interact.ReachDistance;
            if (PlayerInteraction.RayTestSolid(out float3 terrHit)) hitPt = terrHit;
            if (!EntityManager.ESTree.FindClosestAlongRay(PlayerHandler.data.head, hitPt, PlayerHandler.data.info.entityId, out Entity entity))
                return;
            
            static void PlayerDamageEntity(Entity target)
            {
                if (!target.active) return;
                if (target is not IAttackable) return;
                IAttackable atkEntity = target as IAttackable;
                float3 knockback = math.normalize(target.position - PlayerHandler.data.head) * settings.KnockBackStrength;
                float damage = settings.AttackDamage;
                
                var cxt = (damage, knockback);
                PlayerHandler.data.eventCtrl.RaiseEvent(
                    GameEvent.Entity_Attack,
                    PlayerHandler.data,
                    target, ref cxt
                ); (damage, knockback) = cxt;
                
                atkEntity.TakeDamage(settings.AttackDamage, knockback, PlayerHandler.data);
            }
            EntityManager.AddHandlerEvent(() => PlayerDamageEntity(entity));
        }

        /// <summary>Processes damaging the player
        /// whose head being trapped in the terrain.</summary>
        /// <param name="self">The player entity</param>
        /// <param name="density">The density of the terrain around the player's head.
        /// The amount of damage to apply to the player</param>
        public void ProcessEntityInSolid(Entity self, float density) {
            self.eventCtrl.RaiseEvent(GameEvent.Entity_InSolid, self, null, ref density);
            ProcessSuffocation(self, density);
        }

        private void ProcessSuffocation(Entity self, float density){
            if(density <= 0) return;
            if(self is not IAttackable) return;
            IAttackable target = (IAttackable)self;
            if(target.IsDead) return;
            target.TakeDamage(density/255.0f, 0, null);
        }

        /// <summary>Processes what happens when the player's
        /// head is neither underwater or underground. </summary>
        /// <param name="density">The density of the gas surrounding the player's head</param>
        public void ProcessInGas(Entity self, float density){
            self.eventCtrl.RaiseEvent(GameEvent.Entity_InGas, self, null, ref density);
            breath = settings.HoldBreathTime;
        }

        /// <summary>Processes drowning the player whose head
        /// is under a liquid material. </summary>
        /// <param name="self">The player entity</param>
        /// <param name="density">The density of the liquid surrounding the player's head</param>
        public void ProcessInLiquid(Entity self, float density){
            self.eventCtrl.RaiseEvent(GameEvent.Entity_InLiquid, self, null, ref density);
            breath = math.max(breath - Time.fixedDeltaTime, 0);
            if(breath > 0) return;
            ProcessSuffocation(self, density);
        }
    }
}