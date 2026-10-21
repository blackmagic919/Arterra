using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Arterra.Configuration;
using Arterra.Engine.Terrain;
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Arterra.Core.Storage {
    /// <summary>Shared storage/network map format: material registry header and compressed map resolutions.</summary>
    public static class ChunkMapCodec {
        public static void Write(Stream destination, CPUMapManager.ChunkMapInfo chunk,
            bool includeResolutions, bool includeMetadata) {
            // Localization must not rewrite the authoritative map when sending it to a subscriber.
            var map = new MapData[chunk.Length];
            NativeArray<MapData>.Copy(chunk.ChunkData, chunk.Offset, map, 0, map.Length);

            WriteMap(destination, map, includeResolutions,
                includeMetadata ? chunk.MapMetaData.ToArray() : null);
        }

        public static void Write(Stream destination, MapData[] map) =>
            WriteMap(destination, (MapData[])map.Clone(), false, null);

        private static void WriteMap(Stream destination, MapData[] map, bool includeResolutions,
            KeyValuePair<uint, object>[] metadata) {
            var materials = new Dictionary<int, int>();
            var names = new List<string>();

            var registry = Config.CURRENT.Generation.Materials.value.MaterialDictionary;

            for (int i = 0; i < map.Length; i++) {
                int material = map[i].material;
                if (!materials.TryGetValue(material, out int local)) {
                    local = names.Count;
                    materials.Add(material, local);
                    names.Add(registry.RetrieveName(material));
                }

                map[i]._material = local;
            }

            var header = new Chunk.ChunkHeader {
                RegisterNames = names,
                ResolutionOffsets = new List<int>(),
                MapEntryMetaData = metadata,
            };

            int size = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
            using var maps = new MemoryStream();

            for (int skip = 1; skip <= (includeResolutions ? size : 1); skip <<= 1) {
                int axis = size / skip;
                int byteCount = axis * axis * axis * 4;
                byte[] buffer = ArrayPool<byte>.Shared.Rent(byteCount);

                try {
                    int offset = 0;
                    for (int z = 0; z < size; z += skip)
                        for (int y = 0; y < size; y += skip)
                            for (int x = 0; x < size; x += skip) {
                                UnsafeUtility.As<byte, uint>(ref buffer[offset]) = map[
                                    z * size * size + y * size + x].data;
                                offset += 4;
                            }

                    using (var zip = new GZipStream(maps, CompressionMode.Compress, true))
                        zip.Write(buffer, 0, byteCount);
                } finally { ArrayPool<byte>.Shared.Return(buffer); }

                header.ResolutionOffsets.Add((int)maps.Length);
            }

            using var encodedHeader = WriteHeader(header);
            encodedHeader.Position = 0;
            encodedHeader.CopyTo(destination);

            maps.Position = 0;
            maps.CopyTo(destination);
        }

        public static MapData[] Read(Stream source, int size, int depth, out Chunk.ChunkHeader header) {
            long start = source.Position;
            uint mapStart = ReadHeader(source, out header);
            if (depth != 0) mapStart += (uint)header.ResolutionOffsets[depth - 1];
            source.Position = start + mapStart;

            int axis = size >> depth;
            var map = new MapData[axis * axis * axis];
            byte[] buffer = new byte[map.Length * 4];

            using (var zip = new GZipStream(source, CompressionMode.Decompress, true))
                ReadFully(zip, buffer);

            var registry = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
            int[] materials = new int[header.RegisterNames.Count];
            for (int i = 0; i < header.RegisterNames.Count; i++)
                materials[i] = registry.RetrieveIndex(header.RegisterNames[i]);

            for (int i = 0; i < map.Length; i++) {
                map[i].data = UnsafeUtility.As<byte, uint>(ref buffer[i * 4]);
                map[i]._material = materials[map[i].material];
            }

            return map;
        }

        // A presence byte permits new worlds with no saved chunk. Present maps use the shared storage codec.
        public static byte[] EncodePreview(uint[] packed) {
            using var stream = new MemoryStream();
            stream.WriteByte(packed == null ? (byte)0 : (byte)1);
            if (packed != null)
                ChunkMapCodec.Write(stream, Array.ConvertAll(packed, value => new MapData { data = value }));
            return stream.ToArray();
        }

        public static MapData[] DecodePreview(byte[] bytes) {
            using var stream = new MemoryStream(bytes);
            int present = stream.ReadByte();
            if (present == 0 && stream.Length == 1) return null;
            if (present != 1) throw new InvalidDataException("Invalid display chunk.");
            return ChunkMapCodec.Read(stream, Config.CURRENT.Quality.Terrain.value.mapChunkSize, 0, out _);
        }

        public static MemoryStream WriteHeader(object header) {
            var stream = new MemoryStream();
            if (header == null) return stream;

            stream.Position = 4;
            string json = JsonConvert.SerializeObject(header, Formatting.Indented);
            byte[] bytes = Encoding.ASCII.GetBytes(json);
            using (var zip = new GZipStream(stream, CompressionMode.Compress, true))
                zip.Write(bytes, 0, bytes.Length);

            byte[] size = BitConverter.GetBytes((uint)stream.Length);
            stream.Position = 0;
            stream.Write(size, 0, size.Length);

            return stream;
        }

        public static uint ReadHeader<T>(Stream source, out T header) {
            byte[] sizeBytes = new byte[4];
            ReadFully(source, sizeBytes);
            uint size = BitConverter.ToUInt32(sizeBytes, 0);

            byte[] buffer = new byte[checked((int)size - 4)];
            ReadFully(source, buffer);

            using var stream = new MemoryStream(buffer);
            using var zip = new GZipStream(stream, CompressionMode.Decompress);
            using var reader = new StreamReader(zip);
            header = JsonConvert.DeserializeObject<T>(reader.ReadToEnd());

            return size;
        }

        private static void ReadFully(Stream source, byte[] buffer) {
            int offset = 0;
            while (offset < buffer.Length) {
                int read = source.Read(buffer, offset, buffer.Length - offset);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
        }
    }
}
