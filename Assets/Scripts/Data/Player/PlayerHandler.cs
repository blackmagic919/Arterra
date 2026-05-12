using UnityEngine;
using System.IO;
using System;
using Arterra.Core.Events;
using Arterra.Engine.Terrain;
using Arterra.GamePlay.Interaction;
using Arterra.Configuration;
using Arterra.GamePlay.UI;
using Unity.Mathematics;
using Arterra.Core.Storage;
using Arterra.Data.Entity.Behavior;
using Arterra.Utils;
using Arterra.Data.Entity;



namespace Arterra.Configuration.Gameplay{
    /// <summary>
    /// Settings describing different game settings the player can play with.
    /// These settings may drastically change the game's difficulty and
    /// can drastically change the player's experience.
    /// </summary>
    [Serializable]
    public struct Gamemodes {
        /// <summary> Whether the player can toggle flight; whether the player can fly </summary>
        [UIModifiable(CallbackName = "Gamemode:Flight")]
        public bool Flight;
        /// <summary> Whether the player can recieve damage or die. Whether health is a concept for the player. </summary>
        [UIModifiable(CallbackName = "Gamemode:Invulnerability")]
        public bool Invulnerability;
        /// <summary> Whether the player will collide with solid objects and be subject to in-game forces.</summary>
        [UIModifiable(CallbackName = "Gamemode:Intangibility")]
        public bool Intangiblity;
        /// <summary> Whether the player can keep their inventory on death </summary>
        public bool KeepInventory;
        /// <summary> Whether or not the player can access the spectator camera </summary>
        [UIModifiable(CallbackName = "Gamemode:Spectator")]
        public bool SpectatorView;

        /// <summary> Whether or not the player can access the infinite resource inventory </summary>
        [UIModifiable(CallbackName = "Gamemode:ResourceInventory")]
        public bool ResourceInventory;
        /// <summary> Whether or not the player's cursor is shown. </summary>
        [UIModifiable(CallbackName = "ToggleUICrosshair")]
        public bool ShowCrosshair;
    }
}

namespace Arterra.GamePlay{
    /// <summary> The root handler of the currently active player.  </summary>
    /// <remarks> If the player runtime entity were analogous to the 
    /// player's current body, this class would be the player's soul </remarks>///  
    public static class PlayerHandler {
        /// <summary>The current active runtime player entity.</summary>
        public static BehaviorEntity.Animal data;
        /// <summary> The transform of the Unity gameobject representing the viewer </summary>
        public static Transform Viewer;
        /// <summary> The transform of the current Unity MainCamera  </summary>
        public static Transform Camera;
        /// <summary> Whether or not the player is active; whether or not
        /// the user has control of the player instance  </summary>
        public static bool active = false;

        /// <summary>
        /// Initializes the player and retrieves or creates a behavior-model runtime entity.
        /// </summary>
        public static void Initialize() {
            Viewer = GameObject.Find("Viewer").transform;
            Camera = Viewer.Find("CameraHandler");

            active = false;
            if (LoadPlayerData(out data) && (!data.Is(out VitalityBehavior vitality) || !vitality.IsDead)) {
                EntityManager.DeserializeE(data);
            } else RespawnPlayer(immediate: true);

            SpectatorController.Initialize();
            OctreeTerrain.MainLoopUpdateTasks.Enqueue(new IndirectUpdate(Update));
            OctreeTerrain.MainFixedUpdateTasks.Enqueue(new IndirectUpdate(FixedUpdate));
        }

        private static void Update(MonoBehaviour mono) {
            if (!data.active) return;
            if (!active && OctreeTerrain.RequestQueue.IsEmpty)
                active = true;
            if (!active) return;

            data.Update(BehaviorEntity.UpdateContext.Main);
            if (data.Is(out VitalityBehavior vitality))
                PlayerStatDisplay.UpdateIndicator(vitality);
        }

        private static void FixedUpdate(MonoBehaviour mono){
            if(!active) return;
            if(!data.active) return;
            data.Update(BehaviorEntity.UpdateContext.Fixed);
        }

        /// <summary>Releases the player handler and all resources tied to handling the player. </summary>
        public static void Release(){
            InputPoller.SetCursorLock(false); //Release Cursor Lock
            SavePlayerData(new Registerable<BehaviorEntity.Animal>(data));
            InventoryController.Release();
            PanelNavbarManager.Release();
        }
        

        static void SavePlayerData(Registerable<BehaviorEntity.Animal> playerInfo){
            string path = World.WORLD_SELECTION.First.Value.Path + "/PlayerData.json";
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)){
                using StreamWriter writer = new StreamWriter(fs);
                string data = Newtonsoft.Json.JsonConvert.SerializeObject(playerInfo);
                writer.Write(data);
                writer.Flush();
            };
        }

        static (Entity, float3) BuildPlayer() {
            float3 SpawnPoint = StartupPlacer.FindClearingAround(float3.zero);
            uint entityIndex = (uint)Config.CURRENT.Generation.Entities.RetrieveIndex("Player");
            Authoring authoring = Config.CURRENT.Generation.Entities.Reg[(int)entityIndex];
            return (authoring.Entity, SpawnPoint);
        }

        static bool LoadPlayerData(out BehaviorEntity.Animal data){
            data = null;
            string path = World.WORLD_SELECTION.First.Value.Path + "/PlayerData.json";
            if(!File.Exists(path)) return false;

            string rawData = File.ReadAllText(path);
            Registerable<BehaviorEntity.Animal> payload = Newtonsoft.Json.JsonConvert.DeserializeObject<Registerable<BehaviorEntity.Animal>>(rawData);
            data = payload.Value;
            return true;
        }

        /// <summary> Respawns the player initiating the respawn process.
        /// This process will create a new runtime player instance
        /// at a set respawn location, abandoning the current active instance. </summary>
        /// <param name="cb">The option action to perform once respawned</param>
        /// <param name="immediate"> Whether or not creation happens synchronously with call </param>
        public static void RespawnPlayer(Action cb = null, bool immediate = false){
            float3 SpawnPoint = StartupPlacer.FindClearingAround(float3.zero);
            uint entityIndex = (uint)Config.CURRENT.Generation.Entities.RetrieveIndex("Player");
            Entity oldPlayer = data;
            data = Config.CURRENT.Generation.Entities.Reg[(int)entityIndex].Entity as BehaviorEntity.Animal;
            data.context = BehaviorEntity.UpdateContext.Main;

            Action callback = () => {
                //Answer hooks
                (Entity, Entity) prms = (oldPlayer, data);
                oldPlayer?.eventCtrl.RaiseEvent(GameEvent.Entity_Respawn, data, null, 
                    new RefTuple<(Entity, Entity)>(prms));
                
                RebindPlayer(data);
                cb?.Invoke();
            };

            if (!immediate) {
                EntityManager.CreateEntity(SpawnPoint, entityIndex, data, cb : callback);
                return;
            }
            EntityManager.InitializeE(data, SpawnPoint, entityIndex);
            callback?.Invoke();
        }

        private static bool RebindPlayer(Entity player) {
            if (player.Is(out BehaviorEntity.Animal self) && self.controller != null) {
                Viewer.SetParent(self.controller.transform, worldPositionStays: false);
                OctreeTerrain.viewer = self.controller.transform;
                return true;
            } return false;
        }
    }
}
