using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Burst;
using static CPUDensityManager;
using System.Collections.Generic;
using Newtonsoft.Json;
using TerrainGeneration;
using WorldConfig;
using WorldConfig.Generation.Entity;

public static class EntityManager
{
    private static EntityGenOffsets bufferOffsets;
    private static ComputeShader entityGenShader;
    private static ComputeShader entityTranscriber;
    public static NativeList<IntPtr> EntityHandler; //List of all entities placed adjacently
    public static Dictionary<string, int> EntityIndex; //tracks location in handler of entityGUID
    public static STree ESTree; //BVH of entities updated synchronously
    public static Action HandlerEvents; //Buffered updates to modify entities(since it can't happen while job is executing)
    private static EntityJob Executor; //Job that updates all entities
    const int ENTITY_STRIDE_WORD = 3 + 1;
    const int MAX_ENTITY_COUNT = 10000; //10k ~ 5mb


    public static void OnDrawGizmos(){
        if(ESTree.Length == 0) return;
        //DrawRecursive(ESTree.Root, 0);
        //Debug.Log("Average Depth: " + ((float)depth / sampleCount));

        
        for(int i = 1; i < ESTree.Length; i++){
            STree.TreeNode region = ESTree[i];
            Vector3 center = CPUDensityManager.GSToWS(((float3)region.bounds.Min + region.bounds.Max) / 2);
            Vector3 size = (float3)(region.bounds.Max - region.bounds.Min) * 2;
            Gizmos.DrawWireCube(center, size);
        }
    }


    /*private static uint depth = 0;
    private static uint sampleCount = 0;
    
    public static void DrawRecursive(uint ind, int depth){
        STree.TreeNode region = ESTree[(int)ind];
        if(region.IsLeaf) {
            EntityManager.depth += (uint)depth;
            sampleCount++;
            return;
        };
        DrawRecursive(region.Left, depth + 1);
        DrawRecursive(region.Right, depth + 1);
    }*/

    public unsafe static void Release(){
        //Debug.Log(EntityHandler.Length);
        Executor.Complete();
        EntityHandler.Dispose(); //Controllers will automatically release 
        ESTree.Dispose();
    }

    public static void AddHandlerEvent(Action action){
        //If there's no job running directly execute it
        if(!Executor.active || !Executor.dispatched)  action.Invoke();
        else HandlerEvents += action;

    }

    public static void InitializeEntity(GenPoint genInfo, int3 CCoord){AddHandlerEvent(() => InitializeE(genInfo, CCoord));}
    public static void CreateEntity(EntitySerial sEntity){AddHandlerEvent(() => CreateE(sEntity));}
    public static void ReleaseEntity(JGuid entityId){AddHandlerEvent(() => ReleaseEntity(entityId));}
    public unsafe static void ReleaseE(JGuid entityId){
        if(!EntityIndex.ContainsKey(entityId)) {
            return;
        }
        int entityInd = EntityIndex[entityId];
        Entity* entity = (Entity*)EntityHandler[entityInd];
        Entity.Disable(entity);
        EntityIndex.Remove(entityId);

        //Fill in hole
        if(entityInd != EntityHandler.Length-1){
            entity = (Entity*)EntityHandler[EntityHandler.Length-1];
            EntityHandler[entityInd] = (IntPtr)entity;
            EntityIndex[entity->info.entityId] = entityInd;
        }
        EntityHandler.RemoveLast();
    }
    private unsafe static void InitializeE(GenPoint genInfo, int3 CCoord){
        Entity newEntity = new ();
        
        int entityInd = EntityHandler.Length;
        int mapSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
        int3 GCoord = CCoord * mapSize + genInfo.position;
        
        Authoring authoring = Config.CURRENT.Generation.Entities.Reg.value[(int)genInfo.entityIndex].Value;
        newEntity.info.profile = authoring.Info;
        newEntity.info.entityId = Guid.NewGuid();
        newEntity.info.entityType = genInfo.entityIndex;
        newEntity.active = true;
        EntityIndex[newEntity.info.entityId] = entityInd;
        HandlerEvents = null;
        
        IntPtr entity = authoring.Entity.Initialize(ref newEntity, GCoord);
        EntityHandler.Add(entity);
        ((Entity*)entity)->info.SpatialId = ESTree.Insert(GCoord, entity);

        //Add controller
        EntityController controller = UnityEngine.Object.Instantiate(authoring.Controller).GetComponent<EntityController>();
        if(controller == null) throw new Exception("Entity Controller is null");
        controller.Initialize(entity);

        if(Executor.active) return;
        Executor = new EntityJob(); //make new one to reset
        TerrainGeneration.OctreeTerrain.MainFixedUpdateTasks.Enqueue(Executor);
    }

