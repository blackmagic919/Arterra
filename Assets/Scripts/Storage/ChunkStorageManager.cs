using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using Unity.Collections;
using System.Threading.Tasks;
using System.Text;
using System.IO.Compression;
using Utils;

using static CPUDensityManager;
using System.Linq;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using System;
using UnityEngine;
using TerrainGeneration;

/*
Chunk File Layout:
Header: [Material Size][Entity Offset]
//Material List 
+Material Offset:
//Chunk Data
//Lists and headers are zipped, offsets are not zipped
*/

public static class ChunkStorageManager
{
    public delegate void OnReadComplete(ReadbackInfo info);
    public delegate void OnWriteComplete();

    private static int maxChunkSize;
    private static ChunkFinder chunkFinder;

    public static void Initialize(){
        maxChunkSize = WorldOptions.CURRENT.Quality.Rendering.value.mapChunkSize;
        chunkFinder = new ChunkFinder();
    }

    public static void SaveEntitiesToJsonSync(List<Entity> entities, int3 CCoord){
        string entityPath = chunkFinder.GetEntityRegionPath(CCoord);
        if(!Directory.Exists(entityPath))
            Directory.CreateDirectory(entityPath);

        string fileAdd = chunkFinder.GetEntityPath(CCoord);
        chunkFinder.TryAddEntity(CCoord);
        SaveEntityToJsonSync(fileAdd, entities);
    }

    public static void SaveChunkToBinSync(ChunkPtr chunk, int3 CCoord){
        string mapPath = chunkFinder.GetMapRegionPath(CCoord);
        
        if (!Directory.Exists(mapPath))
            Directory.CreateDirectory(mapPath);

        string fileAdd = chunkFinder.GetMapPath(CCoord);
        chunkFinder.TryAddMap(CCoord);
        SaveChunkToBinSync(fileAdd, chunk); //fire and forget
    }

    public static void SaveChunkToBinSync(string fileAdd, ChunkPtr chunk)
    {
        using (FileStream fs = File.Create(fileAdd))
        {
            MemoryStream mapStream = WriteChunkMaps(chunk, out ChunkHeader header);
            MemoryStream headerStream = WriteChunkHeader(header);
            headerStream.Seek(0, SeekOrigin.Begin);
            headerStream.CopyTo(fs);
            headerStream.Close();
            
            mapStream.Seek(0, SeekOrigin.Begin);
            mapStream.CopyTo(fs);
            mapStream.Close();

            fs.Flush();
            fs.Close();
        }
    }

    public static void SaveEntityToJsonSync(string fileAdd, List<Entity> entities){
        using (FileStream fs = File.Create(fileAdd))
        {
            MemoryStream headerStream = WriteChunkHeader(SerializeEntities(entities));
            headerStream.Seek(0, SeekOrigin.Begin);
            headerStream.CopyTo(fs);
            headerStream.Close();
            fs.Flush();
            fs.Close();
        }
    }

    public static MapData[] ReadVisualChunkMap(int3 CCoord, int depth){
        int numPoints = maxChunkSize * maxChunkSize * maxChunkSize;
        MapData[] map = new MapData[numPoints]; //automatically zeored(not dirty)
        int clampedDepth = math.min(depth, (int)math.log2(maxChunkSize));
        int chunkSkip = math.max(maxChunkSize >> depth, 1); 
        int3 dSC = new(); int3 dCC; 
        for(dSC.x = 0; dSC.x < maxChunkSize; dSC.x += chunkSkip){
        for(dSC.y = 0; dSC.y < maxChunkSize; dSC.y += chunkSkip){
        for(dSC.z = 0; dSC.z < maxChunkSize; dSC.z += chunkSkip){
            dCC = (dSC << depth) / maxChunkSize;
            int3 sCC = CCoord + dCC;
            if(!chunkFinder.TryGetMapChunk(sCC, out string chunkAdd)) continue;
            MapData[] chunkMap = ReadChunkBin(chunkAdd, clampedDepth);
            CopyTo(map, chunkMap, dSC, clampedDepth);
        }}}
        return map;

        static void CopyTo(MapData[] map, MapData[] chunkMap, int3 oWC, int depth){
            int3 rSC = new ();
            int chunkSize = maxChunkSize >> depth;
            for(rSC.x = 0; rSC.x < chunkSize; rSC.x++){
            for(rSC.y = 0; rSC.y < chunkSize; rSC.y++){
            for(rSC.z = 0; rSC.z < chunkSize; rSC.z++){
                int wIndex = CustomUtility.indexFromCoord(oWC + rSC, maxChunkSize);
                int rIndex = CustomUtility.indexFromCoord(rSC, chunkSize);
                map[wIndex] = chunkMap[rIndex];
            }}}
        }
    }

