using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using WorldConfig.Generation.Entity;
using WorldConfig;
using WorldConfig.Generation.Item;
using TerrainGeneration;
using MapStorage;
using WorldConfig.Gameplay.Player;

[CreateAssetMenu(menuName = "Generation/Entity/Player")]
public class PlayerStreamer : WorldConfig.Generation.Entity.Authoring
{
    [UISetting(Ignore = true)]
    public Option<Player> _Entity;
    public Option<PlayerSettings> _Setting;
    public static Catalogue<WorldConfig.Generation.Item.Authoring> ItemRegistry => Config.CURRENT.Generation.Items;
    
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
    public class Player : Entity, IAttackable, IRider
    {
        [JsonIgnore]
        public PlayerSettings settings;
        [JsonIgnore]
        public GameObject player;
        [JsonIgnore]
        public Animator animator;
        public DateTime currentTime;
        public PlayerCamera camera;
        public InventoryController.Inventory PrimaryI;
        public InventoryController.Inventory SecondaryI;
        public ArmorInventory ArmorI;
        public PlayerVitality vitality;
        public PlayerCollider collider;
        public StreamingStatus status;

        [JsonIgnore]
        public EntityEvents Events = new();

        [JsonIgnore]
        public override ref TerrainCollider.Transform transform => ref collider.transform;
        [JsonIgnore]
        public override Quaternion Facing => camera.Facing;
        [JsonIgnore]
        public float3 positionWS
        {
            get => CPUMapManager.GSToWS(position);
            set => position = CPUMapManager.WSToGS(value);
        }

        [JsonIgnore]
        public bool IsDead { get => vitality.IsDead; }
        
        public void Interact(Entity target) { }
        public IItem Collect(float collectRate)
        {
            int itemCount = PrimaryI.EntryDict.Count + SecondaryI.EntryDict.Count;

            IItem ret;
            if (PrimaryI.EntryDict.Count > 0) ret = PrimaryI.LootInventory(collectRate);
            else ret = SecondaryI.LootInventory(collectRate);

            float itemDelta = itemCount - (PrimaryI.EntryDict.Count + SecondaryI.EntryDict.Count);
            vitality.health -= (itemDelta / itemCount) * PlayerVitality.settings.DecompositionTime;
            return ret;
        }
        public void TakeDamage(float damage, float3 knockback, Entity attacker = null) {
            //Invulnerability means we don't even process the request
            if (Config.CURRENT.GamePlay.Gamemodes.value.Invulnerability) return;
            if (vitality.Invincibility > 0) return;
            
            var cxt = (damage, knockback, attacker);
            Events.Invoke(EntityEvents.EventType.OnDamaged, ref cxt);
            (damage, knockback, attacker) = cxt;
            
            if (!vitality.Damage(damage)) return;
            EntityManager.AddHandlerEvent(() => Indicators.DisplayDamageParticle(position, knockback));
            velocity += knockback;

            if (status == StreamingStatus.Disconnected) return;
            OctreeTerrain.MainCoroutines.Enqueue(PlayerHandler.cEffects.CameraShake(0.2f, 0.25f));
        }

        public void OnMounted(IRidable mount) => RideMovement.AddHandles(mount);
        public void OnDismounted(IRidable mount) => RideMovement.RemoveHandles();

        public static Player Build() {
            Player p = new();
            p.camera = new PlayerCamera();
            p.PrimaryI = new InventoryController.Inventory(Config.CURRENT.GamePlay.Inventory.value.PrimarySlotCount);
            p.SecondaryI = new InventoryController.Inventory(Config.CURRENT.GamePlay.Inventory.value.SecondarySlotCount);
            p.ArmorI = new ArmorInventory();
            p.currentTime = DateTime.Now.Date + TimeSpan.FromHours(Config.CURRENT.GamePlay.Time.value.startHour);
            p.info.entityType = (uint)Config.CURRENT.Generation.Entities.RetrieveIndex("Player");
            p.info.entityId = Guid.NewGuid();

            p.settings = Config.CURRENT.Generation.Entities.Retrieve((int)p.info.entityType).Setting as PlayerSettings;
            p.collider = new PlayerCollider(p.settings.collider, 0);
            p.vitality = new PlayerVitality();
            p.status = StreamingStatus.Live;
            StartupPlacer.PlaceOnSurface(p);
            return p;
        }

