using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig.Generation.Entity;
using WorldConfig;
using WorldConfig.Generation.Item;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Collections;
using TerrainGeneration;

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
        //Most settings are streamed from worldconfig as we want them to be 
        //modifiable during gameplay
    }
    
    //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
    //**If you release here the controller might still be accessing it
    public class Player : Entity, IAttackable
    {  
        [JsonIgnore]
        public PlayerSettings settings;
        [JsonIgnore]
        public GameObject player;
        public DateTime currentTime;
        public float3 positionGS;
        public Quaternion rotation;
        public Quaternion cameraRot;
        public List<string> SerializedNames;
        public InventoryController.Inventory PrimaryI;
        public InventoryController.Inventory SecondaryI;
        public PlayerVitality vitality;
        public PlayerCollider collider;
        public bool IsStreaming;

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
        [JsonIgnore]
        public bool IsDead{get => vitality.health <= 0; }
        
        public IItem Collect(float collectRate){
            InventoryController.Inventory inv;
            int itemCount = PrimaryI.EntryDict.Count + SecondaryI.EntryDict.Count;
            
            IItem ret;
            if(PrimaryI.EntryDict.Count > 0) ret = PrimaryI.LootInventory(collectRate);
            else ret = SecondaryI.LootInventory(collectRate);

            float itemDelta = itemCount - (PrimaryI.EntryDict.Count + SecondaryI.EntryDict.Count);
            vitality.health -= (itemDelta / itemCount) * PlayerVitality.settings.DecompositionTime;
            return ret;
        }

        public void TakeDamage(float damage, float3 knockback, Entity attacker = null){
            if(!vitality.Damage(damage)) return;
            Indicators.DisplayPopupText(position, knockback);
            PlayerHandler.data.collider.velocity += knockback;
            OctreeTerrain.MainCoroutines.Enqueue(CameraShake(0.25f, 0.25f));
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
            collider = new PlayerCollider();
            vitality = new PlayerVitality();
            IsStreaming = true;
        }

        //This function shouldn't be used
        public override void Initialize(EntitySetting setting, GameObject Controller, int3 GCoord)
        {
            settings = (PlayerSettings)setting;
            collider.OnHitGround = PlayerVitality.ProcessFallDamage;
            player = GameObject.Instantiate(Controller);
            player.transform.SetPositionAndRotation(positionWS, rotation);
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord)
        {
            settings = (PlayerSettings)setting;
            GCoord = (int3)this.positionGS;
            player = GameObject.Instantiate(Controller);
            player.transform.SetPositionAndRotation(positionWS, rotation);
            collider.OnHitGround = PlayerVitality.ProcessFallDamage;
        }


        public override void Update()
        {
            if(!active) return;
            if(!IsDead){
                vitality.Update();
                return;
            }

            if(IsStreaming) EntityManager.AddHandlerEvent(DetatchStreamer);
            if(vitality.health <= -PlayerVitality.settings.DecompositionTime){ //the player isn't idling
                if(PlayerHandler.data == null || PlayerHandler.data.info.entityId != info.entityId)
                    EntityManager.ReleaseEntity(this.info.entityId);
            } vitality.health -= EntityJob.cxt.deltaTime;

            //Apply gravity and take over physics updating
            EntityManager.AddHandlerEvent(() => {
                collider.velocity += (float3)(Physics.gravity * EntityJob.cxt.deltaTime);
                collider.FixedUpdate(this, this.settings.collider);
                player.transform.SetPositionAndRotation(this.positionWS, this.rotation);
            });
        }

        public override void Disable(){
            if(player != null) GameObject.Destroy(player);
        }

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

        private void DetatchStreamer(){
            if(!IsStreaming) return;
            IsStreaming = false;

            GameOverHandler.Activate();
        }

        public IEnumerator CameraShake(float duration, float rStrength){
            Transform CameraLocalT = PlayerHandler.camera.GetChild(0).transform;
            Quaternion OriginRot = CameraLocalT.localRotation;
            Quaternion RandomRot = Quaternion.Euler(new (0, 0, UnityEngine.Random.Range(-180f, 180f) * rStrength));
            duration /= 2;

            float elapsed = 0.0f;
            while(elapsed < duration){
                CameraLocalT.localRotation = Quaternion.Slerp(CameraLocalT.localRotation, RandomRot, elapsed/duration);
                elapsed += Time.deltaTime;
                yield return null;
            } elapsed = 0.0f;
            while(elapsed < duration){
                CameraLocalT.localRotation = Quaternion.Slerp(CameraLocalT.localRotation, OriginRot, elapsed/duration);
                elapsed += Time.deltaTime;
                yield return null;
            }
           CameraLocalT.localRotation = OriginRot;
        }
    }
}
