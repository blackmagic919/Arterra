using Unity.Mathematics;
using Arterra.Core.Events;
using UnityEngine;
using System.Collections.Generic;
using System;

namespace Arterra.Data.Entity.Behavior {

public class DefendFriendSetting : IBehaviorSetting {
    public float CallFriendRadius;
    public float HelpFriendAffection;
    public EntitySMTasks HelpFriendState = EntitySMTasks.ChaseTarget;
    public EntitySMTasks OverridableStates = EntitySMTasks.ChasePreyPlant;

    public object Clone() {
        return new DefendFriendSetting {
            CallFriendRadius = CallFriendRadius,
            HelpFriendState = HelpFriendState,
            OverridableStates = OverridableStates,
            HelpFriendAffection = HelpFriendAffection
        };
    }

}

public class DefendFriendBehavior : SpeciesBehavior {
    private DefendFriendSetting settings;

    private StateMachineManagerBehavior manager;
    private Modifier mod;
    private RelationsBehavior relations;
    private VitalityBehavior vit;
    public void OnAttacked(object src, object attacker, object cxt) {
        if (attacker == null) return;
        if (attacker is not Entity assailant) return;
        if (src is not Entity self) return;
        if (vit != null && vit.IsDead) return;
        
        if (relations.GetAffection(assailant.info.rtEntityId) > relations.settings.SuppressInstinctAffection) 
            return;
        
        float radius = Modifier.Get(mod, MSettings.CallFriendRadius, settings.CallFriendRadius);
        Bounds bounds = new (self.position, new float3(radius*2));
        EntityManager.ESTree.Query(bounds, nEntity => {
            if (!nEntity.Is(out DefendFriendBehavior defend))
                return;
            defend.HeedFriendInTrouble(self, assailant);
        });
    }

    private void HeedFriendInTrouble(Entity friend, Entity attacker) {
        if (relations.GetAffection(friend.info.rtEntityId)
            < Modifier.Get(mod, MSettings.HelpFriendAffection, settings.HelpFriendAffection))
            return;
        if (manager.TaskIndex > settings.OverridableStates) return;
        if (manager.TaskIndex == settings.HelpFriendState) return;
        if(manager.Transition(settings.HelpFriendState))
            manager.TaskTarget = attacker.info.rtEntityId;
    }

    public override void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
        heirarchy.TryAdd(Behaviors.Relations, heirarchy.Count);
        heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
    }

    public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
        heirarchy.TryAdd(typeof(DefendFriendSetting), new DefendFriendSetting());
    }

    public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
        if (!setting.Is(out settings))
            throw new System.Exception("Entity: DefendFriend Behavior Requires AnimalSettings to have DefendFriendSettings");
        if (!self.Is(out relations))
            throw new System.Exception("Entity: DefendFriend Behavior Requires AnimalInstance to have RelationsBehavior");
        if (!self.Is(out manager))
            throw new System.Exception("Entity: DefendFriend Behavior Requires AnimalInstance to have StateMachineManager");
        if (!self.Is(out vit)) vit = null;
        if (!self.Is(out mod)) mod = null;

        self.eventCtrl.AddEventHandler(GameEvent.Entity_Damaged, OnAttacked);
    }
    
    public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
        if (!setting.Is(out settings))
            throw new System.Exception("Entity: DefendFriend Behavior Requires AnimalSettings to have DefendFriendSettings");
        if (!self.Is(out relations))
            throw new System.Exception("Entity: DefendFriend Behavior Requires AnimalInstance to have RelationsBehavior");
        if (!self.Is(out manager))
            throw new System.Exception("Entity: DefendFriend Behavior Requires AnimalInstance to have StateMachineManager");
        if (!self.Is(out vit)) vit = null;
        if (!self.Is(out mod)) mod = null;

        self.eventCtrl.AddEventHandler(GameEvent.Entity_Damaged, OnAttacked);
    }
}
}