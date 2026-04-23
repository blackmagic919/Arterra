#ifndef STRUCTURE_PATH_TYPES
#define STRUCTURE_PATH_TYPES

const static uint MAX_TRANSITIONS_PER_NODE = 12u;
static const uint MAX_BATCH_STEPS = 8u;
static const uint INVALID_VISITED = 0u;
static const uint FRONTIER_COORD_MASK = 0x1FFFFFFFu;
static const uint INVALID_PATH = 0xFFFFFFFFu;
static const uint VISITED_DIR_BIT = 0x80000000u;
static const uint VISITED_TRANSITION_MASK = 0x1FFFFFFu;
static const uint VISITED_STEP_SHIFT = 25u;
static const uint VISITED_STEP_MASK = 0x3Fu << VISITED_STEP_SHIFT;
static const uint VISITED_SEED_TRANSITION = VISITED_TRANSITION_MASK;
static const uint STRUCT_INFO_DEPTH_MASK = 0x3Fu;
static const uint STRUCT_INFO_CAP_BIT = 1u << 6u;
static const uint STRUCT_INFO_PATH_SHIFT = 7u;
static const uint STRUCT_INFO_PATH_MASK = 0xFFFFFFu << STRUCT_INFO_PATH_SHIFT;
static const uint STRUCT_INFO_PARENT_DEPTH_MASK = 0x3Fu;
static const uint INVALID_OWNER_ID = 0xFFFFFFu;
static const int3 NO_ENDS_COORD = int3(-32768, -32768, -32768);
int numVoxelsPerChunk;
int oCellOffset;
int batchSize;

struct pathEnds {
    int3 start;
    int3 end;
};

struct StructurePort {
    int socketSystemId;
    float2 UV;
    int2 sockets;
};

struct PortSocketOption {
    float chance;
    int socketSystemId;
};

struct SocketPortTransitions {
    int2 range;
};

struct TransDeltas {
    int3 deltaPosition;
    int3 originDelta;
    int structMeta;
    int nextPort;
    int inputFace;
};

struct Socket {
    float connectChance;
    uint2 transitions;
};

struct Transition {
    uint structure;
    uint oppSocketFace;
};

struct settings {
    uint3 size;
    int minimumLOD;
    uint config;
};

struct checkData {
    float3 position;
    uint bounds;
};

struct SystemStructure {
    uint structureIndex;
    uint basePorts;
    uint socketAtlasStart;
};

struct anchor {
    int3 pos;
    int system;
    int stct;
};

struct PathNode {
    int id;
    uint visited;
};

struct structureData {
    float3 structurePos;
    uint meta;
};

struct intermediateStructureData {
    structureData structure;
    uint2 info;
};

inline uint3 GetRot(uint meta) { return uint3((meta >> 2) & 0x3u, meta & 0x3u, (meta >> 4) & 0x3u); }
inline uint PackRot(uint3 rot) { return (rot.y & 0x3u) | ((rot.x & 0x3u) << 2) | ((rot.z & 0x3u) << 4); }
inline uint ConcatStct(uint rot, uint index) { return (rot << 24) | index & 0xFFFFFFu; }
inline uint2 UnpackStct(uint stct) { return uint2((stct >> 24) & 0x3Fu, stct & 0xFFFFFFu); }
inline bool VisitedFromEnd(uint visited)
{
    return (visited & VISITED_DIR_BIT) != 0u;
}

inline uint VisitedTransitionIndex(uint visited)
{
    return visited & VISITED_TRANSITION_MASK;
}

inline uint EncodeVisited(uint transitionIndex, bool fromEnd, uint step)
{
    uint stepEnc = (63u - min(step, 63u)) & 0x3Fu;
    uint value = (transitionIndex & VISITED_TRANSITION_MASK) | (stepEnc << VISITED_STEP_SHIFT);
    if (fromEnd) value |= VISITED_DIR_BIT;
    return value;
}

inline uint DecodeVisitedStep(uint visited)
{
    uint stepEnc = (visited & VISITED_STEP_MASK) >> VISITED_STEP_SHIFT;
    return 63u - stepEnc;
}

inline uint2 PackStructInfo(uint depth, bool isCap, uint ownerPathId, uint parentDepth)
{
    uint path = min(ownerPathId, INVALID_OWNER_ID);

    uint x = (depth & STRUCT_INFO_DEPTH_MASK)
        | ((path & 0xFFFFFFu) << STRUCT_INFO_PATH_SHIFT)
        | (isCap ? STRUCT_INFO_CAP_BIT : 0u);

    uint y = (parentDepth & STRUCT_INFO_PARENT_DEPTH_MASK);

    return uint2(x, y);
}

inline uint StructInfoDepth(uint2 info) { return info.x & STRUCT_INFO_DEPTH_MASK; }
inline bool StructInfoIsCap(uint2 info) { return (info.x & STRUCT_INFO_CAP_BIT) != 0u; }
inline uint StructInfoOwnerPath(uint2 info) { return (info.x & STRUCT_INFO_PATH_MASK) >> STRUCT_INFO_PATH_SHIFT; }
inline uint StructInfoParentDepth(uint2 info) { return info.y & STRUCT_INFO_PARENT_DEPTH_MASK; }

inline uint PackCapOwner(uint ownerPathId, uint parentDepth, bool hasParent)
{
    uint owner = min(ownerPathId, INVALID_OWNER_ID);
    return (owner & 0xFFFFFFu)
        | ((parentDepth & 0x3Fu) << 24u)
        | (hasParent ? (1u << 30u) : 0u);
}

inline uint CapOwnerPath(uint packed) { return packed & 0xFFFFFFu; }
inline uint CapOwnerParentDepth(uint packed) { return (packed >> 24u) & 0x3Fu; }
inline bool CapOwnerHasParent(uint packed) { return ((packed >> 30u) & 0x1u) != 0u; }

int3 DecodeBatchCoord(uint flat, int sideLength)
{
    return coordFromIndexManual(flat, (uint)sideLength);
}

uint EncodeCoordFace(uint flatCoord, uint objectFace)
{
    return (flatCoord & FRONTIER_COORD_MASK) | ((objectFace & 0x7u) << 29);
}

void DecodeSocketCap(uint2 packed, out uint portIndex, out int3 localCoord, out uint objectFace) {
    portIndex = packed.x;
    objectFace = (packed.y >> 29) & 0x7u;
    localCoord = DecodeBatchCoord(packed.y & FRONTIER_COORD_MASK, numVoxelsPerChunk) + oCellOffset;
}

uint3 MakeSocketCapMeta(uint portIndex, int3 socketPos, uint objectFace, uint ownerPathId, uint parentDepth, bool hasParent) {
    socketPos -= oCellOffset;
    return uint3(
        portIndex,
        EncodeCoordFace(indexFromCoordManual(socketPos, numVoxelsPerChunk), objectFace),
        PackCapOwner(ownerPathId, parentDepth, hasParent)
    );
}

#endif