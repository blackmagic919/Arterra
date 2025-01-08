using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using WorldConfig;
using WorldConfig.Generation;
using WorldConfig.Generation.Material;
using WorldConfig.Generation.Entity;

namespace TerrainGeneration{
/// <summary>
/// By default,information may be stored in settings or on storage where it is
/// likely serialized to be version independent. This class is responsible for acquiring
/// and deserializing all information pertinent to terrain generation from these locations 
/// and copying the necessary information to the GPU for use in the terrain generation, shaders
/// and other systems primarily on the GPU.
/// </summary>
public static class GenerationPreset
{
    private static MaterialHandle materialHandle;
    private static NoiseHandle noiseHandle;
    private static BiomeHandle biomeHandle;
    private static StructHandle structHandle;
    /// <exclude />
    public static EntityHandle entityHandle;
    /// <summary> Holds a reference to a long-term storage GPU buffer used in the terrain generation process. <seealso cref="MemoryHandle"/> </summary>
    public static MemoryHandle memoryHandle;
    /// <summary> Whether or not the GenerationPreset has been <see cref="Initialize"/>d. If not, generation will be unable to occur </summary>
    public static bool active;

    /// <summary>
    /// Initializes the GenerationPreset. Must be called before any generation is done.
    /// This process loads all necessary information from the settings and copies it to the GPU.
    /// </summary>
    public static void Initialize(){
        active = true;
        materialHandle.Initialize();
        noiseHandle.Initialize();
        biomeHandle.Initialize();
        structHandle.Initialize();
        entityHandle.Initialize();
        memoryHandle.Intiialize();
    }

    /// <summary>
    /// Releases all generation information that has been allocated on the GPU and 
    /// elsewhere. Call this method before the program exits to prevent memory leaks.
    /// </summary>
    public static void Release(){
        if(!active) return;
        active = false;

        materialHandle.Release();
        noiseHandle.Release();
        biomeHandle.Release();
        structHandle.Release();
        entityHandle.Release();
        memoryHandle.Release();
    }

    /// <summary>
    /// Responsible for deserializing all material display information as well as copying all textures to the GPU.
    /// Material display information includes information on each material's visual representation as a solid, liquid, 
    /// and gas <seealso cref="MaterialData"/>. Textures are copied from the <see cref="ItemAuthoring"/> registry and should
    /// be referenced in the GPU by their index in that registry.
    /// </summary>
    public struct MaterialHandle{
        const int textureSize = 512;
        const TextureFormat textureFormat = TextureFormat.RGBA32;

        Texture2DArray textureArray;
        ComputeBuffer terrainData;
        ComputeBuffer liquidData;
        ComputeBuffer atmosphericData;

        /// <summary> Initializes the <see cref="MaterialHandle" />. Deserializes and copies all information to 
        /// the GPU for use in the terrain generation process. Information is stored in global GPU buffers 
        /// <c>_MatTerrainData</c>, <c>_MatAtmosphericData</c>, <c>_MatLiquidData</c>, and <c>_Textures</c>. </summary>
        public void Initialize()
        {
            Release();
            Generation matInfo = Config.CURRENT.Generation.Materials.value;
            Registry<Sprite> textureInfo = Config.CURRENT.Generation.Textures;
            Registry<WorldConfig.Quality.GeoShader> shaderInfo = Config.CURRENT.Quality.GeoShaders;
            MaterialData[] MaterialDictionary = matInfo.MaterialDictionary.SerializedData;
            MaterialData.TerrainData[] MaterialTerrain = new MaterialData.TerrainData[MaterialDictionary.Length];

            int numMats = MaterialDictionary.Length;
            terrainData = new ComputeBuffer(numMats, sizeof(float) * 6 + sizeof(int) * 2, ComputeBufferType.Structured);
            atmosphericData = new ComputeBuffer(numMats, sizeof(float) * 6, ComputeBufferType.Structured);
            liquidData = new ComputeBuffer(numMats, sizeof(float) * (3 * 2 + 2 * 2 + 5), ComputeBufferType.Structured);

            for(int i = 0; i < MaterialDictionary.Length; i++) {
                MaterialData material = MaterialDictionary[i]; 
                MaterialData.TerrainData terrain = material.terrainData;
                string Key = material.RetrieveKey(terrain.Texture);
                if(textureInfo.Contains(Key)) terrain.Texture = textureInfo.RetrieveIndex(Key);
                Key = material.RetrieveKey(terrain.GeoShaderIndex);
                if(shaderInfo.Contains(Key)) terrain.GeoShaderIndex = shaderInfo.RetrieveIndex(Key);
                MaterialTerrain[i] = terrain;
            }

            atmosphericData.SetData(MaterialDictionary.Select(e => e.AtmosphereScatter).ToArray());
            liquidData.SetData(MaterialDictionary.Select(e => e.liquidData).ToArray());
            terrainData.SetData(MaterialTerrain);
            //Bad naming scheme -> (value.texture.value.texture)
            Texture2DArray textures = GenerateTextureArray(textureInfo.SerializedData.Select(e => e.texture).ToArray());
            Shader.SetGlobalTexture("_Textures", textures); 
            Shader.SetGlobalBuffer("_MatTerrainData", terrainData);
            Shader.SetGlobalBuffer("_MatAtmosphericData", atmosphericData);
            Shader.SetGlobalBuffer("_MatLiquidData", liquidData);

            Shader.SetGlobalTexture("_LiquidFineWave", matInfo.liquidFineWave.value);
            Shader.SetGlobalTexture("_LiquidCoarseWave", matInfo.liquidCoarseWave.value);
        }

