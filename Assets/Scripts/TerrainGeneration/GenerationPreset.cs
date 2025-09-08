using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using WorldConfig;
using WorldConfig.Generation;
using WorldConfig.Generation.Material;
using WorldConfig.Generation.Entity;
using WorldConfig.Generation.Biome;
using WorldConfig.Quality;

namespace TerrainGeneration{
/// <summary>  The factory protocol for the collective game system. This
/// protocol tracks the proper process to facilitate large 
/// context switches within the game. Any new systems should
/// be added to this protocol with awareness of its dependencies. </summary>
public static class SystemProtocol{
    /// <summary> Performs the proper startup protocol when the <b>world</b> is initialized
    /// (this excludes when the main menu is displayed). This is a static factory protocol and only 
    /// changes when modifying the system's functionality through its source code. </summary>
    public static void Startup(){
        IRegister.Setup(Config.CURRENT);
        UtilityBuffers.Initialize();
        GenerationPreset.Initialize();

        MapStorage.GPUMapManager.Initialize();
        MapStorage.CPUMapManager.Initialize();

        EntityManager.Initialize();
        LightBaker.Initialize();
        StartupPlacer.Initialize();

        TerrainUpdate.Initialize();
        //We need to make sure keybinds are rebound in the same order
        //as the state they were saved at
        InputPoller.Initialize();
        PlayerHandler.Initialize();
        GameUIManager.Initialize();

        AtmospherePass.Initialize();
        MapStorage.Chunk.Initialize();

        Structure.Generator.PresetData();
        Surface.Generator.PresetData();
        Map.Generator.PresetData();
        ShaderGenerator.PresetData();
        SpriteExtruder.PresetData();
        Readback.AsyncMeshReadback.PresetData();
    }

