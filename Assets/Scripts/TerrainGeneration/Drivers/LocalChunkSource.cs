using System;
using System.Threading.Tasks;
using System.Threading;
using Arterra.Core.Network;
using Arterra.Data.Entity;
using Arterra.Engine.Terrain;
using Unity.Mathematics;
using ChunkMapInfo = Arterra.Core.Storage.CPUMapManager.ChunkMapInfo;

namespace Arterra.Core.Storage {
    public class LocalChunkSource : ChunkSource {
        private static void RequireLocalAuthority() {
            if (!NetworkManager.IsActingServer)
                throw new NotSupportedException("Use ClientSource when connected to a remote server.");
        }

        public override Task<Chunk.ReadbackInfo> ReadAsync(int3 CCoord, CancellationToken token = default) {
            RequireLocalAuthority();

            return Task.Run(() => Chunk.ReadChunkInfo(CCoord), token);
        }

        public override Task<MapData[]> ReadVisualAsync(int3 coordinate, int depth) {
            RequireLocalAuthority();

            return Task.Run(() => Chunk.ReadVisualChunkMap(coordinate, depth));
        }

        public override async Task ReleaseAsync(ChunkMapInfo chunk) {
            RequireLocalAuthority();

            bool saveMap = chunk.HasMap && chunk.Generation.OffloadFlags == ChunkMapInfo.Flags.Dirty;
            Task entities = EntityManager.ReleaseChunkEntitiesAsync(chunk.CCoord, chunk.HasEntities);

            Task map;
            if (saveMap) map = Chunk.SaveChunkToBinAsync(chunk, chunk.CCoord);
            else map = Task.CompletedTask;

            await Task.WhenAll(map, entities);
        }

        //Default values for local chunk
        public override void EnterEntity(Entity entity) => entity.InitializeVisual();

        public override void ReflectChunk(Func<byte[]> GetEncodedChanges = null) {
            // Local map updates already share the authoritative map.
            GetEncodedChanges?.Invoke();
        }

        public override void LeaveEntity(Guid entityId) {
            if (EntityManager.TryGetEntity(entityId, out var entity)) entity.DisposeVisual();
        }
    }
}
