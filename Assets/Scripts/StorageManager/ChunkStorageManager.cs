using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;
using static EndlessTerrain;
using System.Threading.Tasks;
using System.Text;
using System;
using System.IO.Compression;
using System.Security.Cryptography;
using Utils;
using System.Linq;

/*
Chunk File Layout:
Header: [Material Offset][Entity Offset]
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
    public delegate void OnReadComplete(bool isComplete, CPUDensityManager.MapData[] chunk = default);
    public delegate void OnWriteComplete(bool isComplete);

    public static string filePath;
    private const string fileExtension = ".bin";
    private static int numLODs;
    private static int maxChunkSize;

    public static void Initialize(){
        numLODs = meshSkipTable.Length;
        maxChunkSize = WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value.mapChunkSize;
        filePath = WorldStorageHandler.WORLD_SELECTION.First.Value.Path + "/MapData/";
    }

    public static void SaveChunkToBin(NativeArray<CPUDensityManager.MapData> chunk, int offset, int3 CCoord, OnWriteComplete OnSaveComplete = null){
        string fileAdd = filePath + "Chunk_" + CCoord.x + "_" + CCoord.y + "_" + CCoord.z + fileExtension;

        if (!Directory.Exists(filePath))
            Directory.CreateDirectory(filePath);

        Task.Run(() => SaveChunkToBinAsync(fileAdd, chunk, offset, OnSaveComplete)); //fire and forget
    }

    public static void SaveChunkToBinSync(NativeArray<CPUDensityManager.MapData> chunk, int offset, int3 CCoord){
        string fileAdd = filePath + "Chunk_" + CCoord.x + "_" + CCoord.y + "_" + CCoord.z + fileExtension;

        if (!Directory.Exists(filePath))
            Directory.CreateDirectory(filePath);

        SaveChunkToBinSync(fileAdd, chunk, offset); //fire and forget
    }
    
    // Start is called before the first frame update
    public static async void SaveChunkToBinAsync(string fileAdd, NativeArray<CPUDensityManager.MapData> chunk, int offset, OnWriteComplete OnSaveComplete = null)
    {
        using (FileStream fs = File.Create(fileAdd))
        {
            MemoryStream headerStream = WriteChunkHeader(chunk, offset);
            headerStream.Seek(0, SeekOrigin.Begin);
            await headerStream.CopyToAsync(fs);
            headerStream.Close();

            MemoryStream mapStream = WriteChunkMaps(chunk, offset);
            mapStream.Seek(0, SeekOrigin.Begin);
            await mapStream.CopyToAsync(fs);
            mapStream.Close();

            await fs.FlushAsync();
            fs.Close();
        }
        OnSaveComplete?.Invoke(true);
    }

    public static void SaveChunkToBinSync(string fileAdd, NativeArray<CPUDensityManager.MapData> chunk, int offset)
    {
        using (FileStream fs = File.Create(fileAdd))
        {
            MemoryStream headerStream = WriteChunkHeader(chunk, offset);
            headerStream.Seek(0, SeekOrigin.Begin);
            headerStream.CopyTo(fs);
            headerStream.Close();
            fs.Flush();

            MemoryStream mapStream = WriteChunkMaps(chunk, offset);
            mapStream.Seek(0, SeekOrigin.Begin);
            mapStream.CopyTo(fs);
            mapStream.Close();

            fs.Flush();
            fs.Close();
        }
    }

    public static void ReadChunkBin(int3 CCoord, int LoD, OnReadComplete ReadCallback = null){
        string fileAdd = filePath + "Chunk_" + CCoord.x + "_" + CCoord.y + "_" + CCoord.z + fileExtension;
        if(!File.Exists(fileAdd))
        {
            ReadCallback?.Invoke(false);
            return;
        }

        Task.Run(() => ReadChunkBinAsync(fileAdd, LoD, ReadCallback)); //fire and forget
    }

    public static async void ReadChunkBinAsync(string fileAdd, int LoD, OnReadComplete ReadCallback = null)
    {
        CPUDensityManager.MapData[] map;
        //Caller has to copy for persistence
        using (FileStream fs = File.Open(fileAdd, FileMode.Open, FileAccess.Read))
        {
            uint size = ReadChunkHeader(fs, out ChunkHeader header);
            fs.Seek(size, SeekOrigin.Begin);
            map = await ReadChunkMap(fs, LoD);
            DeserializeMaterials(ref map, ref header);
        }
        ReadCallback?.Invoke(true, map);
    }

    static string[] SerializeMaterials(NativeArray<CPUDensityManager.MapData> chunk, int offset){
        Dictionary<int, int> MaterialDict = new Dictionary<int, int>();

        for(int x = 0; x < maxChunkSize; x++){
            for(int y = 0; y < maxChunkSize; y++){
                for(int z = 0; z < maxChunkSize; z++){
                    int readPos = CustomUtility.indexFromCoord(x, y, z, maxChunkSize);
                    CPUDensityManager.MapData mapPt = chunk[offset + readPos];
                    MaterialDict.TryAdd(mapPt.material, MaterialDict.Count);
                    mapPt.material = MaterialDict[mapPt.material];
                    chunk[offset + readPos] = mapPt;
                }
            }
        }

        string[] dict = new string[MaterialDict.Count];
        var reg = WorldStorageHandler.WORLD_OPTIONS.Generation.Materials.value.MaterialDictionary;
        foreach(var pair in MaterialDict){
            dict[pair.Value] = reg.RetrieveName(pair.Key);
        }
        return dict;
    }

    private static void DeserializeMaterials(ref CPUDensityManager.MapData[] map, ref ChunkHeader header){
        var reg = WorldStorageHandler.WORLD_OPTIONS.Generation.Materials.value.MaterialDictionary;
        for(int i = 0; i < map.Length; i++){
            map[i].material = reg.RetrieveIndex(header.RegisterNames[(int)map[i].material]);
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

    private static MemoryStream WriteChunkHeader(NativeArray<CPUDensityManager.MapData> chunk, int offset){
        MemoryStream ms = new MemoryStream();
        ms.Seek(4, SeekOrigin.Begin);
        ChunkHeader header = new ChunkHeader{ RegisterNames = SerializeMaterials(chunk, offset).ToList() };
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(header);
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
        header = Newtonsoft.Json.JsonConvert.DeserializeObject<ChunkHeader>(sr.ReadToEnd());
        return size; //add 4 for the size of the size of the header(yeah lol)
    }

    struct ChunkHeader{
        public List<string> RegisterNames;
        //public List<EntityData> Entities;
    }

    //IT IS CALLERS RESPONSIBILITY TO DISPOSE MEMORY STREAM
    private static MemoryStream WriteChunkMaps(NativeArray<CPUDensityManager.MapData> chunk, int offset){
        MemoryStream ms = new MemoryStream();
        uint headerSize = (uint)(numLODs + 1) * 4;

        byte[] fBuffer = new byte[headerSize]; uint fBPos = 0;
        ms.Write(fBuffer, 0, fBuffer.Length);
        WriteUInt32(fBuffer, ref fBPos, 0);
        for(int LoD = numLODs-1; LoD >= 0; LoD--)
        {
            int skipInc = meshSkipTable[LoD];
            int numPointsAxis = maxChunkSize / skipInc + 1;
            int numPoints = numPointsAxis * numPointsAxis * numPointsAxis;

            var mBuffer = new byte[numPoints * 4]; uint mBPos = 0;
            for(int x = 0; x < maxChunkSize; x += skipInc){
                for(int y = 0; y < maxChunkSize; y += skipInc){
                    for(int z = 0; z < maxChunkSize; z += skipInc){
                        int readPos = CustomUtility.indexFromCoord(x, y, z, maxChunkSize);
                        CPUDensityManager.MapData mapPt = chunk[offset + readPos];

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
            WriteUInt32(fBuffer, ref fBPos, (uint)ms.Length - headerSize);
        }
        ms.Seek(0, SeekOrigin.Begin);
        ms.Write(fBuffer);
        ms.Flush();
        return ms;
    }

    private async static Task<CPUDensityManager.MapData[]> ReadChunkMap(FileStream fs, int LoD){
        int skipInc = meshSkipTable[LoD];
        int numPointsAxis = maxChunkSize / skipInc;
        int numPoints = numPointsAxis * numPointsAxis * numPointsAxis;
        CPUDensityManager.MapData[] outStream = new CPUDensityManager.MapData[numPoints];

        uint headerSize = (uint)(numLODs + 1) * 4;
        byte[] readOffsets = new byte[headerSize];
        fs.Read(readOffsets);
        uint readStart = ReadUInt32(readOffsets, (uint)(numLODs - (LoD + 1)) * 4);
        fs.Seek(readStart, SeekOrigin.Current);
    
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