        /// <summary> Releases all buffers and textures used by the MaterialHandle. 
        /// Call this method before the program exits to prevent memory leaks. </summary>
        public void Release(){
            terrainData?.Release();
            atmosphericData?.Release();
            liquidData?.Release();
        }
        
        private Texture2DArray GenerateTextureArray(Texture2D[] textures)
        {   
            textureArray = new Texture2DArray(textureSize, textureSize, textures.Length, textureFormat, true);
            for(int i = 0; i < textures.Length; i++)
            {
                textureArray.SetPixels32(textures[i].GetPixels32(), i);
            }
            textureArray.Apply();
            return textureArray;
        }
    }
    /// <summary>
    /// Responsible for deserializing all noise generation settings and copying it to the GPU 
    /// for use in the terrain generation process. <seealso cref="Config.CURRENT.Generation.Noise"/>. 
    /// </summary>
    public struct NoiseHandle{
        internal ComputeBuffer indexBuffer;
        internal ComputeBuffer settingsBuffer;
        internal ComputeBuffer offsetsBuffer;
        internal ComputeBuffer splinePointsBuffer;

        /// <summary>
        /// Initializes the <see cref="NoiseHandle"/> . Deserializes and copies all information
        /// contained by <see cref="WorldConfig.Config.GenerationSettings.Noise"/> to the GPU. 
        /// Information is stored in global GPU buffers <c>_NoiseIndexes</c>, <c>_NoiseSettings</c>, 
        /// <c>_NoiseOffsets</c> and <c>_NoiseSplinePoints</c>.
        /// </summary>
        public void Initialize(){
            Release();
            Noise[] samplerDict = Config.CURRENT.Generation.Noise.SerializedData;
            uint[] indexPrefixSum = new uint[(samplerDict.Length + 1) * 2];
            NoiseSettings[] settings = new NoiseSettings[samplerDict.Length];
            List<Vector3> offsets = new List<Vector3>();
            List<Vector4> splinePoints = new List<Vector4>();
            for(int i = 0; i < samplerDict.Length; i++){
                indexPrefixSum[2 * (i+1)] = (uint)samplerDict[i].OctaveOffsets.Length + indexPrefixSum[2*i];
                indexPrefixSum[2 * (i+1) + 1] = (uint)samplerDict[i].SplineKeys.Length + indexPrefixSum[2*i+1];
                settings[i] = new NoiseSettings(samplerDict[i]);
                offsets.AddRange(samplerDict[i].OctaveOffsets);
                splinePoints.AddRange(samplerDict[i].SplineKeys);
            }
            
            indexBuffer = new ComputeBuffer(samplerDict.Length + 1, sizeof(uint) * 2, ComputeBufferType.Structured);
            settingsBuffer = new ComputeBuffer(samplerDict.Length, sizeof(float) * 3, ComputeBufferType.Structured);
            offsetsBuffer = new ComputeBuffer(offsets.Count, sizeof(float) * 3, ComputeBufferType.Structured);
            splinePointsBuffer = new ComputeBuffer(splinePoints.Count, sizeof(float) * 4, ComputeBufferType.Structured);

            indexBuffer.SetData(indexPrefixSum);
            settingsBuffer.SetData(settings);
            offsetsBuffer.SetData(offsets);
            splinePointsBuffer.SetData(splinePoints);

            Shader.SetGlobalBuffer("_NoiseIndexes", indexBuffer);
            Shader.SetGlobalBuffer("_NoiseSettings", settingsBuffer);
            Shader.SetGlobalBuffer("_NoiseOffsets", offsetsBuffer);
            Shader.SetGlobalBuffer("_NoiseSplinePoints", splinePointsBuffer);
        }

