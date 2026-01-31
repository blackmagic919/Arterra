using UnityEngine;
using Unity.Mathematics;
using System;
using Arterra.Configuration;
using System.Collections.Generic;
using Arterra.Data.Entity;
using Arterra.Core.Events;
using Arterra.GamePlay.Interaction;
using TerrainCollider = Arterra.GamePlay.Interaction.TerrainCollider;

namespace Arterra.Configuration.Gameplay.Player{
    /// <summary>
    /// A collection of settings that describe how the player moves.
    /// Movement settings may change during gameplay.
    /// </summary>
    [Serializable]
    public class Movement : ICloneable{
        /// <summary> The maximum speed the player can walk at, in terms of world space. </summary>
        public float walkSpeed = 10f;
        /// <summary> The maximum speed the player can run at, in terms of world space. </summary>
        public float runSpeed = 15f;
        /// <summary>  How much force is applied to the player when they jump, in terms of world space. </summary>
        public float jumpForce = 8f;
        /// <summary> How much speed the user gains when moving, in terms of world space. The acceleration is added onto velocity
        /// meaning the comparative strength of friction increases with velocity. </summary>
        public float acceleration = 50f;
        /// <summary> How far below the player the ground needs to be for the player to be 'on the ground'. 
        /// Being on the ground may affect their ability to jump and the friction they experience. </summary>
        public float groundStickDist = 0.05f;
        /// <summary> A multiplier applied to all movement limits when the player is flying. For example, a multiplier of 2
        /// will mean the player will fly twice as fast as they can run </summary>
        public float flightSpeedMultiplier = 2f;
        
        /// <summary> Creates a new instance of the movement settings, 
        /// copying the values from this instance. </summary>
        /// <returns>The new instance</returns>
        public object Clone() {
            return new Movement {
                walkSpeed = this.walkSpeed,
                runSpeed = this.runSpeed,
                jumpForce = this.jumpForce,
                acceleration = this.acceleration,
                groundStickDist = this.groundStickDist,
                flightSpeedMultiplier = this.flightSpeedMultiplier,
            };
        }
    }
}

namespace Arterra.GamePlay {

    /// <summary>The Manager responsible for handling all types of player movement
    /// and switching between them depending on the situation.  </summary>
    public class PlayerMovement {
        private static Configuration.Gameplay.Player.Movement Setting => Config.CURRENT.GamePlay.Player.value.movement;
        private static ref float3 velocity => ref PlayerHandler.data.collider.transform.velocity;
        private static ref PlayerStreamer.Player data => ref PlayerHandler.data;
        /// <summary>Whether or not the player is currently sprinting.</summary>
        public static bool IsSprinting;
        /// <summary>The xz direction the player desires to move </summary>
        public static float2 InputDir;

        /// <summary>Initializes the players movements and binds the keybind entrypoints to the system. </summary>
        public static void Initialize() {
            //These constructors will hook themselves to input modules and will not be garbage collected
            InputPoller.AddBinding(new ActionBind("Move Vertical", y => InputDir.y = y), "PlayerMove:MV", "4.0::Movement");
            InputPoller.AddBinding(new ActionBind("Move Horizontal", x => InputDir.x = x), "PlayerMove:MH", "4.0::Movement");
            InputPoller.AddBinding(new ActionBind("Sprint", x => { IsSprinting = true; }),  "PlayerMove:SPR", "4.0::Movement");
            SurfaceMovement.Initialize();
            FlightMovement.Initialize();
            SwimMovement.Initialize();
            RideMovement.Initialize();
        }

        /// <summary> Updates the player's movement. </summary>
        public static void Update() {
            InputPoller.InvokeStackTop("Movement::Update");
            data.player.transform.SetPositionAndRotation(data.positionWS, data.collider.transform.rotation);
        }

