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
        public float LandAngleMax = 0.85f;
        public float LandAngleFalloff = 0.5f;
        

        public object Clone() {
            return new LandOnGroundSettings {
                TaskName = TaskName,
                OnReachGround = OnReachGround,
                ProxyTestName = ProxyTestName,
                ProxyTestTransitions = ProxyTestTransitions,
            };
        }
    }
    public class LandOnGroundBehavior : IBehavior {
        protected LandOnGroundSettings settings;
        protected Movement movement;
        protected MMove mmove; //optional

        protected StateMachineManagerBehavior manager;
        protected PathFinderBehavior path;
        protected Modifier mod;
        protected bool foundGround;

        private float LandAngleFalloff => Modifier.Get(mod, MSettings.LandAngleFalloff, settings.LandAngleFalloff);
        private float LandAngleMax => Modifier.Get(mod, MSettings.LandAngleMax, settings.LandAngleMax);
        private float RunSpeed => MMove.Speed(mmove, settings.TaskName, mod, MSettings.RunSpeed, movement.runSpeed);

        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            
            if (path.pathFinder.hasPath) {
                self.PathCollider.Follow(Movement.StaticDirect(
                    MMove.Profile(mmove, settings.TaskName, self.settings), 
                    ref path.pathFinder, self.PathCollider,
                    MMove.MovementType(mmove, settings.TaskName)
                ), RunSpeed, movement.rotSpeed, movement.acceleration, self.DeltaTime);
                return;
            } if (foundGround) manager.Transition(settings.OnReachGround);

            float t = math.abs(manager.TaskDuration);
            float descentAngleProgress = 1 - math.exp(-t * LandAngleFalloff);
            float descentAngleMax = math.clamp(LandAngleMax, 0f, 1.0f);
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
            if (!self.Is(out manager))
                throw new System.Exception("Entity: LandOnGround Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: LandOnGround Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out mod)) mod = null;
            
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
            if (!self.Is(out manager))
                throw new System.Exception("Entity: LandOnGround Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: LandOnGround Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out mod)) mod = null;
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            manager.RegisterTransition(settings.ProxyTestName, TestProxyLand);
        }
    }

}