using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Mathematics;
using System;
using Unity.Collections;
using System.Runtime.InteropServices;

public static class GenerationPreset
{
    private static MaterialHandle materialHandle;
    private static NoiseHandle noiseHandle;
    private static BiomeHandle biomeHandle;
    private static StructHandle structHandle;
    public static EntityHandle entityHandle;
    public static MemoryHandle memoryHandle;
    public static bool active;

    public static void Initialize(){
        active = true;
        materialHandle.Initialize();
        noiseHandle.Initialize();
        biomeHandle.Initialize();
        structHandle.Initialize();
        entityHandle.Initialize();
        memoryHandle.Intiialize();
    }

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

    public struct MaterialHandle{
        const int textureSize = 512;
        const TextureFormat textureFormat = TextureFormat.RGBA32;

        Texture2DArray textureArray;
        ComputeBuffer terrainData;
        ComputeBuffer liquidData;
        ComputeBuffer atmosphericData;

        public void Initialize()
        {
            Release();
            TextureData data = WorldStorageHandler.WORLD_OPTIONS.Generation.Materials.value;
            MaterialData[] MaterialDictionary = data.MaterialDictionary.SerializedData;
            int numMats = MaterialDictionary.Length;
            terrainData = new ComputeBuffer(numMats, sizeof(float) * 6 + sizeof(int), ComputeBufferType.Structured);
            atmosphericData = new ComputeBuffer(numMats, sizeof(float) * 6, ComputeBufferType.Structured);
            liquidData = new ComputeBuffer(numMats, sizeof(float) * (3 * 2 + 2 * 2 + 5), ComputeBufferType.Structured);

            terrainData.SetData(MaterialDictionary.Select(e => e.terrainData).ToArray());
            atmosphericData.SetData(MaterialDictionary.Select(e => e.AtmosphereScatter).ToArray());
            liquidData.SetData(MaterialDictionary.Select(e => e.liquidData).ToArray());
            //Bad naming scheme -> (value.texture.value.texture)
            Texture2DArray textures = GenerateTextureArray(MaterialDictionary.Select(e => e.texture.value.texture).ToArray());
            Shader.SetGlobalTexture("_Textures", textures); 
            Shader.SetGlobalBuffer("_MatTerrainData", terrainData);
            Shader.SetGlobalBuffer("_MatAtmosphericData", atmosphericData);
            Shader.SetGlobalBuffer("_MatLiquidData", liquidData);

            Shader.SetGlobalTexture("_LiquidFineWave", data.liquidFineWave.value);
            Shader.SetGlobalTexture("_LiquidCoarseWave", data.liquidCoarseWave.value);
        }

        public void Release(){
            terrainData?.Release();
            atmosphericData?.Release();
            liquidData?.Release();
        }

        public Texture2DArray GenerateTextureArray(Texture2D[] textures)
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
    
    struct NoiseHandle{
        internal ComputeBuffer indexBuffer;
        internal ComputeBuffer settingsBuffer;
        internal ComputeBuffer offsetsBuffer;
        internal ComputeBuffer splinePointsBuffer;

        
        public void Initialize(){
            Release();
            NoiseData[] samplerDict = WorldStorageHandler.WORLD_OPTIONS.Generation.Noise.SerializedData;
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
            public NoiseSettings(NoiseData noise){
                noiseScale = noise.noiseScale;
                persistance = noise.persistance;
                lacunarity = noise.lacunarity;
            }
        }
    }
    

    struct BiomeHandle{
        ComputeBuffer biomeRTreeBuffer;
        ComputeBuffer biomeAtmosphereBuffer;
        ComputeBuffer biomePrefCountBuffer;
        ComputeBuffer biomeGroundMatBuffer;
        ComputeBuffer biomeSurfaceMatBuffer;
        ComputeBuffer biomeEntityBuffer;
        ComputeBuffer structGenBuffer;

        public void Release()
        {
            biomeRTreeBuffer?.Release();
            biomePrefCountBuffer?.Release();
            biomeAtmosphereBuffer?.Release();
            biomeEntityBuffer?.Release();
            biomeGroundMatBuffer?.Release();
            biomeSurfaceMatBuffer?.Release();
            structGenBuffer?.Release();//
        }