    /// <summary> Performs the proper shutdown protocol when the <b>world</b> is closed
    /// (this excludes when the main menu is displayed). This is a static factory protocol and 
    /// only changes when modifying the system's functionality through its source code. </summary>
    public static void Shutdown(){
        UtilityBuffers.Release();
        MapStorage.GPUMapManager.Release();
        MapStorage.CPUMapManager.Release();
        EntityManager.Release();
        LightBaker.Release();
        ShaderGenerator.Release();
        GenerationPreset.Release();
        AtmospherePass.Release();
        Readback.AsyncMeshReadback.Release();
        PlayerHandler.Release();
    }
}


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
    public static WorldConfig.Quality.MemoryOccupancyBalancer memoryHandle;
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
        memoryHandle = new MemoryOccupancyBalancer(Config.CURRENT.Quality.Memory.value);
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
        memoryHandle?.Release();
    }

    /// <summary>Responsible for deserializing all material display information as well as copying all textures to the GPU.
    /// Material display information includes information on each material's visual representation as a solid, liquid, 
    /// and gas <seealso cref="MaterialData"/>. Textures are copied from the <see cref="Config.GenerationSettings.Textures"/>
    /// registry and should be referenced in the GPU by their index in that registry. </summary>
    public struct MaterialHandle{
        const int textureSize = 128;
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
            static void SerializeGeoShader(MaterialData mat, ref MaterialData.TerrainData terr){
                if (!terr.GeoShaderIndex.HasGeoShader) return;
                Catalogue<WorldConfig.Quality.GeoShader> shaderInfo = Config.CURRENT.Quality.GeoShaders;
                ref MaterialData.TerrainData.GeoShaderInfo info = ref terr.GeoShaderIndex;
                string Key = mat.RetrieveKey((int)info.MajorIndex);
                info.HasGeoShader = false; //If we fail any checks here on out, ignore this entry
                if (!shaderInfo.Contains(Key)) return;

                info.MajorIndex = (uint)shaderInfo.RetrieveIndex(Key);
                IRegister subReg = shaderInfo.Retrieve((int)info.MajorIndex).GetRegistry();
                Key = mat.RetrieveKey((int)info.MinorIndex);
                if (subReg == null || !subReg.Contains(Key)) return;
                info.MinorIndex = (uint)subReg.RetrieveIndex(Key);
                info.HasGeoShader = true;
            }
            Release();
            WorldConfig.Generation.Material.Generation matInfo = Config.CURRENT.Generation.Materials.value;
            Catalogue<TextureContainer> textureInfo = Config.CURRENT.Generation.Textures;
            List<MaterialData> MaterialDictionary = matInfo.MaterialDictionary.Reg;
            MaterialData.TerrainData[] MaterialTerrain = new MaterialData.TerrainData[MaterialDictionary.Count];

            int numMats = MaterialDictionary.Count;
            terrainData = new ComputeBuffer(numMats, sizeof(float) + sizeof(int) * 2, ComputeBufferType.Structured);
            atmosphericData = new ComputeBuffer(numMats, sizeof(float) * 9 + sizeof(uint), ComputeBufferType.Structured);
            liquidData = new ComputeBuffer(numMats, sizeof(float) * (4 * 2 + 2 * 2 + 3), ComputeBufferType.Structured);

            for(int i = 0; i < MaterialDictionary.Count; i++) {
                MaterialData material = MaterialDictionary[i]; 
                MaterialData.TerrainData terrain = material.terrainData;
                string Key = material.RetrieveKey(terrain.Texture);
                
                if(textureInfo.Contains(Key)) terrain.Texture = textureInfo.RetrieveIndex(Key);
                SerializeGeoShader(material, ref terrain);

                MaterialTerrain[i] = terrain;
            }

            atmosphericData.SetData(MaterialDictionary.Select(e => e.AtmosphereScatter).ToArray());
            liquidData.SetData(MaterialDictionary.Select(e => e.liquidData).ToArray());
            terrainData.SetData(MaterialTerrain);
            //Bad naming scheme -> (value.texture.value.texture)
            Texture2DArray textures = GenerateTextureArray(textureInfo.Reg.Select(e => e.self.texture).ToArray());
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
    /// for use in the terrain generation process. <seealso cref="Config.GenerationSettings.Noise"/>. 
    /// </summary>
    public struct NoiseHandle{
        internal ComputeBuffer indexBuffer;
        internal ComputeBuffer settingsBuffer;
        internal ComputeBuffer offsetsBuffer;
        internal ComputeBuffer splinePointsBuffer;

        /// <summary>
        /// Initializes the <see cref="NoiseHandle"/> . Deserializes and copies all information
        /// contained by <see cref="Config.GenerationSettings.Noise"/> to the GPU. 
        /// Information is stored in global GPU buffers <c>_NoiseIndexes</c>, <c>_NoiseSettings</c>, 
        /// <c>_NoiseOffsets</c> and <c>_NoiseSplinePoints</c>.
        /// </summary>
        public void Initialize(){
            Release();
            List<Noise> samplerDict = Config.CURRENT.Generation.Noise.Reg;
            uint[] indexPrefixSum = new uint[(samplerDict.Count + 1) * 2];
            NoiseSettings[] settings = new NoiseSettings[samplerDict.Count];
            List<Vector3> offsets = new List<Vector3>();
            List<Vector4> splinePoints = new List<Vector4>();
            for(int i = 0; i < samplerDict.Count; i++){
                indexPrefixSum[2 * (i+1)] = (uint)samplerDict[i].OctaveOffsets.Length + indexPrefixSum[2*i];
                indexPrefixSum[2 * (i+1) + 1] = (uint)samplerDict[i].SplineKeys.Length + indexPrefixSum[2*i+1];
                settings[i] = new NoiseSettings(samplerDict[i]);
                offsets.AddRange(samplerDict[i].OctaveOffsets);
                splinePoints.AddRange(samplerDict[i].SplineKeys);
            }
            
            indexBuffer = new ComputeBuffer(samplerDict.Count + 1, sizeof(uint) * 2, ComputeBufferType.Structured);
            settingsBuffer = new ComputeBuffer(samplerDict.Count, sizeof(float) * 3, ComputeBufferType.Structured);
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
    /// in the terrain generation process. <seealso cref="Config.GenerationSettings.Biomes"/>.
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
        /// contained by <see cref="Config.GenerationSettings.Biomes"/> to the GPU. Deserializing
        /// the registry involves constructing an R-Tree LUT which is then copied to the GPU. Information 
        /// about this LUT is stored in global GPU buffers <c>_BiomeSurfTree</c>, <c>_BiomeCaveTree</c>,
        /// while information on what each biome contains is stored in <c>_BiomeMaterials</c>, <c>_BiomeStructureData</c>, 
        /// and <c>_BiomeEntities</c>, referencable through the <c>_BiomePrefCount</c> prefix sum buffer.
        /// </summary>
        public void Initialize()
        {
            Release();
            List<WorldConfig.Generation.Biome.CInfo<WorldConfig.Generation.Biome.SurfaceBiome>> surface = Config.CURRENT.Generation.Biomes.value.SurfaceBiomes.Reg;
            List<WorldConfig.Generation.Biome.CInfo<WorldConfig.Generation.Biome.CaveBiome>> cave = Config.CURRENT.Generation.Biomes.value.CaveBiomes.Reg;
            List<WorldConfig.Generation.Biome.CInfo<WorldConfig.Generation.Biome.CaveBiome>> sky = Config.CURRENT.Generation.Biomes.value.SkyBiomes.Reg;
            List<WorldConfig.Generation.Biome.Info> biomes = new List<WorldConfig.Generation.Biome.Info>(); 
            biomes.AddRange(surface.Select(e => e.info.value));
            biomes.AddRange(cave.Select(e => e.info.value));
            biomes.AddRange(sky.Select(e => e.info.value));

            int numBiomes = biomes.Count;
            uint[,] biomePrefSum = new uint[numBiomes + 1, 5]; //Prefix sum
            List<WorldConfig.Generation.Biome.Info.BMaterial> biomeMaterial = new();
            List<WorldConfig.Generation.Biome.Info.TerrainStructure> biomeStructures = new();
            List<WorldConfig.Generation.Biome.Info.EntityGen> biomeEntities = new();

            for (int i = 0; i < numBiomes; i++)
            {
                biomePrefSum[i+1, 0] = (biomes[i].GroundMaterials.value == null ? 0 : (uint)biomes[i].GroundMaterials.value.Count) + biomePrefSum[i, 2];
                biomePrefSum[i+1, 1] = (biomes[i].SurfaceMaterials.value == null ? 0 : (uint)biomes[i].SurfaceMaterials.value.Count) + biomePrefSum[i + 1, 0];
                biomePrefSum[i+1, 2] = (biomes[i].LiquidMaterials.value == null ? 0 : (uint)biomes[i].LiquidMaterials.value.Count) + biomePrefSum[i + 1, 1];
                biomePrefSum[i+1, 3] = (biomes[i].Structures.value == null ? 0 : (uint)biomes[i].Structures.value.Count) + biomePrefSum[i, 3];
                biomePrefSum[i+1, 4] = (biomes[i].Entities.value == null ? 0 : (uint)biomes[i].Entities.value?.Count) + biomePrefSum[i, 4];
                biomeMaterial.AddRange(biomes[i].MaterialSerial(biomes[i].GroundMaterials));
                biomeMaterial.AddRange(biomes[i].MaterialSerial(biomes[i].SurfaceMaterials));
                biomeMaterial.AddRange(biomes[i].MaterialSerial(biomes[i].LiquidMaterials));
                if(biomes[i].Structures.value != null) biomeStructures.AddRange(biomes[i].StructureSerial);
                if(biomes[i].Entities.value != null) biomeEntities.AddRange(biomes[i].EntitySerial);
            }

            int matStride = sizeof(int) + sizeof(float) * 4;
            int structStride = sizeof(uint) + sizeof(float);
            int entityStride = sizeof(uint) + sizeof(float);
            biomePrefCountBuffer = new ComputeBuffer(numBiomes + 1, sizeof(uint) * 5, ComputeBufferType.Structured);
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

            WorldConfig.Generation.Biome.SurfaceBiome[] SurfTree = BDict.Create<SurfaceBiome>(surface, 1).FlattenTree<WorldConfig.Generation.Biome.SurfaceBiome>();
            WorldConfig.Generation.Biome.CaveBiome[] CaveTree = BDict.Create<CaveBiome>(cave, surface.Count + 1).FlattenTree<WorldConfig.Generation.Biome.CaveBiome>();
            WorldConfig.Generation.Biome.CaveBiome[] SkyTree = BDict.Create<CaveBiome>(sky, surface.Count + cave.Count + 1).FlattenTree<WorldConfig.Generation.Biome.CaveBiome>();
            SurfTreeBuffer = new ComputeBuffer(SurfTree.Length, sizeof(float) * 6 * 2 + sizeof(int), ComputeBufferType.Structured);
            CaveTreeBuffer = new ComputeBuffer(CaveTree.Length + SkyTree.Length, sizeof(float) * 4 * 2 + sizeof(int), ComputeBufferType.Structured);
            SurfTree[0].biome = -1; //set defaults
            CaveTree[0].biome = -1 * (surface.Count + 1); 
            SkyTree[0].biome = -1 * (cave.Count + surface.Count + 1);

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
    /// for use in the terrain generation process. <seealso cref="Config.GenerationSettings.Structures"/>.
    /// </summary>
    public struct StructHandle{
        ComputeBuffer indexBuffer; //Prefix sum
        ComputeBuffer mapBuffer;
        ComputeBuffer checksBuffer;
        ComputeBuffer settingsBuffer;


        /// <summary>
        /// Initializes the <see cref="StructHandle"/>. Deserializes and copies all information contained by
        /// <see cref="Config.GenerationSettings.Structures"/> to the GPU. Information about a structure's in 
        /// generation is stored in global GPU buffers <c>_StructureIndexes</c>, <c>_StructureChecks</c>, and  <c>_StructureSettings</c>.
        ///  <c>_StructureSettings</c> also includes information on each structure's start and length of information in the other buffers, 
        ///  including the raw point data in <c>_StructureMap</c>.
        /// </summary>
        public void Initialize()
        {
            Release();
            List<WorldConfig.Generation.Structure.StructureData> StructureDictionary = Config.CURRENT.Generation.Structures.value.StructureDictionary.Reg;
            WorldConfig.Generation.Structure.StructureData.Settings[] settings = new WorldConfig.Generation.Structure.StructureData.Settings[StructureDictionary.Count];
            List<WorldConfig.Generation.Structure.StructureData.PointInfo> map = new ();
            List<WorldConfig.Generation.Structure.StructureData.CheckPoint> checks = new ();
            uint[] indexPrefixSum = new uint[(StructureDictionary.Count+1)*2];

            for(int i = 0; i < StructureDictionary.Count; i++)
            {
                WorldConfig.Generation.Structure.StructureData data = StructureDictionary[i];
                indexPrefixSum[2 * (i + 1)] = (uint)data.map.value.Count + indexPrefixSum[2*i]; //Density is same length as materials
                indexPrefixSum[2 * (i + 1) + 1] = (uint)data.checks.value.Count + indexPrefixSum[2 * i + 1];
                settings[i] = data.settings.value;
                map.AddRange(data.SerializePoints);
                checks.AddRange(data.checks.value);
            }

            indexBuffer = new ComputeBuffer(StructureDictionary.Count + 1, sizeof(uint) * 2, ComputeBufferType.Structured); //By doubling stride, we compress the prefix sums
            mapBuffer = new ComputeBuffer(map.Count, sizeof(uint), ComputeBufferType.Structured);
            checksBuffer = new ComputeBuffer(checks.Count, sizeof(float) * 3 + sizeof(uint), ComputeBufferType.Structured);
            settingsBuffer = new ComputeBuffer(StructureDictionary.Count, sizeof(int) * 4 + sizeof(uint) * 2, ComputeBufferType.Structured);

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
    /// <seealso cref="Config.GenerationSettings.Entities"/>.
    /// </summary>
    public struct EntityHandle{
        private ComputeBuffer entityInfoBuffer;
        private ComputeBuffer entityProfileBuffer; //used by gpu placement
        /// <exclude />
        public NativeArray<ProfileE> entityProfileArray; //used by jobs

        /// <summary>
        /// Initializes the <see cref="EntityHandle"/>. Deserializes and copies all information contained by
        /// <see cref="Config.GenerationSettings.Entities"/> relavent to an entity's placement to the GPU. 
        /// This includes information on each entity's size, and <see cref="ProfileE" />. 
        /// Information is stored in global GPU buffers <c>_EntityInfo</c> and <c>_EntityProfile</c>.
        /// </summary>
        public void Initialize()
        {
            Release();

            List<Authoring> EntityDictionary = Config.CURRENT.Generation.Entities.Reg;
            int numEntities = EntityDictionary.Count;
            EntitySetting.ProfileInfo[] entityInfo = new EntitySetting.ProfileInfo[numEntities];
            List<ProfileE> entityProfile = new List<ProfileE>();

            for(int i = 0; i < numEntities; i++)
            {
                ref EntitySetting.ProfileInfo info = ref EntityDictionary[i].Setting.profile;
                info.profileStart = (uint)entityProfile.Count;
                entityInfo[i] = info;
                entityProfile.AddRange(EntityDictionary[i].Profile.value);
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
            if(entityProfileArray.IsCreated) entityProfileArray.Dispose();
        }
    }
}}