        //This function shouldn't be used
        public override void Initialize(EntitySetting setting, GameObject Controller, float3 GCoord)
        {
            settings = (PlayerSettings)setting;
            collider.OnHitGround = ProcessFallDamage;
            player = GameObject.Instantiate(Controller);
            animator = player.transform.Find("Player").GetComponent<Animator>();
            player.transform.SetPositionAndRotation(positionWS, collider.transform.rotation);
        }

        public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord)
        {
            info.entityType = (uint)Config.CURRENT.Generation.Entities.RetrieveIndex("Player");
            settings = (PlayerSettings)setting;
            GCoord = (int3)this.origin;
            player = GameObject.Instantiate(Controller);
            animator = player.transform.Find("Player").GetComponent<Animator>();
            player.transform.SetPositionAndRotation(positionWS, collider.transform.rotation);
            collider.OnHitGround = ProcessFallDamage;
            camera.Deserailize(this);
            if (IsDead) PlayDead();
        }


        public override void Update()
        {
            if (!active) return;
            vitality.Update();

            if (!IsDead) return;
            if (status != StreamingStatus.Disconnected)
                EntityManager.AddHandlerEvent(DetatchStreamer);
            if (vitality.health <= -PlayerVitality.settings.DecompositionTime){ //the player isn't idling
                if (PlayerHandler.data == null || PlayerHandler.data.info.entityId != info.entityId)
                    EntityManager.ReleaseEntity(this.info.entityId);
            }
            vitality.health -= EntityJob.cxt.deltaTime;

            collider.useGravity = true;
            TerrainInteractor.DetectMapInteraction(position, OnInSolid: null,
            OnInLiquid: (dens) =>
            {
                velocity += EntityJob.cxt.deltaTime * -EntityJob.cxt.gravity;
                velocity.y *= 1 - settings.collider.friction;
                collider.useGravity = false;
            }, OnInGas: null);

            //Apply gravity and take over physics updating
            collider.JobUpdate(EntityJob.cxt);
            EntityManager.AddHandlerEvent(() => player.transform.SetPositionAndRotation(this.positionWS, collider.transform.rotation));
        }

        public override void Disable()
        {
            if (player != null) GameObject.Destroy(player);
        }

        public override void OnDrawGizmos()
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireCube(CPUMapManager.GSToWS(position), Vector3.one * 2);
        }
        
        private void DetatchStreamer() {
            if (status == StreamingStatus.Disconnected) return;
            status = StreamingStatus.Disconnected;
            PlayDead();

            GameOverHandler.Activate();
        }

        private void PlayDead()
        {
            animator.SetBool("IsDead", true);
            SetLayerRecursively(player.transform, LayerMask.NameToLayer("Default"));
        }

        private void SetLayerRecursively(Transform obj, int layer)
        {
            obj.gameObject.layer = layer;
            foreach (Transform child in obj)
            {
                SetLayerRecursively(child, layer);
            }
        }

        public void ProcessFallDamage(float zVelDelta)
        {
            Events.Invoke(EntityEvents.EventType.OnHitGround, ref zVelDelta);
            if (zVelDelta <= Vitality.FallDmgThresh) return;
            float dmgIntensity = zVelDelta - Vitality.FallDmgThresh;
            dmgIntensity = math.pow(dmgIntensity, Config.CURRENT.GamePlay.Player.value.Physicality.value.weight);
            TakeDamage(dmgIntensity, 0, null);
        }

        public enum StreamingStatus
        {
            Live,
            Disconnected
        }
    }
}