        /// <summary>
        /// Releases all buffers used by the NoiseHandle. 
        /// Call this method before the program exits to prevent memory leaks.
        /// </summary>
        public void Release()
        {
            indexBuffer?.Release();
            settingsBuffer?.Release();
            offsetsBuffer?.Release();
            splinePointsBuffer?.Release();
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct NoiseSettings
        {
            public float noiseScale;
            public float persistance;
            public float lacunarity;
            public NoiseSettings(Noise noise){
                noiseScale = noise.noiseScale;
                persistance = noise.persistance;
                lacunarity = noise.lacunarity;
            }
        }
    }
    

    /// <summary>
    /// Responsible for deserializing all biome generation settings and copying it to the GPU for use
    /// in the terrain generation process. <seealso cref="Config.CURRENT.Generation.Biomes"/>.
    /// </summary>
    public struct BiomeHandle{
        ComputeBuffer SurfTreeBuffer;
        ComputeBuffer CaveTreeBuffer;
        ComputeBuffer biomePrefCountBuffer;
        ComputeBuffer biomeMatBuffer;
        ComputeBuffer biomeEntityBuffer;
        ComputeBuffer structGenBuffer;

        /// <summary>
        /// Initializes the <see cref="BiomeHandle"/>. Deserializes and copies all information
        /// contained by <see cref="Config.CURRENT.Generation.Biomes"/> to the GPU. Deserializing
        /// the registry involves constructing an R-Tree LUT which is then copied to the GPU. Information 
        /// about this LUT is stored in global GPU buffers <c>_BiomeSurfTree</c>, <c>_BiomeCaveTree</c>,
        /// while information on what each biome contains is stored in <c>_BiomeMaterials</c>, <c>_BiomeStructureData</c>, 
        /// and <c>_BiomeEntities</c>, referencable through the <c>_BiomePrefCount</c> prefix sum buffer.
        /// </summary>
        public void Initialize()
        {
            Release();
            WorldConfig.Generation.Biome.CInfo<WorldConfig.Generation.Biome.SurfaceBiome>[] surface = Config.CURRENT.Generation.Biomes.value.SurfaceBiomes.SerializedData;
            WorldConfig.Generation.Biome.CInfo<WorldConfig.Generation.Biome.CaveBiome>[] cave = Config.CURRENT.Generation.Biomes.value.CaveBiomes.SerializedData;
            WorldConfig.Generation.Biome.CInfo<WorldConfig.Generation.Biome.CaveBiome>[] sky = Config.CURRENT.Generation.Biomes.value.SkyBiomes.SerializedData;
            List<WorldConfig.Generation.Biome.Info> biomes = new List<WorldConfig.Generation.Biome.Info>(); 
            biomes.AddRange(surface);
            biomes.AddRange(cave);
            biomes.AddRange(sky);

            int numBiomes = biomes.Count;
            uint4[] biomePrefSum = new uint4[numBiomes + 1]; //Prefix sum
            List<WorldConfig.Generation.Biome.Info.BMaterial> biomeMaterial = new();
            List<WorldConfig.Generation.Biome.Info.TerrainStructure> biomeStructures = new();
            List<WorldConfig.Generation.Biome.Info.EntityGen> biomeEntities = new();

            for (int i = 0; i < numBiomes; i++)
            {
                biomePrefSum[i+1].x = (uint)biomes[i].GroundMaterials.value?.Count + biomePrefSum[i].y;
                biomePrefSum[i+1].y = (uint)biomes[i].SurfaceMaterials.value?.Count + biomePrefSum[i + 1].x;
                biomePrefSum[i+1].z = (uint)biomes[i].Structures.value?.Count + biomePrefSum[i].z;
                biomePrefSum[i+1].w = (uint)biomes[i].Entities.value?.Count + biomePrefSum[i].w;
                biomeMaterial.AddRange(biomes[i].MaterialSerial(biomes[i].GroundMaterials));
                biomeMaterial.AddRange(biomes[i].MaterialSerial(biomes[i].SurfaceMaterials));
                biomeStructures.AddRange(biomes[i].StructureSerial);
                biomeEntities.AddRange(biomes[i].EntitySerial);
            }

            int matStride = sizeof(int) + sizeof(float) * 5;
            int structStride = sizeof(uint) + sizeof(float);
            int entityStride = sizeof(uint) + sizeof(float);
            biomePrefCountBuffer = new ComputeBuffer(numBiomes + 1, sizeof(uint) * 4, ComputeBufferType.Structured);
            if(biomeMaterial.Count > 0) biomeMatBuffer = new ComputeBuffer(biomeMaterial.Count, matStride, ComputeBufferType.Structured);
            if(biomeStructures.Count > 0) structGenBuffer = new ComputeBuffer(biomeStructures.Count, structStride, ComputeBufferType.Structured);
            if(biomeEntities.Count > 0) biomeEntityBuffer = new ComputeBuffer(biomeEntities.Count, entityStride, ComputeBufferType.Structured);

            biomePrefCountBuffer?.SetData(biomePrefSum);
            biomeMatBuffer?.SetData(biomeMaterial);
            biomeEntityBuffer?.SetData(biomeEntities);
            structGenBuffer?.SetData(biomeStructures);

            if(biomeMatBuffer != null) Shader.SetGlobalBuffer("_BiomeMaterials", biomeMatBuffer);
            if(structGenBuffer != null) Shader.SetGlobalBuffer("_BiomeStructureData", structGenBuffer);
            if(biomeEntityBuffer != null) Shader.SetGlobalBuffer("_BiomeEntities", biomeEntityBuffer);
            Shader.SetGlobalBuffer("_BiomePrefCount", biomePrefCountBuffer);

            WorldConfig.Generation.Biome.SurfaceBiome[] SurfTree = WorldConfig.Generation.Biome.BDict.Create(surface, 1).FlattenTree<WorldConfig.Generation.Biome.SurfaceBiome>();
            WorldConfig.Generation.Biome.CaveBiome[] CaveTree = WorldConfig.Generation.Biome.BDict.Create(cave, surface.Length + 1).FlattenTree<WorldConfig.Generation.Biome.CaveBiome>();
            WorldConfig.Generation.Biome.CaveBiome[] SkyTree = WorldConfig.Generation.Biome.BDict.Create(sky, surface.Length + cave.Length + 1).FlattenTree<WorldConfig.Generation.Biome.CaveBiome>();
            SurfTreeBuffer = new ComputeBuffer(SurfTree.Length, sizeof(float) * 6 * 2 + sizeof(int), ComputeBufferType.Structured);
            CaveTreeBuffer = new ComputeBuffer(CaveTree.Length + SkyTree.Length, sizeof(float) * 4 * 2 + sizeof(int), ComputeBufferType.Structured);
            SurfTree[0].biome = -1; //set defaults
            CaveTree[0].biome = -1 * (surface.Length + 1); 
            SkyTree[0].biome = -1 * (cave.Length + surface.Length + 1);

            SurfTreeBuffer.SetData(SurfTree);
            CaveTreeBuffer.SetData(CaveTree.Concat(SkyTree).ToArray());

            Shader.SetGlobalBuffer("_BiomeSurfTree", SurfTreeBuffer);
            Shader.SetGlobalBuffer("_BiomeCaveTree", CaveTreeBuffer);
            Shader.SetGlobalInteger("_BSkyStart", CaveTree.Length);
        }

