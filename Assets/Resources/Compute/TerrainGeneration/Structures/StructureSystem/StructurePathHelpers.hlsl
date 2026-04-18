#ifndef STRUCTURE_PATH_HELPERS
#define STRUCTURE_PATH_HELPERS
#include "Assets/Resources/Compute/Utility/RotationTables.hlsl"
#include "Assets/Resources/Compute/TerrainGeneration/Structures/StructureSystem/SolveAnchorRotation.hlsl"
#include "Assets/Resources/Compute/TerrainGeneration/Structures/StructureSystem/StructurePathTypes.hlsl"

#ifndef HAS_STRUCT_SETTINGS
#define HAS_STRUCT_SETTINGS
StructuredBuffer<settings> _StructureSettings;
#endif 

StructuredBuffer<SystemStructure> _SystemStructures;
StructuredBuffer<StructurePort> _StructurePorts;
StructuredBuffer<PortSocketOption> _PortSocketOptions;
StructuredBuffer<SocketPortTransitions> _SocketPortAtlas;
StructuredBuffer<TransDeltas> _TransitionDeltasAtlas;

float3 GetSocketBasePosition(uint3 size, float2 uv, uint baseFace) {
    float align = baseFace < 3u ? 0.0 : 1.0;

    if (baseFace == 0u || baseFace == 3u) //y is up, people don't expect up to ever be x axis
        return float3(align, uv.y, uv.x) * (float3)size;
    if (baseFace == 1u || baseFace == 4u)
        return float3(uv.x, align, uv.y) * (float3)size;
    return float3(uv.x, uv.y, align) * (float3)size;
}

uint GetObjectFaceFromBaseFace(uint baseFace, uint rotMeta) {
    uint3 rot = GetRot(rotMeta);
    float3x3 rotMatrix = RotationLookupTable[rot.y][rot.x][rot.z];
    return DirToFaceIdx(mul(rotMatrix, FaceIdxToDir(baseFace)));
}

uint GetBaseFaceFromObjectFace(uint objectFace, uint rotMeta) {
    uint3 rot = GetRot(rotMeta);
    float3x3 rotMatrix = RotationLookupTable[rot.y][rot.x][rot.z];
    return DirToFaceIdx(mul(transpose(rotMatrix), FaceIdxToDir(objectFace)));
}

uint GetRotatedPortMask(uint basePorts, uint rotMeta) {
    uint rotatedMask = 0u;
    [unroll]
    for (uint baseFace = 0u; baseFace < 6u; baseFace++) {
        if ((basePorts & FaceBit(baseFace)) == 0u)
            continue;
        rotatedMask |= FaceBit(GetObjectFaceFromBaseFace(baseFace, rotMeta));
    }
    return rotatedMask;
}

uint GetBasePortIndex(uint sysStructIndex, uint baseFace) {
    return sysStructIndex * 6u + baseFace;
}

uint GetSocketAtlasIndex(uint portIndex, int socketSystemId, uint inputFace) {
    uint sysStructIndex = portIndex / 6u;
    return _SystemStructures[sysStructIndex].socketAtlasStart + (uint)socketSystemId * 6u + inputFace;
}

uint GetPortBaseFace(uint portIndex) {
    return portIndex % 6u;
}

int3 GetSocketOffset(uint sysStructIndex, uint rotMeta, uint objectFace) {
    SystemStructure systemStructure = _SystemStructures[sysStructIndex];
    settings structureSettings = _StructureSettings[systemStructure.structureIndex];
    uint baseFace = GetBaseFaceFromObjectFace(objectFace, rotMeta);
    uint portIndex = GetBasePortIndex(sysStructIndex, baseFace);
    StructurePort port = _StructurePorts[portIndex];

    uint3 rot = GetRot(rotMeta);
    float3x3 rotMatrix = RotationLookupTable[rot.y][rot.x][rot.z];
    float3 length = mul(rotMatrix, (float3)structureSettings.size);
    float3 newOrigin = min(length, 0.0);
    float3 baseSocket = GetSocketBasePosition(structureSettings.size, port.UV, baseFace);

    return int3(mul(rotMatrix, baseSocket) - newOrigin);
}

int3 GetSocketObjectPosition(int3 origin, uint sysStructIndex, uint rotMeta, uint objectFace) {
    return origin + GetSocketOffset(sysStructIndex, rotMeta, objectFace);
}

uint GetSocketFaceAtWorldPos(int3 socketWorldPos, int3 originWorld, uint sysStructIndex, uint rotMeta) {
    [unroll]
    for (uint face = 0u; face < 6u; face++) {
        if (all(GetSocketObjectPosition(originWorld, sysStructIndex, rotMeta, face) == socketWorldPos))
            return face;
    }

    return INVALID_PATH;
}


bool CanConnectPorts(uint currentPortIndex, uint currentObjectFace, uint nextPortIndex, uint nextObjectFace) {
    if (nextObjectFace != (currentObjectFace + 3u) % 6u)
        return false;

    StructurePort currentPort = _StructurePorts[currentPortIndex];
    StructurePort nextPort = _StructurePorts[nextPortIndex];
    if (nextPort.socketSystemId < 0)
        return false;

    [loop][fastopt]
    for (int optionIndex = currentPort.sockets.x; optionIndex < currentPort.sockets.y; optionIndex++) {
        PortSocketOption option = _PortSocketOptions[optionIndex];
        if (option.socketSystemId != nextPort.socketSystemId)
            continue;

        SocketPortTransitions bucket = _SocketPortAtlas[GetSocketAtlasIndex(currentPortIndex, option.socketSystemId, OppositeFace(currentObjectFace))];
        if (bucket.range.x < bucket.range.y)
            return true;
    }

    return false;
}


bool InsideBatch(int3 coord, uint axis) {
    int3 extent = int3((int)axis, (int)axis, (int)axis);
    return !any(coord < 0) && !any(coord >= extent);
}


uint PathIndexFromCoordAxis(int3 coord, uint axis) {
    uint safeAxis = max(axis, 1u);
    int maxCoord = (int)safeAxis - 1;
    int3 clamped = clamp(coord, int3(0, 0, 0), int3(maxCoord, maxCoord, maxCoord));
    return indexFromCoordManual((uint3)clamped, safeAxis);
}


int3 CoordFromPathIndexAxis(uint index, uint axis)
{
    return coordFromIndexManual(index, max(axis, 1u));
}


#endif