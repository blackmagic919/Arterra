using UnityEngine;
using Unity.Mathematics;
using System;
using WorldConfig;
using WorldConfig.Gameplay.Player;
using System.Collections.Generic;
using WorldConfig.Generation.Entity;
namespace WorldConfig.Gameplay.Player{
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
public class PlayerMovement {
    public static WorldConfig.Gameplay.Player.Movement Setting => Config.CURRENT.GamePlay.Player.value.movement;
    private static ref PlayerStreamer.Player data => ref PlayerHandler.data;
    public static bool IsSprinting;
    public static float2 InputDir;

    public static void Initialize() {
        //These constructors will hook themselves to input modules and will not be garbage collected
        InputPoller.AddBinding(new InputPoller.ActionBind("Move Vertical", (float y) => InputDir.y = y), "4.0::Movement");
        InputPoller.AddBinding(new InputPoller.ActionBind("Move Horizontal", (float x) => InputDir.x = x), "4.0::Movement");
        InputPoller.AddBinding(new InputPoller.ActionBind("Sprint", (float x) => { IsSprinting = true; }), "4.0::Movement");
        SurfaceMovement.Initialize();
        FlightMovement.Initialize();
        SwimMovement.Initialize();
        RideMovement.Initialize();
    }

    public static void Update() {
        InputPoller.InvokeStackTop("Movement::Update");
        data.player.transform.SetPositionAndRotation(data.positionWS, data.collider.transform.rotation);
    }

    public static void SetAnimatorSpeed(float speed) {
        speed = Mathf.InverseLerp(0, Setting.runSpeed, speed);
        PlayerHandler.data.animator.SetFloat("Speed", math.lerp(PlayerHandler.data.animator.GetFloat("Speed"), speed, 0.35f));
    }
}

public static class SurfaceMovement {
    public static WorldConfig.Gameplay.Player.Movement Setting => Config.CURRENT.GamePlay.Player.value.movement;
    public static ref float3 velocity => ref PlayerHandler.data.collider.velocity;
    private static float moveSpeed => PlayerMovement.IsSprinting ? Setting.runSpeed : Setting.walkSpeed;
    public static void Initialize() {
        InputPoller.AddStackPoll(new InputPoller.ActionBind("GroundMove::1", _ => Update()), "Movement::Update");
        InputPoller.AddStackPoll(new InputPoller.ActionBind("GroundMove::2", _ => PlayerHandler.data.collider.useGravity = true), "Movement::Gravity");
        InputPoller.AddBinding(new InputPoller.ActionBind("Jump", (_null_) => {
            TerrainCollider.Settings collider = PlayerHandler.data.settings.collider;
            if (PlayerHandler.data.collider.SampleCollision(PlayerHandler.data.origin, new float3(collider.size.x, -Setting.groundStickDist, collider.size.z), out _)) {
                velocity += Setting.jumpForce * (float3)Vector3.up;
                PlayerHandler.data.animator.SetBool("IsJumping", true);
            }
        }), "4.0::Movement");
    }

    public static void Update() {
        float2 desiredMove = (PlayerHandler.data.Forward * PlayerMovement.InputDir.y + PlayerHandler.data.Right * PlayerMovement.InputDir.x).xz;
        float2 deltaV = Setting.acceleration * Time.deltaTime * desiredMove;

        if (math.length(velocity.xz) < moveSpeed)
            velocity.xz += deltaV;

        if (PlayerHandler.data.animator.GetBool("IsJumping") && PlayerHandler.data.animator.GetBool("_InProgress")) {
            TerrainCollider.Settings collider = PlayerHandler.data.settings.collider;
            if (PlayerHandler.data.collider.SampleCollision(PlayerHandler.data.origin, new float3(collider.size.x, -Setting.groundStickDist, collider.size.z), out _)) {
                PlayerHandler.data.animator.SetBool("IsJumping", false);
            }
        }

        PlayerMovement.SetAnimatorSpeed(math.length(velocity.xz));
        PlayerMovement.IsSprinting = false;
        PlayerMovement.InputDir = float2.zero;
    }
}


public static class SwimMovement{
    private static HashSet<string> OveridableStates = new HashSet<string>{ "GroundMove::1" };
    public static WorldConfig.Gameplay.Player.Movement Setting => Config.CURRENT.GamePlay.Player.value.movement;
    private static float MoveSpeed => PlayerMovement.IsSprinting ? Setting.runSpeed : Setting.walkSpeed;
    public static ref float3 velocity => ref PlayerHandler.data.collider.velocity;
    private static int[] KeyBinds = null;
    private static bool isSwimming = false;

