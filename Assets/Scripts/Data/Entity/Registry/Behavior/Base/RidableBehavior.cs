using System;
using System.Collections.Generic;
using Arterra.Core.Events;
using Arterra.Data.Entity;
using Arterra.Data.Entity.Behavior;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {

public interface IRider {
    public void OnMounted(IRidable entity);
    public void OnDismounted(IRidable entity);
}

public interface IRidable {
    public Transform GetRiderRoot();
    public void WalkInDirection(float3 direction);
    public void Dismount();
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

public class RidableBehavior : IBehavior, IRidable {
    private RideableSettings settings;

    private BehaviorEntity.Animal self;
    private ColliderUpdateBehavior collider;
    [JsonIgnore] public Transform RideRoot;
    [JsonIgnore] public Guid RiderTarget;

    public Transform GetRiderRoot() => RideRoot;
    [JsonIgnore] public Entity AsEntity => self;

    public void Dismount() {
        if (RiderTarget == Guid.Empty) return;
        if (!EntityManager.TryGetEntity(RiderTarget, out Entity target) ||
            !target.Is(out IRider rider))
            return;
        
        self.eventCtrl.RaiseEvent(GameEvent.Action_Dismounted, self, rider);
        EntityManager.AddHandlerEvent(() => rider.OnDismounted(this));
        collider?.IgnoredEntities.Remove(RiderTarget);
        RiderTarget = Guid.Empty;
    }


    public void WalkInDirection(float3 aim) {
        RefTuple<float3> cxt = new (aim);
        self.eventCtrl.RaiseEvent(GameEvent.Entity_Guided, self, null, cxt);
    }

    private void Interact(object src, object interactor) {
        if (interactor == null) return;
        if (interactor is not Entity caller) return;
        if (!caller.Is(out IRider rider)) return;
        if (RiderTarget != Guid.Empty) return;

        RefTuple<bool> cxt = new (true);
        self.eventCtrl.RaiseEvent(GameEvent.Action_Mounted, self, caller, cxt);
        if (!cxt.Value) return; //Event test whether the given entity should be ridden

        RiderTarget = caller.info.rtEntityId;
        if (collider != null) {
            if(collider.IgnoredEntities == null) collider.IgnoredEntities = new HashSet<Guid>() { RiderTarget };
            else collider.IgnoredEntities.Add(RiderTarget);
        }

        EntityManager.AddHandlerEvent(() => rider.OnMounted(self.As<IRidable>()));
    }


     public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
        heirarchy.TryAdd(Behaviors.Collider, heirarchy.Count);
        heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
        heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
        //Deactivated unless IAttackable is implemented
    }

    public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
        heirarchy.TryAdd(typeof(RideableSettings), new RideableSettings());
    }

    public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
        if (!setting.Is(out settings))
            throw new System.Exception("Entity: Rideable Behavior Requires AnimalSettings to have RandomWalkState");
        if ((RideRoot = self.controller.gameObject.transform.Find(settings.RideRoot)) == null)
            throw new System.Exception($"Entity: Rideable Behavior Requires AnimalInstance to have Object at {settings.RideRoot}");
        if (!self.Is(out collider)) collider = null;

        self.Register<IRidable>(this);
        self.eventCtrl.AddContextlessEventHandler(GameEvent.Entity_Interact, Interact);
        this.self = self;
    }

    public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
        if (!setting.Is(out settings))
            throw new System.Exception("Entity: Rideable Behavior Requires AnimalSettings to have RandomWalkState");
        if ((RideRoot = self.controller.gameObject.transform.Find(settings.RideRoot)) == null)
            throw new System.Exception($"Entity: Rideable Behavior Requires AnimalInstance to have Object at {settings.RideRoot}");
        if (!self.Is(out collider)) collider = null;
        
        self.Register<IRidable>(this);
        self.eventCtrl.AddContextlessEventHandler(GameEvent.Entity_Interact, Interact);
        this.self = self;
    }

    public void Disable(BehaviorEntity.Animal self) {
        this.self = null;       
    }

}
}