    private unsafe static void CreateE(EntitySerial sEntity){
        Entity newEntity = new ();
        int entityInd = EntityHandler.Length;
        
        var reg = Config.CURRENT.Generation.Entities;
        Authoring authoring = reg.Retrieve(sEntity.type);
        newEntity.info.profile = authoring.Info;
        newEntity.info.entityId = sEntity.guid;
        newEntity.info.entityType = (uint)reg.RetrieveIndex(sEntity.type);
        newEntity.active = true;
        EntityIndex[newEntity.info.entityId] = entityInd;

        IntPtr entity = sEntity.data.Deserialize(ref newEntity, out int3 GCoord);
        EntityHandler.Add(entity);
        ((Entity*)entity)->info.SpatialId = ESTree.Insert(GCoord, entity);

        EntityController controller = UnityEngine.Object.Instantiate(authoring.Controller).GetComponent<EntityController>();
        if(controller == null) throw new Exception("Entity Controller is null");
        controller.Initialize(entity);

        if(Executor.active) return;
        Executor = new EntityJob(); //make new one to reset
        TerrainGeneration.OctreeTerrain.MainFixedUpdateTasks.Enqueue(Executor);
    }

    public unsafe static void AssertEntityLocation(Entity* entity, int3 GCoord){
        if(entity->info.SpatialId == 0) return;
        if(ESTree[ESTree[entity->info.SpatialId].Parent].bounds.Contains(GCoord)){
            ESTree.GetRef((int)entity->info.SpatialId).bounds = new STree.TreeNode.Bounds(GCoord);
            return;
        } 
        ESTree.Delete((int)entity->info.SpatialId);
        entity->info.SpatialId = ESTree.Insert(GCoord, (IntPtr)entity);
    }

    private static void OnEntitiesRecieved(NativeArray<GenPoint> entities, uint address, int3 CCoord){
        GenerationPreset.memoryHandle.ReleaseMemory(address);
        foreach(GenPoint point in entities){
            InitializeEntity(point, CCoord);
        }
    }

    public static void DeserializeEntities(List<EntitySerial> entities, int3 CCoord){
        if(entities == null) return;
        foreach(EntitySerial sEntity in entities){
            CreateEntity(sEntity);
        }
    }

    public static unsafe void ReleaseChunkEntities(int3 CCoord){
        int mapChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
        ChunkMapInfo mapInfo = AddressDict[HashCoord(CCoord)];
        if(!mapInfo.valid) return; 
        //mapinfo.CCoord is coord of previous chunk
        STree.TreeNode.Bounds bounds = new STree.TreeNode.Bounds{Min = mapInfo.CCoord * mapChunkSize, Max = (mapInfo.CCoord + 1) * mapChunkSize - 1};
        ESTree.Query(bounds, (UIntPtr entity) => {
            ReleaseEntity(((Entity*)entity)->info.entityId);
        });
    }

    public static unsafe List<Entity> GetChunkEntities(int3 CCoord){
        int mapChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
        ChunkMapInfo mapInfo = AddressDict[HashCoord(CCoord)];
        if(!mapInfo.valid) return null;
        List<Entity> entities = new List<Entity>();
        STree.TreeNode.Bounds bounds = new() { Min = mapInfo.CCoord * mapChunkSize, Max = (mapInfo.CCoord + 1) * mapChunkSize - 1};
        //This does not change the entity list so can be done directly
        ESTree.Query(bounds, (UIntPtr entity) => {
            entities.Add(*(Entity*)entity);
        });

        return entities;
    } 
    

    public static void Initialize(){
        EntityHandler = new NativeList<IntPtr>(MAX_ENTITY_COUNT, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        ESTree = new STree(MAX_ENTITY_COUNT * 2 + 1);
        EntityIndex = new Dictionary<string, int>();
        Executor = new EntityJob{active = false};

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
        GPUDensityManager.SetCCoordHash(entityGenShader);

        entityGenShader.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);
        entityGenShader.Dispatch(kernel, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);

        kernel = entityGenShader.FindKernel("Prune");
        entityGenShader.SetBuffer(kernel, "_MemoryBuffer", GPUDensityManager.Storage);
        entityGenShader.SetBuffer(kernel, "_AddressDict", GPUDensityManager.Address);

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
    }