    public static void Initialize() {
        isSwimming = false;
    }

    public static void StartSwim(float _) {
        PlayerHandler.data.animator.SetBool("IsSwimming", true);
        PlayerHandler.data.animator.SetBool("IsJumping", false);
        if (isSwimming) return;
        if (!OveridableStates.Contains(InputPoller.PeekTop("Movement::Update")))
            return;
        
        isSwimming = true;
        AddHandles();
    }

    public static void StopSwim(float _){
        if(!isSwimming) return;
        isSwimming = false;
        RemoveHandles();
    }

    //Two modes: If Sprinting, swim in direction of camera, otherwise, apply gravity but with friction on it
    private static void AddHandles(){
        InputPoller.AddStackPoll(new InputPoller.ActionBind("SwimMove::1", _ => Update()), "Movement::Update");
        InputPoller.AddStackPoll(new InputPoller.ActionBind("SwimMove::2", _ => PlayerHandler.data.collider.useGravity = true), "Movement::Gravity");
        InputPoller.AddKeyBindChange(() => {
            KeyBinds = new int[2];
            KeyBinds[0] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Ascend", (_null_) => {
                if(velocity.y < MoveSpeed) velocity.y += Setting.acceleration * Time.deltaTime;
            }), "4.0::Movement");
            KeyBinds[1] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Descend", (_null_) => {
                if(velocity.y > -MoveSpeed) velocity.y -= Setting.acceleration * Time.deltaTime;
            }), "4.0::Movement");
        });
    }

    private static void RemoveHandles(){
        InputPoller.RemoveStackPoll("SwimMove::1", "Movement::Update");
        InputPoller.RemoveStackPoll("SwimMove::2", "Movement::Gravity");
        InputPoller.AddKeyBindChange(() => {
            InputPoller.RemoveKeyBind((uint)KeyBinds[0], "4.0::Movement");
            InputPoller.RemoveKeyBind((uint)KeyBinds[1], "4.0::Movement");
            KeyBinds = null;
        });
    }

    public static void Update(){
        float3 desiredMove = PlayerHandler.data.Forward *PlayerMovement.InputDir.y + PlayerHandler.data.Right *PlayerMovement.InputDir.x;
        float3 deltaV = Setting.acceleration * Time.deltaTime * desiredMove;

        velocity.y *= 1 - PlayerHandler.data.settings.collider.friction;
        if(PlayerMovement.IsSprinting && math.length(velocity) < MoveSpeed) velocity += deltaV;
        else if(!PlayerMovement.IsSprinting && math.length(velocity.xz) < MoveSpeed) velocity.xz += deltaV.xz;
        
        PlayerMovement.SetAnimatorSpeed(math.length(velocity));
        PlayerMovement.IsSprinting = false;
        PlayerMovement.InputDir = float2.zero;
    }
}

public static class FlightMovement {
    private static HashSet<string> OveridableStates = new HashSet<string>{ "GroundMove::1", "SwimMove::1" };
    public static WorldConfig.Gameplay.Player.Movement Setting => Config.CURRENT.GamePlay.Player.value.movement;
    public static ref float3 velocity => ref PlayerHandler.data.collider.velocity;
    private static float MoveSpeed => Setting.flightSpeedMultiplier * (PlayerMovement.IsSprinting ? Setting.runSpeed : Setting.walkSpeed);
    private static int[] KeyBinds = null;
    public static void Initialize() {
        Config.CURRENT.System.GameplayModifyHooks.Add("Gamemode:Flight", OnFlightRuleChanged);
        OnFlightRuleChanged();
    }