        /// <summary>Gets the 2D velocity as a 3D vector normalized over the player's maximum movement speed. </summary>
        /// <param name="velocity">The vector whose speed is returned</param>
        /// <returns>The maxspeed normalized 3D vector</returns>
        public static float3 GetSpeed2D(float3 velocity) {
            quaternion WSToOSNoScale = Quaternion.Inverse(math.normalize(PlayerHandler.data.Facing));
            velocity = math.mul(WSToOSNoScale, velocity);
            return new float3(velocity.x, 0, velocity.z) / Setting.runSpeed;
        }

        /// <summary>Gets the 3D velocity as a 3D vector normalized over the player's maximum movement speed. </summary>
        /// <param name="velocity">The vector whose speed is returned</param>
        /// <returns>The maxspeed normalized 3D vector</returns>
        public static float3 GetSpeed3D(float3 velocity) {
            quaternion WSToOSNoScale = Quaternion.Inverse(math.normalize(PlayerHandler.data.Facing));
            velocity = math.mul(WSToOSNoScale, velocity);
            return velocity / Setting.runSpeed;
        }

        /// <summary>Adds the 2D delta velocity to the player's xz velocity whilst
        /// respecting the player's current and maximum movement speeds. </summary>
        /// <param name="delta">The change to the player's xz velocity</param>
        /// <param name="maxSpeed">The maximum movement speed of the player</param>
        public static void AddVelocity2D(float2 delta, float maxSpeed) {
            if (math.length(velocity.xz) > maxSpeed) return;
            velocity.xz += delta;
        }

        /// <summary>Adds the 3D delta velocity to the player's velocity whilst
        /// respecting the player's current and maximum movement speeds. </summary>
        /// <param name="delta">The change to the player's 3D velocity</param>
        /// <param name="maxSpeed">The maximum movement speed of the player</param>
        public static void AddVelocity3D(float3 delta, float maxSpeed) {
            if (math.length(velocity) > maxSpeed) return;
            velocity += delta;
        }
    }

    /// <summary> Controls the player's normal movement on the terrain surface. </summary>
    public static class SurfaceMovement {
        private static Configuration.Gameplay.Player.Movement Setting => Config.CURRENT.GamePlay.Player.value.movement;
        private static ref float3 velocity => ref PlayerHandler.data.collider.transform.velocity;
        private static float moveSpeed => PlayerMovement.IsSprinting ? Setting.runSpeed : Setting.walkSpeed;

        /// <summary>Initializes the player's surface movement pattern, and enables it</summary>
        public static void Initialize() {
            InputPoller.AddStackPoll(new ActionBind("GroundMove::1", _ => Update()), "Movement::Update");
            InputPoller.AddStackPoll(new ActionBind("GroundMove::2", _ => PlayerHandler.data.collider.useGravity = true), "Movement::Gravity");
            InputPoller.AddBinding(new ActionBind("Jump", (_null_) => {
                TerrainCollider.Settings collider = PlayerHandler.data.settings.collider;
                if (PlayerHandler.data.collider.SampleCollision(PlayerHandler.data.origin, new float3(collider.size.x, -Setting.groundStickDist, collider.size.z), out _)) {
                    float3 jumpVelocity = Setting.jumpForce * (float3)Vector3.up;
                    PlayerHandler.data.eventCtrl.RaiseEvent(GameEvent.Action_Jump,  PlayerHandler.data, null, jumpVelocity);
                    velocity += jumpVelocity;
                }
            }), "PMSurfaceMovement:JMP", "4.0::Movement");
        }
        
        private static void Update() {
            float2 desiredMove = (PlayerHandler.data.Forward * PlayerMovement.InputDir.y + PlayerHandler.data.Right * PlayerMovement.InputDir.x).xz;
            float2 deltaV = Setting.acceleration * Time.deltaTime * desiredMove;
            PlayerMovement.AddVelocity2D(deltaV, moveSpeed);

            PlayerHandler.data.Effects.PlayAnimatorMove(PlayerMovement.GetSpeed2D(velocity));
            PlayerMovement.IsSprinting = false;
            PlayerMovement.InputDir = float2.zero;
        }
    }

