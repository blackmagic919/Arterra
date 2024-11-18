using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using System.IO;
using NSerializable;

public class PlayerHandler : MonoBehaviour
{
    public static TerraformController terrController;
    private RigidFPController PlayerController;
    private PlayerData info;
    private bool active = false;
    // Start is called before the first frame update
    public static PlayerHandler Instance;

    public void Activate(){
        active = true;
        PlayerController.ActivateCharacter();
    }
    void OnEnable(){
        PlayerController = this.GetComponent<RigidFPController>();

        info = LoadPlayerData();
        transform.SetPositionAndRotation(info.position.GetVector(), info.rotation.GetQuaternion());
        PlayerController.Initialize(WorldStorageHandler.WORLD_OPTIONS.GamePlay.Movement.value);
        
        InventoryController.Primary = info.PrimaryI;
        InventoryController.Secondary = info.SecondaryI;
        terrController = new TerraformController();
    }

    // Update is called once per frame
    void Update() { 
        if(OctreeTerrain.RequestQueue.IsEmpty && !active) Activate();
        if(!active) return;
        
        terrController.Update();
    }

    void OnDisable(){
        info.position = new Vec3(transform.position);
        info.rotation = new Vec4(transform.rotation);

        info.PrimaryI = InventoryController.Primary;
        info.SecondaryI = InventoryController.Secondary;

        InputPoller.SetCursorLock(false); //Release Cursor Lock
        Task.Run(() => SavePlayerData(info));
        active = false;
    }
    

    async Task SavePlayerData(PlayerData playerInfo){
        playerInfo.Serialize();
        string path = WorldStorageHandler.WORLD_SELECTION.First.Value.Path + "/PlayerData.json";
        using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)){
            using StreamWriter writer = new StreamWriter(fs);
            string data = Newtonsoft.Json.JsonConvert.SerializeObject(playerInfo);
            await writer.WriteAsync(data);
            await writer.FlushAsync();
        };
    }

     PlayerData LoadPlayerData(){
        string path = WorldStorageHandler.WORLD_SELECTION.First.Value.Path + "/PlayerData.json";
        if(!File.Exists(path)) {
            return new PlayerData{
                position = new Vec3((new Vector3(0, 0, 0) + Vector3.up * (CPUNoiseSampler.SampleTerrainHeight(new (0, 0, 0)) + 5)) * WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value.lerpScale),
                rotation = new Vec4(Quaternion.LookRotation(Vector3.forward, Vector3.up)),
                PrimaryI = new InventoryController.Inventory(WorldStorageHandler.WORLD_OPTIONS.GamePlay.Inventory.value.PrimarySlotCount),
                SecondaryI = new InventoryController.Inventory(WorldStorageHandler.WORLD_OPTIONS.GamePlay.Inventory.value.SecondarySlotCount)
            };
        }

        string data = System.IO.File.ReadAllText(path);
        PlayerData playerInfo = Newtonsoft.Json.JsonConvert.DeserializeObject<PlayerData>(data);
        playerInfo.Deserialize();
        return playerInfo;
    }

    struct PlayerData{
        public Vec3 position;
        public Vec4 rotation;
        public List<string> SerializedNames;
        public InventoryController.Inventory PrimaryI;
        public InventoryController.Inventory SecondaryI;

        public void Serialize(){
            //Marks updated slots dirty so they are rendered properlly when deserialized
            InventoryController.Serialize(PrimaryI); 
            InventoryController.Serialize(SecondaryI);
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
            for(int i = 0; i < PrimaryI.Info.Length; i++){
                if(PrimaryI.Info[i].IsNull) continue;
                int regInd = PrimaryI.Info[i].IsItem ? 1 : 0;
                PrimaryI.Info[i].Index = registries[regInd].RetrieveIndex(SerializedNames[PrimaryI.Info[i].Index]);
            }
            for(int i = 0; i < SecondaryI.Info.Length; i++){
                if(SecondaryI.Info[i].IsNull) continue;
                int regInd = SecondaryI.Info[i].IsItem ? 1 : 0;
                SecondaryI.Info[i].Index = registries[regInd].RetrieveIndex(SerializedNames[SecondaryI.Info[i].Index]);
            }
        }
    }
}
