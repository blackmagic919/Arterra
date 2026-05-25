using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior{
    public class SwimToSurfaceSettings : IBehaviorSetting {
        public EntitySMTasks TaskName = EntitySMTasks.ApproachSurface;
        public EntitySMTasks OnReachSurface = EntitySMTasks.Idle;
        public MMove.SubProfiles SurfaceProfile;
        [JsonIgnore][UISetting(Ignore = true)][HideInInspector]
        public EntitySetting.ProfileInfo _Profile;

        //Proxy Test is not a real state and cannot be transitioned to, instead, it is used to 
        //Inject tests of other proxy states which require us to pass through this state first
        //E.g. (I need to land to mate, so I perform a test on behalf of MatingBehavior, to 
        //know if I want to mate, and if so, I will attempt to land first)
        public EntitySMTasks ProxyTestName = EntitySMTasks.TestApproach2;
        public float SurfaceThreshold = 0.2f;
        public Option<List<EntitySMTasks>> ProxyTestTransitions = new () {
            value = new () {
                EntitySMTasks.ChasePreyPlant,
                EntitySMTasks.ChaseMate,
            }
        };
        
        //measured in angle phi from positive vertical axis; in terms of pi (e.g. 0.75 => phi = 0.75 * phi)
        public float SurfaceAngleMax = 0.85f;
        public float SurfaceAngleFalloff = 0.5f;
        

        public object Clone() {
            return new SwimToSurfaceSettings {
                TaskName = TaskName,
                OnReachSurface = OnReachSurface,
                SurfaceProfile = SurfaceProfile,
                ProxyTestName = ProxyTestName,
                SurfaceThreshold = SurfaceThreshold,
                ProxyTestTransitions = ProxyTestTransitions,
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            _Profile = new EntitySetting.ProfileInfo(){
                bounds = SurfaceProfile.bounds,
                profileStart = SurfaceProfile.offset + setting.profile.profileStart
            };
        }
    }
    public class SwimToSurfaceBehavior : ISpeciesBehavior {
        private SwimToSurfaceSettings settings;
        private Movement movement;
        private MMove mmove; //optional

        private StateMachineManagerBehavior manager;
        private MapInteractBehavior mInteract; 
        private PathFinderBehavior path;
        private bool foundSurface;
        private Modifier mod;

        private float HoldBreathTime => Modifier.Get(mod, MSettings.HoldBreathTime, mInteract.settings.HoldBreathTime);
        private float SurfaceThresh => Modifier.Get(mod, MSettings.SurfaceThresh, settings.SurfaceThreshold);
        private float SurfaceAngleFalloff => Modifier.Get(mod, MSettings.SurfaceAngleFalloff, settings.SurfaceAngleFalloff);
        private float SurfaceAngleMax => Modifier.Get(mod, MSettings.SurfaceAngleMax, settings.SurfaceAngleMax);
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
            } if (foundSurface) manager.Transition(settings.OnReachSurface);

            float t = math.abs(manager.TaskDuration);
            float descentAngleProgress = 1 - math.exp(-t *SurfaceAngleFalloff);
            float descentAngleMax = math.clamp(SurfaceAngleMax, 0f, 1.0f);
            float descentAngle = 180 * math.lerp(0.5f, descentAngleMax, descentAngleProgress); 

            Vector3 e = self.transform.rotation.eulerAngles;
            e.x = 0f; e.z = 0f;
            Quaternion rotation = Quaternion.Euler(e);

            float3 flightDir = rotation *
                 Quaternion.AngleAxis(descentAngle, Vector3.right) *
                 Vector3.up;
            float3 flightTarget = movement.pathDistance * flightDir;
            
            byte[] nPath = PathFinder.FindMatchAlongRay(self.PathCoord, flightTarget, movement.pathDistance + 1,
                MMove.Profile(mmove, settings.TaskName, self.settings), settings._Profile,
                EntityJob.cxt, out int pLen, out foundSurface);
            path.pathFinder = new PathFinder.PathInfo(self.PathCoord, nPath, pLen);
        }

        private bool IsSurfacing() {
            if (mInteract.breath > 0) return false; //In air
            if (SurfaceThresh == 0) return false; //Doesn't drown
            if (mInteract.breath > SurfaceThresh * HoldBreathTime)
                return false; //Still holding breath
            return true;
        }



        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.MapInteraction, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(SwimToSurfaceSettings), new SwimToSurfaceSettings());
            heirarchy.TryAdd(typeof(Movement), new Movement());
        }

        private bool TransitionTo() {
            if (!IsSurfacing()) return false;
            foundSurface = false;
            return true;
        }//

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
                throw new System.Exception("Entity: SwimToSurface Behavior Requires AnimalSettings to have SwimToSurfaceSettings");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: SwimToSurface Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out manager))
                throw new System.Exception("Entity: SwimToSurface Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: SwimToSurface Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out mInteract))
                throw new System.Exception("Entity: SwimToSurface Behavior Requires AnimalSettings to have MapInteractBehavior");
            if (!self.Is(out mod)) mod = null;
            
            foundSurface = false;
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            manager.RegisterTransition(settings.ProxyTestName, TestProxyLand);
        }

        public virtual void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: SwimToSurface Behavior Requires AnimalSettings to have SwimToSurfaceSettings");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: SwimToSurface Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out manager))
                throw new System.Exception("Entity: SwimToSurface Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: SwimToSurface Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out mInteract))
                throw new System.Exception("Entity: SwimToSurface Behavior Requires AnimalSettings to have MapInteractBehavior");
            if (!self.Is(out mod)) mod = null;
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            manager.RegisterTransition(settings.ProxyTestName, TestProxyLand);
        }
    }

}