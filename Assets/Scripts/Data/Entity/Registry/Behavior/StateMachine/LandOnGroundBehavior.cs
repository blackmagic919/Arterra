using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior{
    public class LandOnGroundSettings : IBehaviorSetting {
        public EntitySMTasks TaskName = EntitySMTasks.ApproachSurface;
        public EntitySMTasks OnReachGround = EntitySMTasks.Idle;

        //Proxy Test is not a real state and cannot be transitioned to, instead, it is used to 
        //Inject tests of other proxy states which require us to pass through this state first
        //E.g. (I need to land to mate, so I perform a test on behalf of MatingBehavior, to 
        //know if I want to mate, and if so, I will attempt to land first)
        public EntitySMTasks ProxyTestName = EntitySMTasks.TestApproach1;
        public Option<List<EntitySMTasks>> ProxyTestTransitions = new () {
            value = new () {
                EntitySMTasks.ChasePreyPlant,
                EntitySMTasks.ChaseMate,
            }
        };
        
        //measured in angle phi from positive vertical axis; in terms of pi (e.g. 0.75 => phi = 0.75 * phi)
        public Genetics.GeneFeature ApproachAngleMax = new Genetics.GeneFeature{mean = 0.85f, var = 0.1f, geneWeight = 0.075f};
        public Genetics.GeneFeature ApproachAngleFalloff = new Genetics.GeneFeature{mean = 0.5f, var = 0.25f, geneWeight = 0.075f};
        

        public object Clone() {
            return new LandOnGroundSettings {
                TaskName = TaskName,
                OnReachGround = OnReachGround,
                ProxyTestName = ProxyTestName,
                ProxyTestTransitions = ProxyTestTransitions,
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            Genetics.AddGene(entityType, ref ApproachAngleMax);
            Genetics.AddGene(entityType, ref ApproachAngleFalloff);
        }
    }
    public class LandOnGroundBehavior : IBehavior {
        protected LandOnGroundSettings settings;
        protected Movement movement;
        protected MMove mmove; //optional

        protected StateMachineManagerBehavior manager;
        protected GeneticsBehavior genetics;
        protected PathFinderBehavior path;
        protected bool foundGround;
        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;

            if (path.pathFinder.hasPath) {
                Movement.FollowStaticPath(MMove.Profile(mmove, settings.TaskName, self.settings),
                ref path.pathFinder, self.PathCollider,
                MMove.Speed(mmove, settings.TaskName, genetics.Genes, movement.runSpeed),
                movement.rotSpeed, movement.acceleration, MMove.MovementType(mmove, settings.TaskName));
                return;
            } if (foundGround) manager.Transition(settings.OnReachGround);

            float t = math.abs(manager.TaskDuration);
            float descentAngleProgress = 1 - math.exp(-t *genetics.Genes.Get(settings.ApproachAngleFalloff));
            float descentAngleMax = math.clamp(genetics.Genes.Get(settings.ApproachAngleMax), 0f, 1.0f);
            float descentAngle = 180 * math.lerp(0.5f, descentAngleMax, descentAngleProgress); 

            Vector3 e = self.transform.rotation.eulerAngles;
            e.x = 0f; e.z = 0f;
            Quaternion rotation = Quaternion.Euler(e);

            float3 flightDir = rotation *
                 Quaternion.AngleAxis(descentAngle, Vector3.right) *
                 Vector3.up;
            float3 flightTarget = movement.pathDistance * flightDir;
            
            byte[] nPath = PathFinder.FindMatchAlongRay(self.PathCoord, flightTarget, movement.pathDistance + 1,
                MMove.Profile(mmove, settings.TaskName, self.settings),
                MMove.Profile(mmove, settings.OnReachGround, self.settings),
                EntityJob.cxt, out int pLen, out foundGround);
            path.pathFinder = new PathFinder.PathInfo(self.PathCoord, nPath, pLen);
        }


        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(LandOnGroundSettings), new LandOnGroundSettings());
            heirarchy.TryAdd(typeof(Movement), new Movement());
        }

        private bool TransitionTo() {
            foundGround = false;
            return true;
        }

        private bool TestProxyLand() {
            if (settings.ProxyTestTransitions.value == null) return false;
            foreach(EntitySMTasks state in settings.ProxyTestTransitions.value) {
                //if we are able to transition 'i.e. the on-land proxy wants to do something', then we try to land
                if (!manager.Transition(state)) continue;
                manager.Transition(settings.TaskName);
                return false;
            } return false;
        }


        public virtual void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: LandOnGround Behavior Requires AnimalSettings to have LandOnGroundState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: LandOnGround Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: LandOnGround Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: LandOnGround Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: LandOnGround Behavior Requires AnimalInstance to have PathFinderBehavior");
            
            foundGround = false;
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            manager.RegisterTransition(settings.ProxyTestName, TestProxyLand);
        }

        public virtual void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: LandOnGround Behavior Requires AnimalSettings to have LandOnGroundState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: LandOnGround Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: LandOnGround Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: LandOnGround Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: LandOnGround Behavior Requires AnimalInstance to have PathFinderBehavior");
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            manager.RegisterTransition(settings.ProxyTestName, TestProxyLand);
        }
    }

}