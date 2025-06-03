using UnityEngine;
using Unity.Mathematics;
using System;
using WorldConfig;
using WorldConfig.Gameplay.Player;
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
        /// <summary> Controls how the camera rotates and responds to user input. See <see cref="Gameplay.CameraMovement"/> for more information </summary>

        public object Clone(){
            return new Movement{
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
public static class PlayerMovement
{
    public static WorldConfig.Gameplay.Player.Camera Camera => Config.CURRENT.GamePlay.Player.value.Camera;
    public static WorldConfig.Gameplay.Player.Movement Setting => Config.CURRENT.GamePlay.Player.value.movement;
    private static ref PlayerStreamer.Player data => ref PlayerHandler.data;
    private static PlayerCamera cameraInput;
    public static bool IsSprinting;
    public static float2 InputDir;

    public static void Initialize(){
        //These constructors will hook themselves to input modules and will not be garbage collected
        InputPoller.AddBinding(new InputPoller.ActionBind("Move Vertical", (float y) => InputDir.y = y), "4.0::Movement");
        InputPoller.AddBinding(new InputPoller.ActionBind("Move Horizontal", (float x) => InputDir.x = x), "4.0::Movement");
        InputPoller.AddBinding(new InputPoller.ActionBind("Sprint", (float x) => {IsSprinting = true;}), "4.0::Movement");
        SurfaceMovement.Initialize();
        FlightMovement.Initialize();
        SwimMovement.Initialize();
        cameraInput = new PlayerCamera(data);
    }

    public static void Update(){ 
        cameraInput.LookRotation(ref data);
        InputPoller.InvokeStackTop("Movement::Update");
    }
}

public static class SurfaceMovement{
    public static WorldConfig.Gameplay.Player.Movement Setting => Config.CURRENT.GamePlay.Player.value.movement;
    public static ref float3 velocity => ref PlayerHandler.data.collider.velocity;
    private static float moveSpeed => PlayerMovement.IsSprinting ? Setting.runSpeed : Setting.walkSpeed;
    public static void Initialize()
    {
        InputPoller.AddStackPoll(new InputPoller.ActionBind("GroundMove::1", _ => Update()), "Movement::Update");
        InputPoller.AddStackPoll(new InputPoller.ActionBind("GroundMove::2", _ => PlayerHandler.data.collider.useGravity = true), "Movement::Gravity");
        InputPoller.AddBinding(new InputPoller.ActionBind("Jump", (_null_) => {
            TerrainColliderJob.Settings collider = PlayerHandler.data.settings.collider;
            if(PlayerHandler.data.collider.SampleCollision(PlayerHandler.data.origin, new float3(collider.size.x, -Setting.groundStickDist, collider.size.z), out _))
                velocity += Setting.jumpForce * (float3)Vector3.up;
        }), "4.0::Movement");
    }

    public static void Update(){ 
        float2 desiredMove = ((float3)(PlayerHandler.camera.forward*PlayerMovement.InputDir.y + PlayerHandler.camera.right*PlayerMovement.InputDir.x)).xz;
        float2 deltaV = Setting.acceleration * Time.deltaTime * desiredMove;

        if(math.length(velocity.xz) < moveSpeed) 
            velocity.xz += deltaV;

        if(math.length(deltaV) > 0.1f){
            if(PlayerMovement.IsSprinting) PlayerHandler.data.animator.SetTrigger("IsRunning");
            else PlayerHandler.data.animator.SetTrigger("IsWalking");
        };

        PlayerMovement.IsSprinting = false;
        PlayerMovement.InputDir = float2.zero;
    }
}

public static class FlightMovement{
    public static WorldConfig.Gameplay.Player.Movement Setting => Config.CURRENT.GamePlay.Player.value.movement;
    public static ref float3 velocity => ref PlayerHandler.data.collider.velocity;
    private static float MoveSpeed => Setting.flightSpeedMultiplier * (PlayerMovement.IsSprinting ? Setting.runSpeed : Setting.walkSpeed);
    private static int[] KeyBinds = null;
    public static void Initialize()
    {
        Config.CURRENT.System.GameplayModifyHooks.Add("Gamemode:Flight", OnFlightRuleChanged);
        OnFlightRuleChanged();
    }
    
    public static void OnFlightRuleChanged(){
        bool EnableFlight = Config.CURRENT.GamePlay.Gamemodes.value.Flight;
        if(!EnableFlight){
            InputPoller.TryRemove("ToggleFly", "4.0::Movement");
            if(KeyBinds != null) RemoveHandles();
        } else {
            if(InputPoller.TryAdd(new InputPoller.ActionBind("ToggleFly", (_null_) => {
                if(KeyBinds == null) AddHandles();
                else RemoveHandles();
            }), "4.0::Movement")) 
                KeyBinds = null;
        }
    }
    private static void AddHandles(){
        InputPoller.AddStackPoll(new InputPoller.ActionBind("FlightMove::1", _ => Update()), "Movement::Update");
        InputPoller.AddStackPoll(new InputPoller.ActionBind("FlightMove::2", _ => PlayerHandler.data.collider.useGravity = false), "Movement::Gravity");
        InputPoller.AddKeyBindChange(() => {
            KeyBinds = InputPoller.KeyBindSaver.Rent(2);
            KeyBinds[0] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Ascend", (_null_) => {
                if(velocity.y < MoveSpeed) velocity.y += Setting.acceleration * Time.deltaTime * Setting.flightSpeedMultiplier;
            }), "4.0::Movement");
            KeyBinds[1] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Descend", (_null_) => {
                if(velocity.y > -MoveSpeed) velocity.y -= Setting.acceleration * Time.deltaTime * Setting.flightSpeedMultiplier;
            }), "4.0::Movement");
        });
    }

    private static void RemoveHandles(){
        InputPoller.RemoveStackPoll("FlightMove::1", "Movement::Update");
        InputPoller.RemoveStackPoll("FlightMove::2", "Movement::Gravity");
        InputPoller.AddKeyBindChange(() => {
            InputPoller.RemoveKeyBind((uint)KeyBinds[0], "4.0::Movement");
            InputPoller.RemoveKeyBind((uint)KeyBinds[1], "4.0::Movement");
            InputPoller.KeyBindSaver.Return(KeyBinds);
            KeyBinds = null;
        });
    }

    public static void Update(){
        float2 desiredMove = ((float3)(PlayerHandler.camera.forward*PlayerMovement.InputDir.y + PlayerHandler.camera.right*PlayerMovement.InputDir.x)).xz;
        float2 deltaV = Setting.acceleration * Time.deltaTime * desiredMove *  Setting.flightSpeedMultiplier;

        velocity.y *= 1 - PlayerHandler.data.settings.collider.friction;
        if(math.length(velocity.xz) < MoveSpeed) 
            velocity.xz += deltaV;

        if(math.length(deltaV) > 0.1f){
            if(PlayerMovement.IsSprinting) PlayerHandler.data.animator.SetTrigger("IsRunning");
            else PlayerHandler.data.animator.SetTrigger("IsWalking");
        };
        
        PlayerMovement.IsSprinting = false;
        PlayerMovement.InputDir = float2.zero;
    }
}

