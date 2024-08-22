using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using System;
using System.Threading.Tasks;

[CreateAssetMenu(menuName = "Entity/Rabbit")]
public class Rabbit : EntityAuthoring
{
    [UIgnore]
    public Option<GameObject> _Controller;
    [UIgnore]
    public Option<RabbitEntity> _Entity;
    public Option<RabbitSetting> _Setting;
    public Option<List<uint2> > _Profile;
     public Option<Entity.Info.ProfileInfo> _Info;
    public override EntityController Controller { get { return _Controller.value.GetComponent<EntityController>(); } }
    public override IEntity Entity { get => _Entity.value; set => _Entity.value = (RabbitEntity)value; }
    public override IEntitySetting Setting { get => _Setting.value; set => _Setting.value = (RabbitSetting)value; }
    public override Entity.Info.ProfileInfo Info { get => _Info.value; set => _Info.value = value; }
    public override uint2[] Profile { get => _Profile.value.ToArray(); set => _Profile.value = value.ToList(); }

    [Serializable]
    public struct RabbitSetting : IEntitySetting{
        public Movement movement;
        public TerrainColliderJob.Settings collider;

        [Serializable]
        public struct Movement{
            public float GroundStickDist; //0.05
            public float moveSpeed; //4
            public float acceleration; //50
            public float friction; //0.075
            public float rotSpeed;//180
            public int pathDistance;//31
            public float AverageIdleTime; //2.5
        }
    }

    [BurstCompile]
    [System.Serializable]
    //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
    //**If you release here the controller might still be accessing it
    public struct RabbitEntity : IEntity
    {  
        //This is the real-time position streamed by the controller
        public int3 GCoord; 
        public uint TaskIndex;
        public float TaskDuration;
        private FunctionPointer<IEntity.UpdateDelegate> Task;
        public PathFinder.PathInfo pathFinder;
        public TerrainColliderJob tCollider;
        public Unity.Mathematics.Random random;
        public static readonly SharedStatic<RabbitSetting> _settings = SharedStatic<RabbitSetting>.GetOrCreate<RabbitEntity, RabbitSetting>();
        public static RabbitSetting settings{get => _settings.Data; set => _settings.Data = value;}
        public readonly void Preset(IEntitySetting setting) => settings = (RabbitSetting)setting;
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
            TaskDuration = settings.movement.AverageIdleTime * random.NextFloat(0f, 2f);
            Task = BurstCompiler.CompileFunctionPointer<IEntity.UpdateDelegate>(Idle);

            Marshal.StructureToPtr(this, entity.obj, false);
            IntPtr nEntity = Marshal.AllocHGlobal(Marshal.SizeOf(entity));
            Marshal.StructureToPtr(entity, nEntity, false);
            return nEntity;
        }
        [BurstCompile]
        public unsafe static void Update(Entity* entity, EntityJob.Context* context)
        {
            if(!entity->active) return;
            RabbitEntity* rabbit = (RabbitEntity*)entity->obj;
            rabbit->GCoord = (int3)rabbit->tCollider.transform.position;
            rabbit->Task.Invoke(entity, context);

            if(rabbit->tCollider.IsGrounded(settings.movement.GroundStickDist, settings.collider, *context))
                rabbit->tCollider.velocity.y *= 1 - settings.movement.friction;
            rabbit->tCollider.velocity.xz *= 1 - settings.movement.friction;

            rabbit->tCollider.Update(*context, settings.collider);
        }

        [BurstCompile] //Task 0
        public static unsafe void Idle(Entity* entity, EntityJob.Context* context){
            RabbitEntity* rabbit = (RabbitEntity*)entity->obj;
            if(rabbit->TaskDuration <= 0){
                rabbit->TaskIndex = 1;
                rabbit->Task = BurstCompiler.CompileFunctionPointer<IEntity.UpdateDelegate>(GeneratePath);
            }
            else rabbit->TaskDuration -= context->deltaTime;
        }

