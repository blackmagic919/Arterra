const static int numThreads = 8;
const static int numThreadsFlat = 64;
const static uint orientationCount = 24;

#include "Assets/Resources/Compute/Utility/GetIndex.hlsl"
#include "Assets/Resources/Compute/Utility/RotationTables.hlsl"

struct modelVertex {
    float3 position;
    float3 normal;
    uint color;
};

struct modelInfo {
    uint modelsStart;
    uint modelsCount;
    uint defaultInfo;
};

struct submodelInfo {
    uint vertexStart;
    uint vertexCount;
    uint indexStart;
    uint indexCount;
};

struct matTerrain {
    int textureIndex;
    float baseTextureScale;
    uint modelIndex;
    uint geoShaderInd;
};

struct vertex {
    float3 position;
#ifndef COMPACT_MODEL_VERTICES
    float3 normal;
#endif
    int2 material;
};

StructuredBuffer<modelVertex> _ModelVertices;
StructuredBuffer<uint> _ModelIndices;
StructuredBuffer<modelInfo> _ModelInfo;
StructuredBuffer<submodelInfo> _SubmodelInfo;
StructuredBuffer<matTerrain> _MatTerrainData;
StructuredBuffer<uint2> Metadata;
StructuredBuffer<uint> MapData;
StructuredBuffer<uint> ModelFlags;
uint useModelFlags;
uint bSTART_modelFlags;
RWStructuredBuffer<uint> modelDict;
RWStructuredBuffer<uint2> chunkModels;
RWStructuredBuffer<vertex> vertexes;
RWStructuredBuffer<uint3> triangles;
RWStructuredBuffer<uint> counter;

uint metadataCount;
uint bSTART_metadata;
uint3 modelGridSize;
uint3 modelMapSize;
uint3 modelMapOffset;
int3 modelPositionOffset;
float IsoLevel;
uint numModels;
uint numMaterials;
uint bSTART_map;
uint bSTART_modelDict;
uint bSTART_models;
uint bSTART_verts;
uint bSTART_modelT;
uint bCOUNT_models;
uint2 counterInd; // x = vertices, y = model triangles

[numthreads(numThreadsFlat, 1, 1)]
void ScatterMetadata(uint3 id : SV_DispatchThreadID) {
    if (id.x >= metadataCount) return;

    uint cell = Metadata[bSTART_metadata + id.x].x;
    if (cell >= modelGridSize.x * modelGridSize.y * modelGridSize.z) return;
    modelDict[bSTART_modelDict + cell] = id.x;
}

[numthreads(numThreads, numThreads, numThreads)]
void CollectModels(uint3 id : SV_DispatchThreadID) {
    if (any(id >= modelGridSize)) return;

    uint mapIndex = indexFromCoordIrregular(id + modelMapOffset, modelMapSize.yz);
    uint data = MapData[bSTART_map + mapIndex];
    uint materialIndex = (data >> 16) & 0x7FFF;
    if (materialIndex >= numMaterials) return;
    uint modelIndex = _MatTerrainData[materialIndex].modelIndex;
    if (modelIndex <= 1 || modelIndex >= numModels) return;
    if (((data >> 8) & 255) == 0) return;

    uint cell = indexFromCoordIrregular(id, modelGridSize.yz);
    modelInfo collection = _ModelInfo[modelIndex];
    uint info = collection.defaultInfo;
    uint candidate = modelDict[bSTART_modelDict + cell];
    // Only uploaded metadata participates in validation; the model append count is unrelated.
    if (candidate < metadataCount) {
        uint2 metadata = Metadata[bSTART_metadata + candidate];
        if (metadata.x == cell) info = metadata.y;
    }

    if (((info >> 8) & 255) >= collection.modelsCount || (info & 255) >= orientationCount) return;

    uint appendIndex;
    InterlockedAdd(counter[bCOUNT_models], 1, appendIndex);
    chunkModels[bSTART_models + appendIndex] = uint2(cell, info);
}

float3 RotateFacing(float3 value, uint facing) {
    value = mul(RotationLookupTable[0][0][facing / 6], value);
    switch (facing % 6) {
        case 1: return mul(RotationLookupTable[3][0][0], value);
        case 2: return mul(RotationLookupTable[2][0][0], value);
        case 3: return mul(RotationLookupTable[1][0][0], value);
        case 4: return mul(RotationLookupTable[0][1][0], value);
        case 5: return mul(RotationLookupTable[0][3][0], value);
        default: return value;
    }
}

[numthreads(numThreadsFlat, 1, 1)]
void EmitModels(uint3 id : SV_DispatchThreadID) {
    if (id.x >= counter[bCOUNT_models]) return;

    uint2 instance = chunkModels[bSTART_models + id.x];
    uint3 cell = uint3(instance.x / (modelGridSize.y * modelGridSize.z),
        (instance.x / modelGridSize.z) % modelGridSize.y, instance.x % modelGridSize.z);
    uint mapIndex = indexFromCoordIrregular(cell + modelMapOffset, modelMapSize.yz);
    uint data = MapData[bSTART_map + mapIndex];
    uint materialIndex = (data >> 16) & 0x7FFF;
    if (materialIndex >= numMaterials) return;
    uint modelIndex = _MatTerrainData[materialIndex].modelIndex;
    if (modelIndex <= 1 || modelIndex >= numModels) return;
    modelInfo collection = _ModelInfo[modelIndex];
    uint variant = (instance.y >> 8) & 255;
    uint facing = instance.y & 255;
    if (variant >= collection.modelsCount || facing >= orientationCount) return;
    submodelInfo model = _SubmodelInfo[collection.modelsStart + variant];
    float density = ((data >> 8) & 255) / 255.0;
    float scale = max((density - IsoLevel) / (1.0 - IsoLevel), 0.0);

    uint flags = 0;
    if (useModelFlags != 0)
        flags = (ModelFlags[bSTART_modelFlags + mapIndex / 4] >> ((mapIndex % 4) * 8)) & 255;

    uint firstVertex, firstTriangle;
    InterlockedAdd(counter[counterInd.x], model.vertexCount, firstVertex);
    InterlockedAdd(counter[counterInd.y], model.indexCount / 3, firstTriangle);

    for (uint v = 0; v < model.vertexCount; v++) {
        modelVertex source = _ModelVertices[model.vertexStart + v];
        vertex output;
        output.position = (float3)cell + modelPositionOffset + RotateFacing(saturate(source.position) - 0.5, facing) * scale;
#ifndef COMPACT_MODEL_VERTICES
        output.normal = RotateFacing(source.normal, facing);
#endif
        output.material = int2(asint(materialIndex | (flags << 24)), asint(source.color));
        vertexes[bSTART_verts + firstVertex + v] = output;
    }

    for (uint i = 0; i < model.indexCount / 3; i++) {
        uint index = model.indexStart + i * 3;
        uint3 tri = uint3(_ModelIndices[index], _ModelIndices[index + 1], _ModelIndices[index + 2]);
        triangles[bSTART_modelT + firstTriangle + i] = firstVertex + tri;
    }
}
