using System;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using WorldConfig.Generation.Material;
using static PlayerInteraction;
using MapStorage;
using TerrainGeneration;


namespace WorldConfig.Generation.Item
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
        private bool active = false;
        public bool Active {
            get => active;
            set => active = value;
        }
        private WeaponToolItemAuthoring settings => ItemInfo.Retrieve(Index) as WeaponToolItemAuthoring;
        public override object Clone() => new WeaponToolItem { data = data, durability = durability };
        public override void OnSelect() {
            OctreeTerrain.MainLoopUpdateTasks.Enqueue(this);
            this.active = true;
            InputPoller.AddKeyBindChange(() => {
                KeyBinds = new int[1];
                KeyBinds[0] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Remove", AttackEntity, InputPoller.ActionBind.Exclusion.ExcludeLayer), "5.0::GamePlay");
            });
        }

        public override void OnDeselect() {
            this.active = false;
            InputPoller.AddKeyBindChange(() => {
                if (KeyBinds == null) return;
                InputPoller.RemoveKeyBind((uint)KeyBinds[0], "5.0::GamePlay");
            });
        }

        public override void AttachDisplay(Transform parent) {
            base.AttachDisplay(parent);
            if (display == null) return;
            UnityEngine.UI.Image shadow = display.transform.Find("Item").GetComponent<UnityEngine.UI.Image>();
            shadow.sprite = TextureAtlas.Retrieve(ItemInfo.Retrieve(Index).TextureName).self;
            shadow.color = new (1, 1, 1, 0.4f);
        }

        public virtual void ClearDisplay(Transform parent) {
            if (display == null) return;
            display.GetComponent<UnityEngine.UI.Image>().fillAmount = 1;
            UnityEngine.UI.Image shadow = display.transform.Find("Item").GetComponent<UnityEngine.UI.Image>();
            shadow.color = new (0, 0, 0, 0);
            shadow.sprite = null;
            base.ClearDisplay(parent);
        }

        public void Update(MonoBehaviour mono = null) {
            if (display == null) return;
            float progress = PlayerHandler.data.vitality.AttackCooldown / settings.AttackCooldown;
            display.GetComponent<UnityEngine.UI.Image>().fillAmount = 1 - progress;
        }

        private void AttackEntity(float _) {
            if (PlayerHandler.data.vitality.AttackCooldown > 0) return;
            PlayerHandler.data.vitality.AttackCooldown = settings.AttackCooldown;
            float3 hitPt = PlayerHandler.data.position + (float3)PlayerHandler.camera.forward
                * Config.CURRENT.GamePlay.Player.value.Interaction.value.ReachDistance;

            if (RayTestSolid(PlayerHandler.data, out float3 terrHit)) hitPt = terrHit;
            if (!EntityManager.ESTree.FindClosestAlongRay(PlayerHandler.data.position, hitPt, PlayerHandler.data.info.entityId, out WorldConfig.Generation.Entity.Entity entity))
                return;
            void PlayerDamageEntity(WorldConfig.Generation.Entity.Entity target) {
                if (!target.active) return;
                if (target is not IAttackable) return;
                IAttackable atkEntity = target as IAttackable;
                float3 knockback = math.normalize(target.position - PlayerHandler.data.position)
                    * settings.KnockBackStrength;

                float atkDmg = settings.AttackDamage;
                if (UnityEngine.Random.Range(0, 1) <= settings.CritChance)
                    atkDmg *= settings.CritMultiplier;

                atkEntity.TakeDamage(atkDmg, knockback, PlayerHandler.data);

                PlayerHandler.data.animator.SetTrigger("Swing");
                durability--;
            
                if (durability > 0) return;
                InventoryController.Primary.RemoveEntry(InventoryController.SelectedIndex);
            }
            EntityManager.AddHandlerEvent(() => PlayerDamageEntity(entity));
        }
    }
}
