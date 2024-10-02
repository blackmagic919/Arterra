using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityStandardAssets.Characters.FirstPerson;
using System.IO;

public class PlayerHandler : MonoBehaviour
{
    public static MaterialInventory Inventory;
    public static TerraformController terrController;
    private RigidFPController PlayerController;
    private PlayerData info;
    private bool active = false;
    // Start is called before the first frame update

    public void Activate(){
        active = true;
        PlayerController.ActivateCharacter();
    }
    void OnEnable(){
        PlayerController = this.GetComponent<RigidFPController>();

        info = LoadPlayerData();
        transform.SetPositionAndRotation(info.position.GetVector(), info.rotation.GetQuaternion());
        PlayerController.Initialize(WorldStorageHandler.WORLD_OPTIONS.GamePlay.value.Movement.value);

        Inventory = info.inventory;
        terrController = new TerraformController();
    }

    // Update is called once per frame
    void Update() { 
        if(EndlessTerrain.RequestQueue.IsEmpty && !active) Activate();
        if(!active) return;
        
        terrController.Update();
        Inventory.Update();
    }

    void OnDisable(){
        info.position = new Vec3(transform.position);
        info.rotation = new Vec4(transform.rotation);
        info.inventory = Inventory;
        InputPoller.SetCursorLock(false); //Release Cursor Lock
        Task.Run(() => SavePlayerData(info));
        active = false;

        Inventory.Release();
    }
    

    async Task SavePlayerData(PlayerData playerInfo){
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
                position = new Vec3((new Vector3(0, 0, 0) + Vector3.up * (CPUNoiseSampler.SampleTerrainHeight(new (0, 0, 0)) + 5)) * WorldStorageHandler.WORLD_OPTIONS.Quality.value.Rendering.value.lerpScale),
                rotation = new Vec4(Quaternion.LookRotation(Vector3.forward, Vector3.up)),
                inventory = new MaterialInventory(WorldStorageHandler.WORLD_OPTIONS.GamePlay.value.Terraforming.value.materialCapacity)
            };
        }

        string data = System.IO.File.ReadAllText(path);
        return  Newtonsoft.Json.JsonConvert.DeserializeObject<PlayerData>(data);
    }

    struct PlayerData{
        public Vec3 position;
        public Vec4 rotation;
        public MaterialInventory inventory;
    }
}
