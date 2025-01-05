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

[CreateAssetMenu(menuName = "Entity/Bird")]
public class Bird : EntityAuthoring
{
    [UISetting(Ignore = true)][JsonIgnore]
    public Option<GameObject> _Controller;
    [UISetting(Ignore = true)][JsonIgnore]
    public Option<BirdEntity> _Entity;
    public Option<BirdSetting> _Setting;
    public Option<List<ProfileE> > _Profile;
    public Option<Entity.Info.ProfileInfo> _Info;
    
    [JsonIgnore]
    public override EntityController Controller { get { return _Controller.value.GetComponent<EntityController>(); } }
    [JsonIgnore]
    public override IEntity Entity { get => _Entity.value; set => _Entity.value = (BirdEntity)value; }
    [JsonIgnore]
    public override IEntitySetting Setting { get => _Setting.value; set => _Setting.value = (BirdSetting)value; }
    [JsonIgnore]
    public override Entity.Info.ProfileInfo Info { get => _Info.value; set => _Info.value = value; }
    [JsonIgnore]
    public override ProfileE[] Profile { get => _Profile.value.ToArray(); set => _Profile.value = value.ToList(); }

    [Serializable]
    public struct BirdSetting : IEntitySetting{
        public Movement movement;
        public Flight flight;
        public TerrainColliderJob.Settings collider;
        [Serializable]
        public struct Movement{
            public float moveSpeed; //15
            public float acceleration; //50
            public float friction; //0.075
            public float rotSpeed;//180
        }
        [Serializable]
        public struct Flight{
            public float AverageIdleTime; //2.5
            public float AverageFlightTime; //120
            public int flightDistance; //4
            public int InfluenceDist; //24
            public float SeperationWeight; //0.75
            public float AlignmentWeight; //0.5
            public float CohesionWeight; //0.25
            public float FlyBiasWeight; //0.25

        }
    }
    [BurstCompile]
    //NOTE: Do not Release Resources Here, Mark as Released and let Controller handle it
    //**If you release here the controller might still be accessing it
    public struct BirdEntity : IEntity
    {  
        //This is the real-time position streamed by the controller
        public int3 GCoord;
        public PathFinder.PathInfo pathFinder;
        public TerrainColliderJob tCollider;
        public Unity.Mathematics.Random random;
        public float3 flightDirection;
        public float TaskDuration;
        public uint TaskIndex;
        
        public static readonly SharedStatic<NativeArray<FunctionPointer<IEntity.UpdateDelegate>>> _task = SharedStatic<NativeArray<FunctionPointer<IEntity.UpdateDelegate>>>.GetOrCreate<BirdEntity, NativeArray<FunctionPointer<IEntity.UpdateDelegate>>>();
        public static readonly SharedStatic<BirdSetting> _settings = SharedStatic<BirdSetting>.GetOrCreate<BirdEntity, BirdSetting>();
        public static BirdSetting settings{get => _settings.Data; set => _settings.Data = value;}
        public static NativeArray<FunctionPointer<IEntity.UpdateDelegate>> Task{get => _task.Data; set => _task.Data = value;}
        public readonly unsafe void Preset(IEntitySetting setting){
            settings = (BirdSetting)setting;
            if(Task != default && Task.IsCreated) return;
            var states = new NativeArray<FunctionPointer<IEntity.UpdateDelegate>>(3, Allocator.Persistent);
            states[0] = BurstCompiler.CompileFunctionPointer<IEntity.UpdateDelegate>(Idle);
            states[1] = BurstCompiler.CompileFunctionPointer<IEntity.UpdateDelegate>(FollowPath);
            states[2] = BurstCompiler.CompileFunctionPointer<IEntity.UpdateDelegate>(FollowPath);
            Task = states;
        }
        public readonly unsafe void Unset(){ 
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
            flightDirection = RandomDirection();
            TaskDuration = settings.flight.AverageIdleTime * random.NextFloat(0f, 2f);
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
            BirdEntity* bird = (BirdEntity*)entity->obj;
            bird->GCoord = (int3)math.floor(bird->tCollider.transform.position);
            Task[(int)bird->TaskIndex].Invoke(entity, context);

            bird->tCollider.velocity *= 1 - settings.movement.friction;
            bird->tCollider.Update(*context, settings.collider);
        }