        public void Initialize()
        {
            Release();
            BiomeInfo[] biomes = WorldStorageHandler.WORLD_OPTIONS.Generation.Biomes.value.biomes.SerializedData;
            int numBiomes = biomes.Length;
            uint4[] biomePrefSum = new uint4[numBiomes + 1]; //Prefix sum
            float[] atmosphereData = new float[numBiomes];
            List<BiomeInfo.BMaterial> biomeGroundMaterial = new();
            List<BiomeInfo.BMaterial> biomeSurfaceMaterial = new();
            List<BiomeInfo.TerrainStructure> biomeStructures = new();
            List<BiomeInfo.EntityGen> biomeEntities = new();

            for (int i = 0; i < numBiomes; i++)
            {
                biomePrefSum[i+1] = new uint4((uint)biomes[i].GroundMaterials.value?.Count + biomePrefSum[i].x, 
                                            (uint)biomes[i].SurfaceMaterials.value?.Count + biomePrefSum[i].y,
                                            (uint)biomes[i].Structures.value?.Count + biomePrefSum[i].z,
                                            (uint)biomes[i].Entities.value?.Count + biomePrefSum[i].w);
                atmosphereData[i] = biomes[i].AtmosphereFalloff;
                biomeGroundMaterial.AddRange(biomes[i].MaterialSerial(biomes[i].GroundMaterials));
                biomeSurfaceMaterial.AddRange(biomes[i].MaterialSerial(biomes[i].SurfaceMaterials));
                biomeStructures.AddRange(biomes[i].StructureSerial);
                biomeEntities.AddRange(biomes[i].EntitySerial);
            }

            BiomeDictionary.RNodeFlat[] RTree = new BiomeDictionary(biomes).FlattenTree();

            int matStride = sizeof(int) + sizeof(float) + sizeof(float) + (sizeof(int) * 3 + sizeof(float) * 2);
            int structStride = sizeof(uint) + sizeof(float) + (sizeof(int) * 3 + sizeof(float) * 2);
            int entityStride = sizeof(uint) + sizeof(float);
            biomeRTreeBuffer = new ComputeBuffer(RTree.Length, sizeof(float) * 6 * 2 + sizeof(int), ComputeBufferType.Structured);
            biomePrefCountBuffer = new ComputeBuffer(numBiomes + 1, sizeof(uint) * 4, ComputeBufferType.Structured);
            biomeAtmosphereBuffer = new ComputeBuffer(numBiomes, sizeof(float), ComputeBufferType.Structured);
            if(biomeGroundMaterial.Count > 0) biomeGroundMatBuffer = new ComputeBuffer(biomeGroundMaterial.Count, matStride, ComputeBufferType.Structured);
            if(biomeSurfaceMaterial.Count > 0) biomeSurfaceMatBuffer = new ComputeBuffer(biomeSurfaceMaterial.Count, matStride, ComputeBufferType.Structured);
            if(biomeStructures.Count > 0) structGenBuffer = new ComputeBuffer(biomeStructures.Count, structStride, ComputeBufferType.Structured);
            if(biomeEntities.Count > 0) biomeEntityBuffer = new ComputeBuffer(biomeEntities.Count, entityStride, ComputeBufferType.Structured);

            biomeRTreeBuffer?.SetData(RTree);
            biomePrefCountBuffer?.SetData(biomePrefSum);
            biomeAtmosphereBuffer?.SetData(atmosphereData);
            biomeGroundMatBuffer?.SetData(biomeGroundMaterial);
            biomeSurfaceMatBuffer?.SetData(biomeSurfaceMaterial);
            biomeEntityBuffer?.SetData(biomeEntities);
            structGenBuffer?.SetData(biomeStructures);

            Shader.SetGlobalBuffer("_BiomeRTree", biomeRTreeBuffer);
            Shader.SetGlobalBuffer("_BiomePrefCount", biomePrefCountBuffer);
            Shader.SetGlobalBuffer("_BiomeAtmosphereData", biomeAtmosphereBuffer);
            if(biomeGroundMatBuffer != null) Shader.SetGlobalBuffer("_BiomeGroundMaterials", biomeGroundMatBuffer);
            if(biomeSurfaceMatBuffer != null) Shader.SetGlobalBuffer("_BiomeSurfaceMaterials", biomeSurfaceMatBuffer);
            if(structGenBuffer != null) Shader.SetGlobalBuffer("_BiomeStructureData", structGenBuffer);
            if(biomeEntityBuffer != null) Shader.SetGlobalBuffer("_BiomeEntities", biomeEntityBuffer);
        }
    }

    struct StructHandle{
        ComputeBuffer indexBuffer; //Prefix sum
        ComputeBuffer mapBuffer;
        ComputeBuffer checksBuffer;
        ComputeBuffer settingsBuffer;