    //More like a BVH, or a dynamic R-Tree with no overlap
    public unsafe struct STree{
        public NativeArray<TreeNode> _tree;
        [NativeDisableUnsafePtrRestriction]
        public TreeNode* tree;
        //mark as volatile so we get the latest value
        public uint length; 
        public readonly uint Length => length - 1;
        public readonly uint Root{
            get{return tree[0].Right;}
            set{GetRef(0).Right = value;}
        }

        public TreeNode this[int index]{
            get{return tree[index];}
            set{tree[index] = value;}
        }

        public TreeNode this[uint index]{
            get{return tree[(int)index];}
            set{tree[(int)index] = value;}
        }

        public readonly ref TreeNode GetRef(int index)
        {
            //Allow tree to access nodes outside Length to workaround threads, it seems unsafe, but is not catastrophic
            if (index < 0 || index >= _tree.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            unsafe { return ref UnsafeUtility.ArrayElementAsRef<TreeNode>(tree, index); }
        }

        public readonly unsafe TreeNode* GetPtr(int index)
        {
            if (index < 0 || index >= _tree.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return tree + index;
        }

        public STree(int capacity){
            _tree = new NativeArray<TreeNode>(capacity, Allocator.Persistent);
            tree = (TreeNode*)_tree.GetUnsafePtr();
            tree[0] = new TreeNode{
                bounds = new TreeNode.Bounds{
                    Min = new int3(int.MinValue),
                    Max = new int3(int.MaxValue)
                },
            };

            //Length includes pointer to head node(0)
            length = 1;
        }

        public void Dispose(){
            _tree.Dispose();
            length = 0;
        }

        public uint Insert(int3 position, IntPtr ptr){
            uint leafInd = length; tree[(int)leafInd] = TreeNode.MakeLeaf(position, ptr);
            length++;

            if(length == 2) Root = leafInd;
            else RecursiveInsert(position, Root, ptr);
            return leafInd;
        }

        private void RecursiveInsert(int3 position, uint current, IntPtr ptr){
            ref TreeNode node = ref GetRef((int)current);
            if(node.IsLeaf){
                tree[(int)length] = new TreeNode{
                    bounds = node.bounds.GetExpand(position),
                    Left = current,
                    Right = length - 1,
                    Parent = node.Parent
                };
                GetRef((int)node.Parent).ReplaceChild(current, length);
                GetRef((int)length - 1).Parent = length;
                node.Parent = length;
                length++;
                return;
            }
            
            node.bounds.Expand(position);
            ref TreeNode.Bounds B1 = ref GetRef((int)node.Left).bounds; 
            ref TreeNode.Bounds B2 = ref GetRef((int)node.Right).bounds; 
            if(B1.Contains(position)) RecursiveInsert(position, node.Left, ptr);
            else if(B2.Contains(position)) RecursiveInsert(position, node.Right, ptr); 
            else{
                TreeNode.Bounds B1Prime, B2Prime;
                B1Prime = B1.GetExpand(position);
                B2Prime = B2.GetExpand(position);

                //I guarantee there's a configuration that does not intersect
                if(B1Prime.Intersects(B2)) RecursiveInsert(position, node.Right, ptr);
                else if(B2Prime.Intersects(B1)) RecursiveInsert(position, node.Left, ptr);
                else if(B1Prime.Size() >= B2Prime.Size()) RecursiveInsert(position, node.Right, ptr);
                else RecursiveInsert(position, node.Left, ptr);
            }
        } 

        public void Delete(int index){
            if(length <= 1) return;
            if(index > length - 1 || index == 0) return;
            if(tree == null) return;

            ref TreeNode node = ref GetRef(index);
            ref TreeNode parent = ref GetRef((int)node.Parent);
            uint Sibling = index == parent.Left ? parent.Right : parent.Left;
            if(!node.IsLeaf) return; 
            if(node.Parent == 0) {
                Root = 0; length = 1;
                return;
            }

            uint parentIndex = node.Parent;
            GetRef((int)parent.Parent).ReplaceChild(node.Parent, Sibling);
            GetRef((int)Sibling).Parent = parent.Parent;
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
                GetRef((int)oNode.Left).Parent = nInd;
                GetRef((int)oNode.Right).Parent = nInd;
            } else{ unsafe {
                Entity* entity = (Entity*)oNode.GetLeaf;
                entity->info.SpatialId = nInd;
            }}

            GetRef((int)oNode.Parent).ReplaceChild((uint)oInd, nInd);
            tree[(int)nInd] = oNode;
        }


        //The callback should not change STree as this will cause unpredictable behavior when query continues
        public readonly void Query(TreeNode.Bounds bounds, Action<UIntPtr> action, int current = -1){
            if(current == -1) current = (int)Root;
            if(current == 0) return;
            ref TreeNode node = ref GetRef(current);
            if(node.IsLeaf){
                action.Invoke(node.GetLeaf);
                return;
            }
            
            if(bounds.Intersects(GetRef((int)node.Left).bounds)) Query(bounds, action, (int)node.Left);
            if(bounds.Intersects(GetRef((int)node.Right).bounds)) Query(bounds, action, (int)node.Right);
        }

        //Note: This function is not thread-safe, TreeNode is not predictable and may be a combination of
        //multiple nodes, however Ptr is 64-bit and is atomic on 64-bit systems, which this query assumes is true
        //Bounds may also be inaccurate, but that failure is not catastrophic.
        public readonly unsafe void QueryAsync(TreeNode.Bounds bounds, FunctionPointer<Action<UIntPtr, UIntPtr>> Action, UIntPtr entity, int current = -1){
            if(current == -1) current = (int)Root;
            if(current == 0) return;
            
            //copy from cache/memory--this is not thread-safe
            TreeNode node = tree[current]; 
            if(node.IsLeaf){
                //This is the only place for catastrophic failure, Leaf MUST be a valid pointer.
                Action.Invoke(node.GetLeaf, entity);
                return;
            }

            //The values obtained here may be inaccurate, but that's okay
            if(bounds.Intersects(tree[(int)node.Left].bounds)) QueryAsync(bounds, Action, entity, (int)node.Left);
            if(bounds.Intersects(tree[(int)node.Right].bounds)) QueryAsync(bounds, Action, entity, (int)node.Right);
        }


        public struct TreeNode{
            public Bounds bounds;
            //64-bit systems guarantee 64-bit copies are atomic for properly aligned elements
            //If 32-bit systems are used, this will not work
            public ulong Ptr;
            public uint Parent;
            public readonly bool IsLeaf => (Ptr & 0x8000000000000000) != 0;
            public readonly UIntPtr GetLeaf => new (Ptr & 0x7FFFFFFFFFFFFFFF);
            public uint Left{
                readonly get{return (uint)(Ptr & 0xFFFFFFFF);}
                set{Ptr = (Ptr & 0xFFFFFFFF00000000) | (value & 0xFFFFFFFF);}
            }

            public uint Right{
                readonly get{return (uint)((Ptr >> 32) & 0x7FFFFFFF);}
                set{Ptr = (Ptr & 0x80000000FFFFFFFF) | ((ulong)value & 0x7FFFFFFF) << 32;}
            }
            public TreeNode(int3 position){
                bounds = new Bounds(position);
                Ptr = 0ul;
                Parent = 0;
            }

            public static TreeNode MakeLeaf(int3 position, IntPtr ptr){
                TreeNode node = new(position){ 
                    Ptr = 0x8000000000000000 | (ulong)ptr,
                    Parent = 0,
                };
                return node;
            }

            public void ReplaceChild(uint a, uint b){
                if(Left == a) Left = b;
                else if(Right == a) Right = b;
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


public struct NativeList<T> where T: unmanaged{
    public NativeArray<T> array;
    private int _length;
    public int Length{get{return _length;}}
    public T this[int index]{get{return array[index];} set{array[index] = value;}}
    public NativeList(int length, Allocator allocator, NativeArrayOptions options){
        array = new NativeArray<T>(length, allocator, options);
        _length = 0;
    }

    public ref T GetRef(int index)
    {
        if (index < 0 || index >= array.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        unsafe { return ref UnsafeUtility.ArrayElementAsRef<T>(array.GetUnsafePtr(), index); }
    }

    public unsafe T* GetPtr(int index)
    {
        if (index < 0 || index >= array.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        void* basePtr = NativeArrayUnsafeUtility.GetUnsafePtr(array);
        return (T*)basePtr + index;
    }

    public void Add(T item){
        if(_length == array.Length) return;
        array[_length] = item;
        _length++;
    }

    public void RemoveLast(){
        if(_length == 0) return; 
        _length--;
    }

    public void Dispose(){
        array.Dispose();
    }
}

public class EntityJob : UpdateTask{
    public bool dispatched = false;
    private JobHandle handle;
    private Context job;

    public unsafe EntityJob(){
        active = true;
        dispatched = false;
        job = new Context{
            Entities = (Entity**)EntityManager.EntityHandler.GetPtr(0),
            Profile = (ProfileE*)GenerationPreset.entityHandle.entityProfileArray.GetUnsafePtr(),
            sTree = EntityManager.ESTree,
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

    public void Complete(){
        if(dispatched) handle.Complete();
        dispatched = false;
    }

    public override void Update(MonoBehaviour mono){
        if(dispatched) handle.Complete();
        dispatched = false;

        EntityManager.HandlerEvents?.Invoke();
        EntityManager.HandlerEvents = null;
        if(EntityManager.EntityHandler.Length == 0){
            this.active = false;
            return;
        }

        job.deltaTime = Time.fixedDeltaTime;
        handle = job.Schedule(EntityManager.EntityHandler.Length, 16);
        dispatched = true;
    }

    [BurstCompile]
    public struct Context: IJobParallelFor{
        [NativeDisableUnsafePtrRestriction]
        public unsafe Entity** Entities;
        [NativeDisableUnsafePtrRestriction]
        [ReadOnly] public unsafe ProfileE* Profile;
        [ReadOnly] public unsafe MapContext mapContext;
        public EntityManager.STree sTree;
        [ReadOnly] public float3 gravity; 
        [ReadOnly] public float deltaTime;
        public unsafe void Execute(int index){
            Context cxt = this; //copy to stack so that address is fixed
            Entity.Update(*(Entities + index), &cxt);
        }
    }

    [BurstCompile]
    public unsafe static bool VerifyProfile(in int3 GCoord, in Entity.ProfileInfo info, in Context context, bool UseExFlag = true){
        bool allC = true; bool anyC = false; bool any0 = false;
        uint3 dC = new (0);
        for(dC.x = 0; dC.x < info.bounds.x; dC.x++){
            for(dC.y = 0; dC.y < info.bounds.y; dC.y++){
                for(dC.z = 0; dC.z < info.bounds.z; dC.z++){
                    uint index = dC.x * info.bounds.y * info.bounds.z + dC.y * info.bounds.z + dC.z;
                    ProfileE profile = context.Profile[index + info.profileStart];
                    if(profile.ExFlag && UseExFlag) continue;
                    bool valid = InBounds(SampleMap(GCoord + (int3)dC, context.mapContext), profile.bounds);
                    allC = allC && (valid || !profile.AndFlag);
                    anyC = anyC || (valid && profile.OrFlag);
                    any0 = any0 || profile.OrFlag;
                }
            }
        } 
        if(allC && (!any0 || anyC)) return true;
        else return false;
    }

    [BurstCompile]
    public unsafe static bool InBounds(in CPUDensityManager.MapData data, uint bounds){
        return data.density >= (bounds & 0xFF) && data.density <= ((bounds >> 8) & 0xFF) && 
               data.viscosity >= ((bounds >> 16) & 0xFF) && data.viscosity <= ((bounds >> 24) & 0xFF);
    }


}


[BurstCompile]
public unsafe struct PathFinder{

    public struct PathInfo{
        public int3 currentPos; 
        public int currentInd;
        public int pathLength;
        [NativeDisableUnsafePtrRestriction][JsonIgnore]
        public unsafe byte* path;
        public bool hasPath; //Resource isn't bound
        //0x4 -> Controller Released, 0x2 -> Job Released, 0x1 -> Resource Released
    }
    
    public int PathMapSize;
    public int PathDistance;
    public int HeapEnd;
    public byte* SetPtr;
    public int4* MapPtr;
    //x = heap score, y = heap->dict ptr, z = dict->heap ptr, w = path dict

    private static readonly int4[] dP = new int4[24]{
        new (1, 0, 0, 10),
        new (1, -1, 0, 14),
        new (0, -1, 0, 10),
        new (-1, -1, 0, 14),
        new (-1, 0, 0, 10),
        new (-1, 1, 0, 14),
        new (0, 1, 0, 10),
        new (1, 1, 0, 14),
        new (1, 0, 1, 14),
        new (1, -1, 1, 17),
        new (0, -1, 1, 14),
        new (-1, -1, 1, 17),
        new (-1, 0, 1, 14),
        new (-1, 1, 1, 17),
        new (0, 1, 1, 14),
        new (1, 1, 1, 17),
        new (1, 0, -1, 14),
        new (1, -1, -1, 17),
        new (0, -1, -1, 14),
        new (-1, -1, -1, 17),
        new (-1, 0, -1, 14),
        new (-1, 1, -1, 17),
        new (0, 1, -1, 14),
        new (1, 1, -1, 17),
    };
    
    public PathFinder(int PathDistance){
        this.PathDistance = PathDistance;
        this.PathMapSize = PathDistance * 2 + 1;
        int mapLength = PathMapSize * PathMapSize * PathMapSize;

        //The part we actually clear
        //We divide by 4 because Map has to be 4 byte aligned
        int SetSize = Mathf.CeilToInt(mapLength / (8 * 16f)); 
        int MapSize = mapLength;
        int TotalSize = (SetSize + MapSize) * 16;
        HeapEnd = 1;

        //We make it one block so less fragment & more cache hits
        void* ptr = UnsafeUtility.Malloc(TotalSize, 16, Allocator.TempJob);;
        SetPtr = (byte*)ptr;
        MapPtr = (int4*)(SetPtr + (SetSize * 16));
        UnsafeUtility.MemClear((void*)SetPtr, SetSize * 16);
    }

    [BurstCompile]
    public readonly void Release(){ UnsafeUtility.Free((void*)SetPtr, Allocator.TempJob); }
    
    [BurstCompile]
    public unsafe void AddNode(int3 ECoord, int score, int prev){
        int index = ECoord.x * PathMapSize * PathMapSize + ECoord.y * PathMapSize + ECoord.z;
        int heapPos = HeapEnd;

        if(((SetPtr[index / 8] >> (index % 8)) & 0x1) == 1){
            // Not Already Visited         Score is better than heap score
            if(MapPtr[index].z != 0 && score < MapPtr[MapPtr[index].z].x) 
                heapPos = MapPtr[index].z;
            else return;
        } else {
            SetPtr[index / 8] |= (byte)(1 << (index % 8));
            HeapEnd++;
        }

        
        while(heapPos > 1){
            int2 parent = MapPtr[heapPos / 2].xy;
            if(parent.x <= score) break;
            MapPtr[heapPos].xy = parent;
            MapPtr[parent.y].z = heapPos;
            heapPos >>= 1;
        }

        MapPtr[heapPos].xy = new int2(score, index);
        MapPtr[index].zw = new int2(heapPos, prev);
    }

    [BurstCompile]
    public int2 RemoveNode(){
        int2 result = MapPtr[1].xy;
        int2 last = MapPtr[HeapEnd - 1].xy;
        HeapEnd--;

        int heapPos = 1;
        while(heapPos < HeapEnd){
            int child = heapPos * 2;
            if(child > HeapEnd) break;
            if(child + 1 < HeapEnd && MapPtr[child + 1].x < MapPtr[child].x) child++;
            if(MapPtr[child].x >= last.x) break;
            MapPtr[heapPos].xy = MapPtr[child].xy;
            MapPtr[MapPtr[heapPos].y].z = heapPos;
            heapPos = child;
        }
        MapPtr[heapPos].xy = last;
        MapPtr[last.y].z = heapPos;
        MapPtr[result.y].z = 0; //Mark as visited
        return result;
    }

    [BurstCompile]
    static int Get3DDistance(in int3 dist)
    {
        int minDist = math.min(dist.x, math.min(dist.y, dist.z));
        int maxDist = math.max(dist.x, math.min(dist.y, dist.z));
        int midDist = dist.x + dist.y + dist.z - minDist - maxDist;

        return minDist * 17 + (midDist - minDist) * 14 + (maxDist - midDist) * 10;
    }

    private static float3 CubicNorm(float3 v){
        if(math.length(v) == 0) return math.forward();
        else return v / math.cmax(math.abs(v));
        //This norm guarantees the vector will be on the edge of a cube
    }

    [BurstCompile]
    //Simplified A* algorithm for maximum performance
    //End Coord is relative to the start coord. Start Coord is always PathDistance
    /*
    X3
    y
    ^     ______________
    |    | 6  |  7 |  8 |
    |    |____|____|____|
    |    | 5  |    |  1 |
    |    |____|____|____|
    |    | 4  |  3 |  2 |
    |    |____|____|____| 
    +--------------------> x
    */
    public static byte* FindPath(in int3 Origin, in int3 iEnd, int PathDistance, in Entity.ProfileInfo info, in EntityJob.Context context, out int PathLength){
        PathFinder finder = new (PathDistance);
        int3 End = math.clamp(iEnd + PathDistance, 0, finder.PathMapSize-1); //We add the distance to make it relative to the start
        int pathEndInd = End.x * finder.PathMapSize * finder.PathMapSize + End.y * finder.PathMapSize + End.z;

        //Find the closest point to the end
        int3 pathStart = new (PathDistance, PathDistance, PathDistance);
        int startInd = pathStart.x * finder.PathMapSize * finder.PathMapSize + pathStart.y * finder.PathMapSize + pathStart.z;
        int hCost = Get3DDistance(math.abs(End - pathStart)); 
        int2 bestEnd = new (startInd, hCost);
    
        finder.AddNode(pathStart, hCost, 13); //13 means dP = (0, 0, 0)
        while(finder.HeapEnd > 1){
            int2 current = finder.RemoveNode();
            int3 ECoord = new (current.y / (finder.PathMapSize * finder.PathMapSize), 
                                current.y / finder.PathMapSize % finder.PathMapSize, 
                                current.y % finder.PathMapSize);
            hCost = Get3DDistance(math.abs(End - ECoord));

            //Always assume the first point is valid
            if(current.y != startInd && !EntityJob.VerifyProfile(Origin + ECoord - PathDistance, info, context)) 
                continue;
            if(hCost < bestEnd.y){
                bestEnd.x = current.y;
                bestEnd.y = hCost;
            } if((int)current.y == pathEndInd)
                break;

            for(int i = 0; i < 24; i++){
                int4 delta = dP[i];
                int3 nCoord = ECoord + delta.xyz;
                if(math.any(nCoord < 0) || math.any(nCoord >= finder.PathMapSize)) continue;
                int FScore = current.x + Get3DDistance(math.abs(End - nCoord)) - hCost + delta.w;
                int dirEnc = (delta.x + 1) * 9 + (delta.y + 1) * 3 + delta.z + 1;
                finder.AddNode(nCoord, FScore, dirEnc);
            }
        }

        byte* path = RetracePath(ref finder, bestEnd.x, startInd, out PathLength);
        finder.Release();
        return path;
    }

    //Find point that matches raw-profile along the path to destination
    public static byte* FindClosestAlongPath(in int3 Origin, in int3 iEnd, int PathDistance, in Entity.ProfileInfo info, in EntityJob.Context context, out int PathLength, out bool ReachedEnd){
        PathFinder finder = new (PathDistance);
        int3 End = math.clamp(iEnd + PathDistance, 0, finder.PathMapSize-1); //We add the distance to make it relative to the start
        int pathEndInd = End.x * finder.PathMapSize * finder.PathMapSize + End.y * finder.PathMapSize + End.z;

        //Find the closest point to the end
        int3 pathStart = new (PathDistance, PathDistance, PathDistance);
        int startInd = pathStart.x * finder.PathMapSize * finder.PathMapSize + pathStart.y * finder.PathMapSize + pathStart.z;
        int hCost = Get3DDistance(math.abs(End - pathStart)); 
        int2 bestEnd = new (startInd, hCost);
    
        ReachedEnd = false;
        finder.AddNode(pathStart, hCost, 13); //13 means dP = (0, 0, 0)
        while(finder.HeapEnd > 1){
            int2 current = finder.RemoveNode();
            int3 ECoord = new (current.y / (finder.PathMapSize * finder.PathMapSize), 
                                current.y / finder.PathMapSize % finder.PathMapSize, 
                                current.y % finder.PathMapSize);
            hCost = Get3DDistance(math.abs(End - ECoord));

            //Always assume the first point is valid
            if(current.y != startInd && !EntityJob.VerifyProfile(Origin + ECoord - PathDistance, info, context)) 
                continue;
            ReachedEnd = EntityJob.VerifyProfile(Origin + ECoord - PathDistance, info, context, false);
            if(hCost < bestEnd.y || ReachedEnd) bestEnd = new (current.y, hCost);
            if(current.y == pathEndInd || ReachedEnd) break;

            for(int i = 0; i < 24; i++){
                int4 delta = dP[i];
                int3 nCoord = ECoord + delta.xyz;
                if(math.any(nCoord < 0) || math.any(nCoord >= finder.PathMapSize)) continue;
                int FScore = current.x + Get3DDistance(math.abs(End - nCoord)) - hCost + delta.w;
                int dirEnc = (delta.x + 1) * 9 + (delta.y + 1) * 3 + delta.z + 1;
                finder.AddNode(nCoord, FScore, dirEnc);
            }
        }

        byte* path = RetracePath(ref finder, bestEnd.x, startInd, out PathLength);
        finder.Release();
        return path;
    }

    //Find point that matches raw-profile along the path to destination with the closest distance to the desired path distance
    public static byte* FindPathAlongRay(in int3 Origin, ref float3 rayDir, int PathDistance, in Entity.ProfileInfo info, in EntityJob.Context context, out int PathLength){
        PathFinder finder = new (PathDistance);
        int3 End = math.clamp((int3)(CubicNorm(rayDir) * PathDistance) + PathDistance, 0, finder.PathMapSize-1); //We add the distance to make it relative to the start

        //Find the closest point to the end
        int3 pathStart = new (PathDistance, PathDistance, PathDistance);
        int startInd = pathStart.x * finder.PathMapSize * finder.PathMapSize + pathStart.y * finder.PathMapSize + pathStart.z;
        int hCost = Get3DDistance(math.abs(End - pathStart)); 
        int bestEnd = startInd;
    
        finder.AddNode(pathStart, hCost, 13); //13 means dP = (0, 0, 0)
        while(finder.HeapEnd > 1){
            int2 current = finder.RemoveNode();
            int3 ECoord = new (current.y / (finder.PathMapSize * finder.PathMapSize), 
                                current.y / finder.PathMapSize % finder.PathMapSize, 
                                current.y % finder.PathMapSize);
            hCost = Get3DDistance(math.abs(End - ECoord));

            //Always assume the first point is valid
            if(current.y != startInd && !EntityJob.VerifyProfile(Origin + ECoord - PathDistance, info, context)) 
                continue;
            //                 FScore - HScore = GScore
            if(current.x - hCost >= PathDistance * 10){
                rayDir = math.normalize(ECoord - pathStart);
                bestEnd = current.y;
                break;
            } 

            for(int i = 0; i < 24; i++){
                int4 delta = dP[i];
                int3 nCoord = ECoord + delta.xyz;
                if(math.any(nCoord < 0) || math.any(nCoord >= finder.PathMapSize)) continue;
                int FScore = current.x + Get3DDistance(math.abs(End - nCoord)) - hCost + delta.w;
                int dirEnc = (delta.x + 1) * 9 + (delta.y + 1) * 3 + delta.z + 1;
                finder.AddNode(nCoord, FScore, dirEnc);
            }
        }

        byte* path = RetracePath(ref finder, bestEnd, startInd, out PathLength);
        finder.Release();
        return path;
    }



    [BurstCompile]
    public static byte* RetracePath(ref PathFinder finder, int dest, int start, out int PathLength){
        PathLength = 0; 
        int currentInd = dest;
        while(currentInd != start){ 
            PathLength++; 
            byte dir = (byte)finder.MapPtr[currentInd].w;
            currentInd -= ((dir / 9) - 1) * finder.PathMapSize * finder.PathMapSize + 
                            ((dir / 3 % 3) - 1) * finder.PathMapSize + ((dir % 3) - 1);
        }

        byte* path = (byte*)UnsafeUtility.Malloc(PathLength, 4, Allocator.Persistent);
        currentInd = dest; int index = PathLength - 1;
        while(currentInd != start){
            byte dir = (byte)finder.MapPtr[currentInd].w;
            currentInd -= ((dir / 9) - 1) * finder.PathMapSize * finder.PathMapSize + 
                            ((dir / 3 % 3) - 1) * finder.PathMapSize + ((dir % 3) - 1);
            path[index] = dir;
            index--;
        } 
        //First point is always 13(i.e. no move)
        //This is so a path is always returned(i.e. path = null is impossible)
        //path[0] = 13; //13 i.e. 0, 0, 0

        return path;
    }
}
