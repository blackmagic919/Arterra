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
using System.Threading;
using System.Linq;
using Arterra.Configuration;
using Arterra.Data.Entity;
using Arterra.Data.Entity.Behavior;
using Arterra.Engine.Terrain;
using Arterra.GamePlay;
using Arterra.Engine.Terrain.Readback;
using Arterra.Utils;
using Arterra.Data.Structure;
using Arterra.Core;
using static Arterra.Core.Storage.SharedResourceManager;
using Arterra.Core.Network;

public static class EntityManager
{
    public static HashSet<Entity> EntityReg;
    public static Dictionary<Guid, int3> EntitySubCCoord; //Trakcs the last chunk coordinate the chunk pushed to listeners
    public static Dictionary<Guid, Entity> EntityIndex; //tracks location in handler of entityGUID
    public static ConcurrentQueue<Action> HandlerEvents; //Buffered updates to modify entities(since it can't happen while job is executing)
    public static STree ESTree; //BVH of entities updated synchronously
    public static Entity[] CurUpdateEntities;
    private static volatile EntityJob Executor; //Job that updates all entities
    const int MAX_ENTITY_COUNT = 10000; //10k ~ 5mb

    public static void AddHandlerEvent(Action action){
        HandlerEvents.Enqueue(action);
    }

    public static bool TryGetEntity(Guid identifier, out Entity entity){
        entity = null;
        return EntityIndex != null && EntityIndex.TryGetValue(identifier, out entity);
    }

    public static void InitializeChunkEntity(GenPoint genInfo, int3 CCoord, byte cxt){
        if ((AsyncGenInfoReadback.CREATE_ENTITIES & cxt) == 0) return; //No Gen
        int mapSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
        int3 GCoord = CCoord * mapSize + genInfo.position;
        CreateEntity(GCoord, genInfo.index);
    }

    public static Entity CreateEntity(float3 GCoord, uint entityIndex, Entity sEntity = null, Action cb = null,
        Action<Exception> onError = null, CancellationToken token = default) {
        if (sEntity == null) {
            Authoring authoring = Config.CURRENT.Generation.Entities.Reg[(int)entityIndex];
            sEntity = authoring.Entity;
        }

        AddHandlerEvent(() => {
            try {
                token.ThrowIfCancellationRequested();
                InitializeE(sEntity, GCoord, entityIndex);
                cb?.Invoke();
            } catch (Exception exception) {
                if (onError == null) throw;
                onError(exception);
            }
        });
        return sEntity;
    }
    public static Entity DeserializeEntity(Entity sEntity, Action cb = null,
        Action<Exception> onError = null, CancellationToken token = default){
        EntityNetworkTrace.Record(sEntity.info.rtEntityId, "DeserializeEntity queued for deferred initialization");
        AddHandlerEvent(() => {
            try {
                EntityNetworkTrace.Record(sEntity.info.rtEntityId, $"Deferred initialization executing; canceled={token.IsCancellationRequested}");
                token.ThrowIfCancellationRequested();
                DeserializeE(sEntity);
                EntityNetworkTrace.Record(sEntity.info.rtEntityId, "Deferred initialization completed");
                cb?.Invoke();
            } catch (Exception exception) {
                EntityNetworkTrace.Record(sEntity.info.rtEntityId, $"Deferred initialization failed: {exception}");
                if (onError == null) throw;
                onError(exception);
            }
        });
        return sEntity;
    }

    public static async Task<Entity> CreateEntityAsync(float3 coordinate, uint entityIndex,
        Entity entity = null, CancellationToken token = default) {
        var completion = new TaskCompletionSource<Entity>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = token.Register(() => completion.TrySetCanceled());
        entity = CreateEntity(coordinate, entityIndex, entity,
            () => completion.TrySetResult(entity), error => completion.TrySetException(error), token);
        return await completion.Task;
    }

    public static async Task<Entity> DeserializeEntityAsync(Entity entity, CancellationToken token = default) {
        var completion = new TaskCompletionSource<Entity>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = token.Register(() => completion.TrySetCanceled());
        DeserializeEntity(entity, () => completion.TrySetResult(entity),
            error => completion.TrySetException(error), token);
        return await completion.Task;
    }

