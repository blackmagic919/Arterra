using System;
using System.Collections.Generic;
using Arterra.Core.Events;
using Arterra.Data.Entity;
using Arterra.Data.Entity.Behavior;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
public interface IRidable {
    public Transform GetRiderRoot();
    public void WalkInDirection(float3 direction);
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
    [JsonIgnore] public Guid RiderTarget;

    public Transform GetRiderRoot() => RideRoot;
    [JsonIgnore] public Entity AsEntity => self;

    public void Dismount() {
        if (!EntityManager.TryGetEntity(RiderTarget, out Entity rider)) {
            Dismounted(self.info.entityId, RiderTarget);
            return;
        }

        rider.eventCtrl.EnqueueEvent(GameEvent.Action_Dismount,
            rider.info.entityId, self.info.entityId);
    }

    public void Dismounted(object srcId, object tgtId) {
        if (RiderTarget == Guid.Empty) return;
        if (tgtId is not Guid riderId || riderId != RiderTarget) return;
        collider?.IgnoredEntities?.Remove(RiderTarget);
        RiderTarget = Guid.Empty;
    }


    public void WalkInDirection(float3 aim) {
        RefTuple<float3> cxt = new (aim);
        self.eventCtrl.RaiseEvent(GameEvent.Entity_Guided, self.info.entityId, null, cxt);
    }

    private void Interact(object src, object interactor) {
        if (interactor is not Guid callerId) return;
        if (!EntityManager.TryGetEntity(callerId, out Entity caller)) return;
        if (RiderTarget != Guid.Empty) return;

        RefTuple<bool> allowed = new(true);
        self.eventCtrl.RaiseEvent(GameEvent.Entity_CanMount, self.info.entityId, callerId, allowed);
        if (!allowed.Value) return;

        RiderTarget = caller.info.entityId;
        if (collider != null) {
            if(collider.IgnoredEntities == null) collider.IgnoredEntities = new HashSet<Guid>() { RiderTarget };
            else collider.IgnoredEntities.Add(RiderTarget);
        }

        caller.eventCtrl.EnqueueEvent(GameEvent.Action_Mount, callerId, self.info.entityId);
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
        if (!setting.Is(out settings))
            throw new Exception("Entity: Rideable Behavior Requires AnimalSettings to have RandomWalkState");
        if ((RideRoot = self.controller.gameObject.transform.Find(settings.RideRoot)) == null)
            throw new Exception($"Entity: Rideable Behavior Requires AnimalInstance to have Object at {settings.RideRoot}");
        if (!self.Is(out collider)) collider = null;
        self.eventCtrl.AddContextlessEventHandler(GameEvent.Action_Dismounted, Dismounted);
        self.eventCtrl.AddContextlessEventHandler(GameEvent.Entity_Interact, Interact);

        self.Register<IRidable>(this);
        this.self = self;
    }

    public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
        if (!setting.Is(out settings))
            throw new Exception("Entity: Rideable Behavior Requires AnimalSettings to have RandomWalkState");
        if ((RideRoot = self.controller.gameObject.transform.Find(settings.RideRoot)) == null)
            throw new Exception($"Entity: Rideable Behavior Requires AnimalInstance to have Object at {settings.RideRoot}");
        if (!self.Is(out collider)) collider = null;
        self.eventCtrl.AddContextlessEventHandler(GameEvent.Action_Dismounted, Dismounted);
        self.eventCtrl.AddContextlessEventHandler(GameEvent.Entity_Interact, Interact);
        
        self.Register<IRidable>(this);
        this.self = self;
    }

    public override void Disable(BehaviorEntity.Animal self) {
        this.self = null;       
    }

}
}