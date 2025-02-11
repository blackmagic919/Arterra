using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Burst;
using static CPUMapManager;
using System.Collections.Generic;
using TerrainGeneration;
using WorldConfig;
using WorldConfig.Generation.Entity;
using System.Collections.Concurrent;
using UnityEngine.Profiling;

public static class EntityManager
{
    private static EntityGenOffsets bufferOffsets;
    private static ComputeShader entityGenShader;
    private static ComputeShader entityTranscriber;
    public static List<Entity> EntityHandler; //List of all entities placed adjacently
    public static Dictionary<Guid, int> EntityIndex; //tracks location in handler of entityGUID
    public static STree ESTree; //BVH of entities updated synchronously
    public static ConcurrentQueue<Action> HandlerEvents; //Buffered updates to modify entities(since it can't happen while job is executing)
    private static volatile EntityJob Executor; //Job that updates all entities
    const int ENTITY_STRIDE_WORD = 3 + 1;
    const int MAX_ENTITY_COUNT = 10000; //10k ~ 5mb

    public static void DrawRecursive(uint ind){
        STree.TreeNode region = ESTree[ind];
        if(region.IsLeaf) return;
        Vector3 center = GSToWS(((float3)region.bounds.Min + region.bounds.Max) / 2);
        Vector3 size = (float3)(region.bounds.Max - region.bounds.Min) * Config.CURRENT.Quality.Terrain.value.lerpScale;
        Gizmos.DrawWireCube(center, size);
        DrawRecursive(region.Left);
        DrawRecursive(region.Right);
    }

    public unsafe static void Release(){
        //Debug.Log(EntityHandler.Length);
        Executor.Complete(); 
        foreach (Entity entity in EntityHandler){
            entity.Disable();
        }
    }

    public static void AddHandlerEvent(Action action){

        //If there's no job running directly execute it
        lock(Executor){
        if(!Executor.active) {
            Executor = new EntityJob(); //make new one to reset
            OctreeTerrain.MainFixedUpdateTasks.Enqueue(Executor);
        } } 
        HandlerEvents.Enqueue(action);
    }

    public unsafe static Entity GetEntity(Guid entityId){
        if(!EntityIndex.ContainsKey(entityId)) {
            return null;
        }
        int entityInd = EntityIndex[entityId];
        return EntityHandler[entityInd];
    }

    public unsafe static Entity GetEntity(int entityIndex){
        return EntityHandler[entityIndex];
    }
    
    public static void InitializeChunkEntity(GenPoint genInfo, int3 CCoord){
        int mapSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
        int3 GCoord = CCoord * mapSize + genInfo.position;
        AddHandlerEvent(() => InitializeE(GCoord, genInfo.entityIndex));
    }
    public static void InitializeEntity(int3 GCoord, uint entityIndex) => AddHandlerEvent(() => InitializeE(GCoord, entityIndex));
    public static void CreateEntity(Entity sEntity) => AddHandlerEvent(() => CreateE(sEntity));
    public static void ReleaseEntity(Guid entityId) => AddHandlerEvent(() => ReleaseE(entityId));
    public static unsafe void ReleaseChunkEntities(int3 CCoord){
        int mapChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
        ChunkMapInfo mapInfo = AddressDict[HashCoord(CCoord)];
        if(!mapInfo.valid) return; 
        //mapinfo.CCoord is coord of previous chunk
        STree.TreeNode.Bounds bounds = new STree.TreeNode.Bounds{
        Min = mapInfo.CCoord * mapChunkSize, Max = (mapInfo.CCoord + 1) * mapChunkSize};

        List<Entity> Entities = new List<Entity>();
        ESTree.Query(bounds, (Entity entity) => {
            if(entity == null) return;
            ReleaseEntity(entity.info.entityId);
            Entities.Add(entity);
        }); 
        ChunkStorageManager.SaveEntitiesToJsonSync(Entities, mapInfo.CCoord);
    }
    public unsafe static void ReleaseE(Guid entityId){
        if(!EntityIndex.ContainsKey(entityId)) {
            return;
        }
        int entityInd = EntityIndex[entityId];
        Entity entity = EntityHandler[entityInd];
        EntityIndex.Remove(entityId);
        ESTree.Delete(entityId);
        entity.Disable();
        entity.active = false;

        //Fill in hole
        if(entityInd != EntityHandler.Count-1){
            entity = EntityHandler[^1];
            EntityHandler[entityInd] = entity;
            EntityIndex[entity.info.entityId] = entityInd;
        }
        EntityHandler.RemoveAt(EntityHandler.Count - 1);
    }
    private unsafe static void InitializeE(int3 GCoord, uint entityIndex){
        
        Authoring authoring = Config.CURRENT.Generation.Entities.Reg.value[(int)entityIndex].Value;
        Entity newEntity = authoring.Entity;
        newEntity.info.entityId = Guid.NewGuid();
        newEntity.info.entityType = entityIndex;
        newEntity.active = true;

        EntityIndex[newEntity.info.entityId] = EntityHandler.Count;
        newEntity.Initialize(authoring.Setting, authoring.Controller, GCoord);
        EntityHandler.Add(newEntity);
        ESTree.Insert(GCoord, newEntity.info.entityId);
    }

