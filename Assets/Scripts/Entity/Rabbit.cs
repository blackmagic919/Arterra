using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using System.IO;
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
        public Unity.Mathematics.Random random;
        public unsafe IntPtr Initialize(ref Entity entity, int3 GCoord)
        {
            this.GCoord = GCoord;
            pathFinder.hasPath = 0x0;

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
            
            //Current path is valid
            if((rabbit->pathFinder.hasPath & 0x2) != 0) AffirmPathValidity(entity, context);
            //Path resource is unbounded
            else if(rabbit->pathFinder.hasPath == 0) GeneratePath(entity, context);
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
                nPath.hasPath = 0x3;
                rabbit->pathFinder = nPath;
            }
        }

        [BurstCompile]
        public static unsafe void AffirmPathValidity(Entity* entity, EntityJob.Context* context){
            RabbitEntity* rabbit = (RabbitEntity*)entity->obj;

            PathInfo* finder = &rabbit->pathFinder;
            byte dir = finder->path[finder->currentInd];
            int3 nextPos = finder->currentPos + new int3((dir / 9) - 1, (dir / 3 % 3) - 1, (dir % 3) - 1);

            //Controller has released path
            if((finder->hasPath & 0x4) == 0) finder->hasPath &= 0x5;
            //Entity has fallen off path
            else if(math.any((uint3)math.abs(finder->currentPos - rabbit->GCoord) > entity->info.profile.bounds)) finder->hasPath &= 0x5;
            //Next point is unreachable
            else if(!EntityJob.VerifyProfile(nextPos, entity->info.profile, *context)) finder->hasPath &= 0x5;
            //if it's a moving target check that the current point is closer than the destination
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
            public byte hasPath; //Resource isn't bound
            //0x4 -> Controller Released, 0x2 -> Job Released, 0x1 -> Resource Released
        }
    }
}


