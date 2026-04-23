using System.Collections.Generic;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Data.Structure;
using Arterra.Utils;
using Unity.Mathematics;
using Arterra.Data.Structure.Jigsaw;

/// <summary>
/// Stores the static jigsaw-system topology used by the structure-system path shaders.
/// </summary>
public struct StructSystem {
    ComputeBuffer StructureSystems;
    ComputeBuffer SystemStructures;
    ComputeBuffer StructurePorts;

    ComputeBuffer PortSocketOptions;
    ComputeBuffer SocketPortAtlas;
    ComputeBuffer TransitionDeltasAtlas;

    private struct SharedSocketBucket {
        public int2 range;
    }

    private sealed class SystemAtlasBuilder
    {
        private readonly int systemIndex;
        private readonly JigsawSystem system;
        private readonly List<StructureData> structureDictionary;
        private readonly List<JigsawSystem.SystemStructure> structures;
        private readonly List<StructurePort> structurePorts;
        private readonly List<PortSocketOption> portSocketOptions;
        private readonly List<SocketPortTransitions> socketPortAtlas;
        private readonly List<TransDeltas> transitionDeltasAtlas;

        private readonly Dictionary<string, int> socketNameIds = new();
        private readonly Dictionary<(uint inputFace, int socketNameId), SharedSocketBucket> socketBuckets = new();

        private readonly List<JigsawSystem.JigsawStructure> sourceStructures;
        private readonly List<StructureAuthoring> authoredStructures = new();

        private int systemStructureStart;
        private int systemStructureEnd;

        private readonly struct StructureAuthoring
        {
            public readonly JigsawSystem.JigsawStructure Source;
            public readonly StructureData.Settings Settings;
            public readonly JigsawSystem.JigsawSocket[] SocketsByFace;
            public readonly uint BasePorts;
            public readonly uint MaxY;
            public readonly uint MaxX;
            public readonly uint RotationCount;

            public StructureAuthoring(
                JigsawSystem.JigsawStructure source,
                StructureData.Settings settings,
                JigsawSystem.JigsawSocket[] socketsByFace,
                uint basePorts,
                uint maxY,
                uint maxX,
                uint rotationCount)
            {
                Source = source;
                Settings = settings;
                SocketsByFace = socketsByFace;
                BasePorts = basePorts;
                MaxY = maxY;
                MaxX = maxX;
                RotationCount = rotationCount;
            }
        }

        public SystemAtlasBuilder(
            int systemIndex,
            JigsawSystem system,
            List<StructureData> structureDictionary,
            List<JigsawSystem.SystemStructure> structures,
            List<StructurePort> structurePorts,
            List<PortSocketOption> portSocketOptions,
            List<SocketPortTransitions> socketPortAtlas,
            List<TransDeltas> transitionDeltasAtlas)
        {
            this.systemIndex = systemIndex;
            this.system = system;
            this.structureDictionary = structureDictionary;
            this.structures = structures;
            this.structurePorts = structurePorts;
            this.portSocketOptions = portSocketOptions;
            this.socketPortAtlas = socketPortAtlas;
            this.transitionDeltasAtlas = transitionDeltasAtlas;
            sourceStructures = system.Structures.value ?? new List<JigsawSystem.JigsawStructure>();
        }

        public uint2 Build()
        {
            systemStructureStart = structures.Count;
            if (sourceStructures.Count == 0)
                return new uint2((uint)systemStructureStart, (uint)systemStructureStart);

            BuildStructureMetadata();
            systemStructureEnd = structures.Count;
            BuildTransitionAtlas();
            return new uint2((uint)systemStructureStart, (uint)systemStructureEnd);
        }