    private unsafe static void CreateE(Entity sEntity){
        var reg = Config.CURRENT.Generation.Entities;
        Authoring authoring = reg.Retrieve((int)sEntity.info.entityType);
        sEntity.active = true;

        EntityIndex[sEntity.info.entityId] = EntityHandler.Count;
        sEntity.Deserialize(authoring.Setting, authoring.Controller, out int3 GCoord);
        EntityHandler.Add(sEntity);
        ESTree.Insert(GCoord, sEntity.info.entityId);
    }

    public unsafe static void AssertEntityLocation(Entity entity, int3 GCoord){
        Guid entityId = entity.info.entityId;
        if(!ESTree.Contains(entityId)) return;
        if(ESTree[ESTree[entityId].Parent].bounds.Contains(GCoord)){
            ESTree[entityId].bounds = new STree.TreeNode.Bounds(GCoord);
            return;
        } 
        ESTree.Delete(entityId);
        ESTree.Insert(GCoord, entityId);
    }

    private static void OnEntitiesRecieved(NativeArray<GenPoint> entities, uint address, int3 CCoord){
        GenerationPreset.memoryHandle.ReleaseMemory(address);
        foreach(GenPoint point in entities){
            InitializeChunkEntity(point, CCoord);
        }
    }

    public static void DeserializeEntities(List<Entity> entities){
        if(entities == null) return;
        foreach(Entity sEntity in entities){
            CreateEntity(sEntity);
        }
    }
    

    public static void Initialize(){
        EntityHandler = new List<Entity>();
        ESTree = new STree(MAX_ENTITY_COUNT * 2 + 1);
        EntityIndex = new Dictionary<Guid, int>();
        Executor = new EntityJob{active = false};
        HandlerEvents = new ConcurrentQueue<Action>();
        Indicators.Initialize();

        int numPointsPerAxis = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
        int numPoints = numPointsPerAxis * numPointsPerAxis * numPointsPerAxis;

        bufferOffsets = new EntityGenOffsets(0, numPoints, ENTITY_STRIDE_WORD);
        entityGenShader = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Entities/EntityIdentifier");
        entityTranscriber = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Entities/EntityTranscriber");

        int kernel = entityGenShader.FindKernel("Identify");
        entityGenShader.SetBuffer(kernel, "chunkEntities", UtilityBuffers.GenerationBuffer);
        entityGenShader.SetBuffer(kernel, "counter", UtilityBuffers.GenerationBuffer);
        entityGenShader.SetBuffer(kernel, "BiomeMap", UtilityBuffers.GenerationBuffer);
        kernel = entityGenShader.FindKernel("Prune");
        entityGenShader.SetBuffer(kernel, "chunkEntities", UtilityBuffers.GenerationBuffer);
        entityGenShader.SetBuffer(kernel, "counter", UtilityBuffers.GenerationBuffer);
        entityGenShader.SetInt("bCOUNTER_entities", bufferOffsets.entityCounter);
        entityGenShader.SetInt("bCOUNTER_prune", bufferOffsets.prunedCounter);
        entityGenShader.SetInt("bSTART_entities", bufferOffsets.entityStart);
        entityGenShader.SetInt("bSTART_prune", bufferOffsets.prunedStart);

        entityTranscriber.SetBuffer(0, "chunkEntities", UtilityBuffers.GenerationBuffer);
        entityTranscriber.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        entityTranscriber.SetInt("bCOUNTER_entities", bufferOffsets.prunedCounter);
        entityTranscriber.SetInt("bSTART_entities", bufferOffsets.prunedStart);
    }

