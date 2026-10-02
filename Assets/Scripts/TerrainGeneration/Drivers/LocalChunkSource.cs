using System;
using System.Collections.Generic;
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

        public override async Task<MapData[]> ReadVisualAsync(VisualChunkGenerationDriver.Subscription subscription) {
            RequireLocalAuthority();
            var request = subscription.Request;
            var map = await Task.Run(() => Chunk.ReadVisualChunkMap(request.CCoord, request.Depth), subscription.Token);
            subscription.Token.ThrowIfCancellationRequested();
            CPUMapManager.CopyLive(request.CCoord, request.Depth, map);
            subscription.Changes.Clear();
            return map;
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

        public override void ReflectChunk(int3 CCoord, IReadOnlyDictionary<uint, MapData> changes,
            Func<byte[]> getEncodedChanges = null, Action abandoned = null) {
            ChunkGenerationDriver.Local?.ReflectChunk(CCoord, changes);
            VisualChunkGenerationDriver.Local?.ReflectChunk(CCoord, changes);
        }

        public override void LeaveEntity(Guid entityId) {
            if (EntityManager.TryGetEntity(entityId, out var entity)) entity.DisposeVisual();
        }
    }
}