        private void BuildStructureMetadata()
        {
            for (int localIndex = 0; localIndex < sourceStructures.Count; localIndex++) {
                JigsawSystem.JigsawStructure sourceStructure = sourceStructures[localIndex];
                string structureName = system.Names.value[sourceStructure.Structure];
                uint structureIndex = (uint)Config.CURRENT.Generation.Structures.value.StructureDictionary.RetrieveIndex(structureName);
                StructureData.Settings settings = structureDictionary[(int)structureIndex].settings.value;

                JigsawSystem.JigsawSocket[] socketsByFace = IndexSocketsByFace(sourceStructure.Sockets.value);
                StructurePort[] baseStructurePorts = new StructurePort[6];
                for (int face = 0; face < baseStructurePorts.Length; face++) {
                    baseStructurePorts[face] = new StructurePort {
                        socketSystemId = -1,
                        UV = float2.zero,
                        sockets = int2.zero,
                    };
                }
                uint basePorts = 0u;

                for (int face = 0; face < socketsByFace.Length; face++) {
                    JigsawSystem.JigsawSocket socket = socketsByFace[face];
                    if (string.IsNullOrEmpty(socket.Name))
                        continue;

                    basePorts |= FaceBit((uint)face);
                    baseStructurePorts[face] = new StructurePort {
                        socketSystemId = GetOrAddSocketNameId(socket.Name),
                        UV = socket.UV,
                        sockets = int2.zero,
                    };
                }

                uint maxY = HadRandYRot(settings.config) ? 4u : 1u;
                uint maxX = HadRandXRot(settings.config) ? 4u : 1u;
                uint rotationCount = GetRotationStateCount(settings.config, maxY, maxX);

                authoredStructures.Add(new StructureAuthoring(
                    sourceStructure,
                    settings,
                    socketsByFace,
                    basePorts,
                    maxY,
                    maxX,
                    rotationCount));

                structurePorts.AddRange(baseStructurePorts);
                structures.Add(new JigsawSystem.SystemStructure {
                    structureIndex = structureIndex,
                    basePorts = basePorts,
                });
            }
        }

        private void BuildTransitionAtlas()
        {
            CollectTargetSocketIds();

            int systemSocketAtlasStart = socketPortAtlas.Count;
            int socketBucketCount = socketNameIds.Count * 6;
            for (int bucketIndex = 0; bucketIndex < socketBucketCount; bucketIndex++)
                socketPortAtlas.Add(default);

            for (int structureIndex = systemStructureStart; structureIndex < systemStructureEnd; structureIndex++) {
                JigsawSystem.SystemStructure systemStructure = structures[structureIndex];
                systemStructure.socketAtlasStart = (uint)systemSocketAtlasStart;
                structures[structureIndex] = systemStructure;
            }

            for (int localIndex = 0; localIndex < authoredStructures.Count; localIndex++) {
                int systemStructIndex = systemStructureStart + localIndex;
                StructureAuthoring authored = authoredStructures[localIndex];
                for (uint baseFace = 0u; baseFace < 6u; baseFace++) {
                    if ((authored.BasePorts & FaceBit(baseFace)) == 0u)
                        continue;

                    int basePortIndex = GetBasePortIndex((uint)systemStructIndex, baseFace);
                    JigsawSystem.JigsawSocket sourceSocket = authored.SocketsByFace[baseFace];
                    List<JigsawSystem.JigsawConnection> connections = sourceSocket.Transitions.value;
                    if (string.IsNullOrEmpty(sourceSocket.Name) || connections == null || connections.Count == 0)
                        continue;

                    int optionStart = portSocketOptions.Count;
                    for (int connectionIndex = 0; connectionIndex < connections.Count; connectionIndex++) {
                        JigsawSystem.JigsawConnection connection = connections[connectionIndex];
                        int targetSocketSystemId = GetOrAddSocketNameId(connection.TargetName);
                        if (targetSocketSystemId < 0)
                            continue;

                        portSocketOptions.Add(new PortSocketOption {
                            chance = connection.chance,
                            socketSystemId = targetSocketSystemId,
                        });

                        for (uint inputFace = 0u; inputFace < 6u; inputFace++) {
                            GetOrCreateSocketBucket(systemSocketAtlasStart, inputFace, connection.TargetName);
                        }
                    }

                    StructurePort basePort = structurePorts[basePortIndex];
                    basePort.sockets = new int2(optionStart, portSocketOptions.Count);
                    structurePorts[basePortIndex] = basePort;
                    ConvertChanceRangeToSequential(portSocketOptions, optionStart, portSocketOptions.Count);
                }
            }
        }

