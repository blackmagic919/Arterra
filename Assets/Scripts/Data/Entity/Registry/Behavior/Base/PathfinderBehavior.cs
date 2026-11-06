using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.Data.Structure;
using Unity.Mathematics;
using UnityEngine;
using static Arterra.Data.Entity.EntitySetting;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Arterra.Data.Entity.Behavior {
    public class PathFinderBehavior : SpeciesBehavior {
        public PathInfo pathFinder;
        private BehaviorEntity.Animal self;
        private Movement movement; // Optional: pathfinding also supports entities without movement settings.
        private MMove mmove;
        private Dictionary<EntitySMTasks, long> TaskBouncer;
        private long updateCycles;
        const double CyclesPerNode = 0.0025f;
        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            pathFinder.hasPath = false;
            TaskBouncer = new ();
            updateCycles = 0;
            this.self = self;
            if (!setting.Is(out movement)) movement = null;
            if (!setting.Is(out mmove)) mmove = null;
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            TaskBouncer = new ();
            updateCycles = 0;
            this.self = self;
            if (!setting.Is(out movement)) movement = null;
            if (!setting.Is(out mmove)) mmove = null;
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context != BehaviorEntity.UpdateContext.Simulation) return;
            updateCycles++;
        }

        public override void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
        

        public override void OnDrawGizmos(BehaviorEntity.Animal self) {
            PathInfo finder = pathFinder; //copy so we don't modify the original
            /*if (finder.hasPath && finder.path != null
                && finder.currentInd >= 0 && finder.currentInd < finder.path.Length) {
                // Show the actual steering target separately from the remaining grid path.
                // Both endpoints use the same center offset; pathfinding still uses origins.
                var collider = self.PathCollider;
                float3 centerOffset = collider.transform.size * 0.5f;
                float3 position = (float3)collider.transform.position;
                int3 next = finder.currentPos + finder.path[finder.currentInd].Delta;
                Color previousColor = Gizmos.color;
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(CPUMapManager.GSToWS(position + centerOffset),
                    CPUMapManager.GSToWS((float3)next + centerOffset));
                Gizmos.DrawWireSphere(CPUMapManager.GSToWS((float3)next + centerOffset), 0.2f);
                Gizmos.color = previousColor;
#if UNITY_EDITOR
                float3 error = (float3)next - position;
                float3 velocity = collider.transform.velocity;
                UnityEditor.Handles.Label(CPUMapManager.GSToWS(position + centerOffset),
                    $"Path {finder.currentInd}/{finder.path.Length}, ticks {finder.stepDuration:0}\n"
                    + $"Waypoint error X: {error.x:F2}, Y: {error.y:F2}, Z: {error.z:F2}\n"
                    + $"Velocity X: {velocity.x:F2}, Y: {velocity.y:F2}, Z: {velocity.z:F2}\n"
                    + $"Body gravity: {collider.UseGravity}");
#endif
            }*/
            if (finder.hasPath) {
                int ind = finder.currentInd;
                while (ind != finder.path.Length) {
                    int3 dest = finder.currentPos + finder.path[ind].Delta;
                    Gizmos.DrawLine(CPUMapManager.GSToWS(finder.currentPos),
                                    CPUMapManager.GSToWS(dest));
                    finder.currentPos = dest;
                    ind++;
                }
            }
        }

        /// <summary> Resolves a task's path origin once for both searching and installing its path.
        /// Missing movement settings, non-planar movement, or a failed ground cast use the collider origin. </summary>
        public int3 GetPathOrigin(EntitySMTasks task) {
            int3 origin = (int3)math.floor(self.PathCollider.transform.position);
            if (movement == null || MMove.MovementType(mmove, task) != Movement.FollowType.Planar)
                return origin;
            if (Movement.TryGetShadowOrigin(self.PathCollider, -1, out float shadowY))
                origin.y = (int)math.floor(shadowY);
            return origin;
        }

        public void SetPath(int3 origin, PathData path) {
            pathFinder = new PathInfo(origin, path);
        }

        public bool FindPath(
            EntitySMTasks task,
            in int3 Origin, in int3 iEnd,
            int PathDistance, in ProfileInfo info,
            in EntityJob.Context context, out PathData path
        ) {
            path = null;
            if (TaskBouncer.TryGetValue(task, out long blockTime))
                if (blockTime > updateCycles) return false;
            path = SearchPath(Origin, iEnd, PathDistance, info, context, out int cost);

            int deltaTime = (int)(cost * CyclesPerNode);
            blockTime = updateCycles + deltaTime;
            TaskBouncer[task] = blockTime;
            return true;
        }

        public bool FindMatchAlongRay (
            EntitySMTasks task,
            in int3 Origin, in float3 rayDir,
            int PathDistance, in ProfileInfo info,
            in ProfileInfo dest, in EntityJob.Context context,
            out bool ReachedEnd, out PathData path
        ) {
            path = null; ReachedEnd = false;
            if (TaskBouncer.TryGetValue(task, out long blockTime))
                if (blockTime > updateCycles) return false;
            path = SearchMatchAlongRay(Origin, rayDir, PathDistance, info, dest, context, out int cost, out ReachedEnd);

            int deltaTime = (int)(cost * CyclesPerNode);
            blockTime = updateCycles + deltaTime;
            TaskBouncer[task] = blockTime;
            return true;
        }

        public bool FindPathAlongRay(
            EntitySMTasks task,
            in int3 Origin, ref float3 rayDir,
            int PathDistance, in ProfileInfo info,
            in EntityJob.Context context, out PathData path
        ) {
            path = null;
            if (TaskBouncer.TryGetValue(task, out long blockTime))
                if (blockTime > updateCycles) return false;
            path = SearchPathAlongRay(Origin, ref rayDir, PathDistance, info, context, out int cost);

            int deltaTime = (int)(cost * CyclesPerNode);
            blockTime = updateCycles + deltaTime;
            TaskBouncer[task] = blockTime;
            return true;
        }

        public bool FindPath(
            EntitySMTasks task,
            in int3 Origin, in int3 iEnd,
            int PathDistance, uint3 bounds,
            List<MatProfileE> matProfile, out PathData path
        ) {
            path = null;
            if (TaskBouncer.TryGetValue(task, out long blockTime))
                if (blockTime > updateCycles) return false;
            path = SearchPath(Origin, iEnd, PathDistance, bounds, matProfile, out int cost);

            int deltaTime = (int)(cost * CyclesPerNode);
            blockTime = updateCycles + deltaTime;
            TaskBouncer[task] = blockTime;
            return true;
        }

        public bool FindMatchAlongRay(
            EntitySMTasks task,
            in int3 Origin, in float3 rayDir,
            int PathDistance, uint3 mBounds,
            List<MatProfileE> mProf, uint3 dBounds,
            List<MatProfileE> destProf,
            out bool ReachedEnd, out PathData path
        ) {
            path = null; ReachedEnd = false;
            if (TaskBouncer.TryGetValue(task, out long blockTime))
                if (blockTime > updateCycles) return false;
            path = SearchMatchAlongRay(Origin, rayDir, PathDistance, mBounds, mProf, dBounds, destProf, out int cost, out ReachedEnd);

            int deltaTime = (int)(cost * CyclesPerNode);
            blockTime = updateCycles + deltaTime;
            TaskBouncer[task] = blockTime;
            return true;
        }

        public bool FindPathAlongRay(
            EntitySMTasks task,
            in int3 Origin, ref float3 rayDir,
            int PathDistance, uint3 mBounds,
            List<MatProfileE> mProf, out PathData path
        ) {
            path = null;
            if (TaskBouncer.TryGetValue(task, out long blockTime))
                if (blockTime > updateCycles) return false;
            path = SearchPathAlongRay(Origin, ref rayDir, PathDistance, mBounds, mProf, out int cost);

            int deltaTime = (int)(cost * CyclesPerNode);
            blockTime = updateCycles + deltaTime;
            TaskBouncer[task] = blockTime;
            return true;
        }

        public bool FindPathOrApproachTarget(
            EntitySMTasks task,
            in int3 Origin, in int3 iEnd,
            int PathDistance, in ProfileInfo info,
            in EntityJob.Context context, out PathData path
        ) {
            path = null;
            if (TaskBouncer.TryGetValue(task, out long blockTime))
                if (blockTime > updateCycles) return false;
            path = SearchPathOrApproachTarget(Origin, iEnd, PathDistance, info, context, out int cost);

            int deltaTime = (int)(cost * CyclesPerNode);
            blockTime = updateCycles + deltaTime;
            TaskBouncer[task] = blockTime;
            return true;
        }

        public bool FindPathOrApproachTarget(
            EntitySMTasks task,
            in int3 Origin, in int3 iEnd,
            int PathDistance, uint3 mBounds,
            List<MatProfileE> mProf, out PathData path
        ) {
            path = null;
            if (TaskBouncer.TryGetValue(task, out long blockTime))
                if (blockTime > updateCycles) return false;
            path = SearchPathOrApproachTarget(Origin, iEnd, PathDistance, mBounds, mProf, out int pathLength);

            int deltaTime = (int)(pathLength * CyclesPerNode);
            blockTime = updateCycles + deltaTime;
            TaskBouncer[task] = blockTime;
            return true;
        }

        protected virtual PathData SearchPath(in int3 origin, in int3 end, int distance, in ProfileInfo profile, in EntityJob.Context context, out int cost)
            => FindPath(origin, end, distance, profile, context, out cost);

        protected virtual PathData SearchPath(in int3 origin, in int3 end, int distance, uint3 bounds, List<MatProfileE> profile, out int cost)
            => FindPath(origin, end, distance, bounds, profile, out cost);

        protected virtual PathData SearchMatchAlongRay(in int3 origin, in float3 direction, int distance, in ProfileInfo profile, in ProfileInfo target, in EntityJob.Context context, out int cost, out bool reached)
            => FindMatchAlongRay(origin, direction, distance, profile, target, context, out cost, out reached);

        protected virtual PathData SearchMatchAlongRay(in int3 origin, in float3 direction, int distance, uint3 bounds, List<MatProfileE> profile, uint3 targetBounds, List<MatProfileE> target, out int cost, out bool reached)
            => FindMatchAlongRay(origin, direction, distance, bounds, profile, targetBounds, target, out cost, out reached);

        protected virtual PathData SearchPathAlongRay(in int3 origin, ref float3 direction, int distance, in ProfileInfo profile, in EntityJob.Context context, out int cost)
            => FindPathAlongRay(origin, ref direction, distance, profile, context, out cost);

        protected virtual PathData SearchPathAlongRay(in int3 origin, ref float3 direction, int distance, uint3 bounds, List<MatProfileE> profile, out int cost)
            => FindPathAlongRay(origin, ref direction, distance, bounds, profile, out cost);

        protected virtual PathData SearchPathOrApproachTarget(in int3 origin, in int3 end, int distance, in ProfileInfo profile, in EntityJob.Context context, out int cost)
            => FindPathOrApproachTarget(origin, end, distance, profile, context, out cost);

        protected virtual PathData SearchPathOrApproachTarget(in int3 origin, in int3 end, int distance, uint3 bounds, List<MatProfileE> profile, out int cost)
            => FindPathOrApproachTarget(origin, end, distance, bounds, profile, out cost);

        public static PathData GetStraightLinePath(int3 p1, int3 p2) {
            if (p1.Equals(p2)) return PathData.Empty;

            int3 cur = p1;
            List<byte> path = new ();
            while (!cur.Equals(p2)) {
                int3 diff = p2 - cur;
                int3 step = new int3(math.sign(diff.x), math.sign(diff.y), math.sign(diff.z));
                byte enc = (byte)((step.x + 1) * 9 + (step.y + 1) * 3 + step.z + 1);
                path.Add(enc);
                cur += step;
            }

            return new VoxelPath(path.ToArray());
        }

        [Serializable]
        public struct MatProfileE {
            public ProfileE profile;
            public TagRegistry.Tags matTag;

            public unsafe static (List<MatProfileE>, uint3) ToMatProfile(in ProfileInfo info, in EntityJob.Context context, TagRegistry.Tags tag = TagRegistry.Tags.None) {
                int count = (int)(info.bounds.x * info.bounds.y * info.bounds.z);
                MatProfileE[] profile = new MatProfileE[count];
                for (int i = 0; i < count; i++) {
                    profile[i] = new MatProfileE {
                        profile = context.Profile[i + info.profileStart],
                        matTag = tag,
                    };
                } return (profile.ToList(), info.bounds);
            }
        }

        [Serializable]
        public struct PathStep {
            public int3 Delta;
            // -1 identifies a step produced by the original adjacent-voxel pathfinder.
            public int Group;
            public int TransitionIndex;
            public int NextGroup;
            public Optional<TransitionCheck> Check;
            public Optional<TransitionMovement> Movement;

            public static PathStep FromDirection(byte direction) => new PathStep {
                Delta = new int3(direction / 9 - 1, direction / 3 % 3 - 1, direction % 3 - 1),
                Group = -1,
                TransitionIndex = -1,
                NextGroup = -1
            };
        }

        public struct PathInfo{
            public int3 currentPos; 
            public int3 destination;
            public float stepDuration;
            public byte recoveryAttempts;
            public int currentInd;
            [JsonConverter(typeof(PathDataConverter))]
            public PathData path;
            public bool hasPath; //Resource isn't bound
            public PathInfo(int3 currentPos, PathData path){
                this.currentPos = currentPos;
                this.currentInd = 0;
                this.stepDuration = 0;
                this.recoveryAttempts = 0;
                hasPath = true;
                int pathLength = path == null ? 0 : path.Length;

                this.path = path?.Clone() ?? PathData.Empty;
                destination = currentPos;
                for(int i = 0; i < pathLength; i++){
                    destination += path[i].Delta;
                } 
            }
        }

        /// <summary>Shared path access; storage remains specific to the pathfinder.</summary>
        [JsonConverter(typeof(PathDataConverter))]
        public abstract class PathData {
            public static readonly PathData Empty = new VoxelPath(Array.Empty<byte>());
            public abstract int Length { get; }
            public abstract PathStep this[int index] { get; }
            public abstract PathData Clone();

            public PathData Concat(PathData other) {
                if (other == null) throw new ArgumentNullException(nameof(other));
                if (this is VoxelPath first && other is VoxelPath second) {
                    byte[] directions = new byte[Length + other.Length];
                    Array.Copy(first.Directions, directions, Length);
                    Array.Copy(second.Directions, 0, directions, Length, other.Length);
                    return new VoxelPath(directions);
                }

                PathStep[] steps = new PathStep[Length + other.Length];
                for (int i = 0; i < Length; i++) steps[i] = this[i];
                for (int i = 0; i < other.Length; i++) steps[Length + i] = other[i];
                return new TransitionPath(steps);
            }
        }

        /// <summary>Ordinary paths retain one encoded byte per edge.</summary>
        public sealed class VoxelPath : PathData {
            internal readonly byte[] Directions;
            public VoxelPath(byte[] directions) => Directions = directions ?? throw new ArgumentNullException(nameof(directions));
            public override int Length => Directions.Length;
            // Decode one value on demand; no expanded array or per-step allocation.
            public override PathStep this[int index] => PathStep.FromDirection(Directions[index]);
            public override PathData Clone() => new VoxelPath((byte[])Directions.Clone());
        }

        public sealed class TransitionPath : PathData {
            internal readonly PathStep[] Steps;
            public TransitionPath(PathStep[] steps) => Steps = steps ?? throw new ArgumentNullException(nameof(steps));
            public override int Length => Steps.Length;
            public override PathStep this[int index] => Steps[index];
            public override PathData Clone() => new TransitionPath((PathStep[])Steps.Clone());
        }

        // Preserve the original compact base64 save format, and read the previous
        // expanded-array format as well as paths produced by the transition finder.
        public sealed class PathDataConverter : JsonConverter {
            public override bool CanConvert(Type type) => typeof(PathData).IsAssignableFrom(type);

            public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer) {
                if (reader.TokenType == JsonToken.Null) return null;
                if (reader.TokenType == JsonToken.String)
                    return new VoxelPath(Convert.FromBase64String((string)reader.Value));

                JArray values = JArray.Load(reader);
                if (values.All(value => value.Type == JTokenType.Integer))
                    return new VoxelPath(values.Select(value => value.Value<byte>()).ToArray());

                PathStep[] steps = values.Select(value => value.Type == JTokenType.Integer
                    ? PathStep.FromDirection(value.Value<byte>())
                    : value.ToObject<PathStep>(serializer)).ToArray();
                if (steps.All(step => step.Group < 0 && !step.Movement.Enabled && math.all(math.abs(step.Delta) <= 1))) {
                    return new VoxelPath(steps.Select(step => (byte)((step.Delta.x + 1) * 9
                        + (step.Delta.y + 1) * 3 + step.Delta.z + 1)).ToArray());
                }
                return new TransitionPath(steps);
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
                if (value is VoxelPath voxel) writer.WriteValue(Convert.ToBase64String(voxel.Directions));
                else if (value is TransitionPath transition) serializer.Serialize(writer, transition.Steps);
                else throw new JsonSerializationException("Unsupported path storage type.");
            }
        }

        public struct PathFinder {
            
            public int PathMapSize;
            public int PathDistance;
            public int HeapEnd;
            public unsafe byte* SetPtr;
            public unsafe int4* MapPtr;
            public unsafe PathFinder(int PathDistance){
                this.PathDistance = PathDistance;
                this.PathMapSize = PathDistance * 2 + 1;
                int mapLength = PathMapSize * PathMapSize * PathMapSize;

                //The part we actually clear
                //We divide by 4 because Map has to be 4 byte aligned
                int SetSize = Mathf.CeilToInt(mapLength / (8 * 16f)); 
                //+1 to avoid seg fault when path distance = 0
                int MapSize = mapLength + 1;
                int TotalSize = (SetSize + MapSize) * 16;
                HeapEnd = 1;

                //We make it one block so less fragment & more cache hits
                void* ptr = UnsafeUtility.Malloc(TotalSize, 16, Allocator.Persistent);;
                SetPtr = (byte*)ptr;
                MapPtr = (int4*)(SetPtr + (SetSize * 16));
                UnsafeUtility.MemClear((void*)SetPtr, SetSize * 16);
            }

            public readonly unsafe void Release(){ UnsafeUtility.Free((void*)SetPtr, Allocator.Persistent); }

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

            public unsafe int2 RemoveNode(){
                int2 result = MapPtr[1].xy;
                int2 last = MapPtr[HeapEnd - 1].xy;
                HeapEnd--;

                int heapPos = 1;
                while(heapPos < HeapEnd){
                    int child = heapPos * 2;
                    if(child >= HeapEnd) break;
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
        }

        public unsafe static bool VerifyProfile(in int3 GCoord, in ProfileInfo info, in EntityJob.Context context, bool UseExFlag = true, bool bodyOnly = false){
            bool allC = true; bool anyC = false; bool any0 = false;
            uint3 dC = new (0);
            for(dC.x = 0; dC.x < info.bounds.x; dC.x++){
                for(dC.y = bodyOnly ? 1u : 0u; dC.y < info.bounds.y; dC.y++){
                    for(dC.z = 0; dC.z < info.bounds.z; dC.z++){
                        uint index = dC.x * info.bounds.y * info.bounds.z + dC.y * info.bounds.z + dC.z;
                        ProfileE profile = context.Profile[index + info.profileStart];
                        if(profile.ExFlag && UseExFlag) continue;
                        bool valid = profile.bounds.Contains(CPUMapManager.SampleMap(GCoord + (int3)dC));
                        allC = allC && (valid || !profile.AndFlag);
                        anyC = anyC || (valid && profile.OrFlag);
                        any0 = any0 || profile.OrFlag;
                    }
                }
            } 
            if(allC && (!any0 || anyC)) return true;
            else return false;
        }

        public static bool VerifyMatProfile(in int3 GCoord, in uint3 bounds, in List<MatProfileE> Profile, bool UseExFlag = true, bool bodyOnly = false){
            if (Profile == null) return true;
            var mReg = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
            bool allC = true; bool anyC = false; bool any0 = false;
            uint3 dC = new (0);
            for(dC.x = 0; dC.x < bounds.x; dC.x++){
                for(dC.y = bodyOnly ? 1u : 0u; dC.y < bounds.y; dC.y++){
                    for(dC.z = 0; dC.z < bounds.z; dC.z++){
                        uint index = dC.x * bounds.y * bounds.z + dC.y * bounds.z + dC.z;
                        MatProfileE matCheck = Profile[(int)index];
                        ProfileE profile = matCheck.profile;
                        if(profile.ExFlag && UseExFlag) continue;
                        MapData data = CPUMapManager.SampleMap(GCoord + (int3)dC);
                        bool valid = profile.bounds.Contains(data);
                        if (valid) {
                            if (data.IsNull) valid = false;
                            else if (matCheck.matTag != TagRegistry.Tags.None) 
                                valid &= mReg.GetMostSpecificTag(matCheck.matTag, data.material, out _);
                        }

                        allC = allC && (valid || !profile.AndFlag);
                        anyC = anyC || (valid && profile.OrFlag);
                        any0 = any0 || profile.OrFlag;
                    }
                }
            } 
            if(allC && (!any0 || anyC)) return true;
            else return false;
        }

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

        protected internal static int Get3DDistance(in int3 dist)
        {
            int minDist = math.min(dist.x, math.min(dist.y, dist.z));
            int maxDist = math.max(dist.x, math.max(dist.y, dist.z));
            int midDist = dist.x + dist.y + dist.z - minDist - maxDist;

            return minDist * 17 + (midDist - minDist) * 14 + (maxDist - midDist) * 10;
        }

        private static float3 CubicNorm(float3 v){
            if(math.length(v) == 0) return math.forward();
            else return v / math.cmax(math.abs(v));
            //This norm guarantees the vector will be on the edge of a cube
        }

        private static unsafe PathData RetracePath(ref PathFinder finder, int dest, int start){
            int PathLength = 0;
            int currentInd = dest;
            while(currentInd != start){ 
                PathLength++; 
                byte dir = (byte)finder.MapPtr[currentInd].w;
                currentInd -= ((dir / 9) - 1) * finder.PathMapSize * finder.PathMapSize + 
                                ((dir / 3 % 3) - 1) * finder.PathMapSize + ((dir % 3) - 1);
            }

            byte[] path = new byte[PathLength];
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

            return new VoxelPath(path);
        }

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
        private static PathData FindPath(in int3 Origin, in int3 iEnd, int PathDistance, in ProfileInfo info, in EntityJob.Context context, out int Cost){
            PathFinder finder = new (PathDistance);
            int3 End = math.clamp(iEnd + PathDistance, 0, finder.PathMapSize-1); //We add the distance to make it relative to the start
            int pathEndInd = End.x * finder.PathMapSize * finder.PathMapSize + End.y * finder.PathMapSize + End.z;

            //Find the closest point to the end
            int3 pathStart = new (PathDistance, PathDistance, PathDistance);
            int startInd = pathStart.x * finder.PathMapSize * finder.PathMapSize + pathStart.y * finder.PathMapSize + pathStart.z;
            int hCost = Get3DDistance(math.abs(End - pathStart)); 
            int2 bestEnd = new (startInd, hCost);

            Cost = 0;
            finder.AddNode(pathStart, hCost, 13); //13 means dP = (0, 0, 0)
            while(finder.HeapEnd > 1){
                int2 current = finder.RemoveNode(); 
                int3 ECoord = new (current.y / (finder.PathMapSize * finder.PathMapSize), 
                                    current.y / finder.PathMapSize % finder.PathMapSize, 
                                    current.y % finder.PathMapSize);
                hCost = Get3DDistance(math.abs(End - ECoord));
                Cost++;

                //Always assume the first point is valid
                if(current.y != startInd && !VerifyProfile(Origin + ECoord - PathDistance, info, context)) 
                    continue;
                if(hCost < bestEnd.y){
                    bestEnd.x = current.y;
                    bestEnd.y = hCost;
                } 
                if((int)current.y == pathEndInd) break;

                for(int i = 0; i < 24; i++){
                    int4 delta = dP[i];
                    int3 nCoord = ECoord + delta.xyz;
                    if(math.any(nCoord < 0) || math.any(nCoord >= finder.PathMapSize)) continue;
                    int FScore = current.x + Get3DDistance(math.abs(End - nCoord)) - hCost + delta.w;
                    int dirEnc = (delta.x + 1) * 9 + (delta.y + 1) * 3 + delta.z + 1;
                    finder.AddNode(nCoord, FScore, dirEnc);
                }
            }

            PathData path = RetracePath(ref finder, bestEnd.x, startInd);
            finder.Release();
            return path;
        }

        //Find point that matches raw-profile along the path to destination
        private static PathData FindMatchAlongRay(in int3 Origin, in float3 rayDir, int PathDistance, in ProfileInfo info, in ProfileInfo dest, in EntityJob.Context context, out int Cost, out bool ReachedEnd){
            PathFinder finder = new (PathDistance);
            int3 End = math.clamp((int3)(CubicNorm(rayDir) * PathDistance) + PathDistance, 0, finder.PathMapSize-1); //We add the distance to make it relative to the start
            int pathEndInd = End.x * finder.PathMapSize * finder.PathMapSize + End.y * finder.PathMapSize + End.z;

            //Find the closest point to the end
            int3 pathStart = new (PathDistance, PathDistance, PathDistance);
            int startInd = pathStart.x * finder.PathMapSize * finder.PathMapSize + pathStart.y * finder.PathMapSize + pathStart.z;
            int hCost = Get3DDistance(math.abs(End - pathStart)); 
            int2 bestEnd = new (startInd, hCost);

            Cost = 0;
            ReachedEnd = false;
            finder.AddNode(pathStart, hCost, 13); //13 means dP = (0, 0, 0)
            while(finder.HeapEnd > 1){
                int2 current = finder.RemoveNode();
                int3 ECoord = new (current.y / (finder.PathMapSize * finder.PathMapSize), 
                                    current.y / finder.PathMapSize % finder.PathMapSize, 
                                    current.y % finder.PathMapSize);
                hCost = Get3DDistance(math.abs(End - ECoord));
                Cost++;

                //Always assume the first point is valid
                if (!(ReachedEnd = VerifyProfile(Origin + ECoord - PathDistance, dest, context, false))) {
                    if(current.y != startInd && !VerifyProfile(Origin + ECoord - PathDistance, info, context)) 
                        continue;
                }
                if(hCost < bestEnd.y || ReachedEnd) bestEnd = new (current.y, hCost);
                if(current.y == pathEndInd || ReachedEnd) break;
                if(current.x - hCost >= PathDistance * 10){
                    bestEnd.x = current.y;
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

            PathData path = RetracePath(ref finder, bestEnd.x, startInd);
            finder.Release();
            return path;
        }

        //Find point that matches raw-profile along the path to destination with the closest distance to the desired path distance
        private static PathData FindPathAlongRay(in int3 Origin, ref float3 rayDir, int PathDistance, in ProfileInfo info, in EntityJob.Context context, out int Cost){
            PathFinder finder = new (PathDistance);
            int3 End = math.clamp((int3)(CubicNorm(rayDir) * PathDistance) + PathDistance, 0, finder.PathMapSize-1); //We add the distance to make it relative to the start

            //Find the closest point to the end
            int3 pathStart = new (PathDistance, PathDistance, PathDistance);
            int startInd = pathStart.x * finder.PathMapSize * finder.PathMapSize + pathStart.y * finder.PathMapSize + pathStart.z;
            int hCost = Get3DDistance(math.abs(End - pathStart)); 
            int bestEnd = startInd;

            Cost = 0;
            finder.AddNode(pathStart, hCost, 13); //13 means dP = (0, 0, 0)
            while(finder.HeapEnd > 1){
                int2 current = finder.RemoveNode();
                int3 ECoord = new (current.y / (finder.PathMapSize * finder.PathMapSize), 
                                    current.y / finder.PathMapSize % finder.PathMapSize, 
                                    current.y % finder.PathMapSize);
                hCost = Get3DDistance(math.abs(End - ECoord));
                Cost++;

                //Always assume the first point is valid
                if(current.y != startInd && !VerifyProfile(Origin + ECoord - PathDistance, info, context)) 
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

            PathData path = RetracePath(ref finder, bestEnd, startInd);
            finder.Release();
            return path;
        }



        /// <summary>
        ///An annoying challenge of pathfinding is that we must search all pathable nodes 
        ///to determine a path doesn't exist. However, if the destination is not a pathable point
        ///pathfinding will always have to search all nodes which is inefficient. Thus if the destination
        ///is not a pathable point, simply follow a ray to a certain length to approach the target, which is a
        ///much cheaper operation than failing to find an exact path.
        /// </summary>
        /// <param name="Origin"></param>
        /// <param name="iEnd"></param>
        /// <param name="PathDistance"></param>
        /// <param name="info"></param>
        /// <param name="context"></param>
        public static PathData FindPathOrApproachTarget(in int3 Origin, in int3 iEnd, int PathDistance, in ProfileInfo info, in EntityJob.Context context, out int Cost){
            int3 dGC = Origin + iEnd;
            if(VerifyProfile(dGC, info, context)){
                return FindPath(Origin, iEnd, PathDistance, info, context, out Cost);
            } else {
                float3 rayDir = (float3)iEnd;
                int pathDist = math.min(math.cmax(math.abs(iEnd)), PathDistance);
                return FindPathAlongRay(Origin, ref rayDir, pathDist, info, context, out Cost);
            }
        }


        private static PathData FindPath(in int3 Origin, in int3 iEnd, int PathDistance, uint3 bounds, List<MatProfileE> matProfile, out int Cost){
            PathFinder finder = new (PathDistance);
            int3 End = math.clamp(iEnd + PathDistance, 0, finder.PathMapSize-1); //We add the distance to make it relative to the start
            int pathEndInd = End.x * finder.PathMapSize * finder.PathMapSize + End.y * finder.PathMapSize + End.z;

            //Find the closest point to the end
            int3 pathStart = new (PathDistance, PathDistance, PathDistance);
            int startInd = pathStart.x * finder.PathMapSize * finder.PathMapSize + pathStart.y * finder.PathMapSize + pathStart.z;
            int hCost = Get3DDistance(math.abs(End - pathStart)); 
            int2 bestEnd = new (startInd, hCost);
            Cost = 0;
        
            finder.AddNode(pathStart, hCost, 13); //13 means dP = (0, 0, 0)
            while(finder.HeapEnd > 1){
                int2 current = finder.RemoveNode();
                int3 ECoord = new (current.y / (finder.PathMapSize * finder.PathMapSize), 
                                    current.y / finder.PathMapSize % finder.PathMapSize, 
                                    current.y % finder.PathMapSize);
                hCost = Get3DDistance(math.abs(End - ECoord));
                Cost++;

                //Always assume the first point is valid
                if(current.y != startInd && !VerifyMatProfile(Origin + ECoord - PathDistance, bounds, matProfile)) 
                    continue;
                if(hCost < bestEnd.y){
                    bestEnd.x = current.y;
                    bestEnd.y = hCost;
                } 
                if((int)current.y == pathEndInd) break;

                for(int i = 0; i < 24; i++){
                    int4 delta = dP[i];
                    int3 nCoord = ECoord + delta.xyz;
                    if(math.any(nCoord < 0) || math.any(nCoord >= finder.PathMapSize)) continue;
                    int FScore = current.x + Get3DDistance(math.abs(End - nCoord)) - hCost + delta.w;
                    int dirEnc = (delta.x + 1) * 9 + (delta.y + 1) * 3 + delta.z + 1;
                    finder.AddNode(nCoord, FScore, dirEnc);
                }
            }

            PathData path = RetracePath(ref finder, bestEnd.x, startInd);
            finder.Release();
            return path;
        }

        //Find point that matches raw-profile along the path to destination
        private static PathData FindMatchAlongRay(in int3 Origin, in float3 rayDir, int PathDistance, uint3 mBounds, List<MatProfileE> mProf, uint3 dBounds, List<MatProfileE> destProf, out int Cost, out bool ReachedEnd){
            PathFinder finder = new (PathDistance);
            int3 End = math.clamp((int3)(CubicNorm(rayDir) * PathDistance) + PathDistance, 0, finder.PathMapSize-1); //We add the distance to make it relative to the start
            int pathEndInd = End.x * finder.PathMapSize * finder.PathMapSize + End.y * finder.PathMapSize + End.z;

            //Find the closest point to the end
            int3 pathStart = new (PathDistance, PathDistance, PathDistance);
            int startInd = pathStart.x * finder.PathMapSize * finder.PathMapSize + pathStart.y * finder.PathMapSize + pathStart.z;
            int hCost = Get3DDistance(math.abs(End - pathStart)); 
            int2 bestEnd = new (startInd, hCost);

            Cost = 0;
            ReachedEnd = false;
            finder.AddNode(pathStart, hCost, 13); //13 means dP = (0, 0, 0)
            while(finder.HeapEnd > 1){
                int2 current = finder.RemoveNode();
                int3 ECoord = new (current.y / (finder.PathMapSize * finder.PathMapSize), 
                                    current.y / finder.PathMapSize % finder.PathMapSize, 
                                    current.y % finder.PathMapSize);
                hCost = Get3DDistance(math.abs(End - ECoord));
                Cost++;

                //Always assume the first point is valid
                if (!(ReachedEnd = VerifyMatProfile(Origin + ECoord - PathDistance, dBounds, destProf, false))) {
                    if(current.y != startInd && !VerifyMatProfile(Origin + ECoord - PathDistance, mBounds, mProf)) 
                        continue;//
                }
                if(hCost < bestEnd.y || ReachedEnd) bestEnd = new (current.y, hCost);
                if(current.y == pathEndInd || ReachedEnd) break;
                if(current.x - hCost >= PathDistance * 10){
                    bestEnd.x = current.y;
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

            PathData path = RetracePath(ref finder, bestEnd.x, startInd);
            finder.Release();
            return path;
        }

        //Find point that matches raw-profile along the path to destination with the closest distance to the desired path distance
        private static PathData FindPathAlongRay(in int3 Origin, ref float3 rayDir, int PathDistance, uint3 mBounds, List<MatProfileE> mProf, out int Cost){
            PathFinder finder = new (PathDistance);
            int3 End = math.clamp((int3)(CubicNorm(rayDir) * PathDistance) + PathDistance, 0, finder.PathMapSize-1); //We add the distance to make it relative to the start

            //Find the closest point to the end
            int3 pathStart = new (PathDistance, PathDistance, PathDistance);
            int startInd = pathStart.x * finder.PathMapSize * finder.PathMapSize + pathStart.y * finder.PathMapSize + pathStart.z;
            int hCost = Get3DDistance(math.abs(End - pathStart)); 
            int bestEnd = startInd;

            Cost = 0;
            finder.AddNode(pathStart, hCost, 13); //13 means dP = (0, 0, 0)
            while(finder.HeapEnd > 1){
                int2 current = finder.RemoveNode();
                int3 ECoord = new (current.y / (finder.PathMapSize * finder.PathMapSize), 
                                    current.y / finder.PathMapSize % finder.PathMapSize, 
                                    current.y % finder.PathMapSize);
                hCost = Get3DDistance(math.abs(End - ECoord));
                Cost++;

                //Always assume the first point is valid
                if(current.y != startInd && !VerifyMatProfile(Origin + ECoord - PathDistance, mBounds, mProf)) 
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

            PathData path = RetracePath(ref finder, bestEnd, startInd);
            finder.Release();
            return path;
        }

        /// <summary>
        ///An annoying challenge of pathfinding is that we must search all pathable nodes 
        ///to determine a path doesn't exist. However, if the destination is not a pathable point
        ///pathfinding will always have to search all nodes which is inefficient. Thus if the destination
        ///is not a pathable point, simply follow a ray to a certain length to approach the target, which is a
        ///much cheaper operation than failing to find an exact path.
        /// </summary>
        /// <param name="Origin"></param>
        /// <param name="iEnd"></param>
        /// <param name="PathDistance"></param>
        /// <param name="mBounds"></param>
        /// <param name="mProf"></param>
        /// <param name="PathLength"></param>
        public static PathData FindPathOrApproachTarget(in int3 Origin, in int3 iEnd, int PathDistance, uint3 mBounds, List<MatProfileE> mProf, out int PathLength){
            int3 dGC = Origin + iEnd;
            if(VerifyMatProfile(dGC, mBounds, mProf)){
                return FindPath(Origin, iEnd, PathDistance, mBounds, mProf, out PathLength);
            } else {
                float3 rayDir = (float3)iEnd;
                int pathDist = math.min(math.cmax(math.abs(iEnd)), PathDistance);
                return FindPathAlongRay(Origin, ref rayDir, pathDist, mBounds, mProf, out PathLength);
            }
        }
    }
}
