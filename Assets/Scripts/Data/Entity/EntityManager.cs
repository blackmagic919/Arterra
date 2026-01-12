using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine.Profiling;
using Arterra.Core.Storage;
using System.Threading.Tasks;
using System.Linq;
using Arterra.Configuration;
using Arterra.Configuration.Generation.Entity;
using Arterra.Core.Terrain;
using Arterra.Core.Player;
using Arterra.Core.Terrain.Readback;

public static class EntityManager
{
    private static EntityGenOffsets bufferOffsets;
    private static ComputeShader entityGenShader;
    private static ComputeShader entityTranscriber;
    public static HashSet<Entity> EntityReg;
    public static Dictionary<Guid, Entity> EntityIndex; //tracks location in handler of entityGUID
    public static STree ESTree; //BVH of entities updated synchronously
    public static ConcurrentQueue<Action> HandlerEvents; //Buffered updates to modify entities(since it can't happen while job is executing)
    public static Entity[] CurUpdateEntities;
    private static volatile EntityJob Executor; //Job that updates all entities
    const int MAX_ENTITY_COUNT = 10000; //10k ~ 5mb

    public static void AddHandlerEvent(Action action){
        HandlerEvents.Enqueue(action);
    }

    public static bool TryGetEntity(Guid identifier, out Entity entity){
        return EntityIndex.TryGetValue(identifier, out entity);
    }

    public static void FlushUpdateCycle() {
        while(HandlerEvents.TryDequeue(out Action action)){
            action.Invoke();
        } HandlerEvents.Clear();
        
        if (EntityIndex.Count == 0) return;
        CurUpdateEntities = EntityReg.ToArray();
        foreach (Entity entity in CurUpdateEntities) {
            ESTree.AssertEntityLocation(entity);
        }
    }

    public static void InitializeChunkEntity(GenPoint genInfo, int3 CCoord){
        int mapSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
        int3 GCoord = CCoord * mapSize + genInfo.position;
        Authoring authoring = Config.CURRENT.Generation.Entities.Reg[(int)genInfo.index];
        Entity newEntity = authoring.Entity;
        AddHandlerEvent(() => InitializeE(newEntity, GCoord, genInfo.index));
    }

    public static void CreateEntity(float3 GCoord, uint entityIndex, Entity sEntity = null, Action cb = null) {
        if (sEntity == null) {
            Authoring authoring = Config.CURRENT.Generation.Entities.Reg[(int)entityIndex];
            sEntity = authoring.Entity;
        }
        
        AddHandlerEvent(() => {
            InitializeE(sEntity, GCoord, entityIndex);
            cb?.Invoke();
        });
    }
    public static void DeserializeEntity(Entity sEntity, Action cb = null) => AddHandlerEvent(() => {
        DeserializeE(sEntity);
        cb?.Invoke();
    });
    public static void ReleaseEntity(Guid entityId, Action cb = null) => AddHandlerEvent(() => {
        ReleaseE(entityId);
        cb?.Invoke();
    });

    public static void ReleaseChunkEntities(int3 CCoord, bool await = false) {
        int mapChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
        CPUMapManager.ChunkMapInfo mapInfo = CPUMapManager.AddressDict[CPUMapManager.HashCoord(CCoord)];
        if (!mapInfo.valid) return;
        //mapinfo.CCoord is coord of previous chunk
        Bounds bounds = new(((float3)mapInfo.CCoord + 0.5f) * mapChunkSize, (float3)mapChunkSize);

        HashSet<Entity> Entities = new HashSet<Entity>();
        ESTree.QueryExclusive(bounds, (Entity entity) => {
            if (entity == null) return;
            //This is the only entity that is not serialized and saved
            if (entity.info.entityId == PlayerHandler.data.info.entityId) return;
            ReleaseEntity(entity.info.entityId);
            Entities.Add(entity);
        });
        Task awaitableTask = Task.Run(() => Chunk.SaveEntitiesToJsonAsync(Entities.ToList(), mapInfo.CCoord));
        if (await) awaitableTask.Wait();
    }
    public static void ReleaseE(Guid entityId){
        if(!EntityIndex.ContainsKey(entityId)) {
            return;
        }
        Entity entity = EntityIndex[entityId];

        EntityReg.Remove(entity);
        EntityIndex.Remove(entityId);
        ESTree.Delete(entityId);
        entity.Disable();
        entity.active = false;
    }
    