    public static ReadbackInfo ReadChunkInfo(int3 CCoord){
        ReadbackInfo info = new ReadbackInfo(false);
        if(chunkFinder.TryGetMapChunk(CCoord, out string chunkAdd)) info.map = ReadChunkBin(chunkAdd, 0);
        if(chunkFinder.TryGetEntityChunk(CCoord, out string entityAdd)) info.entities = ReadEntityJson(entityAdd);
        return info;
    }

    public static MapData[] ReadChunkBin(string fileAdd, int depth)
    {
        MapData[] map = null;
        //Caller has to copy for persistence
        using (FileStream fs = File.Open(fileAdd, FileMode.Open, FileAccess.Read))
        {
            uint mapStart = ReadChunkHeader(fs, out ChunkHeader header);
            if(depth != 0) mapStart += (uint)header.ResolutionOffsets[depth - 1];
            fs.Seek(mapStart, SeekOrigin.Begin);
            map = ReadChunkMap(fs, maxChunkSize >> depth);
            DeserializeHeader(ref map, ref header);
        }
        return map;
    }

    public static List<EntitySerial> ReadEntityJson(string fileAdd)
    {
        List<EntitySerial> entities = null;
        //Caller has to copy for persistence
        using (FileStream fs = File.Open(fileAdd, FileMode.Open, FileAccess.Read)){
            //It's automatically deserialized by a custom json rule
            ReadChunkHeader(fs, out entities);
        }
        return entities;
    }

    static List<EntitySerial> SerializeEntities(List<Entity> entities){
        List<EntitySerial> eSerial = new List<EntitySerial>();
        var eReg = WorldOptions.CURRENT.Generation.Entities;
        for(int i = 0; i < entities.Count; i++){
            EntitySerial entity = new ();
            entity.type = eReg.RetrieveName((int)entities[i].info.entityType);
            entity.guid = entities[i].info.entityId;
            entity.data = (IEntity)Marshal.PtrToStructure(entities[i].obj, eReg.Retrieve(entity.type).Entity.GetType());
            eSerial.Add(entity);
        }
        return eSerial;
     }

    static ChunkHeader SerializeHeader(ChunkPtr chunk){
        Dictionary<int, int> RegisterDict = new Dictionary<int, int>();
        int numPoints = maxChunkSize * maxChunkSize * maxChunkSize;
        for(int i = 0; i < numPoints; i++){
            MapData mapPt = chunk.data[chunk.offset + i];
            RegisterDict.TryAdd(mapPt.material, RegisterDict.Count);
            mapPt._material = RegisterDict[mapPt.material];
            chunk.data[chunk.offset + i] = mapPt;
        }

        string[] dict = new string[RegisterDict.Count];
        var mReg = WorldOptions.CURRENT.Generation.Materials.value.MaterialDictionary;
        foreach(var pair in RegisterDict){
            dict[pair.Value] = mReg.RetrieveName(pair.Key);
        }

        return new ChunkHeader{ RegisterNames = dict.ToList() };
    }

    private static void DeserializeHeader(ref MapData[] map, ref ChunkHeader header){
        var mReg = WorldOptions.CURRENT.Generation.Materials.value.MaterialDictionary;
        for(int i = 0; i < map.Length; i++){
            map[i].material = mReg.RetrieveIndex(header.RegisterNames[(int)map[i].material]);
        }
    }

    static void WriteUInt32(byte[] buffer, ref uint offset, uint value){
        buffer[offset + 0] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        offset += 4;
    }

    static uint ReadUInt32(byte[] buffer, uint offset){
        return (uint)(buffer[offset + 0] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24);
    }

    private static MemoryStream WriteChunkHeader(object header){
        MemoryStream ms = new MemoryStream();
        ms.Seek(4, SeekOrigin.Begin);

        string json = JsonConvert.SerializeObject(header, Formatting.Indented);
        byte[] buffer = Encoding.ASCII.GetBytes(json);
        using(GZipStream zs = new GZipStream(ms, CompressionMode.Compress, true)){ 
            zs.Write(buffer); 
            zs.Flush(); 
        }
        byte[] size = new byte[4]; uint _ = 0;
        WriteUInt32(size, ref _, (uint)ms.Length);
        ms.Seek(0, SeekOrigin.Begin);
        ms.Write(size);
        ms.Flush();
        return ms;
    }

    private static uint ReadChunkHeader<T>(FileStream fs, out T header){
        byte[] readSize = new byte[4]; fs.Read(readSize); 
        uint size = ReadUInt32(readSize, 0);
        byte[] buffer = new byte[size]; fs.Read(buffer);

        using MemoryStream ms = new(buffer);
        using GZipStream zs = new(ms, CompressionMode.Decompress, true);
        using StreamReader sr = new(zs);
        header = JsonConvert.DeserializeObject<T>(sr.ReadToEnd());
        return size; //add 4 for the size of the size of the header(yeah lol)
    }

