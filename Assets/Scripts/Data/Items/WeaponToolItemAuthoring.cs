using System;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Core.Terrain;
using Arterra.Core.Player;
using static Arterra.Core.Player.PlayerInteraction;


namespace Arterra.Config.Generation.Item
{
    //This type inherits ToolItemAuthoring which inherits AuthoringTemplate<ToolItem>  which
    //inherits Authoring which inherits Category<Authoring>
    [CreateAssetMenu(menuName = "Generation/Items/WeaponTool")]
    public class WeaponToolItemAuthoring : ToolItemAuthoring {
        public float AttackDamage;
        public float KnockBackStrength;
        public float AttackCooldown;
        public float CritChance;
        public float CritMultiplier;
        public override IItem Item => new WeaponToolItem();
    }

    [Serializable]
    public class WeaponToolItem : ToolItem, IUpdateSubscriber {
        private ItemContext cxt;
        private bool active = false;
        public bool Active {
            get => active;
            set => active = value;
        }
        private WeaponToolItemAuthoring settings => ItemInfo.Retrieve(Index) as WeaponToolItemAuthoring;
        public override object Clone() => new WeaponToolItem { data = data, durability = durability };
        public override void OnEnter(ItemContext cxt) {
            if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
            if (cxt.TryGetHolder(out IActionEffect effect) && settings.Model.Enabled)
                effect.Play("HoldItem", settings.Model.Value);
            OctreeTerrain.MainLoopUpdateTasks.Enqueue(this);
            this.active = true; this.cxt = cxt;
            InputPoller.AddKeyBindChange(() => {
                InputPoller.AddBinding(new ActionBind(
                    "Remove", PlayerAttack,
                    ActionBind.Exclusion.ExcludeLayer),
                    "ITEM::WeaponTool:RM", "5.0::GamePlay"
                );
            });
        }

        public override void OnLeave(ItemContext cxt) {
            if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
            if (cxt.TryGetHolder(out IActionEffect effect) && settings.Model.Enabled)
                effect.Play("UnHoldItem", settings.Model.Value);
            this.active = false;
            this.cxt = null;
            InputPoller.AddKeyBindChange(() => {
                InputPoller.RemoveBinding("ITEM::WeaponTool:RM", "5.0::GamePlay");
            });
        }

        public override void AttachDisplay(Transform parent) {
            base.AttachDisplay(parent);
            if (display == null) return;
            UnityEngine.UI.Image shadow = display.transform.Find("Item").GetComponent<UnityEngine.UI.Image>();
            shadow.sprite = TextureAtlas.Retrieve(ItemInfo.Retrieve(Index).TextureName).self;
            shadow.color = new (1, 1, 1, 0.4f);
        }

        public override void ClearDisplay(Transform parent) {
            if (display == null) return;
            display.GetComponent<UnityEngine.UI.Image>().fillAmount = 1;
            UnityEngine.UI.Image shadow = display.transform.Find("Item").GetComponent<UnityEngine.UI.Image>();
            shadow.color = new (0, 0, 0, 0);
            shadow.sprite = null;
            base.ClearDisplay(parent);
        }

        public void Update(MonoBehaviour mono = null) {
            if (display == null) return;
            if (!cxt.TryGetHolder(out PlayerStreamer.Player player)) return;
            float progress = player.vitality.AttackCooldown / settings.AttackCooldown;
            display.GetComponent<UnityEngine.UI.Image>().fillAmount = 1 - progress;
        }

        private void PlayerAttack(float _) {
            if (!cxt.TryGetHolder(out PlayerStreamer.Player player)) return;
            if (player.vitality.AttackCooldown > 0) return;
            player.vitality.AttackCooldown = settings.AttackCooldown;
            float3 hitPt = player.head
                + player.Forward
                * Config.CURRENT.GamePlay.Player.value.Interaction.value.ReachDistance;

            if (settings.OnUseAnim.Enabled && player is IActionEffect effectable)
                effectable.Play(settings.OnUseAnim.Value);
                    
            if (RayTestSolid(out float3 terrHit)) hitPt = terrHit;
            if (!EntityManager.ESTree.FindClosestAlongRay(player.head, hitPt, player.info.entityId, out Entity.Entity entity))
                return;
            void PlayerDamageEntity(Entity.Entity target) {
                if (!target.active) return;
                if (target is not IAttackable) return;
                IAttackable atkEntity = target as IAttackable;
                float3 knockback = math.normalize(target.position - player.head)
                    * settings.KnockBackStrength;

                float atkDmg = settings.AttackDamage;
                if (UnityEngine.Random.Range(0, 1) <= settings.CritChance)
                    atkDmg *= settings.CritMultiplier;

                atkEntity.TakeDamage(atkDmg, knockback, player);
                durability--;
            
                if (durability > 0) return;
                cxt.TryRemove();
            }
            EntityManager.AddHandlerEvent(() => PlayerDamageEntity(entity));
        }
    }
}
