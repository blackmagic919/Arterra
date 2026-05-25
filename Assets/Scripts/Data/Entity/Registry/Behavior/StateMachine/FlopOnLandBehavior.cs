using System;
using System.Collections.Generic;
using Arterra.GamePlay.Interaction;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior{
    public class FlopOnLandSetting : IBehaviorSetting {
        public EntitySMTasks TaskName = EntitySMTasks.FlopOnGround;
        public EntitySMTasks OnEnterWater = EntitySMTasks.Idle;
        public float DryOutTime = 10f;
        public float FlopStrength = 6;
        public float JumpStickDist = 0.05f;

        public object Clone() {
            return new FlopOnLandSetting {
                TaskName = TaskName,
                OnEnterWater = OnEnterWater,
                DryOutTime = DryOutTime,
                FlopStrength = FlopStrength,
                JumpStickDist = JumpStickDist,
            };
        }
    }
    public class FlopOnLandBehavior : ISpeciesBehavior {
        private FlopOnLandSetting settings;
        private StateMachineManagerBehavior manager;
        private MapInteractBehavior mInteract;
        private Modifier mod;
        public float dryOutProgress;

        private float FlopStrength => Modifier.Get(mod, MSettings.FlopStrength, settings.FlopStrength);
        private float DryOutTime => Modifier.Get(mod, MSettings.DryOutTime, settings.DryOutTime);
        public void Update(BehaviorEntity.Animal self) {
            if (settings.TaskName != manager.TaskIndex) return;
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            
            if (TerrainCollider.SampleCollision(self.origin, new float3(self.settings.collider.size.x,
                -settings.JumpStickDist, self.settings.collider.size.z), EntityJob.cxt.mapContext, out _)) {
                self.velocity.y += FlopStrength;
            }
        }

        public void OnEntityInWater(object self, object _, object density) {
            dryOutProgress = DryOutTime;
            if (settings.TaskName != manager.TaskIndex) return;
            manager.Transition(settings.OnEnterWater);
        }

        public void OnEntityInAir(object self, object _, object density) {
            if (self is not BehaviorEntity.Animal animal) return;
            dryOutProgress -= animal.DeltaTime;
            if (dryOutProgress <= 0) mInteract.ProcessSuffocation(animal, (float)density);
            manager.Transition(settings.TaskName);
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.MapInteraction, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(FlopOnLandSetting), new FlopOnLandSetting());
        }


        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: FlopOnLand Behavior Requires AnimalSettings to have FlopOnLandSettings");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: FlopOnLand Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out mInteract))
                throw new System.Exception("Entity: FlopOnLand Behavior Requires AnimalInstance to have MapInteractBehavior");
            if (!self.Is(out mod)) mod = null;

            self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Entity_InLiquid, OnEntityInWater);
            self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Entity_InGas, OnEntityInAir);
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: FlopOnLand Behavior Requires AnimalSettings to have FlopOnLandSettings");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: FlopOnLand Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out mInteract))
                throw new System.Exception("Entity: FlopOnLand Behavior Requires AnimalInstance to have MapInteractBehavior");
            if (!self.Is(out mod)) mod = null;
            
            self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Entity_InLiquid, OnEntityInWater);
            self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Entity_InGas, OnEntityInAir);
        }
    }
}