        /// <summary>
        /// Releases all buffers used by the BiomeHandle.
        /// Call this method before the program exits to prevent memory leaks.
        /// </summary>
        public void Release()
        {
            SurfTreeBuffer?.Release();
            CaveTreeBuffer?.Release();
            biomePrefCountBuffer?.Release();
            biomeEntityBuffer?.Release();
            biomeMatBuffer?.Release();
            structGenBuffer?.Release();//
        }
    }

    /// <summary>
    /// Responsible for deserializing all structure generation settings and copying it to the GPU 
    /// for use in the terrain generation process. <seealso cref="Config.CURRENT.Generation.Structures"/>.
    /// </summary>
    public struct StructHandle{
        ComputeBuffer indexBuffer; //Prefix sum
        ComputeBuffer mapBuffer;
        ComputeBuffer checksBuffer;
        ComputeBuffer settingsBuffer;


        /// <summary>
        /// Initializes the <see cref="StructHandle"/>. Deserializes and copies all information contained by
        /// <see cref="Config.CURRENT.Generation.Structures"/> to the GPU. Information about a structure's in 
        /// generation is stored in global GPU buffers <c>_StructureIndexes</c>, <c>_StructureChecks</c>, and  <c>_StructureSettings</c>.
        ///  <c>_StructureSettings</c> also includes information on each structure's start and length of information in the other buffers, 
        ///  including the raw point data in <c>_StructureMap</c>.
        /// </summary>
        public void Initialize()
        {
            Release();
            WorldConfig.Generation.Structure.StructureData[] StructureDictionary = Config.CURRENT.Generation.Structures.value.StructureDictionary.SerializedData;
            WorldConfig.Generation.Structure.StructureData.Settings[] settings = new WorldConfig.Generation.Structure.StructureData.Settings[StructureDictionary.Length];
            List<WorldConfig.Generation.Structure.StructureData.PointInfo> map = new ();
            List<WorldConfig.Generation.Structure.StructureData.CheckPoint> checks = new ();
            uint[] indexPrefixSum = new uint[(StructureDictionary.Length+1)*2];

            for(int i = 0; i < StructureDictionary.Length; i++)
            {
                WorldConfig.Generation.Structure.StructureData data = StructureDictionary[i];
                indexPrefixSum[2 * (i + 1)] = (uint)data.map.value.Count + indexPrefixSum[2*i]; //Density is same length as materials
                indexPrefixSum[2 * (i + 1) + 1] = (uint)data.checks.value.Count + indexPrefixSum[2 * i + 1];
                settings[i] = data.settings.value;
                map.AddRange(data.SerializePoints);
                checks.AddRange(data.checks.value);
            }

            indexBuffer = new ComputeBuffer(StructureDictionary.Length + 1, sizeof(uint) * 2, ComputeBufferType.Structured); //By doubling stride, we compress the prefix sums
            mapBuffer = new ComputeBuffer(map.Count, sizeof(uint), ComputeBufferType.Structured);
            checksBuffer = new ComputeBuffer(checks.Count, sizeof(float) * 3 + sizeof(uint), ComputeBufferType.Structured);
            settingsBuffer = new ComputeBuffer(StructureDictionary.Length, sizeof(int) * 4 + sizeof(uint) * 2, ComputeBufferType.Structured);

            indexBuffer.SetData(indexPrefixSum);
            mapBuffer.SetData(map.ToArray());
            checksBuffer.SetData(checks.ToArray());
            settingsBuffer.SetData(settings);


            Shader.SetGlobalBuffer("_StructureIndexes", indexBuffer);
            Shader.SetGlobalBuffer("_StructureMap", mapBuffer);
            Shader.SetGlobalBuffer("_StructureChecks", checksBuffer);
            Shader.SetGlobalBuffer("_StructureSettings", settingsBuffer);
        }

