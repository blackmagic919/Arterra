using System;
using System.Collections.Generic;
using Arterra.Core.Events;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.GamePlay.Interaction;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using System.Linq;

namespace Arterra.Data.Entity.Behavior {
    /// <summary>
    /// A collection of settings that describe how the player moves.
    /// Movement settings may change during gameplay.
    /// </summary>
    [Serializable]
    public class PlayerMovementSettings : IBehaviorSetting {
        ///<summary>Name of settings object in UI generation</summary>
        [JsonIgnore] public static string Name => "Movement";
        /// <summary>The maximum speed the player can walk at, in world-space units.</summary>
        public float walkSpeed = 3f;
        /// <summary>The maximum speed the player can run at, in world-space units.</summary>
        public float runSpeed = 6f;
        /// <summary>The upward impulse applied when the player jumps.</summary>
        public float jumpForce = 5f;
        /// <summary>
        /// How far below the player the ground can be while still counting as grounded
        /// for jump and friction behavior.
        /// </summary>
        public float groundStickDist = 0.05f;
        /// <summary>
        /// Multiplier applied to movement speed caps while flying.
        /// </summary>
        public float flightSpeedMultiplier = 3f;

        /// <summary>Creates a copy of these movement settings.</summary>
        public object Clone() {
            return new PlayerMovementSettings {
                walkSpeed = walkSpeed,
                runSpeed = runSpeed,
                jumpForce = jumpForce,
                groundStickDist = groundStickDist,
                flightSpeedMultiplier = flightSpeedMultiplier,
            };
        }
    }

    /// <summary>
    /// Handles all player locomotion patterns (ground, swim, flight, ride)
    /// and switches the active movement mode through input stack polls.
    /// </summary>
    public class PlayerMovementBehavior : SpeciesBehavior {
        private IRidable activeMount;
        [JsonIgnore] public bool IsMounted => activeMount != null;
        private const string MountedGravity = "PlayerMovement::Mounted";
        /// <summary>Resolved movement settings for this behavior instance.</summary>
        [JsonIgnore] public PlayerMovementSettings settings;
        private Modifier mod;

        private BehaviorEntity.Animal self;
        private VitalityBehavior vit;
        private bool hasBindings;
        private bool sprinting;
        private float2 inputDir;
        private GroundMovementPattern groundPattern;
        private SwimMovementPattern swimPattern;
        private FlightMovementPattern flightPattern;
        private RideMovementPattern ridePattern;

        private float RunSpeed => Modifier.GetInt(mod, MSettings.RunSpeed, settings.runSpeed);
        private float WalkSpeed => Modifier.GetInt(mod, MSettings.WalkSpeed, settings.walkSpeed);
        private float FlightSpeedMultiplier => Modifier.GetInt(mod, MSettings.FlightSpeedMult, settings.flightSpeedMultiplier);
        private float JumpForce => Modifier.GetInt(mod, MSettings.JumpForce, settings.jumpForce);

        private float MoveSpeed2D => sprinting ? RunSpeed : WalkSpeed;
        private GameEvent MoveEvent => sprinting ? GameEvent.Action_Run : GameEvent.Action_Walk;
        private float FlightMoveSpeed => FlightSpeedMultiplier * MoveSpeed2D;
        const float AccelTime = 0.075f;

        /// <summary>Declares required behavior dependencies.</summary>
        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> hierarchy) {
            hierarchy.TryAdd(Behaviors.Collider, hierarchy.Count);
        }

