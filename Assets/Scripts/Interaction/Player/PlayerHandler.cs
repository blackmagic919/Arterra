using System.Threading.Tasks;
using UnityEngine;
using System.IO;
using TerrainGeneration;
using WorldConfig;
using System;


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

public class PlayerHandler : MonoBehaviour
{
    public static PlayerStreamer.Player data;
    public static new Transform camera;
    private PlayerInteraction interaction;
    private PlayerMovement movement;
    private PlayerVitality vitality;
    private static bool active = false;
    public static void Initialize(){
        PlayerStreamer playerEntity = (PlayerStreamer)Config.CURRENT.Generation.Entities.Retrieve("Player");
        GameObject player = GameObject.Instantiate(playerEntity.Controller.value);
        active = false;
        
        data = LoadPlayerData();
        EntityManager.CreateEntity(data);
        player.transform.SetPositionAndRotation(data.positionWS, data.rotation);
        OctreeTerrain.viewer = player.transform; //set octree's viewer to current player

        InventoryController.Initialize();
        DayNightContoller.Initialize();
    }

    public void Start(){camera = Camera.main.transform;}

    // Update is called once per frame
    public void Update() { 
        if(!active && OctreeTerrain.RequestQueue.IsEmpty) {
            movement = new PlayerMovement(data);
            interaction = new PlayerInteraction(data);
            vitality = new PlayerVitality(data);
            active = true;
        } if(!active) return;
        vitality.Update();
        movement.Update();
        camera.localRotation = data.cameraRot;
    }

    public void FixedUpdate(){
        if(!active) return;
        movement.FixedUpdate();
        data.collider.FixedUpdate(data, data.settings.collider);
        this.transform.SetPositionAndRotation(data.positionWS, data.rotation);
    }

    public void OnDisable(){
        InputPoller.SetCursorLock(false); //Release Cursor Lock
        Task.Run(() => SavePlayerData(data));//

        InventoryController.Release();
        GameObject.Destroy(this.gameObject);
    }
    

    static async Task SavePlayerData(PlayerStreamer.Player playerInfo){
        playerInfo.Serialize();
        string path = WorldStorageHandler.WORLD_SELECTION.First.Value.Path + "/PlayerData.json";
        using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)){
            using StreamWriter writer = new StreamWriter(fs);
            string data = Newtonsoft.Json.JsonConvert.SerializeObject(playerInfo);
            await writer.WriteAsync(data);
            await writer.FlushAsync();
        };
    }

    static PlayerStreamer.Player LoadPlayerData(){
        string path = WorldStorageHandler.WORLD_SELECTION.First.Value.Path + "/PlayerData.json";
        if(!File.Exists(path)) { return new PlayerStreamer.Player(); }

        string data = System.IO.File.ReadAllText(path);
        PlayerStreamer.Player playerInfo = Newtonsoft.Json.JsonConvert.DeserializeObject<PlayerStreamer.Player>(data);
        playerInfo.Deserialize();
        return playerInfo;
    }
}