        /// <summary>
        /// Releases all buffers used by the StructHandle.
        /// Call this method before the program exits to prevent memory leaks.
        /// </summary>
        public void Release()
        {
            indexBuffer?.Release();
            mapBuffer?.Release();
            checksBuffer?.Release();
            settingsBuffer?.Release();
        }
    }
    
    /// <summary>
    /// Responsible for deserializing all entity generation settings and copying it to the GPU 
    /// for use in the terrain generation process. Note that <b>only information
    /// relevant to each entity's placement is copied</b>. 
    /// <seealso cref="Config.CURRENT.Generation.Entities"/>.
    /// </summary>
    public struct EntityHandle{
        private ComputeBuffer entityInfoBuffer;
        private ComputeBuffer entityProfileBuffer; //used by gpu placement
        /// <exclude />
        public NativeArray<ProfileE> entityProfileArray; //used by jobs

        /// <summary>
        /// Initializes the <see cref="EntityHandle"/>. Deserializes and copies all information contained by
        /// <see cref="Config.CURRENT.Generation.Entities"/> relavent to an entity's placement to the GPU. 
        /// This includes information on each entity's size, and <see cref="ProfileE" />. 
        /// Information is stored in global GPU buffers <c>_EntityInfo</c> and <c>_EntityProfile</c>.
        /// </summary>
        public void Initialize()
        {
            Release();

            Authoring[] EntityDictionary = Config.CURRENT.Generation.Entities.SerializedData;
            int numEntities = EntityDictionary.Length;
            Entity.ProfileInfo[] entityInfo = new Entity.ProfileInfo[numEntities];
            List<ProfileE> entityProfile = new List<ProfileE>();

            for(int i = 0; i < numEntities; i++)
            {
                Entity.ProfileInfo info = EntityDictionary[i].Info;
                info.profileStart = (uint)entityProfile.Count;
                entityInfo[i] = info;
                EntityDictionary[i].Info = info;
                EntityDictionary[i].Entity.Preset(EntityDictionary[i].Setting);

                entityProfile.AddRange(EntityDictionary[i].Profile);
            }

            entityInfoBuffer = new ComputeBuffer(numEntities, sizeof(uint) * 4, ComputeBufferType.Structured);
            entityProfileBuffer = new ComputeBuffer(entityProfile.Count, sizeof(uint) * 2, ComputeBufferType.Structured);
            entityProfileArray = new NativeArray<ProfileE>(entityProfile.ToArray(), Allocator.Persistent);

            entityInfoBuffer.SetData(entityInfo);
            entityProfileBuffer.SetData(entityProfile.ToArray());

            Shader.SetGlobalBuffer("_EntityInfo", entityInfoBuffer);
            Shader.SetGlobalBuffer("_EntityProfile", entityProfileBuffer);
        }