    public static uint PlanEntities(int biomeStart, int3 CCoord, int chunkSize){
        int numPointsAxes = chunkSize;
        UtilityBuffers.ClearRange(UtilityBuffers.GenerationBuffer, 2, bufferOffsets.bufferStart);

        int kernel = entityGenShader.FindKernel("Identify");
        entityGenShader.SetInt("bSTART_biome", biomeStart);
        entityGenShader.SetInt("numPointsPerAxis", numPointsAxes);
        entityGenShader.SetInts("CCoord", new int[] { CCoord.x, CCoord.y, CCoord.z });
        GPUMapManager.SetCCoordHash(entityGenShader);

        entityGenShader.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);
        entityGenShader.Dispatch(kernel, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);

        kernel = entityGenShader.FindKernel("Prune");
        entityGenShader.SetBuffer(kernel, "_MemoryBuffer", GPUMapManager.Storage);
        entityGenShader.SetBuffer(kernel, "_AddressDict", GPUMapManager.Address);

        ComputeBuffer args = UtilityBuffers.CountToArgs(entityGenShader, UtilityBuffers.GenerationBuffer, bufferOffsets.entityCounter, kernel: kernel);
        entityGenShader.DispatchIndirect(kernel, args);

        kernel = entityTranscriber.FindKernel("CSMain");
        uint address = GenerationPreset.memoryHandle.AllocateMemory(UtilityBuffers.GenerationBuffer, ENTITY_STRIDE_WORD, bufferOffsets.prunedCounter);
        entityTranscriber.SetBuffer(kernel, "_MemoryBuffer", GenerationPreset.memoryHandle.Storage);
        entityTranscriber.SetBuffer(kernel, "_AddressDict", GenerationPreset.memoryHandle.Address);
        entityTranscriber.SetInt("addressIndex", (int)address);

        args = UtilityBuffers.CountToArgs(entityTranscriber, UtilityBuffers.GenerationBuffer, bufferOffsets.prunedCounter, kernel: kernel);
        entityTranscriber.DispatchIndirect(kernel, args);

