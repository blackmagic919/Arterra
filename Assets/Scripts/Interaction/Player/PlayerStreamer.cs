using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig.Generation.Entity;
using WorldConfig;
using WorldConfig.Generation.Item;
using System.Collections.Generic;
using System.Linq;

[CreateAssetMenu(menuName = "Entity/Player")]
public class PlayerStreamer : WorldConfig.Generation.Entity.Authoring
{
    [UISetting(Ignore = true)]
    public Option<Player> _Entity;
    public Option<PlayerSettings> _Setting;
    public static Registry<WorldConfig.Generation.Item.Authoring> ItemRegistry => Config.CURRENT.Generation.Items;
    
    [JsonIgnore]
    public override Entity Entity { get => new Player(); }
    [JsonIgnore]
    public override EntitySetting Setting { get => _Setting.value; set => _Setting.value = (PlayerSettings)value; }
    [Serializable]
    public class PlayerSettings : EntitySetting{
        public float maxHealth;
        public float healthRegen;
    }
    
    //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
    //**If you release here the controller might still be accessing it
    public class Player : Entity
    {  
        [JsonIgnore]
        public PlayerSettings settings;
        public DateTime currentTime;
        public float3 positionGS;
        public Quaternion rotation;
        public Quaternion cameraRot;
        public List<string> SerializedNames;
        public InventoryController.Inventory PrimaryI;
        public InventoryController.Inventory SecondaryI;
        public Physicality physicality;
        public PlayerCollider collider;

        [JsonIgnore]
        public override float3 position {
            get => positionGS;
            set => positionGS = value;
        }
        [JsonIgnore]
        public float3 positionWS{
            get => CPUMapManager.GSToWS(positionGS);
            set => positionGS = CPUMapManager.WSToGS(value);
        }

        public Player(){
            position = new Vector3(0, 0, 0) + (CPUNoiseSampler.SampleTerrainHeight(new (0, 0, 0)) + 5) * Config.CURRENT.Quality.Terrain.value.lerpScale * Vector3.up;
            rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            cameraRot = Quaternion.identity;
            PrimaryI = new InventoryController.Inventory(Config.CURRENT.GamePlay.Inventory.value.PrimarySlotCount);
            SecondaryI = new InventoryController.Inventory(Config.CURRENT.GamePlay.Inventory.value.SecondarySlotCount);
            currentTime = DateTime.Now.Date + TimeSpan.FromHours(Config.CURRENT.GamePlay.DayNightCycle.value.startHour);
            info.entityType = (uint)Config.CURRENT.Generation.Entities.RetrieveIndex("Player");
            info.entityId = Guid.NewGuid();
            collider = new PlayerCollider{velocity = 0};
        }

        //This function shouldn't be used
        public override void Initialize(EntitySetting setting, GameObject Controller, int3 GCoord)
        {
            settings = (PlayerSettings)setting;
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord)
        {
            settings = (PlayerSettings)setting;
            GCoord = (int3)this.positionGS;
        }


        public override void Update()
        {
            if(!active) return;
            //This is not really safe
        }

        public override void Disable(){}

        public override void OnDrawGizmos(){
            Gizmos.color = Color.blue; 
            Gizmos.DrawWireCube(CPUMapManager.GSToWS(position), Vector3.one * 2);
        }

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

            for(int i = 0; i < PrimaryI.Info.Length; i++){
                Serialize(ref PrimaryI.Info[i]);
            } for(int i = 0; i < SecondaryI.Info.Length; i++){
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
                item.IsDirty = true;
            }

            for(int i = 0; i < PrimaryI.Info.Count(); i++){
                Deserialize(ref PrimaryI.Info[i]);
            } for(int i = 0; i < SecondaryI.Info.Count(); i++){
                Deserialize(ref SecondaryI.Info[i]);
            }
        }
    }

    public struct Physicality{
        public float Health;
        //Add stuff like armor, status effects, etc.
    }
}