        /// <summary>Declares required settings dependencies.</summary>
        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> hierarchy) {
            hierarchy.TryAdd(typeof(PlayerMovementSettings), new PlayerMovementSettings());
        }

        /// <summary>Initializes movement state and input bindings for a newly spawned entity.</summary>
        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: PlayerMovementBehavior requires PlayerMovementSettings");
            if (!self.Is(out vit)) vit = null;
            if (!self.Is(out mod)) mod = null;

            this.self = self;
            self.Register(this);


        }

        /// <summary>Initializes movement state and bindings after entity deserialization.</summary>
        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: PlayerMovementBehavior requires PlayerMovementSettings");
            if (!self.Is(out vit)) vit = null;
            if (!self.Is(out mod)) mod = null;

            this.self = self;
            self.Register(this);


        }

        public override void InitializeVisual(BehaviorEntity.Animal self) {
            if (!self.IsOwner) return;
            BindCommonInput();
            InitializePatterns();
        }

        public override void DisposeVisual(BehaviorEntity.Animal self) {
            if (!self.IsOwner) return;
            UnbindCommonInput();
            groundPattern?.Disable();
            ridePattern?.Disable();
            swimPattern?.Disable();
            flightPattern?.Disable();
            groundPattern = null; ridePattern = null; swimPattern = null; flightPattern = null;
        }

        public override void Disable(BehaviorEntity.Animal self) {
            ClearMount();
            self.Unregister(typeof(PlayerMovementBehavior));
            this.self = null;
        }

        private string MoveVerticalName => $"PlayerMove:MV::{self.info.rtEntityId}";
        private string MoveHorizontalName => $"PlayerMove:MH::{self.info.rtEntityId}";
        private string SprintName => $"PlayerMove:SPR::{self.info.rtEntityId}";

        private void BindCommonInput() {
            if (hasBindings) return;
            hasBindings = true;

            self.eventCtrl.AddEventHandler(GameEvent.Action_Mount, Mount);
            self.eventCtrl.AddEventHandler(GameEvent.Action_Dismount, Dismount);
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Death, OnDeath);
            InputPoller.AddBinding(new ActionBind("Move Vertical", v => inputDir.y = v), MoveVerticalName, "4.0::Movement");
            InputPoller.AddBinding(new ActionBind("Move Horizontal", v => inputDir.x = v), MoveHorizontalName, "4.0::Movement");
            InputPoller.AddBinding(new ActionBind("Sprint", _ => sprinting = true), SprintName, "4.0::Movement");
        }

        private void UnbindCommonInput() {
            if (!hasBindings) return;
            hasBindings = false;
            self.eventCtrl.RemoveEventHandler(GameEvent.Action_Mount, Mount);
            self.eventCtrl.RemoveEventHandler(GameEvent.Action_Dismount, Dismount);
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_Death, OnDeath);

            InputPoller.RemoveBinding(MoveVerticalName, "4.0::Movement");
            InputPoller.RemoveBinding(MoveHorizontalName, "4.0::Movement");
            InputPoller.RemoveBinding(SprintName, "4.0::Movement");
        }

        private void InitializePatterns() {
            groundPattern = new GroundMovementPattern(this);
            swimPattern = new SwimMovementPattern(this);
            flightPattern = new FlightMovementPattern(this);
            ridePattern = new RideMovementPattern(this);

            groundPattern.Initialize();
            swimPattern.Initialize();
            flightPattern.Initialize();
        }


        /// <summary>Runs per-frame movement input stack processing for the controlled player.</summary>
        public override void Update(BehaviorEntity.Animal self) {
            if (activeMount != null) {
                if (activeMount.AsEntity?.active != true || activeMount.RiderTarget != self.info.rtEntityId)
                    ClearMount();
                else if (self.context == BehaviorEntity.UpdateContext.Simulation && !self.IsOwner) {
                    self.position = CPUMapManager.WSToGS(activeMount.GetRiderRoot().position)
                        + new float3(0, self.settings.collider.size.y / 2, 0);
                    self.velocity = float3.zero;
                } else if (self.context == BehaviorEntity.UpdateContext.Visual) {
                    Transform subTransform = GetRiderSubTransform();
                    if (subTransform != null) subTransform.rotation = activeMount.GetRiderRoot().rotation;
                }
            }
            if (!self.VisualActive || self.context != BehaviorEntity.UpdateContext.ClientSimulation) return;
            InputPoller.InvokeStackTop("Movement::Update");
        }

        public void RestoreMount(IRidable mount) {
            if (ReferenceEquals(activeMount, mount)) return;
            if (self.IsOwner) {
                if (ridePattern == null) return;
                if (activeMount != null) ClearMount();
                ridePattern.TryAddHandles(mount.AsEntity);
                return;
            }
            if (activeMount != null) ClearMount();
            activeMount = mount;
            AddIgnoredEntity(mount.AsEntity.info.rtEntityId);
            self.Collider.SetGravityState(MountedGravity,
                GamePlay.Interaction.TerrainCollider.GravityPriority.Movement, false);
            self.eventCtrl.RaiseEvent(GameEvent.Action_MountRideable, self.info.rtEntityId, mount.AsEntity.info.rtEntityId);
        }

        public void ReleaseMount(IRidable mount) {
            if (ReferenceEquals(activeMount, mount)) ClearMount();
        }

        private void ClearMount() {
            if (activeMount == null) return;
            if (self.IsOwner && ridePattern != null) { ridePattern.RemoveHandles(); return; }
            Guid mountId = activeMount.AsEntity?.info.rtEntityId ?? Guid.Empty;
            RemoveIgnoredEntity(mountId);
            self.Collider.RemoveGravityState(MountedGravity);
            self.eventCtrl.RaiseEvent(GameEvent.Action_DismountRideable, self.info.rtEntityId, mountId);
            var subTransform = GetRiderSubTransform();
            if (subTransform != null) subTransform.localRotation = Quaternion.identity;
            activeMount = null;
        }

        private void OnDeath(object source, object target, object context) => self.RemoveBehavior(Id);

        /// <summary>Consumes horizontal move input for free-rotate camera movement.</summary>
        public float ConsumeHorizontalMovementInput(float sensitivity) {
            float rotation = inputDir.x * sensitivity;
            inputDir.x = 0;
            return rotation;
        }

        /// <summary>IRider entrypoint invoked when the player mounts a ridable entity.</summary>
        public void Mount(object srcId, object tgtId, object _) {
            if (tgtId is not Guid targetId) return;
            if (!EntityManager.TryGetEntity(targetId, out Entity mount)) return;
            if(!(ridePattern?.TryAddHandles(mount) ?? false)) return;
            mount.eventCtrl.EnqueueEvent(GameEvent.Action_Mounted, targetId, srcId);
        }

        /// <summary>IRider entrypoint invoked when the player dismounts a ridable entity.</summary>
        public void Dismount(object srcId, object tgtId, object _) {
            if (tgtId is not Guid targetId) return;
            if (!EntityManager.TryGetEntity(targetId, out Entity mount)) return;
            // Keep the local attachment until the server clears RiderTarget.
            mount.eventCtrl.EnqueueEvent(GameEvent.Action_Dismounted, targetId, srcId, null);
        }

        private void AddIgnoredEntity(Guid entityId) {
            if (!self.Is(out ColliderUpdateBehavior collider)) return;
            collider.IgnoredEntities ??= new HashSet<Guid>();
            collider.IgnoredEntities.Add(entityId);
        }

        private void RemoveIgnoredEntity(Guid entityId) {
            if (!self.Is(out ColliderUpdateBehavior collider)) return;
            collider.IgnoredEntities?.Remove(entityId);
        }

        private void ResetFrameInput() {
            sprinting = false;
            inputDir = float2.zero;
        }

        private void UpdateGround() {
            float2 desiredMove = (self.Forward * inputDir.y + self.Right * inputDir.x).xz;
            AddVelocity2D(desiredMove, MoveSpeed2D, MoveEvent);

            if (self.Is(out PlayerEffectsBehavior effects)) {
                effects.PlayAnimatorMove(GetSpeed2D(self.velocity));
            }

            ResetFrameInput();
        }

        private Transform GetRiderSubTransform() {
            if (self?.controller?.transform == null) return null;
            if (self.controller.transform.childCount == 0) return null;
            return self.controller.transform.GetChild(0).transform;
        }

        private float3 GetSpeed2D(float3 velocity) {
            quaternion wsToOSNoScale = Quaternion.Inverse(math.normalize(self.Facing));
            velocity = math.mul(wsToOSNoScale, velocity);
            return new float3(velocity.x, 0, velocity.z) / RunSpeed;
        }

        private float3 GetSpeed3D(float3 velocity) {
            quaternion wsToOSNoScale = Quaternion.Inverse(math.normalize(self.Facing));
            velocity = math.mul(wsToOSNoScale, velocity);
            return velocity / RunSpeed;
        }

        private void AddVelocity2D(float2 aim, float maxSpeed, GameEvent moveEvent) {
            if (!Arterra.GamePlay.PlayerHandler.active) return;
            if (math.length(self.velocity.xz) > maxSpeed) return;
            float3 delta = 0; delta.xz = (maxSpeed / AccelTime) * self.DeltaTime * aim;;
            RefTuple<float3> cxt = new RefTuple<float3>(delta);
            self.eventCtrl.RaiseEvent(moveEvent, self.info.rtEntityId, null, cxt);
            delta = cxt;
            
            self.velocity += delta;
        }

        private void AddVelocity3D(float3 aim, float maxSpeed, GameEvent moveEvent) {
            if (!Arterra.GamePlay.PlayerHandler.active) return;
            if (math.length(self.velocity) > maxSpeed) return;
            float3 delta = (maxSpeed / AccelTime) * self.DeltaTime * aim;
            RefTuple<float3> cxt = new RefTuple<float3>(delta);
            self.eventCtrl.RaiseEvent(moveEvent, self.info.rtEntityId, null, cxt);
            delta = cxt;

            self.velocity += delta;
        }


        private class GroundMovementPattern {
            private readonly PlayerMovementBehavior owner;
            private bool hasJumpBinding;

            public GroundMovementPattern(PlayerMovementBehavior owner) {
                this.owner = owner;
            }

            private string GroundMove1Name => $"GroundMove:1::{owner.self.info.rtEntityId}";
            private string GroundMove2Name => $"GroundMove:2::{owner.self.info.rtEntityId}";
            private string PlayerJumpName => $"PlayerMove:JMP::{owner.self.info.rtEntityId}";

            public void Initialize() {
                InputPoller.AddStackPoll(new ActionBind(GroundMove1Name, _ => owner.UpdateGround()), "Movement::Update");
                InputPoller.AddStackPoll(new ActionBind(GroundMove2Name, _ => owner.self.Collider.SetGravityState(
                    GroundMove2Name, GamePlay.Interaction.TerrainCollider.GravityPriority.Movement,
                    true
                )), "Movement::Gravity");

                if (hasJumpBinding) return;
                hasJumpBinding = true;
                InputPoller.AddBinding(new ActionBind("Jump", Jump), PlayerJumpName, "4.0::Movement");
            }

            public void Disable() {
                InputPoller.RemoveStackPoll(GroundMove1Name, "Movement::Update");
                InputPoller.RemoveStackPoll(GroundMove2Name, "Movement::Gravity");
                owner.self.Collider.RemoveGravityState(GroundMove2Name);
                InputPoller.RemoveBinding(PlayerJumpName, "4.0::Movement");
            }

            private void Jump(float _) {
                if (owner.self == null || !owner.self.active || !Arterra.GamePlay.PlayerHandler.active) return;
                if (owner.self.Collider == null) return;

                float3 sampleSize = new float3(owner.self.settings.collider.size.x, -owner.settings.groundStickDist, owner.self.settings.collider.size.z);
                bool onGround = GamePlay.Interaction.TerrainCollider.SampleCollision(owner.self.origin, sampleSize);
                if (!onGround) return;

                float3 jumpVelocity = owner.JumpForce * (float3)Vector3.up;
                owner.self.eventCtrl.RaiseEvent(GameEvent.Action_Jump, owner.self.info.rtEntityId, null, jumpVelocity);
                owner.self.velocity = math.max(jumpVelocity, owner.self.velocity);
            }
        }

        private class SwimMovementPattern {
            private static readonly HashSet<string> OverridableStates = new() { "GroundMove:1" };
            private readonly PlayerMovementBehavior owner;
            private bool isSwimming;

            private string SwimMove1Name => $"SwimMove:1::{owner.self.info.rtEntityId}";
            private string SwimMove2Name => $"SwimMove:2::{owner.self.info.rtEntityId}";
            private string SwimAscendName => $"PMSwimMove:ASD::{owner.self.info.rtEntityId}";
            private string SwimDescendName => $"PMSwimMove:DSD::{owner.self.info.rtEntityId}";

            public SwimMovementPattern(PlayerMovementBehavior owner) {
                this.owner = owner;
            }

            public void Initialize() {
                isSwimming = false;
                owner.self.eventCtrl.AddEventHandler(GameEvent.Entity_InLiquid, StartSwim);
                owner.self.eventCtrl.AddEventHandler(GameEvent.Entity_InGas, StopSwim);
            }

            public void Disable() {
                isSwimming = false;
                owner.self.eventCtrl.RemoveEventHandler(GameEvent.Entity_InLiquid, StartSwim);
                owner.self.eventCtrl.RemoveEventHandler(GameEvent.Entity_InGas, StopSwim);
                RemoveHandles();
            }

            private void StartSwim(object srcId, object _, object density) {
                if (isSwimming) return;
                string currentState = InputPoller.PeekTop("Movement::Update").Split("::").First();
                if (!OverridableStates.Contains(currentState)) return;

                isSwimming = true;
                AddHandles();
            }

            private void StopSwim(object srcId, object _, object density) {
                if (!isSwimming) return;
                isSwimming = false;
                RemoveHandles();
            }

            private void AddHandles() {
                InputPoller.AddStackPoll(new ActionBind(SwimMove1Name, _ => Update()), "Movement::Update");
                InputPoller.AddStackPoll(new ActionBind(SwimMove2Name, _ => owner.self.Collider.SetGravityState(
                    SwimMove2Name, GamePlay.Interaction.TerrainCollider.GravityPriority.Movement,
                    true
                )), "Movement::Gravity");
                InputPoller.AddBinding(new ActionBind("Ascend", _ => {
                    owner.AddVelocity3D(Vector3.up, owner.MoveSpeed2D, owner.MoveEvent);
                }), SwimAscendName, "4.0::Movement");
                InputPoller.AddBinding(new ActionBind("Descend", _ => {
                    owner.AddVelocity3D(Vector3.down, owner.MoveSpeed2D, owner.MoveEvent);
                }), SwimDescendName, "4.0::Movement");
                
            }

            private void RemoveHandles() {
                InputPoller.RemoveStackPoll(SwimMove1Name, "Movement::Update");
                InputPoller.RemoveStackPoll(SwimMove2Name, "Movement::Gravity");
                owner.self.Collider.RemoveGravityState(SwimMove2Name);
                InputPoller.RemoveBinding(SwimAscendName, "4.0::Movement");
                InputPoller.RemoveBinding(SwimDescendName, "4.0::Movement");
                
            }

            private void Update() {
                float3 desiredMove = owner.self.Forward * owner.inputDir.y + owner.self.Right * owner.inputDir.x;
                owner.self.velocity *= new float3(1, 1 - GamePlay.Interaction.TerrainCollider.BaseFriction, 1);
                if (owner.sprinting) owner.AddVelocity3D(desiredMove, owner.MoveSpeed2D, owner.MoveEvent);
                else owner.AddVelocity2D(desiredMove.xz, owner.MoveSpeed2D, owner.MoveEvent);

                if (owner.self.Is(out PlayerEffectsBehavior effects)) {
                    effects.PlayAnimatorMove(owner.GetSpeed2D(owner.self.velocity));
                }

                owner.ResetFrameInput();
            }
        }

        private class FlightMovementPattern  {
            private static readonly HashSet<string> OverridableStates = new() { "GroundMove:1", "SwimMove:1" };
            private readonly PlayerMovementBehavior owner;
            private bool hasEnabledFlight;
            private bool isFlying;

            private string FlightToggleName => $"PMFlightMove:TF::{owner.self.info.rtEntityId}";
            private string FlightMove1Name => $"FlightMove:1::{owner.self.info.rtEntityId}";
            private string FlightMove2Name => $"FlightMove:2::{owner.self.info.rtEntityId}";
            private string FlightAscendName => $"PMFlightMove:ASD::{owner.self.info.rtEntityId}";
            private string FlightDescendName => $"PMFlightMove:DSD::{owner.self.info.rtEntityId}";

            public FlightMovementPattern(PlayerMovementBehavior owner) {
                this.owner = owner;
            }

            public void Initialize() {
                hasEnabledFlight = false;
                isFlying = false;
                Config.CURRENT.System.AddHook("Gamemode:Flight", OnFlightRuleChanged);
                object enableFlight = Config.CURRENT.GamePlay.Gamemodes.value.Flight;
                OnFlightRuleChanged(ref enableFlight);
            }

            public void Disable() {
                Config.CURRENT.System.RemoveHook("Gamemode:Flight", OnFlightRuleChanged);
                InputPoller.RemoveBinding(FlightToggleName, "4.0::Movement");
                RemoveHandles();
            }

            private void OnFlightRuleChanged(ref object flightRule) {
                bool enableFlight = (bool)flightRule;
                if (hasEnabledFlight == enableFlight) return;
                hasEnabledFlight = enableFlight;

                if (!enableFlight) {
                    InputPoller.RemoveBinding(FlightToggleName, "4.0::Movement");
                    RemoveHandles();
                    return;
                }

                InputPoller.AddBinding(new ActionBind("Toggle Fly", _ => {
                    if (isFlying) RemoveHandles();
                    else AddHandles();
                    owner.self.velocity *= new float3(1, 0, 1);
                }), FlightToggleName, "4.0::Movement");
            }

            private void AddHandles() {
                string currentState = InputPoller.PeekTop("Movement::Update").Split("::").First();
                if (!OverridableStates.Contains(currentState)) return;
                isFlying = true;

                InputPoller.AddStackPoll(new ActionBind(FlightMove1Name, _ => Update()), "Movement::Update");
                InputPoller.AddStackPoll(new ActionBind(FlightMove2Name, _ => owner.self.Collider.SetGravityState(
                    FlightMove2Name, GamePlay.Interaction.TerrainCollider.GravityPriority.Movement,
                    false
                )), "Movement::Gravity");
                InputPoller.AddBinding(new ActionBind("Ascend", _ => {
                    owner.AddVelocity3D(Vector3.up, owner.FlightMoveSpeed, owner.MoveEvent);
                }), FlightAscendName, "4.0::Movement");
                InputPoller.AddBinding(new ActionBind("Descend", _ => {
                    owner.AddVelocity3D(Vector3.down, owner.FlightMoveSpeed, owner.MoveEvent);
                }), FlightDescendName, "4.0::Movement");
                
            }

            private void RemoveHandles() {
                isFlying = false;
                InputPoller.RemoveStackPoll(FlightMove1Name, "Movement::Update");
                InputPoller.RemoveStackPoll(FlightMove2Name, "Movement::Gravity");
                owner.self.Collider.RemoveGravityState(FlightMove2Name);
                InputPoller.RemoveBinding(FlightAscendName, "4.0::Movement");
                InputPoller.RemoveBinding(FlightDescendName, "4.0::Movement");
                
            }

            private void Update() {
                float2 desiredMove = (owner.self.Forward * owner.inputDir.y + owner.self.Right * owner.inputDir.x).xz;
                owner.AddVelocity2D(desiredMove, owner.FlightMoveSpeed, owner.MoveEvent);

                if (owner.self.Is(out PlayerEffectsBehavior effects)) {
                    effects.PlayAnimatorMove(owner.GetSpeed3D(owner.self.velocity));
                }

                owner.ResetFrameInput();
            }
        }

        private class RideMovementPattern {
            private static readonly HashSet<string> OverridableStates = new() { "GroundMove:1", "SwimMove:1", "FlightMove:1" };
            private readonly PlayerMovementBehavior owner;
            private IRidable mount { get => owner.activeMount; set => owner.activeMount = value; }

            private string RideMove1Name => $"RideMove:1::{owner.self.info.rtEntityId}";
            private string RideMove2Name => $"RideMove:2::{owner.self.info.rtEntityId}";
            private string RideFenceName => $"PMRideMove::{owner.self.info.rtEntityId}";
            private string RideMoveVerticalName => $"PMRideMove:MV::{owner.self.info.rtEntityId}";
            private string RideMoveHorizontalName => $"PMRideMove:MH::{owner.self.info.rtEntityId}";
            private string RideDismountName => $"PMRideMove:DSM::{owner.self.info.rtEntityId}";

            private bool IsRideActive => mount != null && mount.AsEntity != null && mount.AsEntity.active;

            public RideMovementPattern(PlayerMovementBehavior owner) {
                this.owner = owner;
            }

            public void Disable() => RemoveHandles();

            public bool TryAddHandles(Entity mount) {
                if (mount == null) return false;
                if (ReferenceEquals(this.mount?.AsEntity, mount)) return true;
                string currentState = InputPoller.PeekTop("Movement::Update").Split("::").First();
                if (!OverridableStates.Contains(currentState)) {
                    mount.eventCtrl.EnqueueEvent(GameEvent.Action_Dismounted,
                        mount.info.rtEntityId, owner.self.info.rtEntityId);
                    return false;
                }

                IRidable newMount;
                if (!mount.Is(out newMount)) {
                    mount.eventCtrl.EnqueueEvent(GameEvent.Action_Dismounted,
                        mount.info.rtEntityId, owner.self.info.rtEntityId);
                    return false;
                }
                else this.mount = newMount;

                owner.AddIgnoredEntity(mount.info.rtEntityId);

                owner.self.velocity = float3.zero;
                owner.self.eventCtrl.RaiseEvent(GameEvent.Action_MountRideable,
                    owner.self.info.rtEntityId,
                    this.mount?.AsEntity?.info.rtEntityId
                );    

                InputPoller.AddStackPoll(new ActionBind(RideMove2Name, _ => owner.self.Collider.SetGravityState(
                    RideMove2Name, GamePlay.Interaction.TerrainCollider.GravityPriority.Movement,
                    false
                )), "Movement::Gravity");
                InputPoller.AddStackPoll(new ActionBind(RideMove1Name, _ => Update()), "Movement::Update");           
                InputPoller.AddContextFence(RideFenceName, "4.0::Movement", ActionBind.Exclusion.ExcludeLayer);
                InputPoller.AddBinding(new ActionBind("Move Vertical", MoveForward), RideMoveVerticalName, "4.0::Movement");
                InputPoller.AddBinding(new ActionBind("Move Horizontal", Rotate), RideMoveHorizontalName, "4.0::Movement");
                InputPoller.AddBinding(new ActionBind("Dismount", _ => 
                    owner.self.eventCtrl.RaiseEvent(GameEvent.Action_Dismount,
                        owner.self.info.rtEntityId,
                        this.mount.AsEntity.info.rtEntityId)),
                        RideDismountName, "5.0::Gameplay"
                );
                return true;
            }

            public void RemoveHandles() {
                IRidable oldMount = mount;
                if (oldMount == null) return;
                InputPoller.RemoveStackPoll(RideMove1Name, "Movement::Update");
                InputPoller.RemoveStackPoll(RideMove2Name, "Movement::Gravity");
                owner.self.Collider.RemoveGravityState(RideMove2Name);
                owner.self.eventCtrl.RaiseEvent(
                    GameEvent.Action_DismountRideable,
                    owner.self.info.rtEntityId,
                    oldMount?.AsEntity?.info.rtEntityId
                );

                Transform subTransform = owner.GetRiderSubTransform();
                if (subTransform != null) {
                    subTransform.localRotation = Quaternion.identity;
                }

                if (oldMount?.AsEntity is Entity entity) {
                    owner.RemoveIgnoredEntity(entity.info.rtEntityId);
                }
                InputPoller.RemoveContextFence(RideFenceName, "4.0::Movement");
                InputPoller.RemoveBinding(RideMoveVerticalName, "4.0::Movement");
                InputPoller.RemoveBinding(RideMoveHorizontalName, "4.0::Movement");
                InputPoller.RemoveBinding(RideDismountName, "5.0::Gameplay");
                
                mount = null;
            }

            private void Update() {
                if (!IsRideActive) return;
                Transform root = mount.GetRiderRoot();
                float3 rootPos = CPUMapManager.WSToGS(root.position);
                owner.self.position = rootPos + new float3(0, owner.self.settings.collider.size.y / 2, 0);

                Transform subTransform = owner.GetRiderSubTransform();
                if (subTransform != null) {
                    subTransform.rotation = root.rotation;
                }
            }

            private void MoveForward(float strength) {
                if (!IsRideActive) return;
                if (math.abs(strength) <= 1E-05f) return;
                Transform root = mount.GetRiderRoot();
                float3 aim = root.forward * strength;
                mount.AsEntity.eventCtrl.EnqueueEvent(GameEvent.Entity_Guided,
                    mount.AsEntity.info.rtEntityId, owner.self.info.rtEntityId, new RefTuple<float3>(aim));
            }

            private void Rotate(float strength) {
                if (!IsRideActive) return;
                if (math.abs(strength) <= 1E-05f) return;
                Transform root = mount.GetRiderRoot();
                float3 aim = root.right * strength;
                mount.AsEntity.eventCtrl.EnqueueEvent(GameEvent.Entity_Guided,
                    mount.AsEntity.info.rtEntityId, owner.self.info.rtEntityId, new RefTuple<float3>(aim));
            }
        }
    }
}