    //IT IS CALLERS RESPONSIBILITY TO DISPOSE MEMORY STREAM
    private static MemoryStream WriteChunkMaps(ChunkPtr chunk, out ChunkHeader header){
        MemoryStream ms = new MemoryStream();
        header = SerializeHeader(chunk);
        header.ResolutionOffsets = new List<int>();

        for(int skipInc = 1; skipInc <= maxChunkSize; skipInc <<= 1){
            int numPointsAxis = maxChunkSize / skipInc;
            int numPoints = numPointsAxis * numPointsAxis * numPointsAxis;

            var mBuffer = new byte[numPoints * 4]; uint mBPos = 0;
            for(int x = 0; x < maxChunkSize; x += skipInc){
                for(int y = 0; y < maxChunkSize; y += skipInc){
                    for(int z = 0; z < maxChunkSize; z += skipInc){
                        int readPos = CustomUtility.indexFromCoord(x, y, z, maxChunkSize);
                        MapData mapPt = chunk.data[chunk.offset + readPos];

                        //if(!mapPt.isDirty) mapPt.data = 0;
                        WriteUInt32(mBuffer, ref mBPos, mapPt.data);
                    }
                }
            }
            //Deals with memory so it's better to not hog all the threads
            using(GZipStream zs = new GZipStream(ms, CompressionMode.Compress, true)){ 
                zs.Write(mBuffer); 
                zs.Flush(); 
            }   
            header.ResolutionOffsets.Add((int)ms.Length);
        }
        ms.Flush();
        return ms;
    }

    private static MapData[] ReadChunkMap(FileStream fs, int chunkSize){
        int numPointsAxis = chunkSize;
        int numPoints = numPointsAxis * numPointsAxis * numPointsAxis;
        MapData[] outStream = new MapData[numPoints];
    
        using(GZipStream zs = new GZipStream(fs, CompressionMode.Decompress, true))
        {
            byte[] buffer = new byte[4 * numPoints]; uint bufferPos = 0;
            zs.Read(buffer);

            for(int index = 0; index < numPoints; index++)
            {
                outStream[index] = new MapData{
                    data = (uint)(buffer[bufferPos + 0] | 
                    buffer[bufferPos + 1] << 8 | 
                    buffer[bufferPos + 2] << 16 | 
                    buffer[bufferPos + 3] << 24)
                };
                bufferPos += 4;
            }
            zs.Close();
        }
        return outStream;
    }

    public struct ChunkHeader{
        public List<string> RegisterNames;
        public List<int> ResolutionOffsets;
    }

    public struct ReadbackInfo{
        public List<EntitySerial> entities;
        public MapData[] map;

        public ReadbackInfo(bool _){
            entities = null;
            map = null;
        }

        public ReadbackInfo(MapData[] map, List<EntitySerial> entities){
            this.entities = entities;
            this.map = map;
        }
    }

    /*
    Checking whether a file exists through the file system(i.e. File.Exists)
    can be very slow because of exception handling, multi-path links, etc.
    so we enumerate all files in our viewable region and cache it so we can
    check whether it exists faster.

    This is also why the file region architechture exists
    */
    private class ChunkFinder{
        private readonly ChunkRegion[] regions;
        private readonly int numChunksAxis;
        private readonly int regionChunkSize;
        public static string chunkPath;
        public static string entityPath;
        private const string fileExtension = ".bin";
        public ChunkFinder(){
            chunkPath = WorldStorageHandler.WORLD_SELECTION.First.Value.Path + "/MapData/";
            entityPath = WorldStorageHandler.WORLD_SELECTION.First.Value.Path + "/EntityData/";

            RenderSettings rSettings = WorldOptions.CURRENT.Quality.Rendering.value;
            numChunksAxis = OctreeTerrain.Octree.GetAxisChunksDepth(rSettings.MaxDepth, rSettings.Balance, (uint)rSettings.MinChunkRadius);
            numChunksAxis = math.max((numChunksAxis << rSettings.MaxDepth) >> rSettings.FileRegionDepth, 1);
            regionChunkSize = 1 << rSettings.FileRegionDepth;

            int numChunks = numChunksAxis * numChunksAxis * numChunksAxis;
            regions = new ChunkRegion[numChunks];
        }

        private int HashCoord(int3 RCoord){
            int3 hashCoord = ((RCoord % numChunksAxis) + numChunksAxis) % numChunksAxis;
            int hash = (hashCoord.x * numChunksAxis * numChunksAxis) + (hashCoord.y * numChunksAxis) + hashCoord.z;
            return hash;
        }

