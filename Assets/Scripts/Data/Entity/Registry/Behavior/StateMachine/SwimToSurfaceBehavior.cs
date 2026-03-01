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
        public Genetics.GeneFeature SurfaceThreshold = new () {mean = 0.2f, var = 0.5f, geneWeight = 0.1f};
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

            Genetics.AddGene(entityType, ref ApproachAngleMax);
            Genetics.AddGene(entityType, ref ApproachAngleFalloff);
            Genetics.AddGene(entityType, ref SurfaceThreshold);
        }
    }
    public class SwimToSurfaceBehavior : IBehavior {
        private SwimToSurfaceSettings settings;
        private Movement movement;
        private MMove mmove; //optional

        private StateMachineManagerBehavior manager;
        private GeneticsBehavior genetics;
        private VitalityBehavior vitality; 
        private PathFinderBehavior path;
        private bool foundSurface;
        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;

            if (path.pathFinder.hasPath) {
                Movement.FollowStaticPath(MMove.Profile(mmove, settings.TaskName, self.settings),
                ref path.pathFinder, ref self.collider,
                MMove.Speed(mmove, settings.TaskName, genetics.Genes, movement.runSpeed),
                movement.rotSpeed, movement.acceleration, MMove.Allow3DRot(mmove, settings.TaskName));
                return;
            } if (foundSurface) manager.Transition(settings.OnReachSurface);

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
            
            byte[] nPath = PathFinder.FindMatchAlongRay(self.GCoord, flightTarget, movement.pathDistance + 1,
                MMove.Profile(mmove, settings.TaskName, self.settings), settings._Profile,
                EntityJob.cxt, out int pLen, out foundSurface);
            path.pathFinder = new PathFinder.PathInfo(self.GCoord, nPath, pLen);
        }

        private bool IsSurfacing() {
            if (vitality.breath > 0) return false; //In air
            if (genetics.Genes.Get(settings.SurfaceThreshold) == 0) return false; //Doesn't drown
            if (vitality.breath > genetics.Genes.Get(settings.SurfaceThreshold)
                * genetics.Genes.Get(vitality.stats.HoldBreathTime))
                return false; //Still holding breath
            return true;
        }



        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(SwimToSurfaceSettings), new SwimToSurfaceSettings());
            heirarchy.TryAdd(typeof(Movement), new Movement());
        }

        private bool TransitionTo() {
            if (!IsSurfacing()) return false;
            foundSurface = false;
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
                throw new System.Exception("Entity: SwimToSurface Behavior Requires AnimalSettings to have SwimToSurfaceSettings");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: SwimToSurface Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: SwimToSurface Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: SwimToSurface Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: SwimToSurface Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: SwimToSurface Behavior Requires AnimalSettings to have VitalityBehavior");
            
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
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: SwimToSurface Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: SwimToSurface Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: SwimToSurface Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: SwimToSurface Behavior Requires AnimalSettings to have VitalityBehavior");
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            manager.RegisterTransition(settings.ProxyTestName, TestProxyLand);
        }
    }

}