using UnityEngine;
using Unity.Mathematics;
using System;
using Newtonsoft.Json;
using Arterra.Config.Generation.Entity;
using Arterra.Config;
using Arterra.Config.Generation.Item;
using Arterra.Core.Storage;
using Arterra.Config.Gameplay.Player;
using Arterra.Core.Events;


namespace Arterra.Core.Player {
    /// <summary> The entity instance of the player holding all data specific
    /// to a single instance of the player. Note, multiple PlayerStreamers can 
    /// exist at the same time. </summary>
    /// <remarks>Using the analogy that <see cref="PlayerHandler"/> is the soul of the player, 
    /// <see cref="PlayerStreamer"/> would be a singular corpreal body of the player. </remarks> 
    [CreateAssetMenu(menuName = "Generation/Entity/Player")]
    public class PlayerStreamer : Config.Generation.Entity.Authoring
    {
        /// <summary>Reference to the player's entity settings. See <see cref="PlayerSettings"/></summary>
        public Option<PlayerSettings> _Setting;
        
        /// <summary> Obtains a new instance of the player streamer </summary>
        [JsonIgnore]
        public override Entity Entity { get => new Player(); }
        /// <summary> Retrieves the player's entity settings. </summary>
        [JsonIgnore]
        public override EntitySetting Setting { get => _Setting.value; set => _Setting.value = (PlayerSettings)value; }
        /// <summary>The settings controlling the entity properties of the player. </summary>
        [Serializable]
        public class PlayerSettings : EntitySetting{
            //Most settings are streamed from config as we want them to be 
            //modifiable during gameplay
        }

        /// <summary>The entity representing a player instance. </summary>
        //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
        //**If you release here the controller might still be accessing it
        public class Player : Entity, IAttackable, IRider, IActionEffect
        {
            /// <summary> The <see cref="PlayerSettings">entity settings
            /// </see> of this player instance </summary>
            [JsonIgnore]
            public PlayerSettings settings;
            /// <summary>The root UnityGameObject tied to this player instance. </summary>
           [JsonIgnore]
            public GameObject player;
            /// <summary> The <see cref="PlayerSettings"/> managing the camera's
            /// movement if this player instance is currently streaming. </summary>
            public PlayerCamera camera;
            /// <summary> The primary inventory/hotbar tied to this player.</summary>
            public InventoryController.Inventory PrimaryI;
            /// <summary> The secondary inventory tied to this player. </summary>
            public InventoryController.Inventory SecondaryI;
            /// <summary> The armor inventory tied to this player. </summary>
            public ArmorInventory ArmorI;
            /// <summary>This player's health and attack patterns. See <see cref="PlayerVitality"/> </summary>
            public PlayerVitality vitality;
            /// <summary>This player's terrain collider. </summary>
            public PlayerCollider collider;
            /// <summary> This player instance's relation to the actual 
            /// user's input and what they see. See <see cref="StreamingStatus"/> </summary>
            private StreamingStatus status;

            /// <summary> The Controller responsible for abstracting the player's
            /// animations as effects. See <see cref="PlayerActionEffects"/> for more info. </summary>
            [JsonIgnore]
            public PlayerActionEffects Effects = new();

            /// <summary>Getter for the <see cref="collider">Player Collider's</see> transform.</summary>
            [JsonIgnore]
            public override ref TerrainCollider.Transform transform => ref collider.transform;
            /// <summary> The quaternion for the direction the player instance is facing. </summary>
            [JsonIgnore]
            public override Quaternion Facing => camera.Facing;
            /// <summary>The location of the player's head/camera in grid space.</summary>
            [JsonIgnore]
            public override float3 head {
                get => position + CPUMapManager.WSToGSScale(Vector3.up * 0.75f);
                set => position = value - CPUMapManager.WSToGSScale(Vector3.up * 0.75f);
            }
            /// <summary>The location of the player's <see cref="Entity.position"/> in world space. </summary>
            [JsonIgnore]
            public float3 positionWS
            {
                get => CPUMapManager.GSToWS(position);
                set => position = CPUMapManager.WSToGS(value);
            }

