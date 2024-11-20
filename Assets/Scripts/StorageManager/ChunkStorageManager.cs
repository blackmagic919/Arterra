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

    public static string chunkPath;
    public static string entityPath;
    private const string fileExtension = ".bin";
    private static int maxChunkSize;

    public static void Initialize(){
        maxChunkSize = WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value.mapChunkSize;
        chunkPath = WorldStorageHandler.WORLD_SELECTION.First.Value.Path + "/MapData/";
        entityPath = WorldStorageHandler.WORLD_SELECTION.First.Value.Path + "/EntityData/";
    }

    public static void SaveEntitiesToJsonSync(List<Entity> entities, int3 CCoord){
        string fileAdd = entityPath + "EChunk_" + CCoord.x + "_" + CCoord.y + "_" + CCoord.z + fileExtension;
        if(!Directory.Exists(entityPath))
            Directory.CreateDirectory(entityPath);
        
        SaveEntityToJsonSync(fileAdd, entities);
    }

    public static void SaveChunkToBinSync(ChunkPtr chunk, int3 CCoord){
        string fileAdd = chunkPath + "Chunk_" + CCoord.x + "_" + CCoord.y + "_" + CCoord.z + fileExtension;
        
        if (!Directory.Exists(chunkPath))
            Directory.CreateDirectory(chunkPath);

        SaveChunkToBinSync(fileAdd, chunk); //fire and forget
    }

    public static void SaveChunkToBinSync(string fileAdd, ChunkPtr chunk)
    {
        using (FileStream fs = File.Create(fileAdd))
        {
            MemoryStream headerStream = WriteChunkHeader(() => SerializeHeader(chunk));
            headerStream.Seek(0, SeekOrigin.Begin);
            headerStream.CopyTo(fs);
            headerStream.Close();
            fs.Flush();

            MemoryStream mapStream = WriteChunkMaps(chunk.data, chunk.offset);
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
            MemoryStream headerStream = WriteChunkHeader(() => SerializeEntities(entities));
            headerStream.Seek(0, SeekOrigin.Begin);
            headerStream.CopyTo(fs);
            headerStream.Close();
            fs.Flush();
            fs.Close();
        }
    }

    public static void ReadChunkInfo(int3 CCoord, OnReadComplete ReadCallback = null){
        Task.Run(() => ReadChunkInfoAsync(CCoord, ReadCallback));
    }

    private static async void ReadChunkInfoAsync(int3 CCoord, OnReadComplete ReadCallback = null){
        ReadbackInfo info = new ReadbackInfo(false);
        string chunkAdd = chunkPath + "Chunk_" + CCoord.x + "_" + CCoord.y + "_" + CCoord.z + fileExtension;
        string entityAdd = entityPath + "EChunk_" + CCoord.x + "_" + CCoord.y + "_" + CCoord.z + fileExtension;
        if(File.Exists(chunkAdd))
        {
            info.map = await ReadChunkBinAsync(chunkAdd);
        }
        if(File.Exists(entityAdd))
        {
            info.entities = ReadEntityJsonAsync(entityAdd);
        }
        ReadCallback?.Invoke(info);
    }

    public static async Task<MapData[]> ReadChunkBinAsync(string fileAdd)
    {
        MapData[] map = null;
        //Caller has to copy for persistence
        using (FileStream fs = File.Open(fileAdd, FileMode.Open, FileAccess.Read))
        {
            uint size = ReadChunkHeader(fs, out ChunkHeader header);
            fs.Seek(size, SeekOrigin.Begin);
            map = await ReadChunkMap(fs);
            DeserializeHeader(ref map, ref header);
        }
        return map;
    }

    public static List<EntitySerial> ReadEntityJsonAsync(string fileAdd)
    {
        List<EntitySerial> entities = null;
        //Caller has to copy for persistence
        using (FileStream fs = File.Open(fileAdd, FileMode.Open, FileAccess.Read))
        {
            ReadChunkHeader(fs, out entities);
            //It's automatically deserialized by a custom json rule
        }
        return entities;
    }

    static List<EntitySerial> SerializeEntities(List<Entity> entities){
        List<EntitySerial> eSerial = new List<EntitySerial>();
        var eReg = WorldStorageHandler.WORLD_OPTIONS.Generation.Entities;
        for(int i = 0; i < entities.Count; i++){
            EntitySerial entity = new ();
            entity.type = eReg.RetrieveName((int)entities[i].info.entityType);
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
        var mReg = WorldStorageHandler.WORLD_OPTIONS.Generation.Materials.value.MaterialDictionary;
        foreach(var pair in RegisterDict){
            dict[pair.Value] = mReg.RetrieveName(pair.Key);
        }

        return new ChunkHeader{ RegisterNames = dict.ToList() };
    }

    private static void DeserializeHeader(ref MapData[] map, ref ChunkHeader header){
        var mReg = WorldStorageHandler.WORLD_OPTIONS.Generation.Materials.value.MaterialDictionary;
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

    private static MemoryStream WriteChunkHeader(Func<object> GetHeader){
        MemoryStream ms = new MemoryStream();
        ms.Seek(4, SeekOrigin.Begin);
        object header = GetHeader();

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
    private static MemoryStream WriteChunkMaps(NativeArray<MapData> chunk, int offset){
        MemoryStream ms = new MemoryStream();

        int numPointsAxis = maxChunkSize;
        int numPoints = numPointsAxis * numPointsAxis * numPointsAxis;

        var mBuffer = new byte[numPoints * 4]; uint mBPos = 0;
        for(int x = 0; x < maxChunkSize; x++){
            for(int y = 0; y < maxChunkSize; y++){
                for(int z = 0; z < maxChunkSize; z++){
                    int readPos = CustomUtility.indexFromCoord(x, y, z, maxChunkSize);
                    MapData mapPt = chunk[offset + readPos];

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
        ms.Flush();
        return ms;
    }

    private static async Task<MapData[]> ReadChunkMap(FileStream fs){
        int numPointsAxis = maxChunkSize;
        int numPoints = numPointsAxis * numPointsAxis * numPointsAxis;
        MapData[] outStream = new MapData[numPoints];
    
        using(GZipStream zs = new GZipStream(fs, CompressionMode.Decompress, true))
        {
            byte[] buffer = new byte[4 * numPoints]; uint bufferPos = 0;
            await zs.ReadAsync(buffer);

            for(int index = 0; index < numPoints; index++)
            {
                outStream[index] = new CPUDensityManager.MapData{
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

}

public struct ChunkHeader{
    public List<string> RegisterNames;
}

public struct EntitySerial{
    public string type;
    public IEntity data;
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