    public static void InitializeE(Entity nEntity, float3 GCoord, uint entityIndex) {
        Authoring authoring = Config.CURRENT.Generation.Entities.Reg[(int)entityIndex];
        nEntity.info.entityId = Guid.NewGuid();
        nEntity.info.entityType = entityIndex;
        nEntity.active = true;

        EntityReg.Add(nEntity);
        EntityIndex[nEntity.info.entityId] = nEntity;
        nEntity.Initialize(authoring.Setting, authoring.Controller, GCoord);
        ESTree.Insert(nEntity);
    }

    public static void DeserializeE(Entity sEntity){
        var reg = Config.CURRENT.Generation.Entities;
        Authoring authoring = reg.Retrieve((int)sEntity.info.entityType);
        sEntity.active = true;

        EntityReg.Add(sEntity);
        EntityIndex[sEntity.info.entityId] = sEntity;
        sEntity.Deserialize(authoring.Setting, authoring.Controller, out int3 GCoord);
        ESTree.Insert(sEntity);
    }
    public static void DeserializeEntities(List<Entity> entities){
        if(entities == null) return;
        foreach(Entity sEntity in entities){
            DeserializeEntity(sEntity);
        }
    }


    public static void Initialize() {
        ESTree = new STree(MAX_ENTITY_COUNT * 2 + 1);
        EntityReg = new HashSet<Entity>();
        EntityIndex = new Dictionary<Guid, Entity>();
        HandlerEvents = new ConcurrentQueue<Action>();
        Executor = new EntityJob();
        OctreeTerrain.MainFixedUpdateTasks.Enqueue(Executor);
        Indicators.Initialize();

        int numPointsPerAxis = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
        int numPoints = numPointsPerAxis * numPointsPerAxis * numPointsPerAxis;

        bufferOffsets = new EntityGenOffsets(0, numPoints, GenPoint.size);
        entityGenShader = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Entities/EntityIdentifier");
        entityTranscriber = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Entities/EntityTranscriber");

        int kernel = entityGenShader.FindKernel("Identify");
        entityGenShader.SetBuffer(kernel, "chunkEntities", UtilityBuffers.GenerationBuffer);
        entityGenShader.SetBuffer(kernel, "counter", UtilityBuffers.GenerationBuffer);
        entityGenShader.SetBuffer(kernel, "BiomeMap", UtilityBuffers.GenerationBuffer);
        entityGenShader.SetInt("GPConfig", (int)GenPoint.GenType.Entity);
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

        //Ensure the entity dictionary has the player. This is non-negotiable and must always be ensured
        Catalogue<Authoring> EntityDictionary = Config.CURRENT.Generation.Entities;
        if (EntityDictionary.Contains("Player") && EntityDictionary.Retrieve("Player").GetType() != typeof(PlayerStreamer))
            EntityDictionary.TryRemove("Player");
        if (!EntityDictionary.Contains("Player")) {
            PlayerStreamer PlayerEntity = Resources.Load<PlayerStreamer>("Prefabs/GameUI/PlayerEntity");
            EntityDictionary.Add("Player", PlayerEntity);
        }

        Genetics.ClearGeneology();
        for (int i = 0; i < EntityDictionary.Reg.Count; i++) {
            EntityDictionary.Retrieve(i).Setting.Preset((uint)i);
        }
    }

    public static void Release() {
        Executor.Complete();
        foreach (Entity entity in EntityReg) {
            entity.Disable();
        }
        List<Authoring> EntityDictionary = Config.CURRENT.Generation.Entities.Reg;
        foreach (Authoring entity in EntityDictionary) entity.Setting.Unset();
    }

    public static void PlanEntities(AsyncGenInfoReadback readback, int biomeStart, int3 CCoord, int chunkSize){
        int numPointsAxes = chunkSize;
        UtilityBuffers.ClearRange(UtilityBuffers.GenerationBuffer, 3, bufferOffsets.bufferStart);

        int kernel = entityGenShader.FindKernel("Identify");
        entityGenShader.SetInt(ShaderIDProps.StartBiome, biomeStart);
        entityGenShader.SetInt(ShaderIDProps.NumPointsPerAxis, numPointsAxes);
        entityGenShader.SetInts(ShaderIDProps.CCoord, new int[] { CCoord.x, CCoord.y, CCoord.z });
        GPUMapManager.SetCCoordHash(entityGenShader);

        entityGenShader.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);
        entityGenShader.Dispatch(kernel, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);

