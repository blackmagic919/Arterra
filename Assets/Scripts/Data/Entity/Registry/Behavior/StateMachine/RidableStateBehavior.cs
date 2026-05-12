using System;
using System.Collections.Generic;
using Arterra.Core.Events;
using Arterra.Data.Entity;
using Arterra.Data.Entity.Behavior;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
public class RideableStateSettings : IBehaviorSetting {
    public EntitySMTasks TaskName = EntitySMTasks.FollowRider;
    public EntitySMTasks OnDismount = EntitySMTasks.Idle;
    public float AllowRideAffinity = 5f;

    public object Clone() {
        return new RideableStateSettings {
            TaskName = this.TaskName,
            OnDismount = this.OnDismount, 
            AllowRideAffinity = this.AllowRideAffinity,
        };
    }
}

public class RidableStateBehavior : IBehavior {
    private RideableStateSettings settings;
    private Movement movement;
    private MMove mmove; //optional

    private BehaviorEntity.Animal self;
    private RidableBehavior ridable;
    private StateMachineManagerBehavior manager;
    private RelationsBehavior relations;
    private Modifier mod;

    private float AllowRideAffinity => Modifier.Get(mod, MSettings.AllowRideAffinity, settings.AllowRideAffinity);
    private float RunSpeed => MMove.Speed(mmove, settings.TaskName, mod, MSettings.RunSpeed, movement.runSpeed);

    public void OnDismounted(object src, object caller) {
        if (manager.TaskIndex == EntitySMTasks.FollowRider)
            manager.Transition(settings.OnDismount);
    }

    public void WalkInDirection(object src, object caller, object cxt) {
        float3 aim = (cxt as RefTuple<float3>).Value;
        aim = new(aim.x, 0, aim.z);
        if (Vector3.Magnitude(aim) <= 1E-05f) return;
        if (math.length(self.velocity) > RunSpeed)
            return;

        self.velocity += movement.acceleration * self.DeltaTime * aim;
    }

    public void Update(BehaviorEntity.Animal self) {
        if (manager.TaskIndex != settings.TaskName) return;
        if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
        
        if (ridable.RiderTarget == Guid.Empty) {
            manager.Transition(settings.OnDismount);
            return;
        }

        if (math.length(self.velocity.xz) < 1E-05f) return;
        float3 aim;
        if (MMove.MovementType(mmove, settings.TaskName) != Movement.FollowType.Planar)
            aim = math.normalize(self.velocity);
        else aim = math.normalize(new float3(self.velocity.x, 0, self.velocity.z));
        
        self.Rotation = Quaternion.RotateTowards(self.Rotation, Quaternion.LookRotation(aim), movement.rotSpeed * self.DeltaTime);
    }

    private bool TransitionTo() => ridable.RiderTarget != Guid.Empty;
    private void Mounted(object src, object interactor, object reject) {
        RefTuple<bool> valid = reject as RefTuple<bool>;
        if (relations != null
            && interactor != null
            && interactor is Entity rider
            && relations.GetAffection(rider.info.rtEntityId)
            < AllowRideAffinity
        ) {
            valid.Value = false;
            return;   
        }
        
        if (manager.TaskIndex < settings.TaskName) 
            manager.Transition(settings.TaskName);
    }


    public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
        heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
        //Deactivated unless IAttackable is implemented
    }

    public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
        heirarchy.TryAdd(typeof(RideableStateSettings), new RideableStateSettings());
        heirarchy.TryAdd(typeof(Movement), new Movement());
    }

    public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
        if (!setting.Is(out settings))
            throw new System.Exception("Entity: RideableState Behavior Requires AnimalSettings to have RideableStateSettings");
        if (!setting.Is(out movement))
            throw new System.Exception("Entity: RideableState Behavior Requires AnimalSettings to have Movement");
        if (!setting.Is(out mmove)) mmove = null;
        if (!self.Is(out manager))
            throw new System.Exception("Entity: RideableState Behavior Requires AnimalInstance to have StateMachineManager");
        if (!self.Is(out ridable))
            throw new System.Exception("Entity: RideableState Behavior Requires AnimalInstance to have RideableBehavior");
        if (!self.Is(out relations)) relations = null;
        if (!self.Is(out mod)) mod = null;
        
        manager.RegisterTransition(settings.TaskName, TransitionTo);
        self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Entity_Guided, WalkInDirection);
        self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Action_Mounted, Mounted);
        this.self = self;
    }

    public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
        if (!setting.Is(out settings))
            throw new System.Exception("Entity: RideableState Behavior Requires AnimalSettings to have RideableStateSettings");
        if (!setting.Is(out movement))
            throw new System.Exception("Entity: RideableState Behavior Requires AnimalSettings to have Movement");
        if (!setting.Is(out mmove)) mmove = null;
        if (!self.Is(out manager))
            throw new System.Exception("Entity: RideableState Behavior Requires AnimalInstance to have StateMachineManager");
        if (!self.Is(out ridable))
            throw new System.Exception("Entity: RideableState Behavior Requires AnimalInstance to have RideableBehavior");
        if (!self.Is(out relations)) relations = null;
        if (!self.Is(out mod)) mod = null;
        
        manager.RegisterTransition(settings.TaskName, TransitionTo);
        self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Entity_Guided, WalkInDirection);
        self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Action_Mounted, Mounted);
        this.self = self;
    }

    public void Disable(BehaviorEntity.Animal self) {
        this.self = null;
    }

}
}