        [BurstCompile] //Task 0
        public static unsafe void Idle(Entity* entity, EntityJob.Context* context){
            BirdEntity* bird = (BirdEntity*)entity->obj;
            if(bird->TaskDuration <= 0) {
                bird->TaskDuration = settings.flight.AverageFlightTime * bird->random.NextFloat(0f, 2f);
                bird->flightDirection = bird->RandomDirection();
                bird->TaskIndex = 1;
                BoidFly(entity, context);
            }
            else bird->TaskDuration -= context->deltaTime;

            //Rotate towards neutral
            ref Quaternion rotation = ref bird->tCollider.transform.rotation;
            float3 lookRotation = new (rotation.eulerAngles.x, 0, rotation.eulerAngles.z);
            if(math.length(lookRotation) != 0) rotation = Quaternion.RotateTowards(rotation, Quaternion.LookRotation(lookRotation), settings.movement.rotSpeed * context->deltaTime);
        }


        [BurstCompile]
        public static unsafe void BoidFly(Entity* entity, EntityJob.Context* context){
            BirdEntity* bird = (BirdEntity*)entity->obj;

            if(bird->TaskDuration <= 0) {
                FindGround(entity, context);
                return;
            }

            CalculateBoidDirection(entity, context);
            PathFinder.PathInfo nPath = new ();
            nPath.path = PathFinder.FindPathAlongRay(bird->GCoord, ref bird->flightDirection, settings.flight.flightDistance + 1, entity->info.profile, *context, out nPath.pathLength);
            nPath.currentPos = bird->GCoord;
            nPath.currentInd = 0;
            nPath.hasPath = true;
            bird->pathFinder = nPath;
        }

        [BurstCompile] 
        public static unsafe void FindGround(Entity* entity, EntityJob.Context* context){
            BirdEntity* bird = (BirdEntity*)entity->obj;
            bird->flightDirection = Normalize(bird->flightDirection + math.down()); 
            int3 dP = (int3)(bird->flightDirection * settings.flight.flightDistance);

            PathFinder.PathInfo nPath = new ();
            nPath.path = PathFinder.FindClosestAlongPath(bird->GCoord, dP, settings.flight.flightDistance + 1, entity->info.profile, *context, out nPath.pathLength, out bool fGround);
            nPath.currentPos = bird->GCoord;
            nPath.currentInd = 0;
            nPath.hasPath = true;
            bird->pathFinder = nPath;
            if(fGround) bird->TaskIndex = 2;
        }

        public static unsafe void CalculateBoidDirection(Entity* entity, EntityJob.Context* context){
            BirdEntity* bird = (BirdEntity*)entity->obj;
            BoidDMtrx boidDMtrx = new(){
                boid = entity,
                SeperationDir = float3.zero,
                AlignmentDir = float3.zero,
                CohesionDir = float3.zero,
                count = 0
            };

            [BurstCompile]
            static unsafe void OnEntityFound(UIntPtr nEntityPtr, UIntPtr BoidMatrix)
            {
                BoidDMtrx* boidDMtrx = (BoidDMtrx*)BoidMatrix;
                Entity* nEntity = (Entity*)nEntityPtr;
                Entity* entity = boidDMtrx->boid;
                if(nEntity->info.entityType != entity->info.entityType) return;
                
                BirdEntity* nBoid = (BirdEntity*)nEntity->obj;
                BirdEntity* boid = (BirdEntity*)entity->obj;
                float3 nBoidPos = nBoid->tCollider.transform.position;
                float3 boidPos = boid->tCollider.transform.position;

                if(math.distance(boidPos, nBoidPos) < settings.flight.flightDistance) 
                    boidDMtrx->SeperationDir += boidPos - nBoidPos;
                boidDMtrx->AlignmentDir += nBoid->flightDirection;
                boidDMtrx->CohesionDir += nBoidPos;
                boidDMtrx->count++;
            }
            
            context->sTree.QueryAsync(new STree.TreeNode.Bounds{
                Min = bird->GCoord - new int3(settings.flight.InfluenceDist),
                Max = bird->GCoord + new int3(settings.flight.InfluenceDist)
            }, BurstCompiler.CompileFunctionPointer<Action<UIntPtr, UIntPtr>>(OnEntityFound), (UIntPtr)(&boidDMtrx));


            if(boidDMtrx.count == 0) return;
            bird->flightDirection = Normalize(bird->flightDirection + 
                settings.flight.SeperationWeight * boidDMtrx.SeperationDir / boidDMtrx.count +
                settings.flight.AlignmentWeight * (boidDMtrx.AlignmentDir / boidDMtrx.count - bird->flightDirection) +
                settings.flight.CohesionWeight * (boidDMtrx.CohesionDir / boidDMtrx.count - bird->tCollider.transform.position) +
                math.up() * bird->random.NextFloat(0, settings.flight.FlyBiasWeight));
        }