    public static void ReleaseEntity(Guid entityId, Action cb = null) {
        if(!TryGetEntity(entityId, out Entity entity))
            return;
        //Mark as inactive IMMEDIATELY so it doesn't float around as a zombie for a while
        entity.active = false;

        AddHandlerEvent(() => {
            ReleaseE(entityId);
            cb?.Invoke();
        });
    }

    public static void CompleteJobs() => Executor?.Complete();

    public static Task InitializeChunkEntitiesAsync(List<Entity> entities, CancellationToken token = default) {
        if (entities == null) return Task.CompletedTask;
        var tasks = new List<Task>(entities.Count);
        foreach (var entity in entities) tasks.Add(DeserializeEntityAsync(entity, token));
        return Task.WhenAll(tasks);
    }

    public static void FlushPendingHandlers() {
        CompleteJobs();
        if (HandlerEvents == null) return;
        while (HandlerEvents.TryDequeue(out Action action)) action();
    }

    public static async Task ReleaseChunkEntitiesAsync(int3 coordinate, bool save = true) {
        if (ESTree.tree == null) return;
        var entities = GetChunkEntities(coordinate);
        entities.RemoveAll(entity => entity.IsOwner || (!PlayerHandler.Players.IsEmpty && PlayerHandler.Players.Values.Contains(entity)));
        if (entities.Count != 0) {
            var released = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            foreach (var entity in entities) ReleaseEntity(entity.info.rtEntityId);
            // The FIFO handler queue runs after the current entity job completes.
            AddHandlerEvent(() => released.TrySetResult(true));
            await released.Task;
        }
        if (save) await Chunk.SaveEntitiesToJsonAsync(entities, coordinate);
    }
    public static void ReleaseE(Guid entityId){
        EntityNetworkTrace.Record(entityId, $"ReleaseE invoked; exists={EntityIndex.ContainsKey(entityId)}");
        if(!EntityIndex.TryGetValue(entityId, out Entity entity))
            return;

        entity.active = false;
        if (entity.IsAuthority && EntitySubCCoord.TryGetValue(entityId, out var coordinate)
            && CPUMapManager.TryGetChunkInfo(coordinate, out var chunk)) {
            foreach (var driver in chunk.Generation.GetDriverSet()) driver.Source.LeaveEntity(entityId);
        }
        EntityReg.Remove(entity);
        EntityIndex.Remove(entityId);
        EntityNetworkTrace.Record(entityId, "ReleaseE removed entity");
        ESTree.Delete(entityId);
        EntitySubCCoord.Remove(entityId);
        entity.Disable();
        EntityNetworkTrace.Record(entityId, "Entity Disable completed");
    }

    private static void InitializeE(Entity nEntity, float3 GCoord, uint entityIndex) {
        Authoring authoring = Config.CURRENT.Generation.Entities.Reg[(int)entityIndex];
        nEntity.info.rtEntityId = Guid.NewGuid();
        EntityNetworkTrace.Record(nEntity.info.rtEntityId, $"Create entity initialization starts; type={entityIndex}, position={GCoord}");
        nEntity.info.entityType = entityIndex;
        nEntity.active = true;

        EntityIndex.Add(nEntity.info.rtEntityId, nEntity);
        EntityReg.Add(nEntity);
        nEntity.Initialize(authoring.Setting, authoring.Controller, GCoord);
        nEntity.transform.Register(nEntity);
        ESTree.Insert(nEntity);
        InitializeLocalVisual(nEntity);
        EntityNetworkTrace.Record(nEntity.info.rtEntityId, "Create entity initialization completed");
    }