        return address;
    }

    public static void BeginEntityReadback(uint address, int3 CCoord){
        static void OnEntitySizeRecieved(AsyncGPUReadbackRequest request, uint2 memHandle, Action<NativeArray<GenPoint>> callback){
            int memSize = (int)(request.GetData<uint>()[0] - ENTITY_STRIDE_WORD);
            int entityStartWord = ENTITY_STRIDE_WORD * (int)memHandle.y;
            AsyncGPUReadback.Request(GenerationPreset.memoryHandle.Storage, size: memSize * 4, offset: 4 * entityStartWord, (req) => callback.Invoke(req.GetData<GenPoint>()));
        }

        static void OnEntityAddressRecieved(AsyncGPUReadbackRequest request, Action<NativeArray<GenPoint>> callback){
            uint2 memHandle = request.GetData<uint2>()[0];
            if(memHandle.x == 0) return; // No entities

            AsyncGPUReadback.Request(GenerationPreset.memoryHandle.Storage, size: 4, offset: 4 * ((int)memHandle.x - 1), (req) => OnEntitySizeRecieved(req, memHandle, callback));
        }

        Action<NativeArray<GenPoint>> callback = (entities) => OnEntitiesRecieved(entities, address, CCoord);
        AsyncGPUReadback.Request(GenerationPreset.memoryHandle.Address, size: 8, offset: (int)(8 * address), (req) => OnEntityAddressRecieved(req, callback));

    }

    public struct EntityGenOffsets: BufferOffsets{
        public int entityCounter;
        public int prunedCounter;
        public int entityStart;
        public int prunedStart;
        private int offsetStart; private int offsetEnd;
        public int bufferStart{get{return offsetStart;}} public int bufferEnd{get{return offsetEnd;}}

        public EntityGenOffsets(int bufferStart, int numPoints, int entityStride){
            offsetStart = bufferStart;
            entityCounter = bufferStart;
            prunedCounter = bufferStart + 1;
            entityStart = Mathf.CeilToInt((float)(bufferStart+2) / entityStride);
            prunedStart = entityStart + numPoints;
            int prunedEnd_W = (prunedStart + numPoints) * entityStride;
            offsetEnd = prunedEnd_W;
        }
    }

    public struct GenPoint{
        public int3 position;
        public uint entityIndex;
        public GenPoint(int3 position, uint entityIndex){
            this.position = position;
            this.entityIndex = entityIndex;
        }
    }

    //More like a BVH, or a dynamic R-Tree with no overlap
    public unsafe struct STree{
        public Dictionary<Guid, uint> SpatialIndex;
        public TreeNode[] tree;
        //mark as volatile so we get the latest value
        public uint length; 
        public readonly uint Length => length - 1;
        public readonly uint Root{
            get{return tree[0].Right;}
            set{tree[0].Right = value;}
        }

        public readonly ref TreeNode this[Guid entityId]{
            get{
                if(SpatialIndex.ContainsKey(entityId)) 
                    return ref tree[SpatialIndex[entityId]];
                throw new KeyNotFoundException($"Entity with ID {entityId} not found.");
            }
        }

        public readonly TreeNode this[uint index]{
            get{return tree[index];}
            set{tree[index] = value;}
        }

        public bool Contains(Guid entityId){
            return SpatialIndex.ContainsKey(entityId);
        }

        public STree(int capacity){
            SpatialIndex = new Dictionary<Guid, uint>();
            tree = new TreeNode[capacity];
            tree[0] = new TreeNode(0){
                bounds = new TreeNode.Bounds{
                    Min = new int3(int.MinValue),
                    Max = new int3(int.MaxValue)
                }
            };
            length = 1;
            Root = 0;
        }

        public void Insert(int3 position, Guid eId){
            if(length+1 >= tree.Length) Resize();
            SpatialIndex[eId] = length;
            length++;

            tree[length - 1] = new TreeNode(position, eId);
            if(length == 2) Root = 1;
            else RecursiveInsert(position, Root);
        }

        private void RecursiveInsert(int3 position, uint current){
            ref TreeNode node = ref tree[(int)current];
            if(node.IsLeaf){
                tree[(int)length] = new TreeNode(0){
                    bounds = node.bounds.GetExpand(position),
                    Left = current,
                    Right = length - 1,
                    Parent = node.Parent
                };
                tree[(int)node.Parent].ReplaceChild(current, length);
                tree[(int)length - 1].Parent = length;
                node.Parent = length;
                length++;
                return;
            }
            
            node.bounds.Expand(position);
            ref TreeNode.Bounds B1 = ref tree[(int)node.Left].bounds; 
            ref TreeNode.Bounds B2 = ref tree[(int)node.Right].bounds; 
            if(B1.Contains(position)) RecursiveInsert(position, node.Left);
            else if(B2.Contains(position)) RecursiveInsert(position, node.Right); 
            else{
                TreeNode.Bounds B1Prime, B2Prime;
                B1Prime = B1.GetExpand(position);
                B2Prime = B2.GetExpand(position);

                //I guarantee there's a configuration that does not intersect
                if(B1Prime.Intersects(B2)) RecursiveInsert(position, node.Right);
                else if(B2Prime.Intersects(B1)) RecursiveInsert(position, node.Left);
                else if(B1Prime.Size() >= B2Prime.Size()) RecursiveInsert(position, node.Right);
                else RecursiveInsert(position, node.Left);
            }
        } 

        public void Delete(Guid entityId){
            if(tree == null) return;
            if(length <= 1) return;
            if(!SpatialIndex.ContainsKey(entityId)) return;
            int index = (int)SpatialIndex[entityId];
            SpatialIndex.Remove(entityId);
            
            if(index > length - 1 || index == 0) return;
            ref TreeNode node = ref tree[index];
            ref TreeNode parent = ref tree[(int)node.Parent];
            uint Sibling = index == parent.Left ? parent.Right : parent.Left;
            if(!node.IsLeaf) return;
            if(node.Parent == 0) {
                Root = 0; length = 1;
                return;
            }
            
            uint parentIndex = node.Parent;
            tree[(int)parent.Parent].ReplaceChild(node.Parent, Sibling);
            tree[(int)parent.Parent].ResizeBranch(tree);
            tree[(int)Sibling].Parent = parent.Parent;
            if(index < length - 2) {//This logic took me forever to figure out
                if(parentIndex != length - 1) MoveEntry((int)length - 1, (uint)index);
                else MoveEntry((int)length - 2, (uint)index);
            }
            if(parentIndex < length - 2){
                if(index != length - 2) MoveEntry((int)length - 2, parentIndex);
                else MoveEntry((int)length - 1, parentIndex); 
            }
            length -= 2;
        }

        private void MoveEntry(int oInd, uint nInd){
            if(oInd == nInd) return;
            TreeNode oNode = tree[oInd];
            if(!oNode.IsLeaf){
                tree[(int)oNode.Left].Parent = nInd;
                tree[(int)oNode.Right].Parent = nInd;
            } else{
                SpatialIndex[oNode.ObjId] = nInd;
            }

            tree[(int)oNode.Parent].ReplaceChild((uint)oInd, nInd);
            tree[(int)nInd] = oNode;
        }


        //The callback should not change STree as this will cause unpredictable behavior when query continues
        public readonly void Query(TreeNode.Bounds bounds, Action<Entity> action, int current = -1){
            if(current == -1) current = (int)Root;
            if(current == 0) return; //Root is zero when it is empty
            TreeNode node = tree[current];
            if(node.IsLeaf){
                action.Invoke(node.GetLeaf);
                return;
            }
            
            if(bounds.Intersects(tree[(int)node.Left].bounds)) Query(bounds, action, (int)node.Left);
            if(bounds.Intersects(tree[(int)node.Right].bounds)) Query(bounds, action, (int)node.Right);
        }

        private void Resize(){
            TreeNode[] newTree = new TreeNode[tree.Length * 2];
            for(int i = 0; i < tree.Length; i++){
                newTree[i] = tree[i];
            } tree = newTree;
        }


        public struct TreeNode{
            public Bounds bounds;
            //64-bit systems guarantee 64-bit copies are atomic for properly aligned elements
            //If 32-bit systems are used, this will not work
            public uint Parent;
            public uint Left;
            public uint Right;
            public Guid ObjId;
            public readonly bool IsLeaf => ObjId != Guid.Empty;
            public readonly Entity GetLeaf => GetEntity(ObjId);
            public TreeNode(int3 position){
                bounds = new Bounds(position);
                Left = 0; Right = 0;
                ObjId = Guid.Empty;
                Parent = 0;
            }

            public TreeNode(int3 position, Guid EntityId){
                bounds = new Bounds(position);
                Left = 0; Right = 0;
                ObjId = EntityId;
                Parent = 0;
            }

            public void ReplaceChild(uint a, uint b){
                if(Left == a) Left = b;
                else if(Right == a) Right = b;
            }

            public void ResizeBranch(TreeNode[] tree){
                if(IsLeaf) return;
                if(Left == 0 || Right == 0) return;
                bounds = tree[Left].bounds.GetExpand(tree[Right].bounds.Min).GetExpand(tree[Right].bounds.Max);
            }

            public struct Bounds{
                public int3 Min;
                public int3 Max;

                public Bounds(int3 pos){
                    Min = pos;
                    Max = pos;
                }

                public void Expand(int3 position){
                    Max = math.max(Max, position);
                    Min = math.min(Min, position);
                }
                
                public readonly Bounds GetExpand(int3 position){
                    Bounds nBounds = this;
                    nBounds.Expand(position);
                    return nBounds;
                }

                public readonly bool Contains(int3 position){
                    return math.all(position >= Min) && math.all(position <= Max);
                }

                public readonly bool Intersects(Bounds bounds){
                    return math.all(bounds.Min <= Max) && math.all(bounds.Max >= Min);
                }
                
                public readonly uint Size(){
                    int3 size = Max - Min;
                    return (uint)(size.x * size.y * size.z);
                }

                public readonly float Perimeter(){
                    int3 size = Max - Min;
                    return 2 * (size.x + size.y) + 2 * (size.y + size.z) + 2 * (size.z + size.x);
                }
            }
        }
    }
}

