using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using System;

[CreateAssetMenu(menuName = "Entity/Rabbit")]
public class Rabbit : EntityAuthoring
{
    [UIgnore]
    public Option<GameObject> _Controller;
    public Option<RabbitEntity> _Entity;
    public Option<Entity.Info> _Info;
    public Option<List<uint2> > _Profile;
    public override EntityController Controller { get { return _Controller.value.GetComponent<EntityController>(); } }
    public override IEntity Entity { get => _Entity.value; set => _Entity.value = (RabbitEntity)value; }
    public override Entity.Info info { get => _Info.value; set => _Info.value = value; }
    public override uint2[] Profile { get => _Profile.value.ToArray(); set => _Profile.value = value.ToList(); }

    [BurstCompile]
    [System.Serializable]
    //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
    //**If you release here the controller might still be accessing it
    public struct RabbitEntity : IEntity
    {  
        //This is the real-time position streamed by the controller
        public int3 GCoord;
        public int pathDistance;
        public PathInfo pathFinder;
        public TerrainColliderJob tCollider;
        public Unity.Mathematics.Random random;
        public float GroundStickDist;
        public float moveSpeed;
        public float acceleration;
        public float friction;
        public float rotSpeed;
        public unsafe IntPtr Initialize(ref Entity entity, int3 GCoord)
        {
            this.GCoord = GCoord;
            pathFinder.hasPath = false;
            tCollider.Intialize();
            tCollider.transform.position = GCoord;

            entity._Update = BurstCompiler.CompileFunctionPointer<IEntity.UpdateDelegate>(Update);
            entity._Disable = BurstCompiler.CompileFunctionPointer<IEntity.DisableDelegate>(Disable);
            entity.obj = Marshal.AllocHGlobal(Marshal.SizeOf(this));

            //The seed is the entity's memory address
            this.random = new Unity.Mathematics.Random((uint)entity.obj);
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

            if(rabbit->pathFinder.hasPath) FollowPath(entity, context);
            else {
                GeneratePath(entity, context);
                if(rabbit->tCollider.IsGrounded(rabbit->GroundStickDist, *context)){
                    rabbit->tCollider.velocity *= 1 - rabbit->friction;
                    rabbit->tCollider.useGravity = false;
                } else rabbit->tCollider.useGravity = true;
            }
            rabbit->tCollider.Update(*context);
        }

        [BurstCompile]
        public static unsafe void GeneratePath(Entity* entity, EntityJob.Context* context){
            RabbitEntity* rabbit = (RabbitEntity*)entity->obj;
            int PathDist = rabbit->pathDistance;
            int3 dP = new (rabbit->random.NextInt(-PathDist, PathDist), rabbit->random.NextInt(-PathDist, PathDist), rabbit->random.NextInt(-PathDist, PathDist));
            if(EntityJob.VerifyProfile(rabbit->GCoord + dP, entity->info.profile, *context)) {
                PathInfo nPath = new ();
                nPath.path = PathFinder.FindPath(rabbit->GCoord, dP, PathDist + 1, entity->info.profile, *context, out nPath.pathLength);
                nPath.currentPos = rabbit->GCoord;
                nPath.currentInd = 0;
                nPath.hasPath = true;
                rabbit->pathFinder = nPath;
            }
        }


        [BurstCompile]
        public static unsafe void FollowPath(Entity* entity, EntityJob.Context* context){
            RabbitEntity* rabbit = (RabbitEntity*)entity->obj;

            ref PathInfo finder = ref rabbit->pathFinder;
            byte dir = finder.path[finder.currentInd];
            int3 nextPos = finder.currentPos + new int3((dir / 9) - 1, (dir / 3 % 3) - 1, (dir % 3) - 1);

            //Entity has fallen off path
            if(math.any((uint3)math.abs(finder.currentPos - rabbit->GCoord) > entity->info.profile.bounds)) finder.hasPath = false;
            //Next point is unreachable
            else if(!EntityJob.VerifyProfile(nextPos, entity->info.profile, *context)) finder.hasPath = false;
            //if it's a moving target check that the current point is closer than the destination
            //if reached destination
            else if(finder.currentInd == finder.pathLength) finder.hasPath = false;
            if(!finder.hasPath) {
                ReleasePath(rabbit);
                return;
            }

            ref TerrainColliderJob tCollider = ref rabbit->tCollider;
            if(math.all(rabbit->GCoord == nextPos)){
                finder.currentPos = nextPos;
                finder.currentInd++;
            } else {
                float3 aim = math.normalize(nextPos - rabbit->GCoord);
                tCollider.transform.rotation = Quaternion.RotateTowards(tCollider.transform.rotation, Quaternion.LookRotation(aim), rabbit->rotSpeed * context->deltaTime);
                if(math.length(rabbit->tCollider.velocity) < rabbit->moveSpeed) 
                    tCollider.velocity += rabbit->acceleration * context->deltaTime * aim;
                rabbit->tCollider.velocity *= 1 - rabbit->friction;
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

        public struct PathInfo{
            public int3 currentPos; 
            public int currentInd;
            public int pathLength;
            [NativeDisableUnsafePtrRestriction]
            public unsafe byte* path;
            public bool hasPath; //Resource isn't bound
            //0x4 -> Controller Released, 0x2 -> Job Released, 0x1 -> Resource Released
        }
    }
}