        private void CollectTargetSocketIds()
        {
            for (int localIndex = 0; localIndex < authoredStructures.Count; localIndex++) {
                JigsawSystem.JigsawSocket[] socketsByFace = authoredStructures[localIndex].SocketsByFace;
                for (int face = 0; face < socketsByFace.Length; face++) {
                    List<JigsawSystem.JigsawConnection> connections = socketsByFace[face].Transitions.value;
                    if (connections == null)
                        continue;

                    for (int connectionIndex = 0; connectionIndex < connections.Count; connectionIndex++)
                        GetOrAddSocketNameId(connections[connectionIndex].TargetName);
                }
            }
        }

        private SharedSocketBucket GetOrCreateSocketBucket(int systemSocketAtlasStart, uint inputFace, string targetName)
        {
            int socketNameId = GetOrAddSocketNameId(targetName);
            if (socketNameId < 0)
                return new SharedSocketBucket { range = int2.zero };

            (uint inputFace, int socketNameId) bucketKey = (inputFace, socketNameId);
            if (socketBuckets.TryGetValue(bucketKey, out SharedSocketBucket existingBucket))
                return existingBucket;

            int transitionStart = transitionDeltasAtlas.Count;
            // Dedup transitions by spatial move only, since runtime visited state is position-based.
            HashSet<(int dx, int dy, int dz)> emittedTransitions = new();

            for (int nextLocalIndex = 0; nextLocalIndex < authoredStructures.Count; nextLocalIndex++) {
                int nextSystemStructIndex = systemStructureStart + nextLocalIndex;
                StructureAuthoring nextAuthored = authoredStructures[nextLocalIndex];

                for (uint nextRotIndex = 0u; nextRotIndex < nextAuthored.RotationCount; nextRotIndex++) {
                    uint nextRotMeta = RotMetaFromIndex(nextRotIndex, nextAuthored.MaxY, nextAuthored.MaxX);
                    uint nextBaseFace = GetBaseFaceFromObjectFace(inputFace, nextRotMeta);
                    JigsawSystem.JigsawSocket inputSocket = nextAuthored.SocketsByFace[nextBaseFace];
                    if (!SocketMatchesName(inputSocket, targetName))
                        continue;

                    int3 inputOffset = GetSocketOffset((uint)nextSystemStructIndex, nextAuthored.Settings, nextRotMeta, inputFace);
                    int3 originDelta = -inputOffset;

                    int structMeta = unchecked((int)PackAtlasStructMeta(nextRotMeta, (uint)nextSystemStructIndex));
                    uint nextRotatedPorts = GetRotatedPortMask(nextAuthored.BasePorts, nextRotMeta) & ~FaceBit(inputFace);
                    uint inputPort = (uint)GetBasePortIndex((uint)nextSystemStructIndex, nextBaseFace);

                    if (nextRotatedPorts == 0u) {
                        var capKey = (0, 0, 0);
                        if (emittedTransitions.Add(capKey)) {
                            transitionDeltasAtlas.Add(new TransDeltas {
                                deltaPosition = int3.zero,
                                originDelta = originDelta,
                                structMeta = structMeta,
                                nextPort = (int)inputPort,
                                inputFace = (int)inputFace,
                            });
                        }
                        continue;
                    }

                    for (uint outgoingFace = 0u; outgoingFace < 6u; outgoingFace++) {
                        if ((nextRotatedPorts & FaceBit(outgoingFace)) == 0u)
                            continue;

                        int3 deltaPosition = GetSocketOffset((uint)nextSystemStructIndex, nextAuthored.Settings, nextRotMeta, outgoingFace) - inputOffset;
                        int nextPort = GetBasePortIndex((uint)nextSystemStructIndex, GetBaseFaceFromObjectFace(outgoingFace, nextRotMeta));
                        var transitionKey = (deltaPosition.x, deltaPosition.y, deltaPosition.z);
                        if (!emittedTransitions.Add(transitionKey))
                            continue;

                        transitionDeltasAtlas.Add(new TransDeltas {
                            deltaPosition = deltaPosition,
                            originDelta = originDelta,
                            structMeta = structMeta,
                            nextPort = nextPort,
                            inputFace = (int)inputFace,
                        });
                    }
                }
            }

            SharedSocketBucket bucket = new SharedSocketBucket {
                range = new int2(transitionStart, transitionDeltasAtlas.Count),
            };

            socketBuckets[bucketKey] = bucket;
            socketPortAtlas[GetSocketAtlasIndex(systemSocketAtlasStart, socketNameId, inputFace)] = new SocketPortTransitions {
                range = bucket.range,
            };
            return bucket;
        }

