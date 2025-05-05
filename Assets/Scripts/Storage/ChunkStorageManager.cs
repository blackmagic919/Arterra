using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using System.Text;
using System.IO.Compression;
using Utils;

using static CPUMapManager;
using System.Linq;
using Newtonsoft.Json;
using System;
using TerrainGeneration;
using WorldConfig;
using WorldConfig.Generation.Entity;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using System.Buffers;
using UnityEngine.Profiling;

/*
Chunk File Layout:
Header: [Material Size][Entity Offset]
//Material List 
+Material Offset:
//Chunk Data
//Lists and headers are zipped, offsets are not zipped
*/

namespace MapStorage{

/// <summary> Manages the access, loading and storage of world-specific data from an external file system location
/// to in-game memory. Concurrently, it also defines the structure of the data that is stored in terms of both
/// format, encoding, and file-structure within the file system.
/// </summary>
public static class Chunk
{

    private static int maxChunkSize;
    private static ChunkFinder chunkFinder;

    /// <summary> Initializes the Chunk Storage Manager. This should be called once at the start of the game.
    /// Sets up information relavent to file formatting, and the <see cref="ChunkFinder"/>. </summary>
    public static void Initialize(){
        maxChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
        chunkFinder = new ChunkFinder();
    }

    /// <summary>Saves a list of entities associated with a chunk to the file system at the appropriate location.
    /// Entities associated with a chunk are saved in a compressed json format to the correct location for
    /// the chunk within the world's file system. See <see cref="ChunkFinder.entityPath"/> for more information </summary>
    /// <param name="entities">A list containing all <see cref="Entity">Entities</see> associated with the chunk(typically within its bounds). </param>
    /// <param name="CCoord">The coordinate in chunk space of the Chunk associated with the entities. 
    /// This information used to identify the location to store the resulting file(s). </param>
    public static void SaveEntitiesToJsonSync(List<Entity> entities, int3 CCoord){
        try{
            string entityPath = chunkFinder.GetEntityRegionPath(CCoord);
            if(!Directory.Exists(entityPath))
                Directory.CreateDirectory(entityPath);

            string fileAdd = chunkFinder.GetEntityPath(CCoord);
            chunkFinder.TryAddEntity(CCoord);
            SaveEntityToJsonSync(fileAdd, entities);
        } catch(Exception e){
            Debug.Log($"Failed on Saving Entity Data for Chunk: {CCoord} with exception {e}");
        }
    }

    /// <summary>Saves a chunk's map information to the file system at the appropriate location. 
    /// The chunk's map information is saved in a multi-resolution compressed binary format
    /// to the correct location for the chunk within the world's file system(<see cref="ChunkFinder.chunkPath"/>).  
    /// See <see cref="ChunkHeader"/> for information on this format. </summary>
    /// <param name="chunk">A <see cref="ChunkPtr"/> referencing the chunk's map in memory</param>
    /// <param name="CCoord">The coordinate in chunk space of the Chunk associated with the map 
    /// information. Used to identify the location to store the resulting file(s). </param>
    public static void SaveChunkToBinSync(ChunkPtr chunk, int3 CCoord){
        try{
            string mapPath = chunkFinder.GetMapRegionPath(CCoord);
            
            if (!Directory.Exists(mapPath))
                Directory.CreateDirectory(mapPath);

            string fileAdd = chunkFinder.GetMapPath(CCoord);
            chunkFinder.TryAddMap(CCoord);
            SaveChunkToBinSync(fileAdd, chunk); 
        } catch(Exception e){
            Debug.Log($"Failed on Saving Chunk Data for Chunk: {CCoord} with exception {e}");
        }
    }

    private static void SaveChunkToBinSync(string fileAdd, ChunkPtr chunk)
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