    private static void DeserializeE(Entity sEntity){
        if (sEntity == null) throw new System.ArgumentNullException(nameof(sEntity));
        if (sEntity.info.rtEntityId == Guid.Empty)
            throw new ArgumentException("A loaded entity must have its shared runtime ID.", nameof(sEntity));
        // Initial snapshots may overlap incremental enters. Never initialize a live ID twice.
        if (EntityIndex.ContainsKey(sEntity.info.rtEntityId)) {
            EntityNetworkTrace.Record(sEntity.info.rtEntityId, "Initialization skipped: entity ID already exists");
            return;
        }
        var reg = Config.CURRENT.Generation.Entities;
        Authoring authoring = reg.Retrieve((int)sEntity.info.entityType);
        sEntity.active = true;

        EntityIndex.Add(sEntity.info.rtEntityId, sEntity);
        EntityReg.Add(sEntity);
        sEntity.Deserialize(authoring.Setting, authoring.Controller, out _);
        sEntity.transform.Register(sEntity);
        ESTree.Insert(sEntity);
        InitializeLocalVisual(sEntity);
    }

    private static void InitializeLocalVisual(Entity entity) {
        if (!entity.IsAuthority || (ChunkGenerationDriver.Local != null
            && CPUMapManager.TryGetChunkInfo(GetChunkCoordinate(entity.position), out var chunk)
            && chunk.Generation.GetDriverSet().Contains(ChunkGenerationDriver.Local)))
            entity.InitializeVisual();
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
        EntitySubCCoord = new Dictionary<Guid, int3>();
        HandlerEvents = new ConcurrentQueue<Action>();
        Executor = new EntityJob();
        ArterraRuntime.MainFixedUpdateTasks.Enqueue(Executor);
        Indicators.Initialize();

        EnsurePlayerEntity();

        Catalogue<Authoring> EntityDictionary = Config.CURRENT.Generation.Entities;
        for (int i = 0; i < EntityDictionary.Reg.Count; i++) {
            EntityDictionary.Retrieve(i).Setting.Preset((uint)i);
        }
    }

    //Ensure the entity dictionary has a properly created player. This is non-negotiable and must always be ensured
    private static void EnsurePlayerEntity() {
        Catalogue<Authoring> EntityDictionary = Config.CURRENT.Generation.Entities;

        if (EntityDictionary.Contains("Player") && EntityDictionary.Retrieve("Player") is not BehaviorEntity)
            EntityDictionary.TryRemove("Player");
        if (!EntityDictionary.Contains("Player")) EntityDictionary.Add("Player", CreateBehaviorPlayerAuthoring());
        BehaviorEntity playerAuthoring = EntityDictionary.Retrieve("Player") as BehaviorEntity;

        ref var gameplaySettings = ref Config.CURRENT.GamePlay;
        if (gameplaySettings.PlayerSettings.value.value == null) {
            gameplaySettings.PlayerSettings.value = PlayerBehavior.DefaultPlayerAnimalSetting;
            gameplaySettings.PlayerSettings.IsDirty = true;
        }

        Arterra.Configuration.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain.value;
        BehaviorEntity.AnimalSetting setting = gameplaySettings.PlayerSettings.value;

        uint size = setting.profile.bounds.x * setting.profile.bounds.y * setting.profile.bounds.z;
        playerAuthoring.Profile.value = new List<ProfileE>(new ProfileE[size]);
        List<ProfileE> playerProfile = playerAuthoring.Profile.value;
        playerAuthoring._Setting.value = setting;
        for(int x = 0; x < setting.profile.bounds.x; x++) {
        for(int y = 0; y < setting.profile.bounds.y; y++) {
        for(int z = 0; z < setting.profile.bounds.z; z++) {
            int3 coord = new (x, y, z);
            ProfileE profile = default;
            StructureData.CheckInfo check = default;
            if (math.any(coord == 0)) {
                check.MinLiquid = 0; check.MaxLiquid = (uint)MapData.MaxDensity;
                check.MinSolid = (uint)(MapData.MaxDensity * rSettings.IsoLevel);
                check.MaxSolid = (uint)MapData.MaxDensity;
                profile.flags = ProfileE.OR;
            } else {
                check.MinLiquid = 0; check.MinSolid = 0;
                check.MaxLiquid = (uint)MapData.MaxDensity;
                check.MaxSolid = (uint)(MapData.MaxDensity * rSettings.IsoLevel);
                profile.flags = ProfileE.AND;
            } profile.bounds = check;
            int index = CustomUtility.irregularIndexFromCoord(coord, new int2(y, z));
            playerProfile[index] = profile;
        }}} //Construct profile from size

        static BehaviorEntity CreateBehaviorPlayerAuthoring() {
            BehaviorEntity behaviorAuthoring = ScriptableObject.CreateInstance<BehaviorEntity>();
            behaviorAuthoring.Name = "Player";

            behaviorAuthoring._Setting.value = Config.CURRENT != null
                && Config.CURRENT.GamePlay.PlayerSettings.value.value != null
                ? Config.CURRENT.GamePlay.PlayerSettings.value
                : PlayerBehavior.DefaultPlayerAnimalSetting;
            behaviorAuthoring.Controller.value = Resources.Load<GameObject>("Prefabs/Player/PlayerController");

            return behaviorAuthoring;
        }
    }

