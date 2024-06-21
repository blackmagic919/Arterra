using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityStandardAssets.Characters.FirstPerson;

public class PlayerHandler : MonoBehaviour
{
    public TerraformController terrController;
    private RigidbodyFirstPersonController PlayerController;
    private bool active = false;
    // Start is called before the first frame update

    public void Activate(){
        active = true;
        PlayerController.ActivateCharacter();
    }
    void OnEnable(){
        PlayerController = this.GetComponent<RigidbodyFirstPersonController>();
        this.terrController.Activate();
    }

    void Start(){
        active = false;
    }
    // Update is called once per frame
    void Update() { 
        if(EndlessTerrain.timeRequestQueue.Count == 0 && !active) Activate();
        if(!active) return;

        terrController.Update(); 
    }

    async Task<PlayerData> LoadPlayerData(){
        string path = WorldStorageHandler.WORLD_OPTIONS.Path + "/playerData.json";
        if(!System.IO.File.Exists(path)) return new PlayerData();

        string[] data = await System.IO.File.ReadAllLinesAsync(path);
        return JsonUtility.FromJson<PlayerData>(data[0]);
    }

    struct PlayerData{
        public Vector3 position;
        public Quaternion rotation;
    }
}