    private static void SaveEntityToJsonSync(string fileAdd, List<Entity> entities){
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

    /// <summary> Reads a virtual(<see cref="TerrainChunk.VisualChunk"/>) chunk's map information from the file system
    /// and returns it as a linear encoded map. Involves reading multiple files associated with different chunks
    /// with the appropriate resolution and merging them into a single map for the chunk(which spans the collective region).
    /// If a chunk is not found, the map is skipped and a bit is set to indicate that the map information is not dirty and 
    /// should be replaced by any default information. </summary>
    /// <param name="CCoord">The coordinate of the origin of the chunk in chunk space. Used to determine locations
    /// of files containing overlapping chunks in the file system</param>
    /// <param name="depth">The <see cref="TerrainChunk.depth"/> of the visual chunk. Used to determine the size of the chunk, 
    /// and equivalently how many chunks and which resolution levels to sample. A depth of 0 corresponds to a real chunk. </param>
    /// <returns>The linearly encoded map information associated with the chunk. </returns>
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

    /// <summary> Reads all information(entity and map) associated with a <see cref="TerrainChunk.RealChunk"> Real Chunk</see> from the file system 
    /// and returns it as a <see cref="ReadbackInfo"/>. This includes the map information and the entities associated with the chunk.
    /// This functionality is not available for visual chunks as entities for visual chunks is not supported. </summary>
    /// <param name="CCoord">The coordinate in chunk space of the real chunk to be read. Used to locate the
    /// chunk's information in the file system.</param>
    /// <returns>The aggregate information associated with the chunk as a <see cref="ReadbackInfo"/>. </returns>
    public static ReadbackInfo ReadChunkInfo(int3 CCoord){
        ReadbackInfo info = new ReadbackInfo(false);
        Profiler.BeginSample("Reading Chunk");
        if(chunkFinder.TryGetMapChunk(CCoord, out string chunkAdd)) info.map = ReadChunkBin(chunkAdd, 0);
        if(chunkFinder.TryGetEntityChunk(CCoord, out string entityAdd)) info.entities = ReadEntityJson(entityAdd);
        Profiler.EndSample();
        return info;
    }
    
    /// <summary>
    /// Reads the map information associated with a chunk file at the specified address at a certain
    /// resolution. Automatically loads and deserializes the map information by recoupling it to the
    /// current game instance. The size of the read map information is dependant on the 
    /// sampled resolution controlled by <paramref name="depth"/>. 
    /// </summary>
    /// <param name="fileAdd">The path of the chunk file in the file system to be read. Caller should ensure this file exists 
    /// and is a properly formatted chunk-map file. </param>
    /// <param name="depth">The resolution within of the map to be sampled from the file. Chunk files contain multiple
    /// resolutions of their maps compressed seperately, see <see cref="ChunkHeader"/> for more information. </param>
    /// <returns>The linearly encoded map data associated with the requested resolution of the chunk</returns>
    public static MapData[] ReadChunkBin(string fileAdd, int depth)
    {
        try{
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
        } catch (Exception e){
            Debug.Log($"Failed on Reading Chunk Data for Chunk: {fileAdd} with exception {e}");
            return null;
        }
    }

    /// <summary>
    /// Reads the entity information associated with a chunk file at the specified address.
    /// Loads and deserializes the entity information by recoupling it to the current 
    /// game instance.
    /// </summary>
    /// <param name="fileAdd">The path of the entity file in the file system to be read.  Caller should ensure a 
    /// properly formatted entity file exists at this location. </param>
    /// <returns>The list containing all entities associated with the chunk.</returns>
    public static List<Entity> ReadEntityJson(string fileAdd)
    {
        try{
            List<Registerable<Entity>> sEntities = null;
            //Caller has to copy for persistence
            using (FileStream fs = File.Open(fileAdd, FileMode.Open, FileAccess.Read)){
                //It's automatically deserialized by a custom json rule
                ReadChunkHeader(fs, out sEntities);
            }
            return DeserializeEntities(sEntities);
        } catch (Exception e){
            Debug.Log($"Failed on Reading Entity Data for Chunk: {fileAdd} with exception {e}");
            return null;
        }
    }

    static List<Registerable<Entity>> SerializeEntities(List<Entity> entities){
        List<Registerable<Entity>> eSerial = new List<Registerable<Entity>>();
        foreach(Entity entity in entities){ eSerial.Add(new Registerable<Entity>(entity)); }
        return eSerial;
    } 
    static List<Entity> DeserializeEntities(List<Registerable<Entity>> eSerial){
        List<Entity> entities = new List<Entity>();
        foreach(Registerable<Entity> sEntity in eSerial){ entities.Add(sEntity.Value); }
        return entities;
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
        var mReg = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        foreach(var pair in RegisterDict){
            dict[pair.Value] = mReg.RetrieveName(pair.Key);
        }

        return new ChunkHeader{ RegisterNames = dict.ToList() };
    }

    private static void DeserializeHeader(ref MapData[] map, ref ChunkHeader header){
        var mReg = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        int[] materialIndexCache = new int[header.RegisterNames.Count];
        for (int i = 0; i < header.RegisterNames.Count; i++) { materialIndexCache[i] = mReg.RetrieveIndex(header.RegisterNames[i]); }
        for(int i = 0; i < map.Length; i++){ map[i].material = materialIndexCache[(int)map[i].material]; }
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
        byte[] size = new byte[4]; 
        UnsafeUtility.As<byte, uint>(ref size[0]) = (uint)ms.Length;
        ms.Seek(0, SeekOrigin.Begin);
        ms.Write(size);
        ms.Flush();
        return ms;
    }

    private static uint ReadChunkHeader<T>(FileStream fs, out T header){
        byte[] readSize = new byte[4]; fs.Read(readSize); 
        uint size = UnsafeUtility.As<byte, uint>(ref readSize[0]);
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
            var mBuffer = ArrayPool<byte>.Shared.Rent(numPoints * 4);

            uint mBPos = 0;
            unsafe{fixed(byte* destPtr = mBuffer){
                if(skipInc == 1) {
                    UnsafeUtility.MemCpy(destPtr, (MapData*)chunk.data.GetUnsafePtr() + 
                    chunk.offset, numPoints * 4);
                } else {
                for(int x = 0; x < maxChunkSize; x += skipInc){
                for(int y = 0; y < maxChunkSize; y += skipInc){
                for(int z = 0; z < maxChunkSize; z += skipInc){
                    int readPos = CustomUtility.indexFromCoord(x, y, z, maxChunkSize);
                    MapData mapPt = chunk.data[chunk.offset + readPos];

                    //if(!mapPt.isDirty) mapPt.data = 0;
                    UnsafeUtility.As<byte, uint>(ref mBuffer[mBPos]) = mapPt.data;
                    mBPos += 4;
                }}}}
            }}
            //Deals with memory so it's better to not hog all the threads
            using(GZipStream zs = new GZipStream(ms, CompressionMode.Compress, true)){ 
                zs.Write(mBuffer); 
                zs.Flush(); 
            } 
            header.ResolutionOffsets.Add((int)ms.Length);
            ArrayPool<byte>.Shared.Return(mBuffer);
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
            byte[] buffer = new byte[4 * numPoints]; 
            zs.Read(buffer);
            unsafe{
                fixed(byte* inPtr = buffer){
                fixed(MapData* outPtr = outStream){
                UnsafeUtility.MemCpy(outPtr, inPtr, numPoints * 4);
                }}
            }
            zs.Close();
        }
        return outStream;
    }

