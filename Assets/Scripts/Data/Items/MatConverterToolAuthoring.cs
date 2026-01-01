using System;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Config.Generation.Material;
using Arterra.Core.Storage;
using static Arterra.Core.Player.PlayerInteraction;
using Arterra.Core.Player;
using Arterra.Core.Events;


namespace Arterra.Config.Generation.Item
{
    //This type inherits ToolItemAuthoring which inherits AuthoringTemplate<ToolItem>  which
    //inherits Authoring which inherits Category<Authoring>
    [CreateAssetMenu(menuName = "Generation/Items/ConverterTool")]
    public class MatConverterToolAuthoring : ToolItemAuthoring {
        /// <summary> The tag used to determine what a material is converted to and how 
        /// to convert it. </summary>
        public TagRegistry.Tags ConverterTag;
        public override IItem Item => new MatConverterToolItem();
    }

    [Serializable]
    public class MatConverterToolItem : ToolItem
    {
        private MatConverterToolAuthoring settings => ItemInfo.Retrieve(Index) as MatConverterToolAuthoring;
        public override object Clone() => new MatConverterToolItem { data = data, durability = durability };
        public override void OnEnter(ItemContext cxt) {
            if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
            if (cxt.TryGetHolder(out IEventControlled effect) && settings.Model.Enabled) 
                effect.RaiseEvent(GameEvent.Item_HoldTool, effect, this, ref settings.Model.Value);
            InputPoller.AddKeyBindChange(() => {
                InputPoller.AddBinding(new ActionBind("ConvertMaterial", _ => PlayerModifyTerrain(cxt)),
                    "ITEM::MCTool:CNV", "5.0::GamePlay");
                InputPoller.AddBinding(new ActionBind("Remove", _ => PlayerRemoveTerrain(cxt)),
                    "ITEM::MCTool:RM", "5.0::GamePlay");
            });
        }

        public override void OnLeave(ItemContext cxt) {
            if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
            if (cxt.TryGetHolder(out IEventControlled effect) && settings.Model.Enabled) 
                effect.RaiseEvent(GameEvent.Item_UnholdTool, effect, this, ref settings.Model.Value);
            InputPoller.AddKeyBindChange(() => {
                InputPoller.RemoveBinding("ITEM::MCTool:CNV", "5.0::GamePlay");
                InputPoller.RemoveBinding("ITEM::MCTool:RM", "5.0::GamePlay");
            });
        }

        private void PlayerModifyTerrain(ItemContext cxt) {
            if (!cxt.TryGetHolder(out PlayerStreamer.Player player)) return;

            InputPoller.SuspendKeybindPropogation("ConvertMaterial", ActionBind.Exclusion.ExcludeLayer);
            if (!RayTestSolid(out float3 hitPt)) return;
            if (EntityManager.ESTree.FindClosestAlongRay(player.head, hitPt, player.info.entityId, out var _))
                return;
            
            bool ModifySolid(int3 GCoord, float speed) {
                MapData mapData = CPUMapManager.SampleMap(GCoord);
                int material = mapData.material;
                if (!IMaterialConverting.CanConvert(mapData, GCoord, settings.ConverterTag, out ConverterToolTag tag))
                    return false;
                if (UnityEngine.Random.Range(0.0f, 1.0f) >= tag.TerraformSpeed)
                    return false;
                if (!MaterialData.SwapMaterial(GCoord, MatInfo.RetrieveIndex(tag.ConvertTarget), out IItem origItem))
                    return false;
                if (tag.GivesItem) InventoryController.DropItem(origItem, GCoord);
                durability -= tag.ToolDamage * (mapData.density / 255.0f);
                return true;
            }

            if (settings.OnUseAnim.Enabled && player is IEventControlled effectable)
                effectable.RaiseEvent(GameEvent.Item_UseTool, player, this, ref settings.OnUseAnim.Value);

            CPUMapManager.Terraform(hitPt, settings.TerraformRadius, ModifySolid, CallOnMapRemoving);
            UpdateDisplay();

            if (durability > 0) return;
            //Removes itself
            cxt.TryRemove();
        }
    }
}