        [BurstCompile] //Task 1 -> Fly, 2 -> Land
        public static unsafe void FollowPath(Entity* entity, EntityJob.Context* context){
            BirdEntity* bird = (BirdEntity*)entity->obj;
            bird->TaskDuration -= context->deltaTime;

            ref PathFinder.PathInfo finder = ref bird->pathFinder;
            ref TerrainColliderJob tCollider = ref bird->tCollider;
            if(math.any(math.abs(tCollider.transform.position - finder.currentPos) > entity->info.profile.bounds)) finder.hasPath = false;
            else if(finder.currentInd == finder.pathLength) finder.hasPath = false;
            if(!finder.hasPath) {
                ReleasePath(bird);
                if(bird->TaskIndex == 1) {
                    BoidFly(entity, context);
                } else {
                    bird->TaskIndex = 0;
                    bird->TaskDuration = settings.flight.AverageIdleTime * bird->random.NextFloat(0f, 2f);
                }
                return;
            }

            byte dir = finder.path[finder.currentInd];
            int3 nextPos = finder.currentPos + new int3((dir / 9) - 1, (dir / 3 % 3) - 1, (dir % 3) - 1);
            if(!EntityJob.VerifyProfile(nextPos, entity->info.profile, *context)) finder.hasPath = false;

            if(math.all(bird->GCoord == nextPos)){
                finder.currentPos = nextPos;
                finder.currentInd++;
            } else {
                float3 aim = Normalize(nextPos - bird->GCoord);
                tCollider.transform.rotation = Quaternion.RotateTowards(tCollider.transform.rotation, 
                                               Quaternion.LookRotation(aim), settings.movement.rotSpeed * context->deltaTime);
                if(math.length(bird->tCollider.velocity) < settings.movement.moveSpeed) 
                    tCollider.velocity += settings.movement.acceleration * context->deltaTime * aim;
            }
        }

        private static float3 Normalize(float3 v){
            if(math.length(v) == 0) return math.forward();
            else return math.normalize(v);
            //This norm guarantees the vector will be on the edge of a cube
        }

        private float3 RandomDirection(){
            float3 normal = new (random.NextFloat(-1, 1), random.NextFloat(-1, 1), random.NextFloat(-1, 1));
            if(math.length(normal) == 0) return math.forward();
            else return Normalize(normal);
        }

        [BurstCompile]
        private static unsafe void ReleasePath(BirdEntity* bird){
            if(bird->pathFinder.hasPath) UnsafeUtility.Free(bird->pathFinder.path, Unity.Collections.Allocator.Persistent);
            bird->pathFinder.hasPath = false;
        }

        [BurstCompile]
        public unsafe static void Disable(Entity* entity){
            entity->active = false;
        }

        unsafe struct BoidDMtrx{
            public Entity* boid;
            public float3 SeperationDir;
            public float3 AlignmentDir;
            public float3 CohesionDir;
            public uint count;
        } 
    }
}


