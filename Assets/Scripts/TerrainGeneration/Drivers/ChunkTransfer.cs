using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arterra.Configuration;
using Arterra.Data.Entity;
using Arterra.Engine.Terrain;

namespace Arterra.Core.Storage {
    /// <summary>Initial chunk delivery. Map bytes include the localized material header; metadata travels separately.</summary>
    public struct ChunkTransfer {
        public byte[] MapData;
        public List<Registerable<Entity>> EntityData;
        public KeyValuePair<uint, object>[] MapMetadata;

        public static ChunkTransfer Capture(CPUMapManager.ChunkMapInfo chunk) {
            byte[] mapData = null;
            if (Config.CURRENT.System.Network.value.Authoritative || chunk.MapFlags != CPUMapManager.ChunkMapInfo.Flags.Clean) {
                using var map = new MemoryStream();
                ChunkMapCodec.Write(map, chunk, includeResolutions: false, includeMetadata: false);
                mapData = map.ToArray();
            }

            return new ChunkTransfer {
                MapData = mapData,
                EntityData = EntityManager.GetChunkEntities(chunk.CCoord)
                    .Select(entity => new Registerable<Entity>(entity)).ToList(),
                MapMetadata = chunk.MapMetaData.ToArray(),
            };
        }

        public Chunk.ReadbackInfo Read() {
            using var map = MapData == null ? null : new MemoryStream(MapData);

            return new Chunk.ReadbackInfo(
                map == null ? null : ChunkMapCodec.Read(map, Config.CURRENT.Quality.Terrain.value.mapChunkSize, 0, out _),
                MapMetadata,
                EntityData.Select(entity => entity.Value).ToList());
        }
    }

    public static partial class Chunk {
        /// <summary>
        /// A structure wrapping the maximum information a chunk may associate
        /// on the file system. Not all chunks may have every member defined, but
        /// every chunk must have at most only these fields populated. Structure
        /// may be partially filled depending on what information exists/can be found.
        /// </summary>
        public struct ReadbackInfo {
            /// <summary> All the entity information associated with the chunk</summary>
            public EntityInfo entity;
            /// <summary> All the map information associated with a chunk  </summary>
            public MapInfo map;

            /// <summary>A constructor to initialize a default instance of
            /// <see cref="ReadbackInfo"/> which zeroes all fields. </summary>
            /// <param name="_">A dummy paramater</param>
            public ReadbackInfo(bool _) {
                entity = default;
                map = default;
            }

            /// <summary> Creates an instance of <see cref="ReadbackInfo"/> to
            /// wrap the specified information.</summary>
            /// <param name="map">The list of deserialized <see cref="map">MapData</see> to wrap.</param>
            /// <param name="mapMeta">The list of deserialized MapMetaData to wrap. </param>
            /// <param name="entities">The list of deserialized <see cref="entities">entities</see> to wrap.</param>
            public ReadbackInfo(MapData[] map, KeyValuePair<uint, object>[] mapMeta, List<Entity> entities) {
                this.entity.entities = entities;
                this.map.mapMeta = mapMeta;
                this.map.map = map;
            }

            public struct MapInfo {
                /// <summary>
                /// A list of all deserialized <see cref="MapData">MapData</see> associated
                /// with the chunk. See <see cref="MapData"/> for more information.
                /// </summary>
                public MapData[] map;
                /// <summary> An array of all the deserialized <see cref="ChunkHeader.MapEntryMetaData"/> entries associated
                /// with the chunk. See <see cref="ChunkHeader.MapEntryMetaData"/> for more information. </summary>
                public KeyValuePair<uint, object>[] mapMeta;
            }

            public struct EntityInfo {
                /// <summary>
                /// A list of all deserialized <see cref="Entity">Entities</see> associated
                /// with the chunk. See <see cref="Entity"/> for more information.
                /// </summary>
                public List<Entity> entities;
            }
        }
    }
}