    public static void Release() {
        Executor?.Complete();
        foreach (Entity entity in EntityReg) {
            entity.active = false;
            entity.Disable();
        }
        EntitySubCCoord.Clear();
        EntityIndex.Clear();
        EntityReg.Clear();
        HandlerEvents.Clear();
        CurUpdateEntities = Array.Empty<Entity>();
        List<Authoring> EntityDictionary = Config.CURRENT.Generation.Entities.Reg;
        foreach (Authoring entity in EntityDictionary) entity.Setting.Unset();
    }

    public static void FlushUpdateCycle() {
        while(HandlerEvents.TryDequeue(out Action action)){
            action.Invoke();
        }

        if (EntityIndex.Count == 0) { CurUpdateEntities = Array.Empty<Entity>(); return; }
        CurUpdateEntities = EntityReg.ToArray();
        foreach (Entity entity in CurUpdateEntities) {
            ESTree.AssertEntityLocation(entity);
            UpdateSubscribedListeners(entity);
            entity.UpdateJobSync();
        }
    }

    private static void UpdateSubscribedListeners(Entity entity) {
        //No listeners if not acting server
        if (!NetworkManager.IsActingServer) return; 
        bool hasPrev; Guid entityId = entity.info.rtEntityId;
        if (!EntitySubCCoord.TryGetValue(entityId, out int3 pCCoord))
            hasPrev = false;
        else hasPrev = true;

        int3 CCoord = GetChunkCoordinate(entity.position);
        if (hasPrev && math.all(pCCoord == CCoord)) return;

        HashSet<ChunkGenerationDriver> oldDrivers;
        HashSet<ChunkGenerationDriver> newDrivers;
        if (hasPrev && CPUMapManager.TryGetChunkInfo(pCCoord, out CPUMapManager.ChunkMapInfo info))
            oldDrivers = info.Generation?.GetDriverSet() ?? new HashSet<ChunkGenerationDriver>();
        else oldDrivers = new HashSet<ChunkGenerationDriver>();
        if (CPUMapManager.TryGetChunkInfo(CCoord, out info)) 
            newDrivers = info.Generation?.GetDriverSet() ?? new HashSet<ChunkGenerationDriver>();
        else newDrivers = new HashSet<ChunkGenerationDriver>();

        IEnumerable<ChunkGenerationDriver> enteringDrivers = newDrivers.Except(oldDrivers);
        IEnumerable<ChunkGenerationDriver> leavingDrivers = oldDrivers.Except(newDrivers);
        foreach(ChunkGenerationDriver driver in enteringDrivers) {
            driver.Source.EnterEntity(entity);
        } foreach(ChunkGenerationDriver driver in leavingDrivers) {
            driver.Source.LeaveEntity(entityId);
        }
        EntitySubCCoord[entityId] = CCoord;
    }

    public static int3 GetChunkCoordinate(float3 position) => CPUMapManager.GSToCS((int3)math.floor(position));

