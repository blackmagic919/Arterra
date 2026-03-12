using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior{
    public class FlopOnLandSetting : IBehaviorSetting {
        public EntitySMTasks TaskName = EntitySMTasks.FlopOnGround;
        public EntitySMTasks OnEnterWater = EntitySMTasks.Idle;
        public Genetics.GeneFeature DryOutTime = new () {mean = 10, var = 0.5f, geneWeight = 0.1f}; 
        public Genetics.GeneFeature JumpStrength = new () {mean = 6, var = 0.4f, geneWeight = 0.05f};
        public float JumpStickDist = 0.05f;

        public object Clone() {
            return new FlopOnLandSetting {
                TaskName = TaskName,
                OnEnterWater = OnEnterWater,
                DryOutTime = DryOutTime,
                JumpStrength = JumpStrength,
                JumpStickDist = JumpStickDist,
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            Genetics.AddGene(entityType, ref DryOutTime);
            Genetics.AddGene(entityType, ref JumpStrength);
        }
    }
    public class FlopOnLandBehavior : IBehavior {
        private FlopOnLandSetting settings;
        private StateMachineManagerBehavior manager;
        private GeneticsBehavior genetics;
        private MapInteractBehavior mInteract;
        public float dryOutProgress;
        public void Update(BehaviorEntity.Animal self) {
            if (settings.TaskName != manager.TaskIndex) return;

            if (self.Collider.SampleCollision(self.origin, new float3(self.settings.collider.size.x,
                -settings.JumpStickDist, self.settings.collider.size.z), EntityJob.cxt.mapContext, out _)) {
                self.velocity.y += genetics.Genes.Get(settings.JumpStrength);
            }
        }

        public void OnEntityInWater(object self, object _, object density) {
            dryOutProgress = genetics.Genes.Get(settings.DryOutTime);
            if (settings.TaskName != manager.TaskIndex) return;
            manager.Transition(settings.OnEnterWater);
        }

        public void OnEntityInAir(object self, object _, object density) {
            dryOutProgress -= EntityJob.cxt.deltaTime;
            if (dryOutProgress <= 0) mInteract.ProcessSuffocation(self as Entity, (float)density);
            manager.Transition(settings.TaskName);
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.MapInteraction, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(FlopOnLandSetting), new FlopOnLandSetting());
        }


        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: FlopOnLand Behavior Requires AnimalSettings to have FlopOnLandSettings");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: FlopOnLand Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: FlopOnLand Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out mInteract))
                throw new System.Exception("Entity: FlopOnLand Behavior Requires AnimalInstance to have MapInteractBehavior");
            
            self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Entity_InLiquid, OnEntityInWater);
            self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Entity_InGas, OnEntityInAir);
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: FlopOnLand Behavior Requires AnimalSettings to have FlopOnLandSettings");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: FlopOnLand Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: FlopOnLand Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out mInteract))
                throw new System.Exception("Entity: FlopOnLand Behavior Requires AnimalInstance to have MapInteractBehavior");
            
            self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Entity_InLiquid, OnEntityInWater);
            self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Entity_InGas, OnEntityInAir);
        }
    }
}