        private static int GetSocketAtlasIndex(int systemSocketAtlasStart, int socketSystemId, uint inputFace)
        {
            return systemSocketAtlasStart + socketSystemId * 6 + (int)inputFace;
        }

        private int GetOrAddSocketNameId(string name)
        {
            if (string.IsNullOrEmpty(name))
                return -1;

            if (socketNameIds.TryGetValue(name, out int nameId))
                return nameId;

            nameId = socketNameIds.Count;
            socketNameIds.Add(name, nameId);
            return nameId;
        }

        private static int GetBasePortIndex(uint systemStructIndex, uint baseFace)
        {
            return (int)(systemStructIndex * 6u + baseFace);
        }

        private int3 GetSocketOffset(uint systemStructIndex, StructureData.Settings settings, uint rotMeta, uint objectFace)
        {
            uint baseFace = GetBaseFaceFromObjectFace(objectFace, rotMeta);
            StructurePort port = structurePorts[GetBasePortIndex(systemStructIndex, baseFace)];
            uint3 rot = GetRot(rotMeta);
            int3x3 rotMatrix = CustomUtility.RotationLookupTable[rot.y, rot.x, rot.z];
            float3 length = math.mul(rotMatrix, (float3)settings.GridSize);
            float3 newOrigin = math.min(length, 0.0f);
            float3 baseSocket = GetSocketBasePosition(settings.GridSize, port.UV, baseFace);
            // Match HLSL int conversion semantics (truncate toward zero) so CPU atlas
            // deltas line up exactly with GPU socket/object position helpers.
            return (int3)(math.mul(rotMatrix, baseSocket) - newOrigin);
        }

        private static JigsawSystem.JigsawSocket[] IndexSocketsByFace(List<JigsawSystem.JigsawSocket> sockets)
        {
            JigsawSystem.JigsawSocket[] socketsByFace = new JigsawSystem.JigsawSocket[6];
            if (sockets == null)
                return socketsByFace;

            for (int index = 0; index < sockets.Count; index++)
                socketsByFace[(int)sockets[index].Connection] = sockets[index];

            return socketsByFace;
        }

        private static bool SocketMatchesName(JigsawSystem.JigsawSocket socket, string targetName)
        {
            return !string.IsNullOrEmpty(targetName) && socket.Name == targetName;
        }
    }
    
