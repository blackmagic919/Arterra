using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using System;
using Unity.Collections;
using Newtonsoft.Json;
using WorldConfig;
using WorldConfig.Generation.Entity;

[CreateAssetMenu(menuName = "Entity/Camel")]
public class Camel : Authoring
{
    [UISetting(Ignore = true)][JsonIgnore]
    public Option<GameObject> _Controller;
    [UISetting(Ignore = true)][JsonIgnore]
    public Option<CamelEntity> _Entity;
    public Option<CamelSetting> _Setting;
    public Option<List<ProfileE> > _Profile;
    public Option<Entity.ProfileInfo> _Info;
    
    [JsonIgnore]
    public override EntityController Controller { get { return _Controller.value.GetComponent<EntityController>(); } }
    [JsonIgnore]
    public override IEntity Entity { get => _Entity.value; set => _Entity.value = (CamelEntity)value; }
    [JsonIgnore]
    public override IEntitySetting Setting { get => _Setting.value; set => _Setting.value = (CamelSetting)value; }
    [JsonIgnore]
    public override Entity.ProfileInfo Info { get => _Info.value; set => _Info.value = value; }
    [JsonIgnore]
    public override ProfileE[] Profile { get => _Profile.value.ToArray(); set => _Profile.value = value.ToList(); }

    [Serializable]
    public struct CamelSetting : IEntitySetting{
        public Movement movement;
        public IdleBehvaior idle;
        public TerrainColliderJob.Settings collider;

        [Serializable]
        public struct Movement{
            public float GroundStickDist; //0.05
            public float moveSpeed; //4
            public float acceleration; //50
            public float friction; //0.075
            public float rotSpeed;//180
            public int pathDistance;//31
        }
        [Serializable]
        public struct IdleBehvaior{
            public float AverageIdleTime; //2.5
            public float AverageRestTime; //32
            public float RestProbability; //0.2
        }
    }

    [BurstCompile]
    //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
    //**If you release here the controller might still be accessing it
    public struct CamelEntity : IEntity
    {  
        //This is the real-time position streamed by the controller
        public int3 GCoord; 
        public uint TaskIndex;
        public float TaskDuration;
        public PathFinder.PathInfo pathFinder;
        public TerrainColliderJob tCollider;
        public Unity.Mathematics.Random random;

        public static readonly SharedStatic<NativeArray<FunctionPointer<IEntity.UpdateDelegate>>> _task = SharedStatic<NativeArray<FunctionPointer<IEntity.UpdateDelegate>>>.GetOrCreate<CamelEntity, NativeArray<FunctionPointer<IEntity.UpdateDelegate>>>();
        public static readonly SharedStatic<CamelSetting> _settings = SharedStatic<CamelSetting>.GetOrCreate<CamelEntity, CamelSetting>();
        public static NativeArray<FunctionPointer<IEntity.UpdateDelegate>> Task{get => _task.Data; set => _task.Data = value;}
        public static CamelSetting settings{get => _settings.Data; set => _settings.Data = value;}
        public unsafe readonly void Preset(IEntitySetting setting){
            settings = (CamelSetting)setting;
            if(Task != default && Task.IsCreated) return;
            var states = new NativeArray<FunctionPointer<IEntity.UpdateDelegate>>(4, Allocator.Persistent);
            states[0] = BurstCompiler.CompileFunctionPointer<IEntity.UpdateDelegate>(Idle);
            states[1] = BurstCompiler.CompileFunctionPointer<IEntity.UpdateDelegate>(Idle);
            states[2] = BurstCompiler.CompileFunctionPointer<IEntity.UpdateDelegate>(GeneratePath);
            states[3] = BurstCompiler.CompileFunctionPointer<IEntity.UpdateDelegate>(FollowPath);
            Task = states;
        }
        public unsafe readonly void Unset(){
            if(Task != default) Task.Dispose(); 
            Task = default; 
        }

        public unsafe IntPtr Initialize(ref Entity entity, int3 GCoord)
        {
            entity._Update = BurstCompiler.CompileFunctionPointer<IEntity.UpdateDelegate>(Update);
            entity._Disable = BurstCompiler.CompileFunctionPointer<IEntity.DisableDelegate>(Disable);
            entity.obj = Marshal.AllocHGlobal(Marshal.SizeOf(this));

            //The seed is the entity's memory address
            this.random = new Unity.Mathematics.Random((uint)entity.obj);
            this.GCoord = GCoord;
            pathFinder.hasPath = false;
            tCollider.transform.position = GCoord;

            //Start by Idling
            TaskDuration = settings.idle.AverageIdleTime * random.NextFloat(0f, 2f);
            TaskIndex = 0;

            Marshal.StructureToPtr(this, entity.obj, false);
            IntPtr nEntity = Marshal.AllocHGlobal(Marshal.SizeOf(entity));
            Marshal.StructureToPtr(entity, nEntity, false);
            return nEntity;
        }