    /// <summary> Controls the player's underwater movement including swimming or wading </summary>
    public static class SwimMovement{
        private static HashSet<string> OveridableStates = new HashSet<string>{ "GroundMove::1" };
        private static Configuration.Gameplay.Player.Movement Setting => Config.CURRENT.GamePlay.Player.value.movement;
        private static float MoveSpeed => PlayerMovement.IsSprinting ? Setting.runSpeed : Setting.walkSpeed;
        private static ref float3 velocity => ref PlayerHandler.data.collider.transform.velocity;
        private static bool isSwimming = false;

        /// <summary> Initializes the swimming system. </summary>
        public static void Initialize() {
            PlayerHandler.data.eventCtrl.AddEventHandler(GameEvent.Entity_InLiquid, StartSwim);
            PlayerHandler.data.eventCtrl.AddEventHandler(GameEvent.Entity_InGas, StopSwim);
            isSwimming = false;
        }

        /// <summary>=Enables underwater movement as the player's current movement pattern</summary>
        /// <param name="_"></param>
        public static void StartSwim(object source, object target, object density) { //ctx: float
            if (isSwimming) return;
            if (!OveridableStates.Contains(InputPoller.PeekTop("Movement::Update")))
                return;
            
            isSwimming = true;
            AddHandles();
        }

        ///<summary>=Disables underwater movement and returns player to original movement pattern</summary>
        public static void StopSwim(object source, object target, object density){ //ctx: float
            if(!isSwimming) return;
            isSwimming = false;
            RemoveHandles();
        }

        //Two modes: If Sprinting, swim in direction of camera, otherwise, apply gravity but with friction on it
        private static void AddHandles(){
            InputPoller.AddStackPoll(new ActionBind("SwimMove::1", _ => Update()), "Movement::Update");
            InputPoller.AddStackPoll(new ActionBind("SwimMove::2", _ => PlayerHandler.data.collider.useGravity = true), "Movement::Gravity");
            InputPoller.AddKeyBindChange(() => {
                InputPoller.AddBinding(new ActionBind("Ascend", (_null_) => {
                    if(velocity.y < MoveSpeed) velocity.y += Setting.acceleration * Time.deltaTime;
                }), "PMSwimMove:ASD", "4.0::Movement");
                InputPoller.AddBinding(new ActionBind("Descend", (_null_) => {
                    if(velocity.y > -MoveSpeed) velocity.y -= Setting.acceleration * Time.deltaTime;
                }), "PMSwimMove:DSD", "4.0::Movement");
            });
        }

        private static void RemoveHandles(){
            InputPoller.RemoveStackPoll("SwimMove::1", "Movement::Update");
            InputPoller.RemoveStackPoll("SwimMove::2", "Movement::Gravity");
            InputPoller.AddKeyBindChange(() => {
                InputPoller.RemoveBinding("PMSwimMove:ASD", "4.0::Movement");
                InputPoller.RemoveBinding("PMSwimMove:DSD", "4.0::Movement");
            });
        }

        private static void Update(){
            float3 desiredMove = PlayerHandler.data.Forward *PlayerMovement.InputDir.y + PlayerHandler.data.Right *PlayerMovement.InputDir.x;
            float3 deltaV = Setting.acceleration * Time.deltaTime * desiredMove;

            velocity.y *= 1 - TerrainCollider.BaseFriction;
            if (PlayerMovement.IsSprinting) PlayerMovement.AddVelocity3D(deltaV, MoveSpeed);
            else if (!PlayerMovement.IsSprinting) PlayerMovement.AddVelocity2D(deltaV.xz, MoveSpeed);
            
            PlayerHandler.data.Effects.PlayAnimatorMove(PlayerMovement.GetSpeed2D(velocity));
            PlayerMovement.IsSprinting = false;
            PlayerMovement.InputDir = float2.zero;
        }
    }

