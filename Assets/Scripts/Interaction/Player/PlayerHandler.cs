using UnityEngine;
using System.IO;
using System;
using Arterra.Core.Events;
using Arterra.Engine.Terrain;
using Arterra.GamePlay.Interaction;
using Arterra.Configuration;
using Arterra.GamePlay.UI;


namespace Arterra.Configuration.Gameplay.Player{
/// <summary>
/// Settings pertinent to the player object itself, and how the user experiences the world.
/// All settings collected here are volatile during gameplay and the developer should be aware
/// of this when accessing these settings.
/// </summary>
[Serializable]
public struct Settings{
    /// <summary> Settings describing how the player moves. See <see cref="Movement"/> for more info. </summary>
    public Option<Movement> movement;
    /// <summary> Settings controlling how the inputs get translated into camera rotations. See <see cref="Player.Camera"/> for more info. </summary>
    public Option<Camera> Camera;
    /// <summary> Settings controlling how the player interacts with the world. See <see cref="Player.Interaction"/> for more information.  </summary>
    public Option<Interaction> Interaction;
    /// <summary>Settings controlling the player's physical statistics. See <see cref="Physicality"/> for more info.</summary>
    public Option<Physicality> Physicality;
    /// <summary> Whether or not the player's cursor is shown. </summary>
    [UIModifiable(CallbackName = "ToggleUICrosshair")]
    public bool ShowCrosshair;
}
}

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
        public bool Intangiblity;
        /// <summary> Whether the player can keep their inventory on death </summary>
        public bool KeepInventory;
    }
}

namespace Arterra.GamePlay{
    /// <summary> The root handler of the currently active player.  </summary>
    /// <remarks> If <see cref="PlayerStreamer"/> were analogous to the 
    /// player's current body, this class would be the player's soul </remarks>///  
    public static class PlayerHandler {
        /// <summary>The current active <see cref="PlayerStreamer.Player"> streamer </see>
        /// or instance of the player. </summary>
        public static PlayerStreamer.Player data;
        /// <summary> The transform of the Unity gameobject representing the viewer </summary>
        public static Transform Viewer;
        /// <summary> The transform of the current Unity MainCamera  </summary>
        public static Transform Camera;
        /// <summary> Whether or not the player is active; whether or not
        /// the user has control of the player instance  </summary>
        public static bool active = false;

        /// <summary> Initializes the player and retrieves/creates
        /// a new <see cref="PlayerStreamer.Player">Player Instance</see>
        /// for it. </summary>
        public static void Initialize() {
            Viewer = GameObject.Find("Viewer").transform;
            Camera = Viewer.Find("CameraHandler");

            active = false;
            data = LoadPlayerData();
            EntityManager.DeserializeE(data);

            var prms = (data, data);
            RebindPlayer(ref prms);

        OctreeTerrain.viewer = data.player.transform; //set octree's viewer to current player
        PlayerCamera.Initialize();
        PlayerMovement.Initialize();
        PlayerInteraction.Initialize();
        OctreeTerrain.MainLoopUpdateTasks.Enqueue(new IndirectUpdate(Update));
        OctreeTerrain.MainFixedUpdateTasks.Enqueue(new IndirectUpdate(FixedUpdate));
    }

    static bool RebindPlayer(ref (PlayerStreamer.Player old, PlayerStreamer.Player cur) cxt) {
        Viewer.SetParent(cxt.cur.player.transform, worldPositionStays: false);
        cxt.cur.eventCtrl.AddEventHandler(
            GameEvent.Entity_Respawn,
            delegate (object actor, object target, object ctx) {
                var args = (ctx as EventContext<(PlayerStreamer.Player, PlayerStreamer.Player)>).Data;
                RebindPlayer(ref args);
            }
        );
        return false;
    }


        private static void Update(MonoBehaviour mono) {
            if (!data.active) return;
            if (!active && OctreeTerrain.RequestQueue.IsEmpty)
                active = true;
            if (!active) return;
            data.camera.Update(Camera);
            PlayerMovement.Update();
            Arterra.GamePlay.UI.PlayerStatDisplay.UpdateIndicator(data.vitality);

        }

        private static void FixedUpdate(MonoBehaviour mono){
            if(!active) return;
            if(!data.active) return;
            TerrainInteractor.DetectMapInteraction(data.position, 
            OnInSolid: (dens) => data.vitality.ProcessEntityInSolid(data, dens), 
            OnInLiquid: (dens) => data.vitality.ProcessInLiquid(data, dens), 
            OnInGas: (dens) => data.vitality.ProcessInGas(data, dens));
            data.collider.FixedUpdate(data);
        }

        /// <summary>Releases the player handler and all resources tied to handling the player. </summary>
        public static void Release(){
            InputPoller.SetCursorLock(false); //Release Cursor Lock
            SavePlayerData(new Registerable<PlayerStreamer.Player>(data));
            InventoryController.Release();
            PanelNavbarManager.Release();
        }
        

        static void SavePlayerData(Registerable<PlayerStreamer.Player> playerInfo){
            string path = Arterra.Core.Storage.World.WORLD_SELECTION.First.Value.Path + "/PlayerData.json";
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)){
                using StreamWriter writer = new StreamWriter(fs);
                string data = Newtonsoft.Json.JsonConvert.SerializeObject(playerInfo);
                writer.Write(data);
                writer.Flush();
            };
        }

        static PlayerStreamer.Player LoadPlayerData(){
            string path = Arterra.Core.Storage.World.WORLD_SELECTION.First.Value.Path + "/PlayerData.json";
            if(!File.Exists(path)) { return PlayerStreamer.Player.Build(); }

            string data = System.IO.File.ReadAllText(path);
            PlayerStreamer.Player playerInfo = Newtonsoft.Json.JsonConvert.DeserializeObject<Registerable<PlayerStreamer.Player>>(data).Value;
            return playerInfo;
        }

        /// <summary> Respawns the player initiating the respawn process.
        /// This process will create a new  <see cref="PlayerStreamer.Player">Player Instance</see>
        /// at a set respawn location, abandoning the current active instance. </summary>
        /// <param name="cb">The option action to perform once respawned</param>
        public static void RespawnPlayer(Action cb = null){
            PlayerStreamer.Player nPlayer = PlayerStreamer.Player.Build();
            EntityManager.DeserializeEntity(nPlayer, () => {
                //Answer hooks
                var prms = (data, nPlayer);
                data.eventCtrl.RaiseEvent(GameEvent.Entity_Respawn, data, null, 
                    new EventContext<(PlayerStreamer.Player, PlayerStreamer.Player)>(ref prms));
                //data.Events.Invoke(EntityEvents.EventType.OnRespawn, ref prms);
                
                OctreeTerrain.viewer = nPlayer.player.transform;
                data = nPlayer;
                cb?.Invoke();
            });
        }
    }
}
