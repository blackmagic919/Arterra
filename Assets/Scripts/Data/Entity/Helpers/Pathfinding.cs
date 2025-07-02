using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using WorldConfig.Generation.Entity;
using static WorldConfig.Generation.Entity.EntitySetting;
using MapStorage;

[BurstCompile]
public unsafe struct PathFinder{

    [BurstCompile]
    public unsafe static bool VerifyProfile(in int3 GCoord, in EntitySetting.ProfileInfo info, in EntityJob.Context context, bool UseExFlag = true){
        bool allC = true; bool anyC = false; bool any0 = false;
        uint3 dC = new (0);
        for(dC.x = 0; dC.x < info.bounds.x; dC.x++){
            for(dC.y = 0; dC.y < info.bounds.y; dC.y++){
                for(dC.z = 0; dC.z < info.bounds.z; dC.z++){
                    uint index = dC.x * info.bounds.y * info.bounds.z + dC.y * info.bounds.z + dC.z;
                    ProfileE profile = context.Profile[index + info.profileStart];
                    if(profile.ExFlag && UseExFlag) continue;
                    bool valid = profile.bounds.Contains(CPUMapManager.SampleMap(GCoord + (int3)dC, context.mapContext));
                    allC = allC && (valid || !profile.AndFlag);
                    anyC = anyC || (valid && profile.OrFlag);
                    any0 = any0 || profile.OrFlag;
                }
            }
        } 
        if(allC && (!any0 || anyC)) return true;
        else return false;
    }

    public struct PathInfo{
        public int3 currentPos; 
        public int3 destination;
        public float stepDuration;
        public int currentInd;
        public byte[] path;
        public bool hasPath; //Resource isn't bound
        public unsafe PathInfo(int3 currentPos, byte* path, int pathLength){
            this.currentPos = currentPos;
            this.currentInd = 0;
            this.stepDuration = 0;
            hasPath = true;

            this.path = new byte[pathLength];
            destination = currentPos;
            for(int i = 0; i < pathLength; i++){
                this.path[i] = path[i];
                destination += new int3((path[i] / 9) - 1, 
                (path[i] / 3 % 3) - 1, (path[i] % 3) - 1);
            } UnsafeUtility.Free(path, Allocator.Persistent);
        }
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
    private static int Get3DDistance(in int3 dist)
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
    public static unsafe byte* FindPath(in int3 Origin, in int3 iEnd, int PathDistance, in ProfileInfo info, in EntityJob.Context context, out int PathLength){
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

        byte* path = RetracePath(ref finder, bestEnd.x, startInd, out PathLength);
        finder.Release();
        return path;
    }
    [BurstCompile]
    //Find point that matches raw-profile along the path to destination
    public static unsafe byte* FindMatchAlongRay(in int3 Origin, in float3 rayDir, int PathDistance, in ProfileInfo info, in ProfileInfo dest, in EntityJob.Context context, out int PathLength, out bool ReachedEnd){
        PathFinder finder = new (PathDistance);
        int3 End = math.clamp((int3)(CubicNorm(rayDir) * PathDistance), 0, finder.PathMapSize-1); //We add the distance to make it relative to the start
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
            if(current.y != startInd && !VerifyProfile(Origin + ECoord - PathDistance, info, context)) 
                continue;
            ReachedEnd = VerifyProfile(Origin + ECoord - PathDistance, dest, context, false);
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

        byte* path = RetracePath(ref finder, bestEnd.x, startInd, out PathLength);
        finder.Release();
        return path;
    }
    [BurstCompile]
    //Find point that matches raw-profile along the path to destination with the closest distance to the desired path distance
    public static unsafe byte* FindPathAlongRay(in int3 Origin, ref float3 rayDir, int PathDistance, in EntitySetting.ProfileInfo info, in EntityJob.Context context, out int PathLength){
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

        byte* path = RetracePath(ref finder, bestEnd, startInd, out PathLength);
        finder.Release();
        return path;
    }



    [BurstCompile]
    public static unsafe byte* RetracePath(ref PathFinder finder, int dest, int start, out int PathLength){
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
    /// <param name="PathLength"></param>
    [BurstCompile]
    public static unsafe byte* FindPathOrApproachTarget(in int3 Origin, in int3 iEnd, int PathDistance, in ProfileInfo info, in EntityJob.Context context, out int PathLength){
        int3 dGC = Origin + iEnd;
        if(VerifyProfile(dGC, info, context)){
            return FindPath(Origin, iEnd, PathDistance, info, context, out PathLength);
        } else {
            float3 rayDir = (float3)iEnd;
            int pathDist = math.min(math.cmax(math.abs(iEnd)), PathDistance);
            return FindPathAlongRay(Origin, ref rayDir, pathDist, info, context, out PathLength);
        }
    }
}