using Arterra.Core.Events;
using Unity.Mathematics;
using Arterra.Core;
using Arterra.Configuration.Generation.Entity;
using Utils;
using UnityEditor;
using Arterra.Configuration;
using Arterra.Core.Player;

namespace Arterra.UI.ToolTips {
    /// <summary>
    /// Tooltip System managing all tooltips used in the game.
    /// </summary>
    public static class TooltipSystem {

        /// <summary>
        /// Initializes the tooltip system. 
        /// 
        /// Hooks into necessary events to snoop for tooltip.
        /// Read configuration data for tooltip display settings. 
        /// </summary>
        public static void Initialize() {
            // Read settings from configuration.
            _settings = Config.CURRENT.System.Tooltips;

            // Initialize tooltip pipeline.
            TooltipPipeline.Instance.Initialize(_settings.MinDisplayTimeInSeconds, _settings.MaxPopups);

            // Hook player events.
            if (_settings.EnableTooltips)
                HookEvents();
        }

        private static Config.TooltipSettings _settings;


        // Hook events to snoop for tooltip triggers.
        private static void HookEvents() {
            // Read from registry which events to hook.
            var ttSettings = Config.CURRENT.System.Tooltips;
            foreach (var gameEvent in ttSettings.TooltipEvents.value) {
                PlayerHandler.data.eventCtrl.AddContextlessEventHandler(gameEvent, (object actor, object target) => {
                    if (target is Entity) {
                        if (!CheckEntityToolTipTag(gameEvent, (Entity)target, out ToolTipTag tag)) {
                            ADebug.LogWarning("TooltipSys: No tooltip tag found for entity.");
                            return;
                        }

                        foreach(var ttConfig in tag.ToolTips.value) {
                            if (ttConfig.TriggerEvent == gameEvent) {
                                string tooltipName = $"{gameEvent} - {((Entity)target).info.entityId} - {ttConfig.PrefabPath}";
                                TooltipData tooltipData = new TooltipData(ttConfig);

                                TooltipPipeline.Instance.EnqueueTooltip(tooltipData);
                                
                                ADebug.LogInfo($"TooltipSys: Showing tooltip for entity {((Entity)target).info.entityId} - {gameEvent} - Tooltip message placeholder");
                                    
                            }
                        }
                        
                    }
                    else {
                        ADebug.LogDebug($"TooltipSys: Non-entity target event received for event {gameEvent}.");
                    }
                });
            }

        }

        private static bool CheckEntityToolTipTag(GameEvent evt, Entity entity, out ToolTipTag tag) {
            tag = null;
            var entityInfo = Config.CURRENT.Generation.Entities;
            if (entityInfo.GetMostSpecificTag(TagRegistry.Tags.ToolTip, entity.Index, out object tagValueObj)) {
                var tagValue = (ToolTipTag)tagValueObj;
                // Check if the tag has tooltip for this event.
                if (tagValue.ToolTips.value != null) {
                    foreach (var tooltip in tagValue.ToolTips.value) {
                        if (tooltip.TriggerEvent == evt) {
                            tag = tagValue;
                            ADebug.LogInfo($"TooltipSys: Found matching tooltip for event - {tooltip.TriggerEvent}.");
                            return true;
                        }
                        else {
                            ADebug.LogInfo($"TooltipSys: No matching tooltip for this event - {tooltip.TriggerEvent}.");
                            continue;
                        }
                    }
                }
            }
            return false;
        }

        // Helper functions
        private static ToolTipTag GetTooltipTag(Entity entity) {
            var entityInfo = Config.CURRENT.Generation.Entities;
            if (entityInfo.GetMostSpecificTag(TagRegistry.Tags.ToolTip, entity.Index, out object tagValueObj)) {
                return(ToolTipTag)tagValueObj;
            }
            else {
                return null;
            }
        }
    }
}