        [BurstCompile] // Task 1
        public static unsafe void GeneratePath(Entity* entity, EntityJob.Context* context){
            RabbitEntity* rabbit = (RabbitEntity*)entity->obj;
            int PathDist = settings.movement.pathDistance;
            int3 dP = new (rabbit->random.NextInt(-PathDist, PathDist), rabbit->random.NextInt(-PathDist, PathDist), rabbit->random.NextInt(-PathDist, PathDist));
            if(EntityJob.VerifyProfile(rabbit->GCoord + dP, entity->info.profile, *context)) {
                PathFinder.PathInfo nPath = new ();
                nPath.path = PathFinder.FindPath(rabbit->GCoord, dP, PathDist + 1, entity->info.profile, *context, out nPath.pathLength);
                nPath.currentPos = rabbit->GCoord;
                nPath.currentInd = 0;
                nPath.hasPath = true;
                rabbit->pathFinder = nPath;
                rabbit->TaskIndex = 2;
                rabbit->Task = BurstCompiler.CompileFunctionPointer<IEntity.UpdateDelegate>(FollowPath);
            }
        }


        [BurstCompile] //Task 2
        public static unsafe void FollowPath(Entity* entity, EntityJob.Context* context){
            RabbitEntity* rabbit = (RabbitEntity*)entity->obj;

            ref PathFinder.PathInfo finder = ref rabbit->pathFinder;
            byte dir = finder.path[finder.currentInd];
            int3 nextPos = finder.currentPos + new int3((dir / 9) - 1, (dir / 3 % 3) - 1, (dir % 3) - 1);
            ref TerrainColliderJob tCollider = ref rabbit->tCollider;

            //Entity has fallen off path
            if(math.any((uint3)math.abs(tCollider.transform.position - finder.currentPos) > entity->info.profile.bounds)) finder.hasPath = false;
            //Next point is unreachable
            else if(!EntityJob.VerifyProfile(nextPos, entity->info.profile, *context)) finder.hasPath = false;
            //if it's a moving target check that the current point is closer than the destination
            //if reached destination
            else if(finder.currentInd == finder.pathLength) finder.hasPath = false;
            if(!finder.hasPath) {
                ReleasePath(rabbit);

                rabbit->TaskIndex = 0;
                rabbit->TaskDuration = settings.movement.AverageIdleTime * rabbit->random.NextFloat(0f, 2f);
                rabbit->Task = BurstCompiler.CompileFunctionPointer<IEntity.UpdateDelegate>(Idle);
                return;
            }

            if(math.all(rabbit->GCoord == nextPos)){
                finder.currentPos = nextPos;
                finder.currentInd++;
            } else {
                float3 aim = math.normalize(nextPos - rabbit->GCoord);
                tCollider.transform.rotation = Quaternion.RotateTowards(tCollider.transform.rotation, 
                                                Quaternion.LookRotation(aim), settings.movement.rotSpeed * context->deltaTime);
                if(math.length(rabbit->tCollider.velocity) < settings.movement.moveSpeed) 
                    tCollider.velocity += settings.movement.acceleration * context->deltaTime * aim;
            }
        }

        [BurstCompile]
        private static unsafe void ReleasePath(RabbitEntity* rabbit){
            if(rabbit->pathFinder.hasPath) 
                UnsafeUtility.Free(rabbit->pathFinder.path, Unity.Collections.Allocator.Persistent);
            rabbit->pathFinder.hasPath = false;
        }

        [BurstCompile]
        public unsafe static void Disable(Entity* entity){
            entity->active = false;
        }

        [Serializable]
        public struct Settings{
            public Movement movement;
            public TerrainColliderJob.Settings collider;
            
            public void Initialize(){
                movement = new Movement{
                    GroundStickDist = 0.05f,
                    moveSpeed = 4,
                    acceleration = 50,
                    friction = 0.075f,
                    rotSpeed = 180,
                    pathDistance = 31,
                    AverageIdleTime = 2.5f,
                };

                collider = new TerrainColliderJob.Settings{
                    size = new float3(0.8f),
                    offset = new float3(-0.4f),
                    useGravity = true
                };
            }

            public struct Movement{
                public float GroundStickDist;
                public float moveSpeed;
                public float acceleration;
                public float friction;
                public float rotSpeed;
                public int pathDistance;
                public float AverageIdleTime;
            }
        }

    }
}


