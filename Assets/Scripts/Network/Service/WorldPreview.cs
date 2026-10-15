using System;
using System.IO;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.Engine.Terrain;

namespace Arterra.Core.Network.Service {
    internal static class WorldPreview {
        // A presence byte permits new worlds with no saved chunk. Present maps use the shared storage codec.
        public static byte[] Encode(uint[] packed) {
            using var stream = new MemoryStream();
            stream.WriteByte(packed == null ? (byte)0 : (byte)1);
            if (packed != null)
                ChunkMapCodec.Write(stream, Array.ConvertAll(packed, value => new MapData { data = value }));
            return stream.ToArray();
        }

        public static MapData[] Decode(byte[] bytes) {
            using var stream = new MemoryStream(bytes);
            int present = stream.ReadByte();
            if (present == 0 && stream.Length == 1) return null;
            if (present != 1) throw new InvalidDataException("Invalid display chunk.");
            return ChunkMapCodec.Read(stream, Config.CURRENT.Quality.Terrain.value.mapChunkSize, 0, out _);
        }
    }
}