        /// <summary>
        /// Releases all buffers used by the EntityHandle.
        /// Call this method before the program exits to prevent memory leaks.s
        /// </summary>
        public void Release(){
            entityInfoBuffer?.Release();
            entityProfileBuffer?.Release();

            //Release Static Entity Data
            Authoring[] EntityDictionary = Config.CURRENT.Generation.Entities.SerializedData;
            foreach(Authoring entity in EntityDictionary) entity.Entity.Unset();
            if(entityProfileArray.IsCreated) entityProfileArray.Dispose();
        }
    }

    /// <summary>
    /// Responsible for managing the allocation and deallocation of memory on the GPU for generation
    /// related tasks. Rather than allowing each system to maintain its own <see cref="ComputeBuffer"/>, 
    /// memory is allocated through a shader-based malloc which allows for more efficient memory management and 
    /// fewer buffer locations to track. Settings on the size of this memory heap can be found in <see cref="WorldConfig.Quality.Memory"/>.
    /// <seealso href = "https://blackmagic919.github.io/AboutMe/2024/08/18/Memory-Heap/"/> 
    /// </summary>
    public struct MemoryHandle{
        private ComputeShader HeapSetupShader;
        private ComputeShader AllocateShader;
        private ComputeShader DeallocateShader;
        private ComputeShader ClearMemShader;

        private ComputeBuffer _GPUMemorySource;
        private ComputeBuffer _EmptyBlockHeap;
        private ComputeBuffer _AddressBuffer;
        private uint2[] addressLL;
        private bool initialized;

