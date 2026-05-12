using System;
using System.Collections.Generic;
using Arterra.Core.Events;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.GamePlay;
using Arterra.GamePlay.Interaction;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    /// <summary>
    /// A collection of settings that describe how the player moves.
    /// Movement settings may change during gameplay.
    /// </summary>
    [Serializable]
    public class PlayerMovementSettings : IBehaviorSetting {
        /// <summary>The maximum speed the player can walk at, in world-space units.</summary>
        public float walkSpeed = 3f;
        /// <summary>The maximum speed the player can run at, in world-space units.</summary>
        public float runSpeed = 6f;
        /// <summary>The upward impulse applied when the player jumps.</summary>
        public float jumpForce = 5f;
        /// <summary>
        /// The acceleration added while moving. Since this is added to velocity,
        /// effective friction scales with current speed.
        /// </summary>
        public float acceleration = 100f;
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
                acceleration = acceleration,
                groundStickDist = groundStickDist,
                flightSpeedMultiplier = flightSpeedMultiplier,
            };
        }
    }

    /// <summary>
    /// Handles all player locomotion patterns (ground, swim, flight, ride)
    /// and switches the active movement mode through input stack polls.
    /// </summary>
    public class PlayerMovementBehavior : IBehavior, IRider {
        /// <summary>The active movement behavior for the currently controlled player entity.</summary>
        [JsonIgnore] public static PlayerMovementBehavior Active { get; private set; }
        /// <summary>Resolved movement settings for this behavior instance.</summary>
        [JsonIgnore] public PlayerMovementSettings settings;

        private BehaviorEntity.Animal self;
        private bool hasBindings;
        private bool sprinting;
        private float2 inputDir;
        private GroundMovementPattern groundPattern;
        private SwimMovementPattern swimPattern;
        private FlightMovementPattern flightPattern;
        private RideMovementPattern ridePattern;

        private float MoveSpeed2D => sprinting ? settings.runSpeed : settings.walkSpeed;
        private float FlightMoveSpeed => settings.flightSpeedMultiplier * MoveSpeed2D;

        /// <summary>Declares required behavior dependencies.</summary>
        public void AddBehaviorDependencies(Dictionary<Behaviors, int> hierarchy) {
            hierarchy.TryAdd(Behaviors.Collider, hierarchy.Count);
        }

        /// <summary>Declares required settings dependencies.</summary>
        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> hierarchy) {
            hierarchy.TryAdd(typeof(PlayerMovementSettings), new PlayerMovementSettings());
        }

        /// <summary>Initializes movement state and input bindings for a newly spawned entity.</summary>
        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: PlayerMovementBehavior requires PlayerMovementSettings");

            this.self = self;
            Active = this;
            self.Register(this);
            self.Register<IRider>(this);
            BindCommonInput();
            InitializePatterns();
        }

        /// <summary>Initializes movement state and bindings after entity deserialization.</summary>
        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: PlayerMovementBehavior requires PlayerMovementSettings");

            this.self = self;
            Active = this;
            self.Register(this);
            self.Register<IRider>(this);
            BindCommonInput();
            InitializePatterns();
        }

        /// <summary>Disables movement patterns and clears active references.</summary>
        public void Disable(BehaviorEntity.Animal self) {
            if (ReferenceEquals(Active, this)) {
                Active = null;
            }

            ridePattern?.Disable();
            swimPattern?.Disable();
            flightPattern?.Disable();

            if (ReferenceEquals(this.self, self)) {
                this.self = null;
            }
        }

        /// <summary>Runs per-frame movement input stack processing for the controlled player.</summary>
        public void Update(BehaviorEntity.Animal self) {
            if (!IsControlledPlayer()) return;
            if (self.context == BehaviorEntity.UpdateContext.Job)
                return;
            if (self.context == BehaviorEntity.UpdateContext.Main)
                return;
            InputPoller.InvokeStackTop("Movement::Update");
        }

        /// <summary>Consumes horizontal move input for free-rotate camera movement.</summary>
        public float ConsumeHorizontalMovementInput(float sensitivity) {
            float rotation = inputDir.x * sensitivity;
            inputDir.x = 0;
            return rotation;
        }

        /// <summary>IRider entrypoint invoked when the player mounts a ridable entity.</summary>
        public void OnMounted(IRidable mount) {
            if (!IsControlledPlayer()) return;
            ridePattern?.AddHandles(mount);
            self.eventCtrl.RaiseEvent(GameEvent.Action_Mount, self, mount, null);
        }

        /// <summary>IRider entrypoint invoked when the player dismounts a ridable entity.</summary>
        public void OnDismounted(IRidable mount) {
            if (!IsControlledPlayer()) return;
            ridePattern?.RemoveHandles();
            self.eventCtrl.RaiseEvent(GameEvent.Action_Dismount, self, mount, null);
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

        private void BindCommonInput() {
            if (hasBindings) return;
            hasBindings = true;

            InputPoller.AddBinding(new ActionBind("Move Vertical", v => inputDir.y = v), "PlayerMove:MV", "4.0::Movement");
            InputPoller.AddBinding(new ActionBind("Move Horizontal", v => inputDir.x = v), "PlayerMove:MH", "4.0::Movement");
            InputPoller.AddBinding(new ActionBind("Sprint", _ => sprinting = true), "PlayerMove:SPR", "4.0::Movement");
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

        private bool IsControlledPlayer() {
            if (self == null || !self.active) return false;
            if (PlayerHandler.data == null) return false;
            return PlayerHandler.data.info.rtEntityId == self.info.rtEntityId;
        }

        private void ResetFrameInput() {
            sprinting = false;
            inputDir = float2.zero;
        }

        private void UpdateGround() {
            float2 desiredMove = (self.Forward * inputDir.y + self.Right * inputDir.x).xz;
            float2 deltaV = settings.acceleration * self.DeltaTime * desiredMove;
            AddVelocity2D(deltaV, MoveSpeed2D);

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
            return new float3(velocity.x, 0, velocity.z) / settings.runSpeed;
        }

        private float3 GetSpeed3D(float3 velocity) {
            quaternion wsToOSNoScale = Quaternion.Inverse(math.normalize(self.Facing));
            velocity = math.mul(wsToOSNoScale, velocity);
            return velocity / settings.runSpeed;
        }

        private void AddVelocity2D(float2 delta, float maxSpeed) {
            if (math.length(self.velocity.xz) > maxSpeed) return;
            self.velocity.xz += delta;
        }

        private void AddVelocity3D(float3 delta, float maxSpeed) {
            if (math.length(self.velocity) > maxSpeed) return;
            self.velocity += delta;
        }


        private class GroundMovementPattern {
            private readonly PlayerMovementBehavior owner;
            private bool hasJumpBinding;

            public GroundMovementPattern(PlayerMovementBehavior owner) {
                this.owner = owner;
            }

            public void Initialize() {
                InputPoller.AddStackPoll(new ActionBind("GroundMove::1", _ => owner.UpdateGround()), "Movement::Update");
                InputPoller.AddStackPoll(new ActionBind("GroundMove::2", _ => owner.self.Collider.useGravity = true), "Movement::Gravity");

                if (hasJumpBinding) return;
                hasJumpBinding = true;
                InputPoller.AddBinding(new ActionBind("Jump", Jump), "PlayerMove:JMP", "4.0::Movement");
            }

            private void Jump(float _) {
                if (owner.self == null || !owner.self.active) return;
                if (owner.self.Collider == null) return;

                float3 sampleSize = new float3(owner.self.settings.collider.size.x, -owner.settings.groundStickDist, owner.self.settings.collider.size.z);
                bool onGround = GamePlay.Interaction.TerrainCollider.SampleCollision(owner.self.origin, sampleSize);
                if (!onGround) return;

                float3 jumpVelocity = owner.settings.jumpForce * (float3)Vector3.up;
                owner.self.eventCtrl.RaiseEvent(GameEvent.Action_Jump, owner.self, null, jumpVelocity);
                owner.self.velocity += jumpVelocity;
            }
        }

        private class SwimMovementPattern {
            private static readonly HashSet<string> OverridableStates = new() { "GroundMove::1" };
            private readonly PlayerMovementBehavior owner;
            private bool isSwimming;

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
                RemoveHandles();
            }

            private void StartSwim(object source, object target, object density) {
                if (isSwimming) return;
                if (!OverridableStates.Contains(InputPoller.PeekTop("Movement::Update"))) return;

                isSwimming = true;
                AddHandles();
            }

            private void StopSwim(object source, object target, object density) {
                if (!isSwimming) return;
                isSwimming = false;
                RemoveHandles();
            }

            private void AddHandles() {
                InputPoller.AddStackPoll(new ActionBind("SwimMove::1", _ => Update()), "Movement::Update");
                InputPoller.AddStackPoll(new ActionBind("SwimMove::2", _ => owner.self.Collider.useGravity = true), "Movement::Gravity");
                InputPoller.AddKeyBindChange(() => {
                    InputPoller.AddBinding(new ActionBind("Ascend", _ => {
                        if (owner.self.velocity.y < owner.MoveSpeed2D) owner.self.velocity.y += owner.settings.acceleration * Time.deltaTime;
                    }), "PMSwimMove:ASD", "4.0::Movement");
                    InputPoller.AddBinding(new ActionBind("Descend", _ => {
                        if (owner.self.velocity.y > -owner.MoveSpeed2D) owner.self.velocity.y -= owner.settings.acceleration * Time.deltaTime;
                    }), "PMSwimMove:DSD", "4.0::Movement");
                });
            }

            private void RemoveHandles() {
                InputPoller.RemoveStackPoll("SwimMove::1", "Movement::Update");
                InputPoller.RemoveStackPoll("SwimMove::2", "Movement::Gravity");
                InputPoller.AddKeyBindChange(() => {
                    InputPoller.RemoveBinding("PMSwimMove:ASD", "4.0::Movement");
                    InputPoller.RemoveBinding("PMSwimMove:DSD", "4.0::Movement");
                });
            }

            private void Update() {
                float3 desiredMove = owner.self.Forward * owner.inputDir.y + owner.self.Right * owner.inputDir.x;
                float3 deltaV = owner.settings.acceleration * owner.self.DeltaTime * desiredMove;

                owner.self.velocity.y *= 1 - GamePlay.Interaction.TerrainCollider.BaseFriction;
                if (owner.sprinting) owner.AddVelocity3D(deltaV, owner.MoveSpeed2D);
                else owner.AddVelocity2D(deltaV.xz, owner.MoveSpeed2D);

                if (owner.self.Is(out PlayerEffectsBehavior effects)) {
                    effects.PlayAnimatorMove(owner.GetSpeed2D(owner.self.velocity));
                }

                owner.ResetFrameInput();
            }
        }

        private class FlightMovementPattern {
            private static readonly HashSet<string> OverridableStates = new() { "GroundMove::1", "SwimMove::1" };
            private readonly PlayerMovementBehavior owner;
            private bool hasEnabledFlight;
            private bool isFlying;

            public FlightMovementPattern(PlayerMovementBehavior owner) {
                this.owner = owner;
            }

            public void Initialize() {
                hasEnabledFlight = false;
                isFlying = false;
                Config.CURRENT.System.GameplayModifyHooks.TrySet("Gamemode:Flight", OnFlightRuleChanged);
                object enableFlight = Config.CURRENT.GamePlay.Gamemodes.value.Flight;
                OnFlightRuleChanged(ref enableFlight);
            }

            public void Disable() {
                InputPoller.RemoveBinding("PMFlightMove:TF", "4.0::Movement");
                RemoveHandles();
            }

            private void OnFlightRuleChanged(ref object flightRule) {
                bool enableFlight = (bool)flightRule;
                if (hasEnabledFlight == enableFlight) return;
                hasEnabledFlight = enableFlight;

                if (!enableFlight) {
                    InputPoller.RemoveBinding("PMFlightMove:TF", "4.0::Movement");
                    RemoveHandles();
                    return;
                }

                InputPoller.AddBinding(new ActionBind("Toggle Fly", _ => {
                    if (isFlying) RemoveHandles();
                    else AddHandles();
                    owner.self.velocity.y = 0;
                }), "PMFlightMove:TF", "4.0::Movement");
            }

            private void AddHandles() {
                if (!OverridableStates.Contains(InputPoller.PeekTop("Movement::Update"))) return;
                isFlying = true;

                InputPoller.AddStackPoll(new ActionBind("FlightMove::1", _ => Update()), "Movement::Update");
                InputPoller.AddStackPoll(new ActionBind("FlightMove::2", _ => owner.self.Collider.useGravity = false), "Movement::Gravity");
                InputPoller.AddKeyBindChange(() => {
                    InputPoller.AddBinding(new ActionBind("Ascend", _ => {
                        if (owner.self.velocity.y < owner.FlightMoveSpeed) owner.self.velocity.y += owner.settings.acceleration * Time.deltaTime * owner.settings.flightSpeedMultiplier;
                    }), "PMFlightMove:ASD", "4.0::Movement");
                    InputPoller.AddBinding(new ActionBind("Descend", _ => {
                        if (owner.self.velocity.y > -owner.FlightMoveSpeed) owner.self.velocity.y -= owner.settings.acceleration * Time.deltaTime * owner.settings.flightSpeedMultiplier;
                    }), "PMFlightMove:DSD", "4.0::Movement");
                });
            }

            private void RemoveHandles() {
                isFlying = false;
                InputPoller.RemoveStackPoll("FlightMove::1", "Movement::Update");
                InputPoller.RemoveStackPoll("FlightMove::2", "Movement::Gravity");
                InputPoller.AddKeyBindChange(() => {
                    InputPoller.RemoveBinding("PMFlightMove:ASD", "4.0::Movement");
                    InputPoller.RemoveBinding("PMFlightMove:DSD", "4.0::Movement");
                });
            }

            private void Update() {
                float2 desiredMove = (owner.self.Forward * owner.inputDir.y + owner.self.Right * owner.inputDir.x).xz;
                float2 deltaV = owner.settings.acceleration * owner.self.DeltaTime * desiredMove * owner.settings.flightSpeedMultiplier;
                owner.AddVelocity2D(deltaV, owner.FlightMoveSpeed);

                if (owner.self.Is(out PlayerEffectsBehavior effects)) {
                    effects.PlayAnimatorMove(owner.GetSpeed3D(owner.self.velocity));
                }

                owner.ResetFrameInput();
            }
        }

        private class RideMovementPattern {
            private static readonly HashSet<string> OverridableStates = new() { "GroundMove::1", "SwimMove::1", "FlightMove::1" };
            private readonly PlayerMovementBehavior owner;
            private IRidable mount;

            private bool IsRideActive => mount != null && mount.AsEntity != null && mount.AsEntity.active;

            public RideMovementPattern(PlayerMovementBehavior owner) {
                this.owner = owner;
            }

            public void Disable() {
                RemoveHandles();
            }

            public void AddHandles(IRidable mount) {
                if (mount == null) return;
                if (!OverridableStates.Contains(InputPoller.PeekTop("Movement::Update"))) {
                    mount.Dismount();
                    return;
                }

                this.mount = mount;
                if (mount.AsEntity != null) {
                    owner.AddIgnoredEntity(mount.AsEntity.info.rtEntityId);
                }

                owner.self.velocity = float3.zero;
                InputPoller.AddStackPoll(new ActionBind("RideMove::2", _ => owner.self.Collider.useGravity = false), "Movement::Gravity");
                InputPoller.AddStackPoll(new ActionBind("RideMove::1", _ => Update()), "Movement::Update");
                owner.self.eventCtrl.RaiseEvent(GameEvent.Action_MountRideable, owner.self, mount);

                InputPoller.AddKeyBindChange(() => {
                    InputPoller.AddContextFence("PMRideMove", "4.0::Movement", ActionBind.Exclusion.ExcludeLayer);
                    InputPoller.AddBinding(new ActionBind("Move Vertical", MoveForward), "PMRideMove:MV", "4.0::Movement");
                    InputPoller.AddBinding(new ActionBind("Move Horizontal", Rotate), "PMRideMove:MH", "4.0::Movement");
                    InputPoller.AddBinding(new ActionBind("Dismount", _ => this.mount?.Dismount()), "PMRideMove:DSM", "5.0::Gameplay");
                });
            }

            public void RemoveHandles() {
                IRidable oldMount = mount;
                InputPoller.RemoveStackPoll("RideMove::1", "Movement::Update");
                InputPoller.RemoveStackPoll("RideMove::2", "Movement::Gravity");
                owner.self.eventCtrl.RaiseEvent(GameEvent.Action_DismountRideable, owner.self, oldMount);

                Transform subTransform = owner.GetRiderSubTransform();
                if (subTransform != null) {
                    subTransform.localRotation = Quaternion.identity;
                }

                if (oldMount is Entity entity) {
                    owner.RemoveIgnoredEntity(entity.info.rtEntityId);
                }

                InputPoller.AddKeyBindChange(() => {
                    InputPoller.RemoveContextFence("PMRideMove", "4.0::Movement");
                    InputPoller.RemoveBinding("PMRideMove:DSM", "5.0::Gameplay");
                });
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
                mount.WalkInDirection(aim);
            }

            private void Rotate(float strength) {
                if (!IsRideActive) return;
                if (math.abs(strength) <= 1E-05f) return;
                Transform root = mount.GetRiderRoot();
                float3 aim = root.right * strength;
                mount.WalkInDirection(aim);
            }
        }
    }
}
