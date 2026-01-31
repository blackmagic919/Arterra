
using UnityEngine;

namespace Arterra.Utils {
    public static class ShaderIDProps {
        public static readonly int LocalToWorld = Shader.PropertyToID("_LocalToWorld");
        public static readonly int StorageMemory = Shader.PropertyToID("_StorageMemory");
        public static readonly int AddressDict = Shader.PropertyToID("_AddressDict");
        public static readonly int AddressIndex = Shader.PropertyToID("addressIndex");
        // Memory Buffer Handler
        public static readonly int CountOffset = Shader.PropertyToID("countOffset");
        public static readonly int AllocStride = Shader.PropertyToID("allocStride");
        public static readonly int AllocCount = Shader.PropertyToID("allocCount");
        public static readonly int NumAddress = Shader.PropertyToID("numAddress");
        //Memory Occupancy Balancer
        public static readonly int SourceMemory = Shader.PropertyToID("_SourceMemory");
        public static readonly int Heap = Shader.PropertyToID("_Heap");
        public static readonly int BufferIndex = Shader.PropertyToID("buffIndex");
        public static readonly int BufferSize4Bytes = Shader.PropertyToID("_BufferSize4Bytes");
        // Count To Args
        public static readonly int Count = Shader.PropertyToID("count");
        public static readonly int Args = Shader.PropertyToID("args");
        public static readonly int NumThreads = Shader.PropertyToID("numThreads");
        // Geoshaders
        public static readonly int MemoryBuffer = Shader.PropertyToID("_MemoryBuffer");
        public static readonly int SCStart = Shader.PropertyToID("SCStart");
        public static readonly int SCEnd = Shader.PropertyToID("SCEnd");
        public static readonly int Vertices = Shader.PropertyToID("Vertices");
        public static readonly int Triangles = Shader.PropertyToID("Triangles");
        public static readonly int TriAddress = Shader.PropertyToID("triAddress");
        public static readonly int VertAddress = Shader.PropertyToID("vertAddress");
        public static readonly int NumSubChunkRegions = Shader.PropertyToID("numSubChunkRegions");
        public static readonly int StartSChunkP = Shader.PropertyToID("bSTART_sChunkP");
        public static readonly int CountOGeo = Shader.PropertyToID("bCOUNT_oGeo");
        public static readonly int DetailLevel = Shader.PropertyToID("detailLevel");
        public static readonly int ArgOffset = Shader.PropertyToID("argOffset");
        public static readonly int SubChunkInd = Shader.PropertyToID("SubChunkInd");
        public static readonly int MemoryBufferBase = Shader.PropertyToID("_MemoryBufferBase");
        public static readonly int SourceVertices = Shader.PropertyToID("SourceVertices");
        public static readonly int SourceTriangles = Shader.PropertyToID("SourceTriangles");
        public static readonly int ScaleInverse = Shader.PropertyToID("ScaleInverse");
        //Clear Range
        public static readonly int Counters = Shader.PropertyToID("counters");
        public static readonly int Length = Shader.PropertyToID("length");
        public static readonly int Start = Shader.PropertyToID("start");
        //Copy Count
        public static readonly int Source = Shader.PropertyToID("source");
        public static readonly int Destination = Shader.PropertyToID("destination");
        public static readonly int ReadOffset = Shader.PropertyToID("rOffset");
        public static readonly int WriteOffset = Shader.PropertyToID("wOffset");
        //Nosie sample data
        public static readonly int SampleOffset = Shader.PropertyToID("sOffset");
        public static readonly int SkipInc = Shader.PropertyToID("skipInc");
        //Map Generator
        public static readonly int CCoord = Shader.PropertyToID("CCoord");
        public static readonly int NumPointsPerAxis = Shader.PropertyToID("numPointsPerAxis");
        public static readonly int NumCubesPerAxis = Shader.PropertyToID("numCubesPerAxis");
        public static readonly int NumTransFaces = Shader.PropertyToID("numTransFaces");
        public static readonly int MapChunkSize = Shader.PropertyToID("mapChunkSize");
        public static readonly int DefaultAddress = Shader.PropertyToID("defAddress");
        public static readonly int IsoLevel = Shader.PropertyToID("IsoLevel");
        //Async Mesh Readback
        public static readonly int CountTri = Shader.PropertyToID("bCOUNT_Tri");
        public static readonly int StartTri = Shader.PropertyToID("bSTART_Tri");
        public static readonly int BufferCounter = Shader.PropertyToID("bCOUNTER");
        //GPU Map Manager
        public static readonly int ChunkData = Shader.PropertyToID("chunkData");
        public static readonly int StartRead = Shader.PropertyToID("bSTART_read");
        public static readonly int SizeReadAxis = Shader.PropertyToID("sizeRdAxis");
        public static readonly int SizeWriteAxis = Shader.PropertyToID("sizeWrAxis");
        public static readonly int Offset = Shader.PropertyToID("offset");
        public static readonly int OriginCCoord = Shader.PropertyToID("oCCoord");
        public static readonly int StartMap = Shader.PropertyToID("bSTART_map");
        public static readonly int ChunkAddressDict = Shader.PropertyToID("_ChunkAddressDict");
        public static readonly int ChunkInfoBuffer = Shader.PropertyToID("_ChunkInfoBuffer");
        public static readonly int Dimension = Shader.PropertyToID("dimension");
        public static readonly int ChunkIncrement = Shader.PropertyToID("chunkInc");
        public static readonly int StartOffset = Shader.PropertyToID("bOffset");
        public static readonly int LerpScale = Shader.PropertyToID("lerpScale");
        public static readonly int NumChunksAxis = Shader.PropertyToID("numChunksAxis");

        //Surface Map Generator
        public static readonly int SurfaceMemoryBuffer = Shader.PropertyToID("_SurfMemoryBuffer");
        public static readonly int SurfaceAddress = Shader.PropertyToID("surfAddress");
        public static readonly int NumPoints = Shader.PropertyToID("numPoints");
        //Structures
        public static readonly int BaseDepthLoD = Shader.PropertyToID("BaseDepthL");
        public static readonly int StructureStride = Shader.PropertyToID("STRUCTURE_STRIDE_4BYTE");
        public static readonly int StructureCount = Shader.PropertyToID("structCount");
        public static readonly int MetaMemoryBuffer = Shader.PropertyToID("_MetaMemoryBuffer");
        public static readonly int MetaAddressIndex = Shader.PropertyToID("mAddressIndex");
        //Entities
        public static readonly int StartBiome = Shader.PropertyToID("bSTART_biome");
        //GenInfo Readback
        public static readonly int DestMemory = Shader.PropertyToID("_DestMemory");
        public static readonly int NewAddressIndex = Shader.PropertyToID("nAddressIndex");
        public static readonly int TempCounter = Shader.PropertyToID("bCOUNT_temp");
    }
}
