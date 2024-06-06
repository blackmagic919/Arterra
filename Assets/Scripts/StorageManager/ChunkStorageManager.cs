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

public static class ChunkStorageManager
{
    public delegate void OnReadComplete(bool isComplete, CPUDensityManager.MapData[] chunk = default);
    public delegate void OnWriteComplete(bool isComplete);

    private static string filePath = Application.persistentDataPath + "/MapData/";
    private const string fileExtension = ".bin";
    private static readonly int numLODs;
    private static readonly int headerSize;
    private static readonly int maxChunkSize;

    static ChunkStorageManager(){
        numLODs = meshSkipTable.Length;
        maxChunkSize = mapChunkSize;
        headerSize = (numLODs + 1) * 4;
    }

    public static void SaveChunkToBin(NativeArray<CPUDensityManager.MapData> chunk, int offset, int3 CCoord, OnWriteComplete OnSaveComplete = null){
         string fileAdd = filePath + "Chunk_" + CCoord.x + "_" + CCoord.y + "_" + CCoord.z + fileExtension;

        if (!Directory.Exists(filePath))
            Directory.CreateDirectory(filePath);

        Task.Run(() => SaveChunkToBinAsync(fileAdd, chunk, offset, OnSaveComplete)); //fire and forget
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
    
    // Start is called before the first frame update
    public static async void SaveChunkToBinAsync(string fileAdd, NativeArray<CPUDensityManager.MapData> chunk, int offset, OnWriteComplete OnSaveComplete = null)
    {
        using (FileStream fs = File.Create(fileAdd))
        {
            byte[] fBuffer = new byte[(numLODs+1) * 4]; uint fBPos = 0;
            WriteUInt32(fBuffer, ref fBPos, 0);

            using(MemoryStream ms = new MemoryStream())
            {
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
                    WriteUInt32(fBuffer, ref fBPos, (uint)ms.Length);
                }

                ms.Seek(0, SeekOrigin.Begin);
                await fs.WriteAsync(fBuffer);
                await ms.CopyToAsync(fs);
            }
            await fs.FlushAsync();
            fs.Close();
        }
        OnSaveComplete?.Invoke(true);
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
        int skipInc = meshSkipTable[LoD];
        int numPointsAxis = maxChunkSize / skipInc;
        int numPoints = numPointsAxis * numPointsAxis * numPointsAxis;
        CPUDensityManager.MapData[] outStream = new CPUDensityManager.MapData[numPoints];
        //Caller has to copy for persistence
        using (FileStream fs = File.Open(fileAdd, FileMode.Open, FileAccess.Read))
        {
            byte[] readOffsets = new byte[headerSize];
            await fs.ReadAsync(readOffsets);
            uint readStart = ReadUInt32(readOffsets, (uint)(numLODs - (LoD + 1)) * 4) + (uint)headerSize;
            uint readEnd = ReadUInt32(readOffsets, (uint)(numLODs - LoD) * 4) + (uint)headerSize;
            fs.Seek(readStart, SeekOrigin.Begin);

            using(GZipStream zs = new GZipStream(fs, CompressionMode.Decompress, true))
            {
                byte[] buffer = new byte[4 * numPoints]; uint bufferPos = 0;
                await zs.ReadAsync(buffer);

                for(int index = 0; index < numPoints; index++)
                {
                    CPUDensityManager.MapData mapPt = new CPUDensityManager.MapData
                    {
                        data = (uint)(buffer[bufferPos + 0] | buffer[bufferPos + 1] << 8 | buffer[bufferPos + 2] << 16 | buffer[bufferPos + 3] << 24)
                    };

                    outStream[index] = mapPt;
                    bufferPos += 4;
                }
                zs.Close();
            }
            fs.Close();
        }
        ReadCallback?.Invoke(true, outStream);
    }

    /*
    //Seperate LoDs
    for(int skipInc = 1 << numLODs, numPoints = maxChunkSize/skipInc; skipInc > 0; skipInc >>= 1, numPoints <<=1)
    {
        for(int x = 0; x <= numPoints; x++)
        {
            for(int y = 0; y <= numPoints; y++)
            {
                for(int z = ~((x | y | (skipInc >> numLODs))&0x1), zD = z+1; z <= numPoints; z += zD)
                {
                    int index = x * skipInc + y * skipInc * maxChunkSize + z * skipInc * maxChunkSize * maxChunkSize;
                    uint value = ((uint)chunk[index].material & 0xFFFF) | (uint)(chunk[index].viscosity * 256) << 16 | (uint)(chunk[index].density * 256) << 24;
                    writer.Write(value);
                }
            }
        }
    }
    */
}
