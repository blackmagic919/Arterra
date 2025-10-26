using System.Threading.Tasks;
using UnityEngine;
using System.IO;
using TerrainGeneration;
using WorldConfig;
using System;
using UnityEngine.PlayerLoop;
using WorldConfig.Gameplay.Player;


namespace WorldConfig.Gameplay.Player{
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
}
}

namespace WorldConfig.Gameplay{
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

public static class PlayerHandler
{
    public static PlayerStreamer.Player data;
    public static Transform Viewer;
    public static Transform Camera;
    public static CameraEffects cEffects;
    public static bool active = false;
    public static void Initialize() {
        Viewer = GameObject.Find("Viewer").transform;
        Camera = Viewer.Find("CameraHandler");

        active = false;
        data = LoadPlayerData();
        EntityManager.DeserializeE(data);

        var prms = (data, data);
        RebindPlayer(ref prms);

        cEffects = new CameraEffects();
        OctreeTerrain.viewer = data.player.transform; //set octree's viewer to current player

        PlayerCamera.Initialize();
        PlayerMovement.Initialize();
        PlayerInteraction.Initialize();
        OctreeTerrain.MainLoopUpdateTasks.Enqueue(new IndirectUpdate(Update));
        OctreeTerrain.MainFixedUpdateTasks.Enqueue(new IndirectUpdate(FixedUpdate));
    }

    static bool RebindPlayer(ref (PlayerStreamer.Player old, PlayerStreamer.Player cur) cxt) {
        Viewer.SetParent(cxt.cur.player.transform, worldPositionStays: false);
        cxt.cur.Events.AddEvent<(PlayerStreamer.Player, PlayerStreamer.Player)>(
            EntityEvents.EventType.OnRespawn,
            RebindPlayer
        );
        return false;
    }

    // Update is called once per frame
    public static void Update(MonoBehaviour mono) {
        if (!data.active) return;
        if (!active && OctreeTerrain.RequestQueue.IsEmpty)
            active = true;
        if (!active) return;
        data.camera.Update(Camera);
        PlayerMovement.Update();
        PlayerStatDisplay.UpdateIndicator(data.vitality);

    }

    public static void FixedUpdate(MonoBehaviour mono){
        if(!active) return;
        if(!data.active) return;
        TerrainInteractor.DetectMapInteraction(data.position, 
        OnInSolid: (dens) => data.vitality.ProcessSuffocation(data, dens), 
        OnInLiquid: (dens) => data.vitality.ProcessInLiquid(data, dens), 
        OnInGas: data.vitality.ProcessInGas);
        data.collider.FixedUpdate();
    }

    public static void Release(){
        InputPoller.SetCursorLock(false); //Release Cursor Lock
        SavePlayerData(new Registerable<PlayerStreamer.Player>(data));
        PanelNavbarManager.Release();
    }
    

    static void SavePlayerData(Registerable<PlayerStreamer.Player> playerInfo){
        string path = MapStorage.World.WORLD_SELECTION.First.Value.Path + "/PlayerData.json";
        using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)){
            using StreamWriter writer = new StreamWriter(fs);
            string data = Newtonsoft.Json.JsonConvert.SerializeObject(playerInfo);
            writer.Write(data);
            writer.Flush();
        };
    }

    static PlayerStreamer.Player LoadPlayerData(){
        string path = MapStorage.World.WORLD_SELECTION.First.Value.Path + "/PlayerData.json";
        if(!File.Exists(path)) { return PlayerStreamer.Player.Build(); }

        string data = System.IO.File.ReadAllText(path);
        PlayerStreamer.Player playerInfo = Newtonsoft.Json.JsonConvert.DeserializeObject<Registerable<PlayerStreamer.Player>>(data).Value;
        return playerInfo;
    }

    public static void RespawnPlayer(Action cb = null){
        PlayerStreamer.Player nPlayer = PlayerStreamer.Player.Build();
        EntityManager.DeserializeEntity(nPlayer, () => {
            //Answer hooks
            var prms = (data, nPlayer);
            data.Events.Invoke(EntityEvents.EventType.OnRespawn, ref prms);
            
            OctreeTerrain.viewer = nPlayer.player.transform;
            data = nPlayer;
            cb?.Invoke();
        });
    }
}
