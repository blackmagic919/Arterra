using System;
using System.Collections.Generic;
using System.Linq;
using Arterra.Configuration;
using Arterra.Core.Events;
using Arterra.GamePlay.Interaction;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior{

    public class BurrowInGroundSetting : IBehaviorSetting {
        public EntitySMTasks Task1Name = EntitySMTasks.Burrow;
        public EntitySMTasks Task2Name = EntitySMTasks.Hide;
        public EntitySMTasks Task3Name = EntitySMTasks.Unburrow;
        public EntitySMTasks OnHitOutState = EntitySMTasks.RunFromTarget;
        public EntitySMTasks OnReachSurface = EntitySMTasks.Idle;
        public TagRegistry.Tags DiggableMats;

        public Genetics.GeneFeature MinAttackerDist = new () {mean = 12, var = 0.25f, geneWeight = 0.1f};
        public Genetics.GeneFeature MaxAttackerDist = new () {mean = 28, var = 0.25f, geneWeight = 0.1f};
        public Genetics.GeneFeature SurfaceThresh = new () {mean = 0.3f, var = 0.25f, geneWeight = 0.1f};
        public Genetics.GeneFeature DigDist = new () {mean = 3, var = 0.25f, geneWeight = 0.1f};
        public Option<List<EntitySMTasks> > UnburrowTransitions;
        public Option<List<EntitySMTasks>> EscapingTargetStates = new () {value = new () {
            EntitySMTasks.RunFromTarget, EntitySMTasks.RunFromPredator
        }};
        [JsonIgnore][UISetting(Ignore = true)][HideInInspector]
        public HashSet<EntitySMTasks> _EscapeTargetStates;
        public float GroundStickDist = 0.05f;

        [HideInInspector][UISetting(Ignore = true)][JsonIgnore]
        public Profiles paths;
        
        public struct Profiles {
            public List<PathFinder.MatProfileE> HiddenProfile;
            public uint3 HiddenBounds;
            public List<PathFinder.MatProfileE> BurrowProfile;
            public uint3 BurrowBounds;
            public List<PathFinder.MatProfileE> SurfProfile;
            public uint3 SurfBounds;  
            public bool hasPaths;
        }

        public object Clone() {
            return new BurrowInGroundSetting {
                Task1Name = Task1Name,
                Task2Name = Task2Name,
                Task3Name = Task3Name,
                OnHitOutState = OnHitOutState,
                OnReachSurface = OnReachSurface,
                DiggableMats = DiggableMats,
                
                MinAttackerDist = MinAttackerDist,
                MaxAttackerDist = MaxAttackerDist,
                SurfaceThresh = SurfaceThresh,
                DigDist = DigDist,
                UnburrowTransitions = UnburrowTransitions,
                GroundStickDist = GroundStickDist,
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting settings) {
            Genetics.AddGene(entityType, ref MinAttackerDist);
            Genetics.AddGene(entityType, ref MaxAttackerDist);
            Genetics.AddGene(entityType, ref SurfaceThresh);
            Genetics.AddGene(entityType, ref DigDist);
            _EscapeTargetStates = new HashSet<EntitySMTasks>(EscapingTargetStates.value);
            this.paths.hasPaths = false;
        }

        public void TrySetUpPaths(MMove mmove, EntitySetting settings) {
            if (paths.hasPaths) return;
            (paths.BurrowProfile, paths.BurrowBounds)= PathFinder.MatProfileE.ToMatProfile(MMove.Profile(mmove, Task1Name, settings), EntityJob.cxt, DiggableMats);
            (paths.HiddenProfile, paths.HiddenBounds)= PathFinder.MatProfileE.ToMatProfile(MMove.Profile(mmove, Task2Name, settings), EntityJob.cxt, DiggableMats);
            (paths.SurfProfile, paths.SurfBounds)= PathFinder.MatProfileE.ToMatProfile(MMove.Profile(mmove, OnReachSurface, settings), EntityJob.cxt);
            paths.hasPaths = true;
        }
        
    }
    public class BurrowInGroundBehavior : IBehavior {
        private BurrowInGroundSetting settings;
        private Movement movement;
        private MMove mmove;

        private BehaviorEntity.Animal self;
        private ColliderUpdateBehavior collider;
        private MapInteractBehavior mInteract;
        private PathFinderBehavior path;
        private StateMachineManagerBehavior manager;
        private GeneticsBehavior genetics;
        private bool foundSurface;
        private bool IsAttached;

        public void Update(BehaviorEntity.Animal self) {
            bool IsBurrowing = true;
            if (manager.TaskIndex == settings.Task1Name) 
                BurrowUnderground(self);
            else if (manager.TaskIndex == settings.Task2Name)
                HideUnderground(self);
            else if (manager.TaskIndex == settings.Task3Name)
                Unburrow(self);
            else {
                IsBurrowing = false;
                if (settings._EscapeTargetStates.Contains(manager.TaskIndex))
                    manager.Transition(settings.Task1Name);
            }
            ModifyHooks(IsBurrowing);
        }

        private void BurrowUnderground(BehaviorEntity.Animal self) {
            if (path.pathFinder.hasPath) {
                float speed = MMove.Speed(mmove, settings.Task1Name, genetics.Genes, movement.walkSpeed);
                Movement.FollowStaticPath(settings.paths.BurrowProfile, settings.paths.BurrowBounds, ref path.pathFinder, ref self.collider,
                    speed, movement.rotSpeed, movement.acceleration, MMove.Allow3DRot(mmove, settings.Task1Name));
                return;
            }

            if (!PathFinder.VerifyMatProfile(self.GCoord, settings.paths.HiddenBounds, settings.paths.HiddenProfile, false)) {
                if(!manager.Transition(settings.Task1Name))
                    manager.Transition(settings.OnHitOutState);
                return;
            }
                

            manager.Transition(settings.Task2Name);
        }

        private void HideUnderground(BehaviorEntity.Animal self) {
            if (!EntityManager.TryGetEntity(manager.TaskTarget, out Entity attacker))
                manager.Transition(settings.Task3Name);
            if (Recognition.GetColliderDist(attacker, self) > genetics.Genes.Get(settings.MaxAttackerDist))
                manager.Transition(settings.Task3Name);
            if (mInteract != null && mInteract.breathPercent < genetics.Genes.Get(settings.SurfaceThresh))
                manager.Transition(settings.Task3Name);

            byte contact = TerrainInteractor.SampleContact(self.position, self.transform.size, out _, null);
            if (!TerrainInteractor.TouchSolid(contact)) manager.Transition(settings.OnHitOutState);
        
            
            if (settings.UnburrowTransitions.value != null)
            foreach(var trans in settings.UnburrowTransitions.value) {
                if (manager.Transition(trans)) manager.Transition(settings.Task3Name);
            }
        } 

        private void Unburrow(BehaviorEntity.Animal self) {
            if (!path.pathFinder.hasPath) {
                if (foundSurface) manager.Transition(settings.OnReachSurface);
                else FindPathOut();
            }

            float speed = MMove.Speed(mmove, settings.Task3Name, genetics.Genes, movement.walkSpeed);
            Movement.FollowStaticPath(settings.paths.BurrowProfile, settings.paths.BurrowBounds, ref path.pathFinder, ref self.collider,
                speed, movement.rotSpeed, movement.acceleration, MMove.Allow3DRot(mmove, settings.Task3Name));
        }

        private void FindPathOut() {
             byte[] nPath = PathFinder.FindMatchAlongRay(self.GCoord, (float3)Vector3.up, movement.pathDistance,
                settings.paths.BurrowBounds, settings.paths.BurrowProfile,
                settings.paths.SurfBounds, settings.paths.SurfProfile,
                out int pLen, out foundSurface);
            path.pathFinder = new PathFinder.PathInfo(self.GCoord, nPath, pLen);
        }

        public bool TransitionToTask1() {
            if (!EntityManager.TryGetEntity(manager.TaskTarget, out Entity attacker)) {
                return false;}
            float dist = Recognition.GetColliderDist(attacker, self);
            if (dist < genetics.Genes.Get(settings.MinAttackerDist) || dist > genetics.Genes.Get(settings.MaxAttackerDist)) {
                return false;}
            if (!self.collider.SampleCollision(self.origin, new float3(self.settings.collider.size.x,
                -settings.GroundStickDist, self.settings.collider.size.z), EntityJob.cxt.mapContext, out _)){
                return false;}
            
            float3 digDir = Vector3.down;
            EntitySetting.ProfileInfo profile = MMove.Profile(mmove, settings.Task1Name, self.settings);
            float3 origin = self.origin + new float3(0, -(int)profile.bounds.y, 0);
            int pathLength = (int)genetics.Genes.Get(settings.DigDist);
            byte[] nPath = PathFinder.FindPathAlongRay((int3)origin, ref digDir, pathLength, 
                settings.paths.BurrowBounds, settings.paths.BurrowProfile, out int pLen);
            byte[] toStart = PathFinder.GetStraightLinePath(self.GCoord, (int3)origin);
            if (pLen < pathLength) return false;
            path.pathFinder = new PathFinder.PathInfo(self.GCoord, toStart.Concat(nPath).ToArray(), pLen);
            return true;
        }
 
        public bool TransitionToTask3() {
            FindPathOut();
            return true;
        }

        private void OnHitBurrowing(object src, object atkr, object info) {
            byte contact = TerrainInteractor.SampleContact(self.position, self.transform.size, out _, null);
            RefTuple<(float dmg, float3 kb)> data = (RefTuple<(float, float3)>)info;
            //Stop kb if fully underground
            if (!TerrainInteractor.TouchGas(contact)) {
                data.Value.kb = float3.zero;
                return;
            }

            manager.Transition(settings.OnHitOutState);
        }

        private void OnInSolid(object src, object _, object cxt) {
            mInteract.breath = math.max(mInteract.breath - EntityJob.cxt.deltaTime, 0);
            if (mInteract.breath > 0) return;
            mInteract.ProcessSuffocation(self, (float)cxt);
        }

        private void ModifyHooks(bool isBurrowing) {
            if (isBurrowing == IsAttached) return;
            IsAttached = isBurrowing;

            if (IsAttached) {
                self.eventCtrl.AddEventHandler(GameEvent.Entity_InSolid, OnInSolid);
                self.eventCtrl.AddEventHandler(GameEvent.Entity_Damaged, OnHitBurrowing);
                mInteract?.SetInteractionType(MapInteractorSettings.InteractType.SubTerraneal);
                collider?.SetInteractionType(ColliderUpdateSettings.InteractType.NoGround);
            } else {
                self.eventCtrl.RemoveEventHandler(GameEvent.Entity_InSolid, OnInSolid);
                self.eventCtrl.RemoveEventHandler(GameEvent.Entity_Damaged, OnHitBurrowing);
                mInteract?.ResetInteractionType();
                collider?.ResetInteractionType();
            }
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(BurrowInGroundSetting), new BurrowInGroundSetting());
            heirarchy.TryAdd(typeof(Movement), new Movement());
        }
        
        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out collider)) collider = null;
            if (!self.Is(out mInteract)) mInteract = null;

            settings.TrySetUpPaths(mmove, self.settings);
            manager.RegisterTransition(settings.Task1Name, TransitionToTask1);
            manager.RegisterTransition(settings.Task3Name, TransitionToTask3);
            this.IsAttached = false;
            this.foundSurface = false;
            this.self = self;
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out collider)) collider = null;
            if (!self.Is(out mInteract)) mInteract = null;

            settings.TrySetUpPaths(mmove, self.settings);
            manager.RegisterTransition(settings.Task1Name, TransitionToTask1);
            manager.RegisterTransition(settings.Task3Name, TransitionToTask3);
            this.IsAttached = false;
            this.foundSurface = false;
            this.self = self;
        }

        public void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}