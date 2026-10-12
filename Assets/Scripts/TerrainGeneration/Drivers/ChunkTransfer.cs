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
            using var map = new MemoryStream();
            ChunkMapCodec.Write(map, chunk, includeResolutions: false, includeMetadata: false);

            return new ChunkTransfer {
                MapData = map.ToArray(),
                EntityData = EntityManager.GetChunkEntities(chunk.CCoord)
                    .Select(entity => new Registerable<Entity>(entity)).ToList(),
                MapMetadata = chunk.MapMetaData.ToArray(),
            };
        }

        public Chunk.ReadbackInfo Read() {
            using var map = new MemoryStream(MapData);

            return new Chunk.ReadbackInfo(
                ChunkMapCodec.Read(map, Config.CURRENT.Quality.Terrain.value.mapChunkSize, 0, out _),
                MapMetadata,
                EntityData.Select(entity => entity.Value).ToList());
        }
    }
}