    /// <summary>Fresh origin-based chunk membership.</summary>
    public static List<Entity> GetChunkEntities(int3 coordinate) {
        int size = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
        var result = new HashSet<Entity>();
        if (ESTree.tree == null) return new List<Entity>();
        ESTree.QueryExclusive(new Bounds(((float3)coordinate + 0.5f) * size, (float3)size), candidate => {
            // Spatial aliases resolve to their root before membership is checked.
            if (candidate == null || !TryGetEntity(candidate.info.rtEntityId, out var entity) || !entity.active) return;
            if (math.all(GetChunkCoordinate(entity.position) == coordinate)) result.Add(entity);
        });
        return result.ToList();
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
                if(SpatialIndex.TryGetValue(entityId, out uint index))
                    return ref tree[index];
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
            AssertEntityLocation(entity.info.rtEntityId, nBounds);
        }

        //We allow this option to support objects with multiple bounds
        //by allowing bounds to be registered under an alias id of the entity
        //Every unique bound must have a unique ID, but they can be associated to the same
        //Entity under EntityIndex
        public void AssertEntityLocation(Guid entityId, Bounds bounds){
            if(!SpatialIndex.TryGetValue(entityId, out uint index)) return;
            ref TreeNode node = ref tree[index];
            if(Contains(tree[node.Parent].bounds, bounds)){
                node.bounds = bounds;
                return;
            }
            this.Delete(entityId);
            this.Insert(bounds, entityId);
        }

        public void Insert(Entity entity){
            float3 colliderSize = entity.transform.size;
            Bounds eEBounds = new (entity.position, colliderSize);
            Insert(eEBounds, entity.info.rtEntityId);
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
            if(!SpatialIndex.TryGetValue(entityId, out uint spatialIndex)) return;
            int index = (int)spatialIndex;
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
                if (entity != null && ContainsExclusive(bounds, entity.position))
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

        public readonly bool FindClosestAlongRay(float3 startGS, float3 endGS, Guid callerId, out Entity closestHit, out float dist){
            Ray viewRay = new (startGS, endGS - startGS);
            float cDist = math.distance(startGS, endGS);
            Entity cEntity = null;
            void OnFoundEntity(Entity entity){
                if(entity.info.rtEntityId == callerId) return; //Ignore the caller
                Bounds bounds = new Bounds(entity.position, entity.transform.size);
                bounds.IntersectRay(viewRay, out float dist);
                if(dist <= cDist){
                    cEntity = entity;
                    cDist = dist;
                }
            }

            QueryRay(viewRay, cDist, OnFoundEntity);
            closestHit = cEntity;
            dist = cDist;

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

public class EntityJob : Arterra.Core.ArterraRuntime.IUpdateSubscriber{
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

            gravity = Physics.gravity / Config.CURRENT.Quality.Terrain.value.lerpScale,
            deltaTime = 0,
            UpdateCount = 0,
        };
    }


    public bool TryComplete(){
        if(!dispatched) return true;
        if(!handle.IsCompleted) return false;
        Complete();
        return true;
    }

    public void Complete(){
        handle.Complete();
        dispatched = false;
    }

    public void Update(MonoBehaviour mono){
        accumulatedTime += Time.fixedDeltaTime;
        if (!TryComplete()) return;
        cxt.totDeltaTime = accumulatedTime;
        cxt.deltaTime = Time.fixedDeltaTime;
        cxt.UpdateCount++;
        accumulatedTime = 0;

        EntityManager.FlushUpdateCycle();
        if (!NetworkManager.IsActingServer) return;
        if (EntityManager.CurUpdateEntities == null || EntityManager.CurUpdateEntities.Length == 0) return;
        handle = cxt.Schedule(EntityManager.CurUpdateEntities.Length, 16);
        dispatched = true;
    }

    public struct Context: IJobParallelFor{
        [NativeDisableUnsafePtrRestriction]
        [ReadOnly] public unsafe ProfileE* Profile;
        [ReadOnly] public float3 gravity;
        [ReadOnly] public float deltaTime;
        [ReadOnly] public float totDeltaTime;
        [ReadOnly] public int UpdateCount;
        public void Execute(int index){
            Profiler.BeginSample(EntityManager.CurUpdateEntities[index].GetType().ToString());
            EntityManager.CurUpdateEntities[index].Update();
            Profiler.EndSample();
        }
    }

}