    /// <summary> The Manager responsible for controlling the player's movement when they fly.
    /// See <see cref="Configuration.Gameplay.Gamemodes.Flight"/>.  </summary>
    public static class FlightMovement {
        private static HashSet<string> OveridableStates = new HashSet<string>{ "GroundMove::1", "SwimMove::1" };
        private static Configuration.Gameplay.Player.Movement Setting => Config.CURRENT.GamePlay.Player.value.movement;
        private static ref float3 velocity => ref PlayerHandler.data.collider.transform.velocity;
        private static float MoveSpeed => Setting.flightSpeedMultiplier * (PlayerMovement.IsSprinting ? Setting.runSpeed : Setting.walkSpeed);
        private static bool HasEnabledFlight;
        private static bool IsFlying;

        /// <summary> Initializes the player's flight movement and 
        /// sets up to the triggers to enable it. </summary>
        public static void Initialize() {
            IsFlying = false;
            HasEnabledFlight = false;
            Config.CURRENT.System.GameplayModifyHooks.Add("Gamemode:Flight", OnFlightRuleChanged);
            object EnableFlight = Config.CURRENT.GamePlay.Gamemodes.value.Flight;
            OnFlightRuleChanged(ref EnableFlight);
        }

        private static void OnFlightRuleChanged(ref object flightRule) {
            bool EnableFlight = (bool)flightRule; 
            if (HasEnabledFlight == EnableFlight) return;
            HasEnabledFlight = EnableFlight;

            if (!EnableFlight) {
                InputPoller.RemoveBinding("PMFlightMove:TF", "4.0::Movement");
                RemoveHandles();
            } else {
                InputPoller.AddBinding(new ActionBind("Toggle Fly", (_null_) => {
                    if (IsFlying) RemoveHandles();
                    else AddHandles();
                    velocity.y = 0;
                }), "PMFlightMove:TF", "4.0::Movement");   
            }
        }
        private static void AddHandles() {
            if (!OveridableStates.Contains(InputPoller.PeekTop("Movement::Update"))) return;
            IsFlying = true;

            InputPoller.AddStackPoll(new ActionBind("FlightMove::1", _ => Update()), "Movement::Update");
            InputPoller.AddStackPoll(new ActionBind("FlightMove::2", _ => PlayerHandler.data.collider.useGravity = false), "Movement::Gravity");
            InputPoller.AddKeyBindChange(() => {
                InputPoller.AddBinding(new ActionBind("Ascend", (_null_) => {
                    if (velocity.y < MoveSpeed) velocity.y += Setting.acceleration * Time.deltaTime * Setting.flightSpeedMultiplier;
                }), "PMFlightMove:ASD", "4.0::Movement");
                InputPoller.AddBinding(new ActionBind("Descend", (_null_) => {
                    if (velocity.y > -MoveSpeed) velocity.y -= Setting.acceleration * Time.deltaTime * Setting.flightSpeedMultiplier;
                }), "PMFlightMove:DSD", "4.0::Movement");
            });
        }

        private static void RemoveHandles() {
            IsFlying = false;
            InputPoller.RemoveStackPoll("FlightMove::1", "Movement::Update");
            InputPoller.RemoveStackPoll("FlightMove::2", "Movement::Gravity");
            InputPoller.AddKeyBindChange(() => {
                InputPoller.RemoveBinding("PMFlightMove:ASD", "4.0::Movement");
                InputPoller.RemoveBinding("PMFlightMove:DSD", "4.0::Movement");
            });
        }

        private static void Update() {
            float2 desiredMove = (PlayerHandler.data.Forward * PlayerMovement.InputDir.y + PlayerHandler.data.Right * PlayerMovement.InputDir.x).xz;
            float2 deltaV = Setting.acceleration * Time.deltaTime * desiredMove * Setting.flightSpeedMultiplier;

            PlayerMovement.AddVelocity2D(deltaV, MoveSpeed);
            PlayerHandler.data.Effects.PlayAnimatorMove(PlayerMovement.GetSpeed3D(velocity));
            PlayerMovement.IsSprinting = false;
            PlayerMovement.InputDir = float2.zero;
        }
    }

