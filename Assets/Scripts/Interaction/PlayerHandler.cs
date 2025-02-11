using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using System.IO;
using System.Linq;
using TerrainGeneration;
using WorldConfig;
using WorldConfig.Generation.Item;
using Unity.Services.Analytics;


public class PlayerHandler : UpdateTask
{
    public static GameObject player;
    public static GameObject UIHandle;
    public static TerraformController terrController;
    private static RigidFPController PlayerController;
    private static PlayerData info;
    public static void Initialize(){
        UIHandle = GameObject.Find("MainUI");
        player = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/PlayerController"));
        PlayerController = player.GetComponent<RigidFPController>();

        info = LoadPlayerData();
        player.transform.SetPositionAndRotation(info.position, info.rotation);
        PlayerController.Initialize();
        
        InventoryController.Primary = info.PrimaryI;
        InventoryController.Secondary = info.SecondaryI;
        terrController = new TerraformController();
        DayNightContoller.currentTime = info.currentTime;

        OctreeTerrain.MainLoopUpdateTasks.Enqueue(new PlayerHandler{active = true});
        OctreeTerrain.viewer = player.transform; //set octree's viewer to current player

        LoadingHandler.Initialize();
        PauseHandler.Initialize();
        InventoryController.Initialize();
        DayNightContoller.Initialize();
    }

    // Update is called once per frame
    public override void Update(MonoBehaviour mono) { 
        if(!active) return;
        if(OctreeTerrain.RequestQueue.IsEmpty) {
            PlayerController.ActivateCharacter();
            active = false;
        }
    }

    public static void Release(){
        info.position = player.transform.position;
        info.rotation = player.transform.rotation;

        info.PrimaryI = InventoryController.Primary;
        info.SecondaryI = InventoryController.Secondary;
        info.currentTime = DayNightContoller.currentTime;

        InputPoller.SetCursorLock(false); //Release Cursor Lock
        Task.Run(() => SavePlayerData(info));

        InventoryController.Release();
        GameObject.Destroy(player);
    }
    

    static async Task SavePlayerData(PlayerData playerInfo){
        playerInfo.Serialize();
        string path = WorldStorageHandler.WORLD_SELECTION.First.Value.Path + "/PlayerData.json";
        using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)){
            using StreamWriter writer = new StreamWriter(fs);
            string data = Newtonsoft.Json.JsonConvert.SerializeObject(playerInfo);
            await writer.WriteAsync(data);
            await writer.FlushAsync();
        };
    }

    static PlayerData LoadPlayerData(){
        string path = WorldStorageHandler.WORLD_SELECTION.First.Value.Path + "/PlayerData.json";
        if(!File.Exists(path)) { return PlayerData.GetDefault(); }

        string data = System.IO.File.ReadAllText(path);
        PlayerData playerInfo = Newtonsoft.Json.JsonConvert.DeserializeObject<PlayerData>(data);
        playerInfo.Deserialize();
        return playerInfo;
    }

    struct PlayerData{
        public Vector3 position;
        public Quaternion rotation;
        public List<string> SerializedNames;
        public InventoryController.Inventory PrimaryI;
        public InventoryController.Inventory SecondaryI;
        public DateTime currentTime;

        public void Serialize(){
            //Marks updated slots dirty so they are rendered properlly when deserialized
            // (Register Name, Index) -> Name Index
            Dictionary<string, int> lookup = new Dictionary<string, int>();
            
            void Serialize(ref IItem item){
                if(item is null) return;
                IRegister registry = item.GetRegistry();
                string name = registry.RetrieveName(item.Index);
                lookup.TryAdd(name, lookup.Count);
                item.Index = lookup[name];
            }

            for(int i = 0; i < PrimaryI.Info.Count(); i++){
                Serialize(ref PrimaryI.Info[i]);
            } for(int i = 0; i < SecondaryI.Info.Count(); i++){
                Serialize(ref SecondaryI.Info[i]);
            }

            SerializedNames = lookup.Keys.ToList();
        }

        public void Deserialize(){
            List<string> names = SerializedNames;
            void Deserialize(ref IItem item){
                if(item is null) return;
                if(item.Index >= names.Count || item.Index < 0) return;
                IRegister registry = item.GetRegistry();
                item.Index = registry.RetrieveIndex(names[item.Index]);
            }

            for(int i = 0; i < PrimaryI.Info.Count(); i++){
                Deserialize(ref PrimaryI.Info[i]);
            } for(int i = 0; i < SecondaryI.Info.Count(); i++){
                Deserialize(ref SecondaryI.Info[i]);
            }
        }

        public static PlayerData GetDefault(){
            return new PlayerData{
                position = new Vector3(0, 0, 0) + (CPUNoiseSampler.SampleTerrainHeight(new (0, 0, 0)) + 5) * Config.CURRENT.Quality.Terrain.value.lerpScale * Vector3.up,
                rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up),
                PrimaryI = new InventoryController.Inventory(Config.CURRENT.GamePlay.Inventory.value.PrimarySlotCount),
                SecondaryI = new InventoryController.Inventory(Config.CURRENT.GamePlay.Inventory.value.SecondarySlotCount),
                currentTime = DateTime.Now.Date + TimeSpan.FromHours(Config.CURRENT.GamePlay.DayNightCycle.value.startHour)
            };
        }
    }
}