        public void Initialize()
        {
            Release();
            StructureData[] StructureDictionary = WorldStorageHandler.WORLD_OPTIONS.Generation.Structures.SerializedData;
            uint[] indexPrefixSum = new uint[(StructureDictionary.Length+1)*2];
            List<StructureData.PointInfo> map = new List<StructureData.PointInfo>();
            List<StructureData.CheckPoint> checks = new List<StructureData.CheckPoint>();
            StructureData.Settings[] settings = new StructureData.Settings[StructureDictionary.Length];

            for(int i = 0; i < StructureDictionary.Length; i++)
            {
                StructureData data = StructureDictionary[i];
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

        public void Release()
        {
            indexBuffer?.Release();
            mapBuffer?.Release();
            checksBuffer?.Release();
            settingsBuffer?.Release();
        }
    }

    public struct EntityHandle{
        private ComputeBuffer entityInfoBuffer;
        private ComputeBuffer entityProfileBuffer; //used by gpu placement
        public NativeArray<ProfileE> entityProfileArray; //used by jobs

        public void Release(){
            entityInfoBuffer?.Release();
            entityProfileBuffer?.Release();

            //Release Static Entity Data
            EntityAuthoring[] EntityDictionary = WorldStorageHandler.WORLD_OPTIONS.Generation.Entities.SerializedData;
            foreach(EntityAuthoring entity in EntityDictionary) entity.Entity.Unset();
            if(entityProfileArray.IsCreated) entityProfileArray.Dispose();
        }

        public void Initialize()
        {
            Release();

            EntityAuthoring[] EntityDictionary = WorldStorageHandler.WORLD_OPTIONS.Generation.Entities.SerializedData;
            int numEntities = EntityDictionary.Length;
            Entity.Info.ProfileInfo[] entityInfo = new Entity.Info.ProfileInfo[numEntities];
            List<ProfileE> entityProfile = new List<ProfileE>();

            for(int i = 0; i < numEntities; i++)
            {
                Entity.Info.ProfileInfo info = EntityDictionary[i].Info;
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
    }

    public struct MemoryHandle{
        private ComputeShader HeapSetupShader;
        private ComputeShader AllocateShader;
        private ComputeShader DeallocateShader;

        private ComputeBuffer _GPUMemorySource;
        private ComputeBuffer _EmptyBlockHeap;
        private ComputeBuffer _AddressBuffer;
        private uint2[] addressLL;
        private bool initialized;

        public void Intiialize()
        {
            if(initialized) Release();

            MemoryBufferSettings settings = WorldStorageHandler.WORLD_OPTIONS.Quality.Memory.value;
            HeapSetupShader = Resources.Load<ComputeShader>("Compute/MemoryStructures/Heap/PrepareHeap");
            AllocateShader = Resources.Load<ComputeShader>("Compute/MemoryStructures/Heap/AllocateData");
            DeallocateShader = Resources.Load<ComputeShader>("Compute/MemoryStructures/Heap/DeallocateData");

            _GPUMemorySource = new ComputeBuffer(settings._BufferSize4Bytes, sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.Immutable);
            //2 channels, 1 for size, 2 for memory address
            _EmptyBlockHeap = new ComputeBuffer(settings._MaxHeapSize, sizeof(uint) * 2, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
            _AddressBuffer = new ComputeBuffer(settings._MaxAddressSize+1, sizeof(uint) * 2, ComputeBufferType.Structured, ComputeBufferMode.Immutable);

            addressLL = new uint2[settings._MaxAddressSize+1];
            addressLL[0].y = 1;
            
            initialized = true;
            PrepareMemory();
        }

        public void Release()
        {
            _GPUMemorySource?.Release();
            _EmptyBlockHeap?.Release();
            _AddressBuffer?.Release();
            initialized = false;
        }

        void PrepareMemory()
        {
            MemoryBufferSettings settings = WorldStorageHandler.WORLD_OPTIONS.Quality.Memory.value;
            HeapSetupShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
            HeapSetupShader.SetBuffer(0, "_Heap", _EmptyBlockHeap);
            HeapSetupShader.SetInt("_BufferSize4Bytes", settings._BufferSize4Bytes);

            HeapSetupShader.Dispatch(0, 1, 1, 1);
        }

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

        public void ReleaseMemoryDirect(ComputeBuffer address)
        {   
            if(!initialized) return;
            //Allocate Memory
            DeallocateShader.EnableKeyword("DIRECT_DEALLOCATE");
            DeallocateShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
            DeallocateShader.SetBuffer(0, "_Heap", _EmptyBlockHeap);
            DeallocateShader.SetBuffer(0, "_Address", address);

            DeallocateShader.Dispatch(0, 1, 1, 1);
        }

        //Returns compute buffer with memory address of empty space
        //NOTE: It is caller's responsibility to release memory address
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

        //Releases heap memory of block at memory address
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

        public readonly ComputeBuffer Storage => _GPUMemorySource;
        public readonly ComputeBuffer Address => _AddressBuffer;
    }
}