    /// <summary> The Manager responsible for translating the player's movement when they are riding something.
    /// See <see cref="IRidable"/> for more info. </summary>
    public static class RideMovement {
        private static HashSet<string> OveridableStates = new HashSet<string> { "GroundMove::1", "SwimMove::1", "FlightMove::1" };
        private static IRidable mount;
        private static bool IsActive => mount != null && (mount as Entity).active;
        private static Transform SubTransform => PlayerHandler.data.player.transform.GetChild(0).transform;

        /// <summary>Initializes the rider movement system.</summary>
        public static void Initialize() {
            mount = null;
        }
        /// <summary>Enables the Rider Movement pattern as the player's current movement pattern.</summary>
        /// <param name="mount"></param>
        public static void AddHandles(IRidable mount) {
            if (mount == null) return;
            if (!OveridableStates.Contains(InputPoller.PeekTop("Movement::Update"))) {
                mount.Dismount();
                return;
            }

            PlayerHandler.data.collider.transform.velocity = float3.zero;
            InputPoller.AddStackPoll(new ActionBind("RideMove::2", _ => PlayerHandler.data.collider.useGravity = false), "Movement::Gravity");
            InputPoller.AddStackPoll(new ActionBind("RideMove::1", _ => Update()), "Movement::Update");
            PlayerHandler.data.eventCtrl.RaiseEvent(GameEvent.Action_MountRideable, PlayerHandler.data, mount);

            InputPoller.AddKeyBindChange(() => {
                InputPoller.AddContextFence("PMRideMove", "4.0::Movement", ActionBind.Exclusion.ExcludeLayer);
                InputPoller.AddBinding(new ActionBind("Move Vertical", MoveForward), "PMRideMove:MV", "4.0::Movement");
                InputPoller.AddBinding(new ActionBind("Move Horizontal", Rotate), "PMRideMove:MH", "4.0::Movement");
                InputPoller.AddBinding(new ActionBind("Dismount", _ => mount?.Dismount()), "PMRideMove:DSM", "5.0::Gameplay");
            });
            RideMovement.mount = mount;
        }

        /// <summary>Disables the Rider Movement Pattern returning the player to their original movement pattern.</summary>
        public static void RemoveHandles() {
            InputPoller.RemoveStackPoll("RideMove::1", "Movement::Update");
            InputPoller.RemoveStackPoll("RideMove::2", "Movement::Gravity");
            PlayerHandler.data.eventCtrl.RaiseEvent(GameEvent.Action_DismountRideable, PlayerHandler.data, mount);
            SubTransform.transform.localRotation = Quaternion.identity;


            InputPoller.AddKeyBindChange(() => {
                InputPoller.RemoveContextFence("PMRideMove", "4.0::Movement");
                InputPoller.RemoveBinding("PMRideMove:DSM", "5.0::Gameplay");
            });
            mount = null;
        }

        private static void Update() {
            if (!IsActive) return;
            Transform root = mount.GetRiderRoot();
            float3 rootPos = Arterra.Core.Storage.CPUMapManager.WSToGS(root.position);
            PlayerHandler.data.position = rootPos + new float3(0, PlayerHandler.data.settings.collider.size.y / 2, 0);
            SubTransform.rotation = root.rotation;
        }

        private static void MoveForward(float strength) {
            if (!IsActive) return;
            if (math.abs(strength) <= 1E-05f) return;
            Transform root = mount.GetRiderRoot();
            float3 aim = root.forward * strength;
            mount.WalkInDirection(aim);
        }

        private static void Rotate(float strength) {
            if (!IsActive) return;
            if (math.abs(strength) <= 1E-05f) return;
            Transform root = mount.GetRiderRoot();
            float3 aim = root.right * strength;
            mount.WalkInDirection(aim);
        }
    }
}