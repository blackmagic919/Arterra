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
using UnityEngine;

/*
Chunk File Layout:
Header: [Material Size][Entity Offset]
//Material List 
+Material Offset:
//Entity List
+Entity Offset: [LOD0 Offset]...[LODn Offset]
//LOD0 Data
+LOD0 Offset: [Data]
...
+LODn Offset: [Data]
//Lists and headers are zipped, offsets are not zipped
*/

public static class ChunkStorageManager
{
    public delegate void OnReadComplete(ReadbackInfo info);
    public delegate void OnWriteComplete();

    public static string filePath;
    private const string fileExtension = ".bin";
    private static int maxChunkSize;

    public static void Initialize(){
        maxChunkSize = WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value.mapChunkSize;
        filePath = WorldStorageHandler.WORLD_SELECTION.First.Value.Path + "/MapData/";
    }

    public static void SaveChunkToBin(ChunkPtr chunk, List<Entity> entities, int3 CCoord, OnWriteComplete OnSaveComplete = null){
        string fileAdd = filePath + "Chunk_" + CCoord.x + "_" + CCoord.y + "_" + CCoord.z + fileExtension;

        if (!Directory.Exists(filePath))
            Directory.CreateDirectory(filePath);

        Task.Run(() => SaveChunkToBinAsync(fileAdd, chunk, entities, OnSaveComplete)); //fire and forget
    }

    public static void SaveChunkToBinSync(ChunkPtr chunk, List<Entity> entities, int3 CCoord){
        string fileAdd = filePath + "Chunk_" + CCoord.x + "_" + CCoord.y + "_" + CCoord.z + fileExtension;
        
        if (!Directory.Exists(filePath))
            Directory.CreateDirectory(filePath);

        SaveChunkToBinSync(fileAdd, chunk, entities); //fire and forget
    }
    
    // Start is called before the first frame update
    public static async void SaveChunkToBinAsync(string fileAdd, ChunkPtr chunk, List<Entity> entities, OnWriteComplete OnSaveComplete = null)
    {
        using (FileStream fs = File.Create(fileAdd))
        {
            MemoryStream headerStream = WriteChunkHeader(chunk, entities);
            headerStream.Seek(0, SeekOrigin.Begin);
            await headerStream.CopyToAsync(fs);
            headerStream.Close();

            if(chunk.isDirty){
                MemoryStream mapStream = WriteChunkMaps(chunk.data, chunk.offset);
                mapStream.Seek(0, SeekOrigin.Begin);
                await mapStream.CopyToAsync(fs);
                mapStream.Close();
            }

            await fs.FlushAsync();
            fs.Close();
        }
        OnSaveComplete?.Invoke();
    }

    public static void SaveChunkToBinSync(string fileAdd, ChunkPtr chunk, List<Entity> entities)
    {
        using (FileStream fs = File.Create(fileAdd))
        {
            MemoryStream headerStream = WriteChunkHeader(chunk, entities);
            headerStream.Seek(0, SeekOrigin.Begin);
            headerStream.CopyTo(fs);
            headerStream.Close();
            fs.Flush();

            if(chunk.isDirty){
                MemoryStream mapStream = WriteChunkMaps(chunk.data, chunk.offset);
                mapStream.Seek(0, SeekOrigin.Begin);
                mapStream.CopyTo(fs);
                mapStream.Close();

                fs.Flush();
            }
            fs.Close();
        }
    }

    public static void ReadChunkBin(int3 CCoord, OnReadComplete ReadCallback = null){
        string fileAdd = filePath + "Chunk_" + CCoord.x + "_" + CCoord.y + "_" + CCoord.z + fileExtension;
        if(!File.Exists(fileAdd))
        {
            ReadCallback?.Invoke(new ReadbackInfo(false));
            return;
        }

        Task.Run(() => ReadChunkBinAsync(fileAdd, ReadCallback)); //fire and forget
    }

    public static async void ReadChunkBinAsync(string fileAdd, OnReadComplete ReadCallback = null)
    {
        ReadbackInfo info = new ReadbackInfo(true);
        //Caller has to copy for persistence
        using (FileStream fs = File.Open(fileAdd, FileMode.Open, FileAccess.Read))
        {
            uint size = ReadChunkHeader(fs, out ChunkHeader header);
            if(header.RegisterNames != null){ //if it's null, the chunk isn't dirty
                fs.Seek(size, SeekOrigin.Begin);
                info.map = await ReadChunkMap(fs);
                DeserializeHeader(ref info.map, ref header);
            }
            info.entities = header.Entities;
        }
        ReadCallback?.Invoke(info);
    }

    static ChunkHeader SerializeHeader(ChunkPtr chunk, List<Entity> entities){

        //Serialize Entities
        List<EntitySerial> eSerial = new List<EntitySerial>();
        var eReg = WorldStorageHandler.WORLD_OPTIONS.Generation.Entities;
        for(int i = 0; i < entities.Count; i++){
            EntitySerial entity = new ();
            entity.type = eReg.RetrieveName((int)entities[i].info.entityType);
            entity.data = (IEntity)Marshal.PtrToStructure(entities[i].obj, eReg.Retrieve(entity.type).Entity.GetType());
            eSerial.Add(entity);
            //Debug.Log("Serial");
        }

        if(!chunk.isDirty) return new ChunkHeader{ RegisterNames = null, Entities = eSerial };

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

        return new ChunkHeader{ RegisterNames = dict.ToList(), Entities = eSerial };
    }

    private static void DeserializeHeader(ref MapData[] map, ref ChunkHeader header){
        var mReg = WorldStorageHandler.WORLD_OPTIONS.Generation.Materials.value.MaterialDictionary;
        for(int i = 0; i < map.Length; i++){
            map[i].material = mReg.RetrieveIndex(header.RegisterNames[(int)map[i].material]);
        }

        //Entities are already deserialized by a custom JsonConverter.
        //It needs to be done this way because to Newtonsoft needs to know the structure's real type(not interface)
        //to even deserialize it into a ChunkHeader in the first place
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

    private static MemoryStream WriteChunkHeader(ChunkPtr chunk, List<Entity> entities = null){
        MemoryStream ms = new MemoryStream();
        ms.Seek(4, SeekOrigin.Begin);
        ChunkHeader header = SerializeHeader(chunk, entities);

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

    private static uint ReadChunkHeader(FileStream fs, out ChunkHeader header){
        byte[] readSize = new byte[4]; fs.Read(readSize); 
        uint size = ReadUInt32(readSize, 0);
        byte[] buffer = new byte[size]; fs.Read(buffer);

        using MemoryStream ms = new(buffer);
        using GZipStream zs = new(ms, CompressionMode.Decompress, true);
        using StreamReader sr = new(zs);
        header = JsonConvert.DeserializeObject<ChunkHeader>(sr.ReadToEnd());
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
    public List<EntitySerial> Entities;
}

public class EntitySerial{
    public string type;
    public IEntity data;
}

public struct ReadbackInfo{
    public bool isComplete;
    public List<EntitySerial> entities;
    public MapData[] map;

    public ReadbackInfo(bool isComplete){
        this.isComplete = isComplete;
        entities = null;
        map = null;
    }

    public ReadbackInfo(bool isComplete, MapData[] map, List<EntitySerial> entities){
        this.isComplete = isComplete;
        this.entities = entities;
        this.map = map;
    }
}