    /// <summary> A struct representing the header of a chunk's map information file. 
    /// Map information is stored in a multi-resolution compressed binary format. This format is divided in the following way:
    /// - 4Byte-Int(Uncompressed): The exact size of the compressed header in bytes 
    /// - Header(Compressed): The current object 
    /// - Map Data(Compressed): The map data of the chunk, compressed seperately by resolution and stored sequentially starting with the lowest resolution.
    /// The header contains the following two types of information: RegisterNames and ResolutionOffsets </summary>
    public struct ChunkHeader{
        /// <summary>A list of the names of all unique materials used in the chunk. This is used to 
        /// decouple the chunk's materials from the current game-version, allowing the same material to be reloaded
        /// even if the exact index of the material changes.</summary>
        public List<string> RegisterNames;
        /// <summary> A list of offsets in bytes from the end of the compressed header to the start
        /// of each resolution's compressed map data. This can be used to selectively jump to a specific resolution's
        /// map information and only decompress/process it if other resolutions are not needed. </summary>
        public List<int> ResolutionOffsets;
    }

    /// <summary>
    /// A structure wrapping the maximum information a chunk may associate 
    /// on the file system. Not all chunks may have every member defined, but 
    /// every chunk must have at most only these fields populated. Structure
    /// may be partially filled depending on what information exists/can be found.
    /// </summary>
    public struct ReadbackInfo{
        /// <summary>
        /// A list of all deserialized <see cref="Entity">Entities</see> associated 
        /// with the chunk. See <see cref="Entity"/> for more information.
        /// </summary>
        public List<Entity> entities;
        /// <summary>
        /// A list of all deserialized <see cref="MapData">MapData</see> associated
        /// with the chunk. See <see cref="MapData"/> for more information.
        /// </summary>
        public MapData[] map;

        /// <summary>A constructor to initialize a default instance of
        /// <see cref="ReadbackInfo"/> which zeroes all fields. </summary>
        /// <param name="_">A dummy paramater</param>
        public ReadbackInfo(bool _){
            entities = null;
            map = null;
        }

        /// <summary> Creates an instance of <see cref="ReadbackInfo"/> to 
        /// wrap the specified information.</summary>
        /// <param name="map">The list of deserialized <see cref="map">MapData</see> to wrap.</param>
        /// <param name="entities">The list of deserialized <see cref="entities">entities</see> to wrap.</param>
        public ReadbackInfo(MapData[] map, List<Entity> entities){
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
            chunkPath = World.WORLD_SELECTION.First.Value.Path + "/MapData/";
            entityPath = World.WORLD_SELECTION.First.Value.Path + "/EntityData/";

            WorldConfig.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain.value;
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
}}