        private int3 CSToRS(int3 CCoord){
            int3 MCoord = ((CCoord % regionChunkSize) + regionChunkSize) % regionChunkSize;
            int3 RCoord = (CCoord - MCoord) / regionChunkSize;
            return RCoord;
        } 

        public bool TryGetMapChunk(int3 CCoord, out string address){
            address = null;
            int3 RCoord = CSToRS(CCoord);
            int hash = HashCoord(RCoord);

            if(!regions[hash].active || math.any(regions[hash].RCoord != RCoord)) 
                ReconstructRegion(RCoord);
            if(!regions[hash].MapChunks.Contains(CCoord)) 
                return false;
            address = GetMapPath(CCoord);
            return true;
        }

        public bool TryGetEntityChunk(int3 CCoord, out string address){
            address = null;
            int3 RCoord = CSToRS(CCoord);
            int hash = HashCoord(RCoord);

            if(!regions[hash].active || math.any(regions[hash].RCoord != RCoord)) 
                ReconstructRegion(RCoord);
            if(!regions[hash].EntityChunks.Contains(CCoord)) 
                return false;
            address = GetEntityPath(CCoord);
            return true;
        }

        private void ReconstructRegion(int3 RCoord){
            string RegionAddress = MapRegionPath(RCoord);
            string EntityAddress = EntityRegionPath(RCoord);
            ChunkRegion region = new (RCoord);
            PopulateChunkDict(ref region.MapChunks, RegionAddress);
            PopulateChunkDict(ref region.EntityChunks, EntityAddress);
            regions[HashCoord(RCoord)] = region;

            static void PopulateChunkDict(ref HashSet<int3> dict, string regionAddress){
                if(!Directory.Exists(regionAddress)) return;
                string[] chunkFiles = Directory.GetFiles(regionAddress, "Chunk*"+fileExtension);
                foreach(string chunkFile in chunkFiles){
                    try{
                    string[] split = Path.GetRelativePath(regionAddress, chunkFile).Split('_', '.');
                    int3 CCoord = new(int.Parse(split[1]), int.Parse(split[2]), int.Parse(split[3]));
                    dict.Add(CCoord);
                    } catch (Exception) { } 
                    //We ignore the file if it's not in the correct format
                }
            }
        }

        public void TryAddMap(int3 CCoord){
            int3 RCoord = CSToRS(CCoord); int hash = HashCoord(RCoord);

            if(!regions[hash].active || math.any(regions[hash].RCoord != RCoord)) 
                ReconstructRegion(RCoord);
            if(!regions[hash].MapChunks.Contains(CCoord))
                regions[hash].MapChunks.Add(CCoord);
        }

        public void TryAddEntity(int3 CCoord){
            int3 RCoord = CSToRS(CCoord); int hash = HashCoord(RCoord);

            if(!regions[hash].active || math.any(regions[hash].RCoord != RCoord)) 
                ReconstructRegion(RCoord);
            if(!regions[hash].EntityChunks.Contains(CCoord))
                regions[hash].EntityChunks.Add(CCoord);
        }

        public string GetMapPath(int3 CCoord){
            int3 RCoord = CSToRS(CCoord);
            return chunkPath + "Region_" + RCoord.x + "_" + RCoord.y + "_" + RCoord.z + '/' +
                   "Chunk_" + CCoord.x + "_" + CCoord.y + "_" + CCoord.z + fileExtension;
        }

        public string GetEntityPath(int3 CCoord){
            int3 RCoord = CSToRS(CCoord);
            return entityPath + "Region_" + RCoord.x + "_" + RCoord.y + "_" + RCoord.z + '/' +
                   "Chunk_" + CCoord.x + "_" + CCoord.y + "_" + CCoord.z + fileExtension;
        }

        public string GetMapRegionPath(int3 CCoord) => MapRegionPath(CSToRS(CCoord));
        public string GetEntityRegionPath(int3 CCoord) => EntityRegionPath(CSToRS(CCoord));

        private string MapRegionPath(int3 RCoord) => chunkPath + "Region_" + RCoord.x + "_" + RCoord.y + "_" + RCoord.z + '/';
        private string EntityRegionPath(int3 RCoord) => entityPath + "Region_" + RCoord.x + "_" + RCoord.y + "_" + RCoord.z + '/';
    }

    private struct ChunkRegion{
        public int3 RCoord;
        public HashSet<int3> MapChunks;
        public HashSet<int3> EntityChunks;
        public bool active;

        public ChunkRegion(int3 RCoord){
            this.RCoord = RCoord;
            MapChunks = new HashSet<int3>();
            EntityChunks = new HashSet<int3>();
            active = true;
        }
    }
}