            /// <summary> Whether or not the player is dead. <see cref="IAttackable.IsDead"/> </summary>
            [JsonIgnore]
            public bool IsDead { get => vitality.IsDead; }
            /// <summary> Shorthand for <see cref="PlayerActionEffects.Play"/> </summary>
            /// <param name="name"></param>
            /// <param name="args"></param>
            public void Play(string name, params object[] args) => Effects.Play(name, args);
            /// <summary>Interacts with the Player Instance. See <see cref="IAttackable.Interact(Entity)"/></summary>            
            public void Interact(Entity target) { }
            /// <summary> Collects items from the dead player instance
            /// if it contains items in its inventories, causing
            /// it to slowly decay. <see cref="IAttackable.Collect(float)"/> </summary>
            /// <param name="collectRate">The speed at which items are removed</param>
            /// <returns>The collected item, or null.</returns>
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

            /// <summary>Attempts to apply a specific amount of damage and knockback
            /// to the player. Success depends on player's defense systems such as
            /// <see cref="Physicality.InvincTime">invincibility </see>  </summary>
            /// <param name="damage">The attempted damage to deal to the player</param>
            /// <param name="knockback">The attempted knockback to deal to the player</param>
            /// <param name="attacker">If the damage was dealt by an entity, the attacker instance.
            /// Otherwise, this field can be omitted. </param>
            public void TakeDamage(float damage, float3 knockback, Entity attacker = null) {
                //Invulnerability means we don't even process the request
                if (Config.Config.CURRENT.GamePlay.Gamemodes.value.Invulnerability) return;
                if (vitality.Invincibility > 0) return;
                
                var cxt = (damage, knockback, attacker);
                eventCtrl.RaiseEvent(GameEvent.Entity_Damaged, attacker, this, ref cxt);
                (damage, knockback, attacker) = cxt;

                if (!vitality.Damage(damage)) return;
                EntityManager.AddHandlerEvent(() => Indicators.DisplayDamageParticle(position, knockback));
                velocity += knockback;

                if (status == StreamingStatus.Disconnected) return;
                Effects.Play("RecieveDamage", damage, knockback);
            }
            /// <summary>Handler that's called when the player mounts an entity <see cref="IRider"/></summary>
            /// <param name="mount">The mount the player is riding</param>
            public void OnMounted(IRidable mount) => RideMovement.AddHandles(mount);
            /// <summary>Handler that's called when the player dismounts an entity <see cref="IRider"/></summary>
            /// <param name="mount">The mount the player is no longer riding</param>
            public void OnDismounted(IRidable mount) => RideMovement.RemoveHandles();

            /// <summary> Constructs a fresh clean player instance. With 
            /// the appropriate settings based off the
            /// /// <see cref="Config.Config.CURRENT">current world</see> </summary>
            /// <returns>The newly instantiated player instance.</returns>
            public static Player Build() {
                Player p = new();
                p.camera = new PlayerCamera();
                p.PrimaryI = new InventoryController.Inventory(Config.Config.CURRENT.GamePlay.Inventory.value.PrimarySlotCount);
                p.SecondaryI = new InventoryController.Inventory(Config.Config.CURRENT.GamePlay.Inventory.value.SecondarySlotCount);
                p.ArmorI = new ArmorInventory();
                p.info.entityType = (uint)Config.Config.CURRENT.Generation.Entities.RetrieveIndex("Player");
                p.info.entityId = Guid.NewGuid();

                p.settings = Config.Config.CURRENT.Generation.Entities.Retrieve((int)p.info.entityType).Setting as PlayerSettings;
                p.collider = new PlayerCollider(p.settings.collider, 0);
                p.vitality = new PlayerVitality();
                p.status = StreamingStatus.Live;
                StartupPlacer.PlaceOnSurface(p);
                return p;
            }