        kernel = entityGenShader.FindKernel("Prune");
        entityGenShader.SetBuffer(kernel, ShaderIDProps.MemoryBuffer, GPUMapManager.Storage);
        entityGenShader.SetBuffer(kernel, ShaderIDProps.AddressDict, GPUMapManager.Address);

        ComputeBuffer args = UtilityBuffers.CountToArgs(entityGenShader, UtilityBuffers.GenerationBuffer, bufferOffsets.entityCounter, kernel: kernel);
        entityGenShader.DispatchIndirect(kernel, args);

        kernel = entityTranscriber.FindKernel("CSMain");
        int address = readback.AddGenPoints(UtilityBuffers.GenerationBuffer, bufferOffsets.prunedCounter, bufferOffsets.tempCounter);
        entityTranscriber.SetBuffer(kernel, ShaderIDProps.MemoryBuffer, GenerationPreset.memoryHandle.GetBlockBuffer(address));
        entityTranscriber.SetBuffer(kernel, ShaderIDProps.AddressDict, GenerationPreset.memoryHandle.Address);
        entityTranscriber.SetInt(ShaderIDProps.AddressIndex, (int)address);

        args = UtilityBuffers.CountToArgs(entityTranscriber, UtilityBuffers.GenerationBuffer, bufferOffsets.prunedCounter, kernel: kernel);
        entityTranscriber.DispatchIndirect(kernel, args);
    }

    public struct EntityGenOffsets: BufferOffsets{
        public int entityCounter;
        public int prunedCounter;
        public int tempCounter;
        public int entityStart;
        public int prunedStart;
        private int offsetStart; private int offsetEnd;
        public int bufferStart{get{return offsetStart;}} public int bufferEnd{get{return offsetEnd;}}

        public EntityGenOffsets(int bufferStart, int numPoints, int entityStride){
            offsetStart = bufferStart;
            entityCounter = bufferStart;
            prunedCounter = bufferStart + 1;
            tempCounter = bufferStart + 2;
            entityStart = Mathf.CeilToInt((float)(bufferStart+3) / entityStride);
            prunedStart = entityStart + numPoints;
            int prunedEnd_W = (prunedStart + numPoints) * entityStride;
            offsetEnd = prunedEnd_W;
        }
    }


    //More like a BVH, or a dynamic R-Tree
    public struct STree{
        private Dictionary<Guid, uint> SpatialIndex;
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
            tree[0] = new TreeNode(new Bounds(float3.zero, new float3(float.MaxValue)));
            length = 1;
            Root = 0;
        }

        public void AssertEntityLocation(Entity entity){
            Bounds nBounds = new Bounds(entity.position, entity.transform.size);
            AssertEntityLocation(entity.info.entityId, nBounds);
        }
        
        //We allow this option to support objects with multiple bounds
        //by allowing bounds to be registered under an alias id of the entity
        //Every unique bound must have a unique ID, but they can be associated to the same 
        //Entity under EntityIndex
        public void AssertEntityLocation(Guid entityId, Bounds bounds){
            if(!this.Contains(entityId)) return;
            if(Contains(this[this[entityId].Parent].bounds, bounds)){
                this[entityId].bounds = bounds;
                return;
            } 
            this.Delete(entityId);
            this.Insert(bounds, entityId);
        }

        public void Insert(Entity entity){
            float3 colliderSize = entity.transform.size;
            Bounds eEBounds = new (entity.position, colliderSize);
            Insert(eEBounds, entity.info.entityId);
        }

        public void Insert(Bounds bounds, Guid eId){
            if(length+1 >= tree.Length) Resize();
            SpatialIndex[eId] = length;
            length++;

            tree[length - 1] = new TreeNode(bounds, eId);
            if(length == 2) Root = 1;
            else RecursiveInsert(bounds, Root);
        }

        private void RecursiveInsert(Bounds bounds, uint current){
            ref TreeNode node = ref tree[(int)current];
            if(node.IsLeaf){
                tree[(int)length] = new TreeNode{
                    bounds = GetExpand(node.bounds, bounds),
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
            
            node.bounds.Encapsulate(bounds);
            ref Bounds B1 = ref tree[(int)node.Left].bounds; 
            ref Bounds B2 = ref tree[(int)node.Right].bounds; 
            if(Contains(B1, bounds)) RecursiveInsert(bounds, node.Left);
            else if(Contains(B2, bounds)) RecursiveInsert(bounds, node.Right); 
            else{
                Bounds B1Prime, B2Prime;
                B1Prime = GetExpand(B1, bounds);
                B2Prime = GetExpand(B2, bounds);
                //I guarantee there's a configuration that does not intersect
                if(Volume(B1Prime) >= Volume(B2Prime)) RecursiveInsert(bounds, node.Right);
                else RecursiveInsert(bounds, node.Left);
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
        public readonly void Query(Bounds bounds, Action<Entity> action, int current = -1){
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

        public readonly void QueryExclusive(Bounds bounds, Action<Entity> action, int current = -1){
            if(current == -1) current = (int)Root;
            if(current == 0) return; //Root is zero when it is empty
            TreeNode node = tree[current];
            if (node.IsLeaf) {
                Entity entity = node.GetLeaf; 
                //We use entity.position because some entities could have multiple bounds
                if (ContainsExclusive(bounds, entity.position))
                    action.Invoke(entity);
                return;
            }
            
            if(bounds.Intersects(tree[(int)node.Left].bounds)) QueryExclusive(bounds, action, (int)node.Left);
            if (bounds.Intersects(tree[(int)node.Right].bounds)) QueryExclusive(bounds, action, (int)node.Right);
            bool ContainsExclusive(Bounds b, float3 p) {
                float3 Min = b.min, Max = b.max;
                return Max.x > p.x && Max.y > p.y && Max.z > p.z &&
                    Min.x <= p.x && Min.y <= p.y && Min.z <= p.z;
            }
        }

        public readonly void QueryRay(Ray ray, float maxDist, Action<Entity> action, int current = -1){
            if(current == -1) current = (int)Root;
            if(current == 0) return; //Root is zero when it is empty
            TreeNode node = tree[current];
            if(node.IsLeaf){
                action.Invoke(node.GetLeaf);
                return;
            }

            if(tree[(int)node.Left].bounds.IntersectRay(ray, out float dist) && dist <= maxDist) 
                QueryRay(ray, maxDist, action, (int)node.Left);
            if(tree[(int)node.Right].bounds.IntersectRay(ray, out dist) && dist <= maxDist) 
                QueryRay(ray, maxDist, action, (int)node.Right);
        }

        public readonly bool FindClosestAlongRay(float3 startGS, float3 endGS, Guid callerId, out Entity closestHit){
            Ray viewRay = new (startGS, endGS - startGS);
            float cDist = math.distance(startGS, endGS);
            Entity cEntity = null;
            void OnFoundEntity(Entity entity){
                if(entity.info.entityId == callerId) return; //Ignore the caller
                Bounds bounds = new Bounds(entity.position, entity.transform.size);
                bounds.IntersectRay(viewRay, out float dist);
                if(dist <= cDist){
                    cEntity = entity;
                    cDist = dist;
                }
            }

            QueryRay(viewRay, cDist, OnFoundEntity);
            closestHit = cEntity;
            return cEntity != null;
        }

        private void Resize(){
            TreeNode[] newTree = new TreeNode[tree.Length * 2];
            for(int i = 0; i < tree.Length; i++){
                newTree[i] = tree[i];
            } tree = newTree;
        }


        public struct TreeNode {
            public Bounds bounds;
            //64-bit systems guarantee 64-bit copies are atomic for properly aligned elements
            //If 32-bit systems are used, this will not work
            public uint Parent;
            public uint Left;
            public uint Right;
            public Guid ObjId;
            public readonly bool IsLeaf => ObjId != Guid.Empty;
            public readonly Entity GetLeaf {
                get {
                    TryGetEntity(ObjId, out Entity ent);
                    return ent;
                }
            }
            public TreeNode(Bounds bounds){
                this.bounds = bounds;
                Left = 0; Right = 0;
                ObjId = Guid.Empty;
                Parent = 0;
            }

            public TreeNode(Bounds bounds, Guid EntityId){
                this.bounds = bounds;
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
                bounds = GetExpand(tree[Left].bounds, tree[Right].bounds);
            }
        }

        private static Bounds GetExpand(Bounds a, Bounds b){
            Bounds nBounds = new(a.center, a.size);
            nBounds.Encapsulate(b);
            return nBounds;
        }

        private static double Volume(Bounds bounds){
            float3 size = bounds.size;
            return size.x * size.y * size.z;
        }

        private static bool Contains(Bounds src, Bounds qry){
            return src.Contains(qry.min) && src.Contains(qry.max);
        }
    }

    public class AliasManager<TIdentifier> {
        private Dictionary<TIdentifier, LinkedList<TIdentifier>> IdToAliases;
        private Dictionary<TIdentifier, TIdentifier> AliasToId;

        public AliasManager() {
            IdToAliases = new Dictionary<TIdentifier, LinkedList<TIdentifier>>();
            AliasToId = new Dictionary<TIdentifier, TIdentifier>();
        }

        public void RegisterAlias(TIdentifier Base, TIdentifier Alias, Bounds bounds) {
            if(!AliasToId.TryAdd(Alias, Base)) return;
            if (IdToAliases.TryGetValue(Base, out LinkedList<TIdentifier> aliases)) {
                aliases.AddLast(Alias);
            } else {
                aliases = new LinkedList<TIdentifier>();
                aliases.AddLast(Alias);
                IdToAliases.Add(Base, aliases);
            }
        }

        public void ReleaseAlias(TIdentifier Alias) {
            if(!AliasToId.TryGetValue(Alias, out TIdentifier entityId)) return;
            AliasToId.Remove(Alias);
            if(!IdToAliases.TryGetValue(entityId, out LinkedList<TIdentifier> aliases))
                return;
            aliases.Remove(Alias);
        }

        public void ReleaseBase(TIdentifier Base) {
            if(!IdToAliases.TryGetValue(Base, out LinkedList<TIdentifier> aliases)) return;
            foreach(TIdentifier alias in aliases) AliasToId.Remove(alias);
            IdToAliases.Remove(Base);
        }

        public bool TryFindBase(TIdentifier Alias, out TIdentifier Base) => AliasToId.TryGetValue(Alias, out Base);
    }
}

public class EntityJob : IUpdateSubscriber{
    //Always active
    public bool Active {
        get => true;
        set { return; }
    }
    public bool dispatched = false;
    private JobHandle handle;
    public static Context cxt;
    private float accumulatedTime;
    

    public unsafe EntityJob(){
        dispatched = false;
        accumulatedTime = 0;
        cxt = new Context{
            Profile = (ProfileE*)GenerationPreset.entityHandle.entityProfileArray.GetUnsafePtr(),
            mapContext = new CPUMapManager.MapContext{
                MapData = (MapData*)CPUMapManager.SectionedMemory.GetUnsafePtr(),
                AddressDict = (CPUMapManager.ChunkMapInfo*)CPUMapManager.AddressDict.GetUnsafePtr(),
                mapChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize,
                numChunksAxis = CPUMapManager.numChunksAxis,
                IsoValue = (int)Math.Round(Config.CURRENT.Quality.Terrain.value.IsoLevel * 255.0)
            },

            gravity = Physics.gravity / Config.CURRENT.Quality.Terrain.value.lerpScale,
            deltaTime = 0
        };
    }


    public bool Complete(){
        if(!dispatched) return true;
        if(!handle.IsCompleted) return false; 
        handle.Complete();
        dispatched = false;
        return true;
    }

    public void Update(MonoBehaviour mono){
        accumulatedTime += Time.fixedDeltaTime;
        if (!Complete()) return;
        cxt.totDeltaTime = accumulatedTime;
        cxt.deltaTime = Time.fixedDeltaTime;
        accumulatedTime = 0;

        EntityManager.FlushUpdateCycle();
        handle = cxt.Schedule(EntityManager.CurUpdateEntities.Length, 16);
        dispatched = true;
    }

    public struct Context: IJobParallelFor{
        [NativeDisableUnsafePtrRestriction]
        [ReadOnly] public unsafe ProfileE* Profile;
        [ReadOnly] public CPUMapManager.MapContext mapContext;
        [ReadOnly] public float3 gravity;
        [ReadOnly] public float deltaTime;
        [ReadOnly] public float totDeltaTime;
        public unsafe void Execute(int index){
            Profiler.BeginSample(EntityManager.CurUpdateEntities[index].GetType().ToString());
            EntityManager.CurUpdateEntities[index].Update();
            Profiler.EndSample();
        }
    }

}