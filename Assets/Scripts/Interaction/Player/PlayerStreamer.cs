using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig.Generation.Entity;
using WorldConfig;
using WorldConfig.Generation.Item;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using TerrainGeneration;
using System.Runtime.Serialization;

[CreateAssetMenu(menuName = "Generation/Entity/Player")]
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
        [JsonIgnore]
        public Animator animator;
        public DateTime currentTime;
        public Quaternion cameraRot;
        public List<string> SerializedNames;
        public InventoryController.Inventory PrimaryI;
        public InventoryController.Inventory SecondaryI;
        public PlayerVitality vitality;
        public PlayerCollider collider;
        public bool IsStreaming;

        [JsonIgnore]
        public override float3 position {
            get => collider.transform.position + settings.collider.size/2;
            set => collider.transform.position = value - settings.collider.size/2;
        }
        [JsonIgnore]
        public float3 positionWS{
            get => CPUMapManager.GSToWS(position);
            set => position = CPUMapManager.WSToGS(value);
        }

        [JsonIgnore]
        public override float3 origin{
            get => collider.transform.position;
            set => collider.transform.position = value;
        }
        [JsonIgnore]
        public bool IsDead{get => vitality.IsDead; }
        
        public IItem Collect(float collectRate){
            int itemCount = PrimaryI.EntryDict.Count + SecondaryI.EntryDict.Count;
            
            IItem ret;
            if(PrimaryI.EntryDict.Count > 0) ret = PrimaryI.LootInventory(collectRate);
            else ret = SecondaryI.LootInventory(collectRate);

            float itemDelta = itemCount - (PrimaryI.EntryDict.Count + SecondaryI.EntryDict.Count);
            vitality.health -= (itemDelta / itemCount) * PlayerVitality.settings.DecompositionTime;
            return ret;
        }

        public void TakeDamage(float damage, float3 knockback, Entity attacker = null){
            //Invulnerability means we don't even process the request
            if(Config.CURRENT.GamePlay.Gamemodes.value.Invulnerability) return; 
            if(!vitality.Damage(damage)) return;
            Indicators.DisplayDamageParticle(position, knockback);
            collider.velocity += knockback;

            if(!IsStreaming) return;
            OctreeTerrain.MainCoroutines.Enqueue(CameraShake(0.2f, 0.25f));
        }

        public Player(){
            cameraRot = Quaternion.identity;
            PrimaryI = new InventoryController.Inventory(Config.CURRENT.GamePlay.Inventory.value.PrimarySlotCount);
            SecondaryI = new InventoryController.Inventory(Config.CURRENT.GamePlay.Inventory.value.SecondarySlotCount);
            currentTime = DateTime.Now.Date + TimeSpan.FromHours(Config.CURRENT.GamePlay.Time.value.startHour);
            info.entityType = (uint)Config.CURRENT.Generation.Entities.RetrieveIndex("Player");
            info.entityId = Guid.NewGuid();

            settings = Config.CURRENT.Generation.Entities.Retrieve((int)info.entityType).Setting as PlayerSettings;
            collider = new PlayerCollider(
                new TerrainColliderJob.Transform((CPUNoiseSampler.SampleTerrainHeight(new (0, 0, 0)) + 5) * Config.CURRENT.Quality.Terrain.value.lerpScale * Vector3.up, 
                Quaternion.LookRotation(Vector3.forward, Vector3.up)
            ));
            vitality = new PlayerVitality();
            IsStreaming = true;
        }

        //This function shouldn't be used
        public override void Initialize(EntitySetting setting, GameObject Controller, int3 GCoord)
        {
            settings = (PlayerSettings)setting;
            collider.OnHitGround = ProcessFallDamage;
            player = GameObject.Instantiate(Controller);
            animator = player.GetComponent<Animator>();
            player.transform.SetPositionAndRotation(positionWS, collider.transform.rotation);
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord)
        {
            settings = (PlayerSettings)setting;
            GCoord = (int3)this.origin;
            player = GameObject.Instantiate(Controller);
            animator = player.GetComponent<Animator>();
            player.transform.SetPositionAndRotation(positionWS, collider.transform.rotation);
            collider.OnHitGround = ProcessFallDamage;
            if(IsDead) PlayDead(); 
        }


        public override void Update()
        {
            if(!active) return;
            vitality.Update();

            if(!IsDead) return;
            if(IsStreaming) EntityManager.AddHandlerEvent(DetatchStreamer);
            if(vitality.health <= -PlayerVitality.settings.DecompositionTime){ //the player isn't idling
                if(PlayerHandler.data == null || PlayerHandler.data.info.entityId != info.entityId)
                    EntityManager.ReleaseEntity(this.info.entityId);
            } vitality.health -= EntityJob.cxt.deltaTime;

            collider.useGravity = true;
            Recognition.DetectMapInteraction(position, OnInSolid: null,
            OnInLiquid: (dens) => {
                collider.velocity += EntityJob.cxt.deltaTime * -EntityJob.cxt.gravity;
                collider.velocity.y *= 1 - settings.collider.friction;
                collider.useGravity = false;
            }, OnInGas: null);

            //Apply gravity and take over physics updating
            collider.JobUpdate(EntityJob.cxt, this.settings.collider);
            EntityManager.AddHandlerEvent(() => player.transform.SetPositionAndRotation(this.positionWS, collider.transform.rotation));
        }

        public override void Disable(){
            if(player != null) GameObject.Destroy(player);
        }

        public override void OnDrawGizmos(){
            Gizmos.color = Color.blue; 
            Gizmos.DrawWireCube(CPUMapManager.GSToWS(position), Vector3.one * 2);
        }

        [OnSerializing]
        public void OnSerializing(StreamingContext cxt){
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

        [OnDeserialized]
        public void OnDeserialized(StreamingContext cxt){
            List<string> names = SerializedNames;
            void Deserialize(ref IItem item){
                if(item is null) return;
                if(item.Index >= names.Count || item.Index < 0) return;
                IRegister registry = item.GetRegistry();
                item.Index = registry.RetrieveIndex(names[item.Index]);
                item.IsDirty = true;
            }
            info.entityType = (uint)Config.CURRENT.Generation.Entities.RetrieveIndex("Player");
            PrimaryI.EntryDict = new Dictionary<int, int>(); SecondaryI.EntryDict = new Dictionary<int, int>();
            for(int i = 0; i < PrimaryI.Info.Count(); i++){
                Deserialize(ref PrimaryI.Info[i]);
                if(PrimaryI.Info[i] != null) 
                    PrimaryI.EntryDict.Add(PrimaryI.Info[i].Index, i);
            } for(int i = 0; i < SecondaryI.Info.Count(); i++){
                Deserialize(ref SecondaryI.Info[i]);
                if(SecondaryI.Info[i] != null) 
                    SecondaryI.EntryDict.Add(SecondaryI.Info[i].Index, i);
            }
        }

        private void DetatchStreamer(){
            if(!IsStreaming) return;
            IsStreaming = false;
            PlayDead();

            GameOverHandler.Activate();
        }

        private void PlayDead(){
            animator.SetBool("IsDead", true);
            SetLayerRecursively(player.transform, LayerMask.NameToLayer("Default"));
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

        private void SetLayerRecursively(Transform obj, int layer) {
            obj.gameObject.layer = layer;
            foreach (Transform child in obj) {
                SetLayerRecursively(child, layer);
            }
        }

        public void ProcessFallDamage(float zVelDelta){
            if(zVelDelta <= Vitality.FallDmgThresh) return;
            float dmgIntensity = zVelDelta - Vitality.FallDmgThresh;    
            dmgIntensity = math.pow(dmgIntensity, Config.CURRENT.GamePlay.Player.value.Physicality.value.weight);
            TakeDamage(dmgIntensity, 0, null);
        }
    }
}