        public unsafe IntPtr Deserialize(ref Entity entity, out int3 GCoord)
        {
            entity._Update = BurstCompiler.CompileFunctionPointer<IEntity.UpdateDelegate>(Update);
            entity._Disable = BurstCompiler.CompileFunctionPointer<IEntity.DisableDelegate>(Disable);
            entity.obj = Marshal.AllocHGlobal(Marshal.SizeOf(this));

            GCoord = this.GCoord;

            Marshal.StructureToPtr(this, entity.obj, false);
            IntPtr nEntity = Marshal.AllocHGlobal(Marshal.SizeOf(entity));
            Marshal.StructureToPtr(entity, nEntity, false);
            return nEntity;
        }


        [BurstCompile]
        public unsafe static void Update(Entity* entity, EntityJob.Context* context)
        {
            if(!entity->active) return;
            CamelEntity* camel = (CamelEntity*)entity->obj;
            camel->GCoord = (int3)camel->tCollider.transform.position;
            Task[(int)camel->TaskIndex].Invoke(entity, context);

            if(camel->tCollider.IsGrounded(settings.movement.GroundStickDist, settings.collider, context->mapContext))
                camel->tCollider.velocity.y *= 1 - settings.movement.friction;
            camel->tCollider.velocity.xz *= 1 - settings.movement.friction;

            camel->tCollider.Update(*context, settings.collider);
        }
        

        [BurstCompile] //Task 0
        public static unsafe void Idle(Entity* entity, EntityJob.Context* context){
            CamelEntity* camel = (CamelEntity*)entity->obj;
            if(camel->TaskDuration <= 0){
                camel->TaskIndex = 2;
            }
            else camel->TaskDuration -= context->deltaTime;
        }
        

        [BurstCompile] // Task 2
        public static unsafe void GeneratePath(Entity* entity, EntityJob.Context* context){
            CamelEntity* camel = (CamelEntity*)entity->obj;
            int PathDist = settings.movement.pathDistance;
            int3 dP = new (camel->random.NextInt(-PathDist, PathDist), camel->random.NextInt(-PathDist, PathDist), camel->random.NextInt(-PathDist, PathDist));
            if(EntityJob.VerifyProfile(camel->GCoord + dP, entity->info.profile, *context)) {
                PathFinder.PathInfo nPath = new ();
                nPath.path = PathFinder.FindPath(camel->GCoord, dP, PathDist + 1, entity->info.profile, *context, out nPath.pathLength);
                nPath.currentPos = camel->GCoord;
                nPath.currentInd = 0;
                nPath.hasPath = true;
                camel->pathFinder = nPath;
                camel->TaskIndex = 3;
            }
        }


        [BurstCompile] //Task 3
        public static unsafe void FollowPath(Entity* entity, EntityJob.Context* context){
            CamelEntity* camel = (CamelEntity*)entity->obj;

            ref PathFinder.PathInfo finder = ref camel->pathFinder;
            ref TerrainColliderJob tCollider = ref camel->tCollider;
            if(math.any(math.abs(tCollider.transform.position - finder.currentPos) > entity->info.profile.bounds)) finder.hasPath = false;
            if(finder.currentInd == finder.pathLength) finder.hasPath = false;
            if(!finder.hasPath) {
                ReleasePath(camel);

                bool IsResting = camel->random.NextFloat() >= settings.idle.RestProbability;
                camel->TaskIndex = IsResting ? 1u : 0u;
                camel->TaskDuration = IsResting ? settings.idle.AverageRestTime : settings.idle.AverageIdleTime;
                camel->TaskDuration *= camel->random.NextFloat(0f, 2f);
                return;
            }

            byte dir = finder.path[finder.currentInd];
            int3 nextPos = finder.currentPos + new int3((dir / 9) - 1, (dir / 3 % 3) - 1, (dir % 3) - 1);
            if(!EntityJob.VerifyProfile(nextPos, entity->info.profile, *context)) finder.hasPath = false;

            if(math.all(math.abs(camel->GCoord - nextPos) <= 1)){
                finder.currentPos = nextPos;
                finder.currentInd++;
            } else {
                float3 aim = math.normalize(nextPos - camel->GCoord);
                Quaternion rot = tCollider.transform.rotation;
                if(math.any(aim.xz != 0))rot = Quaternion.LookRotation(new Vector3(aim.x, 0, aim.z));
                tCollider.transform.rotation = Quaternion.RotateTowards(tCollider.transform.rotation, rot, settings.movement.rotSpeed * context->deltaTime);
                if(math.length(camel->tCollider.velocity) < settings.movement.moveSpeed) 
                    tCollider.velocity += settings.movement.acceleration * context->deltaTime * aim;
            }
        }

        [BurstCompile]
        private static unsafe void ReleasePath(CamelEntity* camel){
            if(camel->pathFinder.hasPath) 
                UnsafeUtility.Free(camel->pathFinder.path, Unity.Collections.Allocator.Persistent);
            camel->pathFinder.hasPath = false;
        }

        [BurstCompile]
        public unsafe static void Disable(Entity* entity){
            entity->active = false;
        }

    }
}