        /// <summary>
        /// Initializes the <see cref="MemoryHandle"/>. Allocates a memory heap on the GPU for use in the terrain generation process.
        /// Information is stored in local GPU Buffers which should be obtained through <see cref="Storage"/> and <see cref="Address"/>
        /// properties and bound to any shader that needs to use it. 
        /// </summary>
        public void Intiialize()
        {
            if(initialized) Release();

            WorldConfig.Quality.Memory settings = Config.CURRENT.Quality.Memory.value;
            HeapSetupShader = Resources.Load<ComputeShader>("Compute/MemoryStructures/Heap/PrepareHeap");
            AllocateShader = Resources.Load<ComputeShader>("Compute/MemoryStructures/Heap/AllocateData");
            DeallocateShader = Resources.Load<ComputeShader>("Compute/MemoryStructures/Heap/DeallocateData");

            _GPUMemorySource = new ComputeBuffer(settings.StorageSize, sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.Immutable);
            //2 channels, 1 for size, 2 for memory address
            _EmptyBlockHeap = new ComputeBuffer(settings.HeapSize, sizeof(uint) * 2, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
            _AddressBuffer = new ComputeBuffer(settings.AddressSize+1, sizeof(uint) * 2, ComputeBufferType.Structured, ComputeBufferMode.Immutable);

            addressLL = new uint2[settings.AddressSize+1];
            addressLL[0].y = 1;
            
            initialized = true;
            PrepareMemory();
        }

        /// <summary>
        /// Releases all buffers used by the MemoryHandle.
        /// Call this method before the program exits to prevent memory leaks.
        /// </summary>
        public void Release()
        {
            _GPUMemorySource?.Release();
            _EmptyBlockHeap?.Release();
            _AddressBuffer?.Release();
            initialized = false;
        }

        private void PrepareMemory()
        {
            WorldConfig.Quality.Memory settings = Config.CURRENT.Quality.Memory.value;
            HeapSetupShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
            HeapSetupShader.SetBuffer(0, "_Heap", _EmptyBlockHeap);
            HeapSetupShader.SetInt("_BufferSize4Bytes", settings.StorageSize);

            HeapSetupShader.Dispatch(0, 1, 1, 1);
        }

        /// <summary>
        /// Allocates a memory block of size (<paramref name="count"/> * <paramref name="stride"/>) with the 
        /// specified <paramref name="stride"/> on the GPU. The unit is a 4-byte integer(word) and byte-level
        /// allocation is not supported. 
        /// </summary>
        /// <remarks>
        /// As this malloc functions on the GPU, though its synchronous complexity is O(log N) where N is the size of
        /// the free heap, it is an inefficient use of GPU resources as it must be performed synchronously. As a result
        /// avoid using this function for small allocations or in performance-critical sections of the code.
        /// 
        /// The <paramref name="stride"/> is specified such that one can cast the <see cref="Storage"/> buffer as containing a struct of size <paramref name="stride"/>
        /// which expedites and simplifies the process of reading and writing to the buffer.
        /// </remarks>
        /// <param name="count">The amount of structures of size <paramref name = "stride" /> to be allocated adjacently. The total size of the allocation is (<paramref name="count"/> * <paramref name="stride"/>)</param>
        /// <param name="stride">The alignment of structures relative to <see cref="Storage"/>, every entry will be at a relative address that is a multiple of <paramref name = "stride" />. Stride is in terms of 4-byte words </param>
        /// <returns>
        /// The address within the <see cref="Address"/> buffer of the entry which holds the address of the first entry
        /// within the <see cref="Storage"/> buffer. To read from the memory block, a shader should follow the access pattern
        /// <b>return addres</b> -> Address Buffer -> Storage Memory. This indirection is necessary to avoid GPU readback. 
        /// <remarks> 
        /// The address within the <see cref="Address"/> buffer is will contain two entries, the first being the 4-byte relative address
        /// and the second being relative to the requested <paramref name = "stride" /> <paramref name="stride"/>.
        /// </remarks>
        /// </returns>
        public uint AllocateMemoryDirect(int count, int stride)
        {
            if(!initialized) return 0;

            uint addressIndex = addressLL[0].y;
            //Allocate Memory
            AllocateShader.EnableKeyword("DIRECT_ALLOCATE");
            AllocateShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
            AllocateShader.SetBuffer(0, "_Heap", _EmptyBlockHeap);
            AllocateShader.SetBuffer(0, "_AddressDict", _AddressBuffer);
            AllocateShader.SetInt("addressIndex", (int)addressIndex);
            AllocateShader.SetInt("allocCount", count);
            AllocateShader.SetInt("allocStride", stride);

            AllocateShader.Dispatch(0, 1, 1, 1);

            //Head node always points to first free node, remove node from LL to mark allocated
            uint pAddress = addressLL[addressIndex].x;//should always be 0
            uint nAddress = addressLL[addressIndex].y == 0 ? addressIndex+1 : addressLL[addressIndex].y;

            addressLL[pAddress].y = nAddress;
            addressLL[nAddress].x = pAddress;

            return addressIndex;
        }

        /// <summary>
        /// Same as <see cref="AllocateMemoryDirect(int, int)"/> but with the count being indirectly referenced. This is applicable
        /// if the amount of objects to be allocated is not known on the CPU. To reference the count, a <see cref="ComputeBuffer"/> is passed
        /// while the location of the count within the buffer is specified by <paramref name="countOffset"/>.
        /// </summary>
        /// <param name="count">A buffer containing the amount of objects of size stride to be allocated. The count must be a 4-byte integer aligned within the buffer. <seealso cref="AllocateMemoryDirect(int, int)"/></param>
        /// <param name="stride"><see cref="AllocateMemoryDirect(int, int)"/></param>
        /// <param name="countOffset">The 4-byte offset within the buffer of the count.</param>
        /// <returns> <see cref="AllocateMemoryDirect(int, int)"/> </returns>
        public uint AllocateMemory(ComputeBuffer count, int stride, int countOffset = 0)
        {
            if(!initialized) return 0;

            uint addressIndex = addressLL[0].y;
            
            //Allocate Memory
            AllocateShader.DisableKeyword("DIRECT_ALLOCATE");
            AllocateShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
            AllocateShader.SetBuffer(0, "_Heap", _EmptyBlockHeap);
            AllocateShader.SetBuffer(0, "_AddressDict", _AddressBuffer);
            AllocateShader.SetInt("addressIndex", (int)addressIndex);
            AllocateShader.SetInt("countOffset", countOffset);

            AllocateShader.SetBuffer(0, "allocCount", count);
            AllocateShader.SetInt("allocStride", stride);

            AllocateShader.Dispatch(0, 1, 1, 1);

            //Head node always points to first free node, remove node from LL to mark allocated
            uint pAddress = addressLL[addressIndex].x;//should always be 0
            uint nAddress = addressLL[addressIndex].y == 0 ? addressIndex+1 : addressLL[addressIndex].y;

            addressLL[pAddress].y = nAddress;
            addressLL[nAddress].x = pAddress;

            return addressIndex;
        }

        /// <summary>
        /// Releases an allocated memory block from the <see cref="Storage"/> buffer. The caller
        /// must ensure that the address points to a valid allocated memory block. Improper use
        /// may corrupt the memory heap and cause undefined behavior.
        /// </summary>
        /// <param name="addressIndex">
        /// The address of the entry within the <see cref="Address"/> buffer which points to the
        /// allocated memory block within the <see cref="Storage"/> buffer. 
        /// </param>
        public void ReleaseMemory(uint addressIndex)
        {//
            if(!initialized || addressIndex == 0) return;
            //Release Memory
            DeallocateShader.DisableKeyword("DIRECT_DEALLOCATE");
            DeallocateShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
            DeallocateShader.SetBuffer(0, "_Heap", _EmptyBlockHeap);
            DeallocateShader.SetBuffer(0, "_AddressDict", _AddressBuffer);
            DeallocateShader.SetInt("addressIndex", (int)addressIndex);

            DeallocateShader.Dispatch(0, 1, 1, 1);
            //Add node back to head of LL
            uint nAddress = addressLL[0].y;
            uint pAddress = addressLL[nAddress].x; //Is equivalent to 0(head node), but this is more clear
            addressLL[pAddress].y = addressIndex;
            addressLL[nAddress].x = addressIndex;
            addressLL[addressIndex] = new uint2(pAddress, nAddress);
            
            //uint[] heap = new uint[6];
            //_EmptyBlockHeap.GetData(heap);
            //Debug.Log("Primary: " + heap[3]/250000 + "MB");
        }

        /// <summary>
        /// Same as <see cref="ReleaseMemory(uint)"/> but with the address being directly referenced. If the caller
        /// knows the specific location of the address within the GPU, this method can be used to release memory.
        /// </summary>
        /// <param name="address">A compute buffer containing the 4-byte sized and aligned address within <see cref="Storage"/> of the memroy block.</param>
        /// <param name="countOffset">The 4-byte offset within <paramref name="address"/> of the entry containing the address to the memory block</param>
        public void ReleaseMemoryDirect(ComputeBuffer address, int countOffset = 0)
        {   
            if(!initialized) return;
            //Allocate Memory
            DeallocateShader.EnableKeyword("DIRECT_DEALLOCATE");
            DeallocateShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
            DeallocateShader.SetBuffer(0, "_Heap", _EmptyBlockHeap);
            DeallocateShader.SetBuffer(0, "_Address", address);
            DeallocateShader.SetInt("countOffset", countOffset);

            DeallocateShader.Dispatch(0, 1, 1, 1);
        }

        const int WorkerThreads = 64;

        /// <summary>
        /// Clears a memory block of size <paramref name="count"/> * <paramref name="stride"/> with the specified <paramref name="stride"/> 
        /// at the given <paramref name="address"/> on the <see cref="Storage"/> buffer. The unit is a 4-byte integer(word) and byte-level allocation is not supported. 
        /// This operation is done in parallel on the GPU and is more efficient than clearing memory on the CPU.
        /// </summary>
        /// <param name="address"> The address of the entry within the <see cref="Address"/> buffer which points to the memory block to be cleared within the <see cref="Storage"/> buffer. </param>
        /// <param name="count"> The amount of structures of size <paramref name = "stride" /> to be cleared adjacently. The total size that is cleared is (<paramref name="count"/> * <paramref name="stride"/>) </param>
        /// <param name="stride"> The alignment of structures relative to <see cref="Storage"/>, every entry will be at a relative address that is a multiple of <paramref name = "stride" /> </param>
        public void ClearMemory(int address, int count, int stride){
            if(!initialized || address == 0) return;
            ClearMemShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
            ClearMemShader.SetBuffer(0, "_AddressDict", _AddressBuffer);
            ClearMemShader.SetInt("addressIndex", address);
            ClearMemShader.SetInt("freeCount", count);
            ClearMemShader.SetInt("freeStride", stride);

            ClearMemShader.SetInt("workerCount", WorkerThreads);
            ClearMemShader.GetKernelThreadGroupSizes(0, out uint threadsAxis, out _, out _);
            int threadGroups = Mathf.CeilToInt(WorkerThreads / (float)threadsAxis);
            ClearMemShader.Dispatch(0, threadGroups, 1, 1);
        }

        /// <summary> The primary memory buffer used for long-term memory storage in the terrain generation process. </summary>
        public readonly ComputeBuffer Storage => _GPUMemorySource;
        /// <summary>
        /// A buffer containing addresses to memory blocks within the <see cref="Storage"/> buffer. This buffer tracks the raw 4-byte address
        /// as well as the address relative to the requested stride during allocation of each memory block. 
        /// </summary>
        public readonly ComputeBuffer Address => _AddressBuffer;
    }
}}
