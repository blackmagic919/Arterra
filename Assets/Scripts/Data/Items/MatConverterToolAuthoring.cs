using System;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using WorldConfig.Generation.Material;
using static PlayerInteraction;
using MapStorage;


namespace WorldConfig.Generation.Item
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
            InputPoller.AddKeyBindChange(() => {
                KeyBinds = new int[2];
                KeyBinds[0] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("ConvertMaterial", _ => PlayerModifyTerrain(cxt)), "5.0::GamePlay");
                KeyBinds[1] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Remove", _ => PlayerRemoveTerrain(cxt)), "5.0::GamePlay");
            });
        }

        public override void OnLeave(ItemContext cxt) {
            if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
            InputPoller.AddKeyBindChange(() => {
                if (KeyBinds == null) return;
                InputPoller.RemoveKeyBind((uint)KeyBinds[0], "5.0::GamePlay");
                InputPoller.RemoveKeyBind((uint)KeyBinds[1], "5.0::GamePlay");
            });
        }

        private void PlayerModifyTerrain(ItemContext cxt) {
            if (!cxt.TryGetHolder(out PlayerStreamer.Player player)) return;
            if (!RayTestSolid(player, out float3 hitPt)) return;
            if (EntityManager.ESTree.FindClosestAlongRay(player.position, hitPt, player.info.entityId, out var _))
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

            CPUMapManager.Terraform(hitPt, settings.TerraformRadius, ModifySolid, CallOnMapRemoving);
            UpdateDisplay();

            InputPoller.SuspendKeybindPropogation("ConvertMaterial", InputPoller.ActionBind.Exclusion.ExcludeLayer);
            if (durability > 0) return;
            //Removes itself
            cxt.TryRemove();
        }
    }
}
