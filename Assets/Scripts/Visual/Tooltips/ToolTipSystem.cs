using Arterra.Core.Events;
using Unity.Mathematics;
using Arterra.Core;
using Arterra.Data.Entity;
using Arterra.Utils;
using UnityEditor;
using Arterra.Configuration;
using Arterra.GamePlay;
using System.Collections.Generic;
using System.Linq;

namespace Arterra.GamePlay.UI.ToolTips {
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
                    if (target is IRegistered entry) {
                        if (!CheckForTooltipTag(gameEvent, entry, out TooltipTag tag)) {
                            ADebug.LogWarning("TooltipSys: No tooltip tag found for target.");
                            return;
                        }

                        foreach(var ttConfig in tag.Tooltips.value) {
                            if (ttConfig.TriggerEvent == gameEvent) {
                                string tooltipName = $"{gameEvent} - {entry.Index} - {ttConfig.PrefabPath}";
                                TooltipData tooltipData = new TooltipData(ttConfig);
                                
                                TooltipPipeline.Instance.EnqueueTooltip(tooltipData);
                                
                                ADebug.LogInfo($"TooltipSys: Showing tooltip for target {entry.Index} - {gameEvent} - Tooltip message placeholder");
                                    
                            }
                        }
                        
                    }
                    else {
                        ADebug.LogDebug($"TooltipSys: Non-registered target event received for event {gameEvent}.");
                    }
                });
            }

            foreach(var gameEvent in ttSettings.TooltipDismissorEvents.value) {
                PlayerHandler.data.eventCtrl.AddContextlessEventHandler(gameEvent, (object actor, object target) => {
                    if (target is IRegistered entry) {
                        if (!CheckForTooltipDismissorTag(gameEvent, entry, out TooltipDismissorTag tag)) {
                            ADebug.LogWarning("TooltipSys: No tooltip tag found for target.");
                            return;
                        }

                        foreach(var dismissor in tag.Dismissors.value) {
                            if (dismissor.DismissEvent == gameEvent) {  
                                TooltipPipeline.Instance.DismissTooltip(dismissor.PrefabPath);                            
                                ADebug.LogInfo($"TooltipSys: Dismissing tooltip for prefab {dismissor.PrefabPath} on target {entry.Index} - {gameEvent} - Tooltip dismissor placeholder");
                                    
                            }
                        }
                        
                    }
                    else {
                        ADebug.LogDebug("TooltipSys: Non-registered target dismissor event received.");
                    }
                });
            }

            //Deserialize tooltips by re-enqueing them
            TooltipData[] ActiveToolTips = TooltipPipeline.Instance.State.activeToolTips.Values.ToArray();
            TooltipPipeline.Instance.State.activeToolTips.Clear(); //Clear so we don't block them
            foreach (var tooltip in ActiveToolTips) {
                TooltipPipeline.Instance.EnqueueTooltip(tooltip);
            }

            TooltipDismissorConfig[] DismissorToolTips = TooltipPipeline.Instance.State.dismissEvents.ToArray();
            foreach (var dismissor in DismissorToolTips) {
                RefEventHandler lambda = null;
                lambda = PlayerHandler.data.eventCtrl.AddContextlessEventHandler(dismissor.DismissEvent, DismissGenericToolTip);

                void DismissGenericToolTip(object _1, object _2) {
                    PlayerHandler.data.eventCtrl.RemoveEventHandler(dismissor.DismissEvent, lambda);
                    if (dismissor.DismissType == TooltipDismissorConfig.DismissTypes.Dismiss)
                        TooltipPipeline.Instance.DismissTooltip(dismissor.PrefabPath);
                    else TooltipPipeline.Instance.IgnoreToolTip(dismissor.PrefabPath);
                }
            }

            foreach(var toolTip in TooltipPipeline.Instance.State.pendingToolTips.Values) {
                if (toolTip.TriggerEvent == GameEvent.None) {
                    TooltipPipeline.Instance.EnqueueTooltip(new TooltipData(toolTip));
                    continue;
                }

                RefEventHandler lambda = null;
                lambda = PlayerHandler.data.eventCtrl.AddContextlessEventHandler(toolTip.TriggerEvent, TriggerGenericToolTip);

                void TriggerGenericToolTip(object _1, object _2) {
                    TooltipData tooltipData = new TooltipData(toolTip);
                    if (TooltipPipeline.Instance.IsBlacklisted(tooltipData)) {
                        PlayerHandler.data.eventCtrl.RemoveEventHandler(toolTip.TriggerEvent, lambda);
                        TooltipPipeline.Instance.State.pendingToolTips.Remove(toolTip.PrefabPath);
                    } TooltipPipeline.Instance.EnqueueTooltip(tooltipData);
                }
            }
        }


        private static bool CheckForTooltipTag(GameEvent evt, IRegistered target, out TooltipTag tag) {
            tag = null;
            if (target.GetRegistry() is not ICatalgoue catalogue) {
                ADebug.LogInfo($"TooltipSys: Expected target {target} is a catalogue registered element.");
                return false;
            }

            if (catalogue.GetMostSpecificTag(TagRegistry.Tags.Tooltip, target.Index, out object tagValueObj)) {
                var tagValue = (TooltipTag)tagValueObj;
                // Check if the tag has tooltip for this event.
                if (tagValue.Tooltips.value != null) {
                    foreach (var tooltip in tagValue.Tooltips.value) {
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

        private static bool CheckForTooltipDismissorTag(GameEvent evt, IRegistered target, out TooltipDismissorTag tag) {
            tag = null;
            if (target.GetRegistry() is not ICatalgoue catalogue) {
                ADebug.LogInfo($"TooltipSys: Expected target {target} is a catalogue registered element.");
                return false;
            }

            if (catalogue.GetMostSpecificTag(TagRegistry.Tags.TooltipDismissor, target.Index, out object tagValueObj)) {
                var tagValue = (TooltipDismissorTag)tagValueObj;
                // Check if the tag has tooltip for this event.
                if (tagValue.Dismissors.value != null) {
                    foreach (var dismissor in tagValue.Dismissors.value) {
                        if (dismissor.DismissEvent == evt) {
                            tag = tagValue;
                            ADebug.LogInfo($"TooltipSys: Found matching tooltip dismissor for event - {dismissor.DismissEvent}.");
                            return true;
                        }
                        else {
                            ADebug.LogInfo($"TooltipSys: No matching tooltip dismissor for this event - {dismissor.DismissEvent}.");
                            continue;
                        }
                    }
                }
            }
            return false;
        }

        // Helper functions
        private static TooltipTag GetTooltipTag(Entity entity) {
            var entityInfo = Config.CURRENT.Generation.Entities;
            if (entityInfo.GetMostSpecificTag(TagRegistry.Tags.Tooltip, entity.Index, out object tagValueObj)) {
                return (TooltipTag)tagValueObj;
            }
            else {
                return null;
            }
        }
    }
}