public class EntityJob : UpdateTask{
    private float cumulativeDelta;
    public bool dispatched = false;
    private JobHandle handle;
    public static Context cxt;

    public unsafe EntityJob(){
        active = true;
        dispatched = false;
        cumulativeDelta = 0;
        cxt = new Context{
            Profile = (ProfileE*)GenerationPreset.entityHandle.entityProfileArray.GetUnsafePtr(),
            mapContext = new MapContext{
                MapData = (MapData*)SectionedMemory.GetUnsafePtr(),
                AddressDict = (ChunkMapInfo*)AddressDict.GetUnsafePtr(),
                mapChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize,
                numChunksAxis = numChunksAxis,
                IsoValue = (int)Math.Round(Config.CURRENT.Quality.Terrain.value.IsoLevel * 255.0)
            },

            gravity = Physics.gravity / Config.CURRENT.Quality.Terrain.value.lerpScale,
            deltaTime = Time.fixedDeltaTime
        };
    }

    public bool Complete(){
        if(!dispatched) return true;
        if(!handle.IsCompleted) return false; 
        handle.Complete();
        dispatched = false;
        return true;
    }

    public override void Update(MonoBehaviour mono){
        cumulativeDelta += Time.fixedDeltaTime;
        if(!Complete()) return;
        cxt.deltaTime = cumulativeDelta;
        cumulativeDelta = 0;

        while(EntityManager.HandlerEvents.TryDequeue(out Action action)){
            action.Invoke();
        } EntityManager.HandlerEvents.Clear();

        if(EntityManager.EntityHandler.Count == 0){
            this.active = false;
            return;
        }

        handle = cxt.Schedule(EntityManager.EntityHandler.Count, 16);
        dispatched = true;
    }

    public struct Context: IJobParallelFor{
        [NativeDisableUnsafePtrRestriction]
        [ReadOnly] public unsafe ProfileE* Profile;
        [ReadOnly] public unsafe MapContext mapContext;
        [ReadOnly] public float3 gravity; 
        [ReadOnly] public float deltaTime;
        public unsafe void Execute(int index){
            Profiler.BeginSample(EntityManager.GetEntity(index).GetType().ToString());
            EntityManager.GetEntity(index).Update();
            Profiler.EndSample();
        }
    }

}