    /// <summary>
    /// Initializes the static jigsaw-system data and uploads the transition atlas used by pathfinding.
    /// </summary>
    public void Initialize() {
        List<JigsawSystem> SystemDictionary = Config.CURRENT.Generation.Structures.value.SystemDictionary.Reg;
        List<StructureData> StructureDictionary = Config.CURRENT.Generation.Structures.value.StructureDictionary.Reg;

        List<StructSystemInfo> systems = new(SystemDictionary.Count);
        for (int index = 0; index < SystemDictionary.Count; index++)
            systems.Add(default);

        bool hasAnySystemData = false;
        List<JigsawSystem.SystemStructure> structures = new ();
        List<StructurePort> structurePorts = new ();
        List<PortSocketOption> portSocketOptions = new ();
        List<SocketPortTransitions> socketPortAtlas = new ();
        List<TransDeltas> transitionDeltasAtlas = new ();

        for(int i = 0; i < SystemDictionary.Count; i++) {
            JigsawSystem system = SystemDictionary[i];
            List<JigsawSystem.JigsawStructure> sysStructs = system.Structures.value;
            if (sysStructs == null || sysStructs.Count == 0)
                continue;

            hasAnySystemData = true;//

            SystemAtlasBuilder builder = new(
                i, system,
                StructureDictionary, structures,
                structurePorts,
                portSocketOptions,
                socketPortAtlas,
                transitionDeltasAtlas);

            uint2 systemStructRange = builder.Build();

            systems[i] = new StructSystemInfo {
                edgeFrequency = system.edgeFrequency,
                poissonRadius = system.PoissonRadius,
                cullIsolatedAnchors = system.cullIsolatedAnchors ? 1u : 0u,
                structures = systemStructRange
            };
        }

        if (portSocketOptions.Count == 0)
            portSocketOptions.Add(default);
        if (socketPortAtlas.Count == 0)
            socketPortAtlas.Add(default);
        if (transitionDeltasAtlas.Count == 0)
            transitionDeltasAtlas.Add(default);

        if (!hasAnySystemData) return;
        StructureSystems = new ComputeBuffer(SystemDictionary.Count, StructSystemInfo.size, ComputeBufferType.Structured); //By doubling stride, we compress the prefix sums
        SystemStructures = new ComputeBuffer(structures.Count, JigsawSystem.SystemStructure.size, ComputeBufferType.Structured);
        StructurePorts = new ComputeBuffer(structurePorts.Count, StructurePort.size, ComputeBufferType.Structured);
        PortSocketOptions = new ComputeBuffer(portSocketOptions.Count, PortSocketOption.size, ComputeBufferType.Structured);
        SocketPortAtlas = new ComputeBuffer(socketPortAtlas.Count, SocketPortTransitions.size, ComputeBufferType.Structured);
        TransitionDeltasAtlas = new ComputeBuffer(transitionDeltasAtlas.Count, TransDeltas.size, ComputeBufferType.Structured);

        StructureSystems.SetData(systems);
        SystemStructures.SetData(structures);
        StructurePorts.SetData(structurePorts);
        PortSocketOptions.SetData(portSocketOptions);
        SocketPortAtlas.SetData(socketPortAtlas);
        TransitionDeltasAtlas.SetData(transitionDeltasAtlas);

        Shader.SetGlobalBuffer("_SystemInfo", StructureSystems);
        Shader.SetGlobalBuffer("_SystemStructures", SystemStructures);
        Shader.SetGlobalBuffer("_StructurePorts", StructurePorts);
        Shader.SetGlobalBuffer("_PortSocketOptions", PortSocketOptions);
        Shader.SetGlobalBuffer("_SocketPortAtlas", SocketPortAtlas);
        Shader.SetGlobalBuffer("_TransitionDeltasAtlas", TransitionDeltasAtlas);
    }

    private static void ConvertChanceRangeToSequential(
        List<PortSocketOption> portSocketOptions,
        int start,
        int end)
    {
        if (end <= start)
            return;

        float totalPositiveWeight = 0.0f;
        int positiveCount = 0;

        for (int index = start; index < end; index++) {
            float weight = math.max(portSocketOptions[index].chance, 0.0f);
            totalPositiveWeight += weight;
            if (weight > 0.0f)
                positiveCount++;
        }

        if (positiveCount == 0) {
            int remainingCount = end - start;
            for (int index = start; index < end; index++) {
                PortSocketOption entry = portSocketOptions[index];
                entry.chance = 1.0f / remainingCount;
                portSocketOptions[index] = entry;
                remainingCount--;
            }
            return;
        }

        float remainingWeight = totalPositiveWeight;
        int lastPositiveIndex = -1;
        for (int index = end - 1; index >= start; index--) {
            if (portSocketOptions[index].chance > 0.0f) {
                lastPositiveIndex = index;
                break;
            }
        }

        for (int index = start; index < end; index++) {
            PortSocketOption entry = portSocketOptions[index];
            float weight = math.max(entry.chance, 0.0f);

            if (weight <= 0.0f) {
                entry.chance = 0.0f;
            } else if (index == lastPositiveIndex || remainingWeight <= weight) {
                entry.chance = 1.0f;
            } else {
                entry.chance = weight / remainingWeight;
            }

            portSocketOptions[index] = entry;
            remainingWeight -= weight;
        }
    }

