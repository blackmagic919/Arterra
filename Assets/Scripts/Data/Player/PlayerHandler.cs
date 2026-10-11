using UnityEngine;
using Arterra.Core.Network;
using Arterra.Core.Network.Authentication;
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
using Arterra.Core;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Unity.Networking.Transport;



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
        [UIModifiable(CallbackName = "EnableMultiplayer")]
        // <summary> Whether this world will become available for discovery via the configured network </summary>
        public bool EnableMultiplayer;
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
        public static readonly ConcurrentDictionary<NetworkConnection, BehaviorEntity.Animal> Players = new();
        public static event Action<Guid> PlayerDisconnected;

        // A default connection denotes this machine's local player.
        public static Guid GetPlayerId(NetworkConnection peer = default) {
            var player = peer.IsCreated ? (Players.TryGetValue(peer, out var remote) ? remote : null) : data;
            return player == null ? Guid.Empty : player.info.rtEntityId;
        }
        private static AccountId localAccount;
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
            Players.Clear();
            localAccount = ClientAuthentication.LocalAccount;
            if (NetworkManager.IsActingServer) PlayerStorage.Initialize();
            else PlayerStorage.Release();
            if (NetworkManager.IsActingServer) {
                if (!TryLoadExistingPlayer()) CreatePlayer(immediate: true);
            } else {
                data = PlayerConnection.TakeInitialPlayer();
                EntityManager.DeserializeE(data);
                RebindPlayer(data);
            }

            SpectatorController.Initialize();
            ArterraRuntime.MainLoopUpdateTasks.Enqueue(new ArterraRuntime.IndirectUpdate(Update));
            ArterraRuntime.MainFixedUpdateTasks.Enqueue(new ArterraRuntime.IndirectUpdate(FixedUpdate));
            LoadingHandler.OnStartupTasksFinished += OnStartupReady;
        }

        private static void OnStartupReady() {
            if (data == null || !data.active) return;
            if (!NetworkManager.IsActingServer) PlayerConnection.AcknowledgeReady(data.info.rtEntityId);
            active = true;
        }

        private static void Update(MonoBehaviour mono) {
            if (!data.active) return;
            if (!active) return;

            EntityManager.CompleteJobs();
            data.UpdateSynchronous();
        }

        private static void FixedUpdate(MonoBehaviour mono){
            if(!active) return;
            if(!data.active) return;
            data.Update(BehaviorEntity.UpdateContext.ClientSimulation);
            data.Update(BehaviorEntity.UpdateContext.Simulation);
        }

        /// <summary>Releases the player handler and all resources tied to handling the player. </summary>
        public static void Release(){
            active = false;
            LoadingHandler.OnStartupTasksFinished -= OnStartupReady;
            InputPoller.SetCursorLock(false); //Release Cursor Lock
            SaveAll();
            Players.Clear();
            PlayerStorage.Release();
            InventoryController.Release();
            PanelNavbarManager.Release();
        }
        

        /// <summary>Save registered players and the local player. Invoke on the main thread.</summary>
        public static void SaveAll() {
            if (!PlayerStorage.IsInitialized) return;
            EntityManager.CompleteJobs();
            PlayerStorage.Capture(localAccount, data);
            foreach (var pair in Players) {
                if (PlayerConnection.TryGetAccount(pair.Key, out var account)) 
                    PlayerStorage.Capture(account, pair.Value);
            }
            PlayerStorage.Save();
        }

        /// <summary>Load and register saved data for an authenticated peer; does not initialize or spawn the entity.
        /// Returns null for a new account. The caller creates its player and calls RegisterPlayer.</summary>
        public static BehaviorEntity.Animal LoadPlayer(NetworkConnection peer) {
            var account = RequireAccount(peer);
            if (Players.TryGetValue(peer, out var current)) return current;
            var player = PlayerStorage.Load(account);
            if (player != null) Players.TryAdd(peer, player);
            return player;
        }

        /// <summary>Invoked at the entity boundary after authentication; mapping precedes behavior setup.</summary>
        internal static BehaviorEntity.Animal SpawnPlayer(NetworkConnection peer) {
            var player = LoadPlayer(peer);
            if (player != null) {
                EntityManager.DeserializeE(player);
                return player;
            }
            uint index = (uint)Config.CURRENT.Generation.Entities.RetrieveIndex("Player");
            player = (BehaviorEntity.Animal)Config.CURRENT.Generation.Entities.Reg[(int)index].Entity;
            RegisterPlayer(peer, player);
            EntityManager.InitializeE(player, StartupPlacer.FindClearingAround(float3.zero), index);
            return player;
        }

        /// <summary>Associate an existing player with an authenticated connection, including after respawn.</summary>
        public static void RegisterPlayer(NetworkConnection peer, BehaviorEntity.Animal player) {
            RequireAccount(peer);
            if (player == null) throw new System.ArgumentNullException(nameof(player));
            Players[peer] = player;
        }

        public static void SavePlayer(NetworkConnection peer) {
            var account = RequireAccount(peer);
            if (!Players.TryGetValue(peer, out var player)) return;
            EntityManager.CompleteJobs();
            PlayerStorage.Capture(account, player);
            PlayerStorage.Save();
        }

        private static AccountId RequireAccount(NetworkConnection peer) {
            if (!PlayerStorage.IsInitialized) throw new InvalidOperationException("Open a local world before accessing its player saves.");
            if (!PlayerConnection.TryGetAccount(peer, out var account))
                throw new UnauthorizedAccessException("Authenticate the connection before accessing player data.");
            return account;
        }

        // Pending authentication work checks the connection before creating another player.
        internal static void RemovePeer(NetworkConnection peer, AccountId account) {
            if (!Players.TryGetValue(peer, out var player)) return;
            EntityManager.CompleteJobs();
            try {
                if (PlayerStorage.IsInitialized && EntityManager.ESTree.Contains(player.info.rtEntityId)) {
                    PlayerStorage.Capture(account, player);
                    PlayerStorage.Save();
                }
            } finally {
                EntityManager.ReleaseEntity(player.info.rtEntityId);
                Players.TryRemove(peer, out _);
                PlayerDisconnected?.Invoke(player.info.rtEntityId);
            }
        }

        public static bool TryLoadExistingPlayer() {
            data = PlayerStorage.IsInitialized ? PlayerStorage.Load(localAccount) : null;
            if (data == null) return false;
            if (data.Is(out VitalityBehavior vitality) && vitality.IsDead) return false;
            EntityManager.DeserializeE(data);
            RebindPlayer(data);
            return true;
        }

        /// <summary> Respawns the player initiating the respawn process.
        /// This process will create a new runtime player instance
        /// at a set respawn location, abandoning the current active instance. </summary>
        /// <param name="cb">The option action to perform once respawned</param>
        /// <param name="immediate"> Whether or not creation happens synchronously with call </param>
        public static void CreatePlayer(Action cb = null, bool immediate = false){
            float3 SpawnPoint = StartupPlacer.FindClearingAround(float3.zero);
            uint entityIndex = (uint)Config.CURRENT.Generation.Entities.RetrieveIndex("Player");
            BehaviorEntity.Animal nPlayer = Config.CURRENT.Generation.Entities.Reg[(int)entityIndex].Entity as BehaviorEntity.Animal;

            Action initialize = () => {
                var previous = data;
                // Ownership must be visible to behavior initialization, including on respawn.
                data = nPlayer;
                EntityManager.InitializeE(nPlayer, SpawnPoint, entityIndex);
                //Answer hooks
                previous?.eventCtrl.RaiseEvent(GameEvent.Entity_Respawn, previous, nPlayer, null);
                RebindPlayer(data);
                cb?.Invoke();
            };

            if (immediate) initialize();
            else EntityManager.AddHandlerEvent(initialize);
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