public static class SwimMovement{
    public static WorldConfig.Gameplay.Player.Movement Setting => Config.CURRENT.GamePlay.Player.value.movement;
    private static float MoveSpeed => PlayerMovement.IsSprinting ? Setting.runSpeed : Setting.walkSpeed;
    public static ref float3 velocity => ref PlayerHandler.data.collider.velocity;
    private static int[] KeyBinds = null;
    private static bool isSwimming = false;
    public static void Initialize()
    {
        isSwimming = false;
    }

    public static void StartSwim(float _){
        if(isSwimming) return;
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
            KeyBinds = InputPoller.KeyBindSaver.Rent(2);
            KeyBinds[0] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Ascend", (_null_) => {
                if(velocity.y < MoveSpeed) velocity.y += Setting.acceleration * Time.deltaTime * Setting.flightSpeedMultiplier;
            }), "4.0::Movement");
            KeyBinds[1] = (int)InputPoller.AddBinding(new InputPoller.ActionBind("Descend", (_null_) => {
                if(velocity.y > -MoveSpeed) velocity.y -= Setting.acceleration * Time.deltaTime * Setting.flightSpeedMultiplier;
            }), "4.0::Movement");
        });
    }

    private static void RemoveHandles(){
        InputPoller.RemoveStackPoll("SwimMove::1", "Movement::Update");
        InputPoller.RemoveStackPoll("SwimMove::2", "Movement::Gravity");
        InputPoller.AddKeyBindChange(() => {
            InputPoller.RemoveKeyBind((uint)KeyBinds[0], "4.0::Movement");
            InputPoller.RemoveKeyBind((uint)KeyBinds[1], "4.0::Movement");
            InputPoller.KeyBindSaver.Return(KeyBinds);
            KeyBinds = null;
        });
    }

    public static void Update(){
        float3 desiredMove = ((float3)(PlayerHandler.camera.forward*PlayerMovement.InputDir.y + PlayerHandler.camera.right*PlayerMovement.InputDir.x));
        float3 deltaV = Setting.acceleration * Time.deltaTime * desiredMove *  Setting.flightSpeedMultiplier;

        velocity.y *= 1 - PlayerHandler.data.settings.collider.friction;
        if(PlayerMovement.IsSprinting && math.length(velocity) < MoveSpeed) velocity += deltaV;
        else if(!PlayerMovement.IsSprinting && math.length(velocity.xz) < MoveSpeed) velocity.xz += deltaV.xz;

        if(math.length(deltaV) > 0.1f){
            if(PlayerMovement.IsSprinting) PlayerHandler.data.animator.SetTrigger("IsRunning");
            else PlayerHandler.data.animator.SetTrigger("IsWalking");
        };
        
        PlayerMovement.IsSprinting = false;
        PlayerMovement.InputDir = float2.zero;
    }
}
