using System;
using System.Collections.Generic;
using System.Linq;
using Arterra.Configuration;
using Arterra.Editor;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Structure.Jigsaw {
    [CreateAssetMenu(menuName = "Generation/Structure/Jigsaw/System")]
    public class JigsawSystem : Category<JigsawSystem>{
        /// <summary>The names of all structures within the external <see cref="Config.GenerationSettings.Structures"/> registry that
        /// are used in the system. All entries that require references to a structure may indicate the index within
        /// this list of the name of the material in the external registry. </summary>
        public Option<List<string>> Names;
        public Option<List<JigsawStructure>> Structures;
        public float edgeFrequency;
        public float anchorDensity = 1.0f;
        public bool cullIsolatedAnchors = false;

        [Serializable]
        public struct JigsawStructure {
            [RegistryReference("Structures")]
            public int Structure;
            public Option<List<JigsawSocket>> Sockets;
        }

        [Serializable]
        public struct JigsawSocket {
            public string Name;
            public Facing Connection;
            public float2 UV;
            public Option<List<JigsawConnection>> Transitions;
            public enum Facing : uint {
                Left = 0, Right = 3,
                Bottom = 1, Top = 4,
                Back = 2, Forward = 5,
            }
        };

        [Serializable]
        public struct JigsawConnection {
            public string TargetName;
            public float chance;
        };

        public struct SystemStructure {
            public static int size => 3 * sizeof(uint);
            public uint structureIndex;
            public uint basePorts;
            public uint socketAtlasStart;
        };
    }
}