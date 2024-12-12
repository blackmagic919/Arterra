using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using System.IO;
using static OctreeTerrain;


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

        MainLoopUpdateTasks.Enqueue(new PlayerHandler{active = true});
        viewer = player.transform; //set octree's viewer to current player

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
            Dictionary<(int, int), int> lookup = new Dictionary<(int, int), int>();
            for(int i = 0; i < PrimaryI.Info.Length; i++){
                if(PrimaryI.Info[i].IsNull) continue;
                //if(inv.Info[i].IsItem) --> Add when implementing items
                //  lookup.TryAdd((1, inv.Info[i].Index), SerializedNames.Count);
                //else 
                lookup.TryAdd((0, PrimaryI.Info[i].Index), lookup.Count);
                PrimaryI.Info[i].Index = lookup[(0, PrimaryI.Info[i].Index)];
            }
            for(int i = 0; i < SecondaryI.Info.Length; i++){
                if(SecondaryI.Info[i].IsNull) continue;
                lookup.TryAdd((0, SecondaryI.Info[i].Index), lookup.Count);
                SecondaryI.Info[i].Index = lookup[(0, SecondaryI.Info[i].Index)];
            }

            IRegister[] registries = {
                WorldStorageHandler.WORLD_OPTIONS.Generation.Materials.value.MaterialDictionary
            };
            string[] serialized = new string[lookup.Count];
            foreach(var entry in lookup){
                serialized[entry.Value] = registries[entry.Key.Item1].RetrieveName(entry.Key.Item2);
            }
            SerializedNames = new List<string>(serialized);
        }

        public void Deserialize(){
            IRegister[] registries = {
                WorldStorageHandler.WORLD_OPTIONS.Generation.Materials.value.MaterialDictionary
            };
            for(uint i = 0; i < PrimaryI.Info.Length; i++){
                if(PrimaryI.Info[i].IsNull) continue;
                int regInd = PrimaryI.Info[i].IsItem ? 1 : 0;
                PrimaryI.MakeDirty(i);
                PrimaryI.Info[i].Index = registries[regInd].RetrieveIndex(SerializedNames[PrimaryI.Info[i].Index]);
            }
            for(uint i = 0; i < SecondaryI.Info.Length; i++){
                if(SecondaryI.Info[i].IsNull) continue;
                int regInd = SecondaryI.Info[i].IsItem ? 1 : 0;
                SecondaryI.MakeDirty(i);
                SecondaryI.Info[i].Index = registries[regInd].RetrieveIndex(SerializedNames[SecondaryI.Info[i].Index]);
            }
        }

        public static PlayerData GetDefault(){
            return new PlayerData{
                position = new Vector3(0, 0, 0) + (CPUNoiseSampler.SampleTerrainHeight(new (0, 0, 0)) + 5) * WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value.lerpScale * Vector3.up,
                rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up),
                PrimaryI = new InventoryController.Inventory(WorldStorageHandler.WORLD_OPTIONS.GamePlay.Inventory.value.PrimarySlotCount),
                SecondaryI = new InventoryController.Inventory(WorldStorageHandler.WORLD_OPTIONS.GamePlay.Inventory.value.SecondarySlotCount),
                currentTime = DateTime.Now.Date + TimeSpan.FromHours(WorldStorageHandler.WORLD_OPTIONS.GamePlay.DayNightCycle.value.startHour)
            };
        }
    }
}
