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

        public float BurrowMinDist = 12;
        public float BurrowMaxDist = 28;
        public float UnburrowThresh = 0.3f;
        public float DigDist = 3f;
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
            public List<PathFinderBehavior.MatProfileE> HiddenProfile;
            public uint3 HiddenBounds;
            public List<PathFinderBehavior.MatProfileE> BurrowProfile;
            public uint3 BurrowBounds;
            public List<PathFinderBehavior.MatProfileE> SurfProfile;
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
                
                BurrowMinDist = BurrowMinDist,
                BurrowMaxDist = BurrowMaxDist,
                UnburrowThresh = UnburrowThresh,
                DigDist = DigDist,
                UnburrowTransitions = UnburrowTransitions,
                GroundStickDist = GroundStickDist,
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting settings) {
            _EscapeTargetStates = new HashSet<EntitySMTasks>(EscapingTargetStates.value);
            this.paths.hasPaths = false;
        }

        public void TrySetUpPaths(MMove mmove, EntitySetting settings) {
            if (paths.hasPaths) return;
            (paths.BurrowProfile, paths.BurrowBounds)= PathFinderBehavior.MatProfileE.ToMatProfile(MMove.Profile(mmove, Task1Name, settings), EntityJob.cxt, DiggableMats);
            (paths.HiddenProfile, paths.HiddenBounds)= PathFinderBehavior.MatProfileE.ToMatProfile(MMove.Profile(mmove, Task2Name, settings), EntityJob.cxt, DiggableMats);
            (paths.SurfProfile, paths.SurfBounds)= PathFinderBehavior.MatProfileE.ToMatProfile(MMove.Profile(mmove, OnReachSurface, settings), EntityJob.cxt);
            paths.hasPaths = true;
        }
        
    }
    public class BurrowInGroundBehavior : ISpeciesBehavior {
        private BurrowInGroundSetting settings;
        private Movement movement;
        private MMove mmove;

        private BehaviorEntity.Animal self;
        private ColliderUpdateBehavior collider;
        private MapInteractBehavior mInteract;
        private PathFinderBehavior path;
        private StateMachineManagerBehavior manager;
        private Modifier mod;
        private bool foundSurface;
        private bool IsAttached;

        private float RunSpeed(EntitySMTasks taskName) => MMove.Speed(mmove, taskName, mod, MSettings.RunSpeed, movement.runSpeed);
        private float WalkSpeed(EntitySMTasks taskName) => MMove.Speed(mmove, taskName, mod, MSettings.WalkSpeed, movement.walkSpeed);
        private float BurrowMaxDist => Modifier.Get(mod, MSettings.BurrowMaxDist, settings.BurrowMaxDist);
        private float BurrowMinDist => Modifier.Get(mod, MSettings.BurrowMinDist, settings.BurrowMinDist);
        private float UnburrowThresh => Modifier.Get(mod, MSettings.UnburrowThresh, settings.UnburrowThresh);
        private float DigDist => Modifier.Get(mod, MSettings.DigDist, settings.DigDist);
        public void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
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
                float speed = WalkSpeed(settings.Task1Name);
                self.PathCollider.Follow(Movement.StaticDirect(
                    settings.paths.BurrowProfile, settings.paths.BurrowBounds, ref path.pathFinder, self.PathCollider,
                    MMove.MovementType(mmove, settings.Task1Name)
                ), speed, movement.rotSpeed, movement.acceleration, self.DeltaTime);
                return;
            }

            if (!PathFinderBehavior.VerifyMatProfile(self.PathCoord, settings.paths.HiddenBounds, settings.paths.HiddenProfile, false)) {
                if(!manager.Transition(settings.Task1Name))
                    manager.Transition(settings.OnHitOutState);
                return;
            }
                

            manager.Transition(settings.Task2Name);
        }

        private void HideUnderground(BehaviorEntity.Animal self) {
            if (!EntityManager.TryGetEntity(manager.TaskTarget, out Entity attacker))
                manager.Transition(settings.Task3Name);
            if (ColliderUpdateBehavior.GetColliderDist(attacker, self) > BurrowMaxDist)
                manager.Transition(settings.Task3Name);
            if (mInteract != null && mInteract.breathPercent < UnburrowThresh)
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

            float speed = WalkSpeed(settings.Task3Name);
            self.PathCollider.Follow(Movement.StaticDirect(
                settings.paths.BurrowProfile, settings.paths.BurrowBounds, ref path.pathFinder, self.PathCollider,
                MMove.MovementType(mmove, settings.Task3Name)
            ), speed, movement.rotSpeed, movement.acceleration, self.DeltaTime);
        }

        private void FindPathOut() {
             if(!path.FindMatchAlongRay(settings.Task3Name, self.PathCoord, (float3)Vector3.up, movement.pathDistance,
                settings.paths.BurrowBounds, settings.paths.BurrowProfile,
                settings.paths.SurfBounds, settings.paths.SurfProfile,
                out foundSurface, out byte[] nPath)) return;
            path.SetPath(nPath);
        }

        public bool TransitionToTask1() {
            if (!EntityManager.TryGetEntity(manager.TaskTarget, out Entity attacker)) {
                return false;}
            float dist = ColliderUpdateBehavior.GetColliderDist(attacker, self);
            if (dist < BurrowMinDist || dist > BurrowMaxDist) {
                return false;}
            if (!GamePlay.Interaction.TerrainCollider.SampleCollision(self.origin, new float3(self.settings.collider.size.x,
                -settings.GroundStickDist, self.settings.collider.size.z), EntityJob.cxt.mapContext, out _)){
                return false;}
            
            float3 digDir = Vector3.down;
            EntitySetting.ProfileInfo profile = MMove.Profile(mmove, settings.Task1Name, self.settings);
            float3 origin = self.origin + new float3(0, -(int)profile.bounds.y, 0);
            int pathLength = (int)DigDist;

            if(!path.FindPathAlongRay(settings.Task1Name, (int3)origin, ref digDir, pathLength, 
                settings.paths.BurrowBounds, settings.paths.BurrowProfile, out byte[] nPath)) 
                return false;
            byte[] toStart = PathFinderBehavior.GetStraightLinePath(self.PathCoord, (int3)origin);
            if (nPath.Length < pathLength) return false;
            path.SetPath(toStart.Concat(nPath).ToArray());
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
            mInteract.breath = math.max(mInteract.breath - self.DeltaTime, 0);
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
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out collider)) collider = null;
            if (!self.Is(out mInteract)) mInteract = null;
            if (!self.Is(out mod)) mod = null;

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
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out collider)) collider = null;
            if (!self.Is(out mInteract)) mInteract = null;
            if (!self.Is(out mod)) mod = null;

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