    private static bool HadRandYRot(uint config) => (config & 0x1u) != 0u;

    private static bool HadRandXRot(uint config) => (config & 0x2u) != 0u;

    private static bool HadRandZRot(uint config) => (config & 0x4u) != 0u;

    private static uint GetRotationStateCount(uint config, uint maxY, uint maxX) => maxY * maxX * (HadRandZRot(config) ? 4u : 1u);

    private static uint PackAtlasStructMeta(uint rotMeta, uint sysStructIndex) => (rotMeta << 24) | (sysStructIndex & 0xFFFFFFu);

    private static uint RotMetaFromIndex(uint rotIndex, uint maxY, uint maxX) {
        uint rotY = rotIndex % maxY;
        uint rotX = (rotIndex / maxY) % maxX;
        uint rotZ = rotIndex / (maxY * maxX);
        return (rotY & 0x3u) | ((rotX & 0x3u) << 2) | ((rotZ & 0x3u) << 4);
    }

    private static uint FaceBit(uint face) => 1u << (int)face;

    private static uint3 GetRot(uint meta) => new((meta >> 2) & 0x3u, meta & 0x3u, (meta >> 4) & 0x3u);

    private static uint GetObjectFaceFromBaseFace(uint baseFace, uint rotMeta) {
        uint3 rot = GetRot(rotMeta);
        int3x3 rotMatrix = CustomUtility.RotationLookupTable[rot.y, rot.x, rot.z];
        return DirToFaceIdx(math.mul(rotMatrix, FaceIdxToDir(baseFace)));
    }

    private static uint GetBaseFaceFromObjectFace(uint objectFace, uint rotMeta) {
        uint3 rot = GetRot(rotMeta);
        int3x3 rotMatrix = CustomUtility.RotationLookupTable[rot.y, rot.x, rot.z];
        int3 baseDirection = math.mul(math.transpose(rotMatrix), FaceIdxToDir(objectFace));
        return DirToFaceIdx(baseDirection);
    }

    private static int3 FaceIdxToDir(uint face) {
        int axis = (int)(face % 3u);
        int sign = face / 3u == 0u ? -1 : 1;
        return axis switch {
            0 => new int3(sign, 0, 0),
            1 => new int3(0, sign, 0),
            _ => new int3(0, 0, sign),
        };
    }

    private static uint DirToFaceIdx(int3 dir) {
        if (math.abs(dir.x) == 1)
            return dir.x > 0 ? 3u : 0u;
        if (math.abs(dir.y) == 1)
            return dir.y > 0 ? 4u : 1u;
        return dir.z > 0 ? 5u : 2u;
    }

    private static uint GetRotatedPortMask(uint basePorts, uint rotMeta) {
        uint rotatedMask = 0u;
        for (uint baseFace = 0u; baseFace < 6u; baseFace++) {
            if ((basePorts & FaceBit(baseFace)) == 0u)
                continue;
            rotatedMask |= FaceBit(GetObjectFaceFromBaseFace(baseFace, rotMeta));
        }

        return rotatedMask;
    }

    private static float3 GetSocketBasePosition(uint3 size, float2 uv, uint baseFace) {
        float align = baseFace < 3u ? 0.0f : 1.0f;

        return baseFace switch { //y is up, people don't expect up to ever be x axis
            0u or 3u => new float3(align, uv.y, uv.x) * size,
            1u or 4u => new float3(uv.x, align, uv.y) * size,
            _ => new float3(uv.x, uv.y, align) * size,
        };
    }

