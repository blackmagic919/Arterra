using System;
using System.Collections.Generic;
using Arterra.Core.Events;
using Arterra.Core.Network;
using Arterra.Data.Entity;
using Arterra.Data.Entity.Behavior;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
public interface IRidable {
    public Transform GetRiderRoot();
    public Guid RiderTarget { get; }
    [JsonIgnore] public Entity AsEntity => this as Entity;
}

public class RideableSettings : IBehaviorSetting {
    public string RideRoot = "Armature/root/base";

    public object Clone() {
        return new RideableSettings {
            RideRoot = this.RideRoot
        };
    }
}

public class RidableBehavior : SpeciesBehavior, IRidable {
    private RideableSettings settings;

    private BehaviorEntity.Animal self;
    private ColliderUpdateBehavior collider;
    [JsonIgnore] public Transform RideRoot;
    [JsonProperty] private NetworkVariable<Guid> riderTarget = new("RiderTarget", NetworkPermission.Server);
    [JsonIgnore] public Guid RiderTarget { get => riderTarget.Value; private set => riderTarget.Value = value; }

    public Transform GetRiderRoot() => RideRoot;
    [JsonIgnore] public Entity AsEntity => self;

    public void Dismount() {
        Guid riderId = RiderTarget;
        Dismounted(self.info.rtEntityId, riderId);
        if (EntityManager.TryGetEntity(riderId, out Entity rider))
            rider.eventCtrl.EnqueueEvent(GameEvent.Action_Dismount,
                riderId, self.info.rtEntityId);
    }

    public void Dismounted(object srcId, object tgtId) {
        if (RiderTarget == Guid.Empty) return;
        if (tgtId is not Guid riderId || riderId != RiderTarget) return;
        collider?.IgnoredEntities?.Remove(RiderTarget);
        RiderTarget = Guid.Empty;
    }


    private void Interact(object src, object interactor) {
        if (interactor is not Guid callerId) return;
        if (!EntityManager.TryGetEntity(callerId, out Entity caller)) return;
        if (RiderTarget != Guid.Empty) return;

        RefTuple<bool> allowed = new(true);
        self.eventCtrl.RaiseEvent(GameEvent.Entity_CanMount, self.info.rtEntityId, callerId, allowed);
        if (!allowed.Value) return;

        RiderTarget = caller.info.rtEntityId;
        if (collider != null) {
            if(collider.IgnoredEntities == null) collider.IgnoredEntities = new HashSet<Guid>() { RiderTarget };
            else collider.IgnoredEntities.Add(RiderTarget);
        }

        caller.eventCtrl.EnqueueEvent(GameEvent.Action_Mount, callerId, self.info.rtEntityId);
    }


     public override void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
        heirarchy.TryAdd(Behaviors.Collider, heirarchy.Count);
        heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
        //Deactivated unless IVitality is implemented
    }

    public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
        heirarchy.TryAdd(typeof(RideableSettings), new RideableSettings());
    }

    public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
        self.controller.KeepAnimatorOnDisposeVisual = true;
        if (!setting.Is(out settings))
            throw new Exception("Entity: Rideable Behavior Requires AnimalSettings to have RandomWalkState");
        if ((RideRoot = self.controller.gameObject.transform.Find(settings.RideRoot)) == null)
            throw new Exception($"Entity: Rideable Behavior Requires AnimalInstance to have Object at {settings.RideRoot}");
        if (!self.Is(out collider)) collider = null;
        if (self.IsAuthority) self.eventCtrl.AddContextlessEventHandler(GameEvent.Action_Dismounted, Dismounted);
        if (self.IsAuthority) self.eventCtrl.AddContextlessEventHandler(GameEvent.Entity_Interact, Interact);

        this.self = self;
        self.Register<IRidable>(this);
        riderTarget.Register(self);
    }

    public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
        self.controller.KeepAnimatorOnDisposeVisual = true;
        if (!setting.Is(out settings))
            throw new Exception("Entity: Rideable Behavior Requires AnimalSettings to have RandomWalkState");
        if ((RideRoot = self.controller.gameObject.transform.Find(settings.RideRoot)) == null)
            throw new Exception($"Entity: Rideable Behavior Requires AnimalInstance to have Object at {settings.RideRoot}");
        if (!self.Is(out collider)) collider = null;
        if (self.IsAuthority) self.eventCtrl.AddContextlessEventHandler(GameEvent.Action_Dismounted, Dismounted);
        if (self.IsAuthority) self.eventCtrl.AddContextlessEventHandler(GameEvent.Entity_Interact, Interact);
        
        self.Register<IRidable>(this);
        this.self = self;
        riderTarget.Register(self);
    }

    public override void Update(BehaviorEntity.Animal self) {
        if (!self.ShouldUpdateAnimations()) return;
        RestoreRider(this);
    }

    public static void RestoreRider(IRidable mount) {
        if (mount.RiderTarget == Guid.Empty) return;
        if (EntityManager.TryGetEntity(mount.RiderTarget, out var rider)
            && rider.Is(out PlayerMovementBehavior movement)) movement.RestoreMount(mount);
    }

    public static void ReleaseRider(IRidable mount) {
        if (EntityManager.TryGetEntity(mount.RiderTarget, out var rider)
            && rider.Is(out PlayerMovementBehavior movement)) movement.ReleaseMount(mount);
    }

    public override void Disable(BehaviorEntity.Animal self) {
        if (this.self == null) return;
        ReleaseRider(this);
        self.Unregister(typeof(IRidable));
        riderTarget?.Dispose();
        this.self = null;       
    }

}
}