    public static void OnFlightRuleChanged() {
        bool EnableFlight = Config.CURRENT.GamePlay.Gamemodes.value.Flight;
        if (!EnableFlight) {
            InputPoller.TryRemove("Toggle Fly", "4.0::Movement");
            if (KeyBinds != null) RemoveHandles();
            return;
        }

        if (InputPoller.TryAdd(new InputPoller.ActionBind("Toggle Fly", (_null_) => {
            if (KeyBinds == null) AddHandles();
            else RemoveHandles();
        }), "4.0::Movement"))
            KeyBinds = null;
    }
    private static void AddHandles() {
        if (!OveridableStates.Contains(InputPoller.PeekTop("Movement::Update"))) return;
        PlayerHandler.data.animator.SetBool("IsJumping", false);
        InputPoller.AddStackPoll(new InputPoller.ActionBind("FlightMove::1", _ => Update()), "Movement::Update");
        InputPoller.AddStackPoll(new InputPoller.ActionBind("FlightMove::2", _ => PlayerHandler.data.collider.useGravity = false), "Movement::Gravity");
        InputPoller.AddKeyBindChange(() => {
            KeyBinds = new int[2];
            KeyBinds[0] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Ascend", (_null_) => {
                if (velocity.y < MoveSpeed) velocity.y += Setting.acceleration * Time.deltaTime * Setting.flightSpeedMultiplier;
            }), "4.0::Movement");
            KeyBinds[1] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Descend", (_null_) => {
                if (velocity.y > -MoveSpeed) velocity.y -= Setting.acceleration * Time.deltaTime * Setting.flightSpeedMultiplier;
            }), "4.0::Movement");
        });
    }

    private static void RemoveHandles() {
        InputPoller.RemoveStackPoll("FlightMove::1", "Movement::Update");
        InputPoller.RemoveStackPoll("FlightMove::2", "Movement::Gravity");
        InputPoller.AddKeyBindChange(() => {
            InputPoller.RemoveKeyBind((uint)KeyBinds[0], "4.0::Movement");
            InputPoller.RemoveKeyBind((uint)KeyBinds[1], "4.0::Movement");
            KeyBinds = null;
        });
    }

    public static void Update() {
        float2 desiredMove = (PlayerHandler.data.Forward * PlayerMovement.InputDir.y + PlayerHandler.data.Right * PlayerMovement.InputDir.x).xz;
        float2 deltaV = Setting.acceleration * Time.deltaTime * desiredMove * Setting.flightSpeedMultiplier;

        velocity.y *= 1 - PlayerHandler.data.settings.collider.friction;
        if (math.length(velocity.xz) < MoveSpeed)
            velocity.xz += deltaV;

        PlayerMovement.SetAnimatorSpeed(math.length(velocity));
        PlayerMovement.IsSprinting = false;
        PlayerMovement.InputDir = float2.zero;
    }
}


public static class RideMovement {
    private static HashSet<string> OveridableStates = new HashSet<string> { "GroundMove::1", "SwimMove::1", "FlightMove::1" };
    public static WorldConfig.Gameplay.Player.Movement Setting => Config.CURRENT.GamePlay.Player.value.movement;
    private static int[] KeyBinds = null;
    private static IRidable mount;
    private static bool IsActive => mount != null && (mount as Entity).active;
    private static Transform SubTransform => PlayerHandler.data.player.transform.GetChild(0).transform;

    public static void Initialize() {
        KeyBinds = null;
        mount = null;
    }
    public static void AddHandles(IRidable mount) {
        if (mount == null) return;
        if (!OveridableStates.Contains(InputPoller.PeekTop("Movement::Update"))) {
            mount.Dismount();
            return;
        }

        PlayerHandler.data.collider.velocity = float3.zero;
        InputPoller.AddStackPoll(new InputPoller.ActionBind("RideMove::2", _ => PlayerHandler.data.collider.useGravity = false), "Movement::Gravity");
        InputPoller.AddStackPoll(new InputPoller.ActionBind("RideMove::1", _ => Update()), "Movement::Update");
        PlayerHandler.data.animator.SetBool("IsSitting", true);
        PlayerHandler.data.animator.SetBool("IsJumping", false);

        InputPoller.AddKeyBindChange(() => {
            KeyBinds = new int[2];
            KeyBinds[0] = (int)InputPoller.AddContextFence("4.0::Movement", InputPoller.ActionBind.Exclusion.ExcludeLayer);
            InputPoller.AddBinding(new InputPoller.ActionBind("Move Vertical", MoveForward), "4.0::Movement");
            InputPoller.AddBinding(new InputPoller.ActionBind("Move Horizontal", Rotate), "4.0::Movement");
            KeyBinds[1] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Dismount", _ => mount?.Dismount()), "5.0::Gameplay");
        });
        RideMovement.mount = mount;
    }

    public static void RemoveHandles() {
        InputPoller.RemoveStackPoll("RideMove::1", "Movement::Update");
        InputPoller.RemoveStackPoll("RideMove::2", "Movement::Gravity");
        PlayerHandler.data.animator.SetBool("IsSitting", false);
        SubTransform.transform.localRotation = Quaternion.identity;


        InputPoller.AddKeyBindChange(() => {
            if (KeyBinds == null) return;
            InputPoller.RemoveContextFence((uint)KeyBinds[0], "4.0::Movement");
            InputPoller.RemoveKeyBind((uint)KeyBinds[1], "5.0::Gameplay");
            KeyBinds = null;
        });
        mount = null;
    }

    public static void Update() {
        if (!IsActive) return;
        Transform root = mount.GetRiderRoot();
        float3 rootPos = MapStorage.CPUMapManager.WSToGS(root.position);
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