    /* 
    
    Floodfill Pathfind with Atlas:
        - Given a structure, look at its Ports in _StructurePorts
        - For each port select a random SocketPortTransitions option from its lookup range
        - Each option carries both its source-specific chance and the shared transition range for a specific (face, name) bucket within the system
        - For each transition within TransitionDeltasAtlas, enqueue the next position (deltaPos + currentPos) and port (index in _StructurePorts)
        Backtracking:
        - Store at each batchVisit coord just the index within SocketTransAtlas and pathId, backtrack by subtracting deltaPosition
        */
    /// <summary>
    /// A source-port-specific option used to choose a target socket id by chance.
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct PortSocketOption {
        /// <summary>The sequential chance used when selecting this option from the source port.</summary>
        public float chance;
        /// <summary>The system-local socket id of the target socket name.</summary>
        public int socketSystemId;

        /// <summary>The GPU stride for <see cref="PortSocketOption"/>.</summary>
        public static int size => sizeof(float) + sizeof(int);
    }

    /// <summary>
    /// A shared lookup table entry for one system-local (socket id, input face) bucket.
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct SocketPortTransitions {
        /// <summary>The range within <see cref="TransitionDeltasAtlas"/>.</summary>
        public int2 range;

        /// <summary>The GPU stride for <see cref="SocketPortTransitions"/>.</summary>
        public static int size => sizeof(int) * 2;
    }
    /// <summary>
    /// A concrete transition candidate emitted by the CPU atlas builder.
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct TransDeltas {
        /// <summary>
        /// The offset from the current input socket to the emitted output socket.
        /// </summary>
        public int3 deltaPosition;
        /// <summary>The offset from the current input socket to the structure origin.</summary>
        public int3 originDelta;
        /// <summary>The packed system-structure index and rotation of the next structure.</summary>
        public int structMeta; 
        /// <summary>The index of the emitted base-space output port.</summary>
        public int nextPort;
        /// <summary>The object-space face on which this structure receives the current socket.</summary>
        public int inputFace;

        /// <summary>The GPU stride for <see cref="TransDeltas"/>.</summary>
        public static int size => sizeof(int) * 9;
    }

    /// <summary>
    /// A base-space port definition. There are exactly six of these per system structure.
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct StructurePort {
        /// <summary>The system-local socket id associated with this base-space port.</summary>
        public int socketSystemId;
        /// <summary>The UV of the source socket on the base face.</summary>
        public float2 UV;
        /// <summary>The range of per-port options in <see cref="PortSocketOption"/> for this port.</summary>
        public int2 sockets;

        /// <summary>The GPU stride for <see cref="StructurePort"/>.</summary>
        public static int size => sizeof(int) * 3 + sizeof(float) * 2;
    }

    /// <summary>
    /// Metadata describing each jigsaw system and the range of structures it owns.
    /// </summary>
    public struct StructSystemInfo {
        /// <summary>The GPU stride for <see cref="StructSystemInfo"/>.</summary>
        public static int size => sizeof(float) * 2 + 3 * sizeof(uint);
        /// <summary>The probability weighting used when seeding edges from biomes.</summary>
        public float edgeFrequency;
        /// <summary>The poisson-prune radius in object-space units for this system's anchors.</summary>
        public float poissonRadius;
        /// <summary>When true, anchors with no successful path connections are dropped.</summary>
        public uint cullIsolatedAnchors;
        /// <summary>The range of system-structure entries belonging to this system.</summary>
        public uint2 structures;
    };

    /// <summary>
    /// Releases all GPU buffers owned by the jigsaw-system preset.
    /// </summary>
    public void Release()
    {
        StructureSystems?.Release();
        SystemStructures?.Release();
        StructurePorts?.Release();
        PortSocketOptions?.Release();
        SocketPortAtlas?.Release();
        TransitionDeltasAtlas?.Release();
    }
}