            /// <summary>Initializes the player entity with its entity settings.
            /// See <see cref="EntityManager.InitializeE(Entity, float3, uint)"/> </summary>
            /// <param name="setting">The settings of the player entity</param>
            /// <param name="Controller">The root Unity Gameobject of the player</param>
            /// <param name="GCoord">The coordinate in grid space of the player</param>
            public override void Initialize(EntitySetting setting, GameObject Controller, float3 GCoord)
            {
                settings = (PlayerSettings)setting;
                collider.OnHitGround = ProcessFallDamage;
                player = GameObject.Instantiate(Controller);
                Effects.Initialize(player);
                player.transform.SetPositionAndRotation(positionWS, collider.transform.rotation);
            }

            /// <summary>Deserializes the player entity after it has been serialized for storage.
            /// The player entity may be saved by the EntitySystem or in a seperate file depending on 
            /// whether it is streaming, but in either case, this function must be called. </summary>
            /// <param name="setting">The settings of the player entity</param>
            /// <param name="Controller">The root Unity Gameobject of the player</param>
            /// <param name="GCoord">Retrievs the saved coordinate in grid space of the player</param>
            public override void Deserialize(EntitySetting setting, GameObject Controller, out int3 GCoord)
            {
                info.entityType = (uint)Config.Config.CURRENT.Generation.Entities.RetrieveIndex("Player");
                settings = (PlayerSettings)setting;
                GCoord = (int3)this.origin;
                player = GameObject.Instantiate(Controller);
                Effects.Initialize(player);
                player.transform.SetPositionAndRotation(positionWS, collider.transform.rotation);
                collider.OnHitGround = ProcessFallDamage;
                camera.Deserailize(this);
                if (!IsDead) return;

                Effects.Play("Die");
                SetLayerRecursively(player.transform, LayerMask.NameToLayer("Default"));
            }


            /// <summary> Updates the player instance via the Entity system.
            /// This function should and will not be called if the player
            /// is the current active player tied to the user. </summary>
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
                OnInLiquid: (dens) => {
                    velocity += EntityJob.cxt.deltaTime * -EntityJob.cxt.gravity;
                    collider.useGravity = false;
                }, OnInGas: null);

                //Apply gravity and take over physics updating
                collider.JobUpdate(this);
                EntityManager.AddHandlerEvent(() => {
                    if (player == null) return;
                    player.transform.SetPositionAndRotation(this.positionWS, collider.transform.rotation);
                });
            }

            /// <summary>Disables the player instance, releasing all resources tied to it. </summary>
            public override void Disable() {
                if (player != null) GameObject.Destroy(player);
            }

            /// <summary>Draws Gizmos overlays for the player (Unity Editor Only).</summary>
            public override void OnDrawGizmos() {
                Gizmos.color = Color.blue;
                Gizmos.DrawWireCube(CPUMapManager.GSToWS(position), Vector3.one * 2);
            }
            
            private void DetatchStreamer() {
                if (status == StreamingStatus.Disconnected) return;
                status = StreamingStatus.Disconnected;
                Effects.Play("Die");
                SetLayerRecursively(player.transform, LayerMask.NameToLayer("Default"));

                GameOverHandler.Activate();
            }

            private void SetLayerRecursively(Transform obj, int layer)
            {
                obj.gameObject.layer = layer;
                foreach (Transform child in obj)
                {
                    SetLayerRecursively(child, layer);
                }
            }
            

            private void ProcessFallDamage(float zVelDelta)
            {
                eventCtrl.RaiseEvent(GameEvent.Entity_HitGround, this, null, ref zVelDelta);
                if (zVelDelta <= Vitality.FallDmgThresh) return;
                float dmgIntensity = zVelDelta - Vitality.FallDmgThresh;
                dmgIntensity = math.pow(dmgIntensity, Config.Config.CURRENT.GamePlay.Player.value.Physicality.value.weight);
                TakeDamage(dmgIntensity, 0, null);
            }

            private enum StreamingStatus
            {
                Live,
                Disconnected
            }
        }
    }
}
