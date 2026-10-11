# Multiplayer Chunk Drivers

Status: initial driver implementation complete; Unity host/client runtime verification remains pending.

This document records the agreed driver model and identifies the details still to discuss. It does not authorize additional protocol machinery or changes to the existing event system.

## Scope

Implement the client/server source pair that connects client terrain generation to the server's shared chunk generation. Reuse `ChunkGenerationDriver`, `GenerationState`, and the existing event-stream machinery.

Chunk caching and delivery of map edits to active subscribers remain a separate, pending discussion. Client-editable configuration is future work.

## Paired sources

Each connected remote player has a pair:

- A `ClientSource` on that player's client machine, used by the client's local chunk driver.
- A `NetworkSource` on the server machine, associated with a server-side chunk driver for that player.

The sources are paired for the lifetime of that player's connection. The server-side driver tracks that player's bounded region of subscriptions to the server's shared `ChunkMapInfo` records. Overlapping server-side drivers share chunk data and generation work through the existing machinery.

There is **no subscription between a client chunk and a server chunk**. The client drives the server-side driver through requests. A server-side subscription refers only to that driver's membership in the server's shared chunk state.

For a listen server, “server-side” means the authoritative side of the host process. It does not imply a second copy of the host's entities. Existing local/singleplayer behavior remains the basis for local authoritative generation.

## Communication

Use a reference ID derived from the player's GUID and the name `ChunkSource`, following the existing named-reference convention. The source pair uses the existing `TransportManager.RequestEventAsync` and `SendEvent` APIs directly, as approved during implementation. `NetEventStream` remains unchanged: it does not expose request/reply functionality.

Requests carry the chunk coordinate; replies use the existing transport message ID and `ReplyTo` correlation. The server accepts requests only from the paired connection and replies only to it. The client accepts notifications only from its server.

### Response content and shared encoding

The loading response content is a named struct with three fields:

- Map data, compressed.
- Entity data.
- Map metadata.

Encode map data using the chunk-storage approach: include the chunk header, extract the materials into a chunk-local lookup registry, remap the entries' material IDs to that registry, and compress the map representation. Map metadata remains a separate response field.

Material extraction/localization and compression must use a shared helper used by both networking and chunk storage. Reuse or extract the existing storage implementation; do not duplicate its logic.

### Connection setup

On the server, create the player's `NetworkSource`, driver, and stream after spawning the authenticated player and before sending the existing `Accepted` response. On the client, create its `ClientSource` and stream after receiving that player and before starting terrain generation. No additional handshake is required.

## Generation flow

```text
Client terrain generation
  -> client driver / GenerationState
  -> ClientSource.ReadAsync
  -> request to paired NetworkSource
  -> server-side driver.Allocate
  -> shared server generation completes
  -> response to ClientSource
  -> ClientSource completes ReadAsync
  -> client GenerationState finishes loading
```

On receipt of a generate request, `NetworkSource` calls `Allocate` on its associated driver and waits for that subscription's readiness before returning the chunk content. Existing server chunk data or an existing shared generation operation can satisfy the allocation.

The server remains responsible for authoritative loading and generation. The proposed separation is that the network request handler allocates through the driver, while the source's server-side `ReadAsync` supplies authoritative stored content to the existing generation pipeline. Receiving a request must not make that pipeline recursively request the same chunk over the network.

The client receives the resulting content through its pending `ReadAsync`. This design does not add client-side authoritative generation.

## Initial entities and enter/leave events

Do not retain or buffer a serialized entity list in the server's `GenerationState`. When generation completes, query and serialize the current entities and answer all requests waiting for that generation. Every request arriving after generation has completed queries and serializes the entities again, so it receives current state rather than a retained generation-time list.

Use `QueryExclusive` for entity capture through the existing `GetChunkEntities` helper. Do not force entity or terrain jobs to complete during requests, saves, authentication, or ordinary gameplay; preserve the existing buffered access model. Forced completion is reserved for world release/teardown. The resulting entity data belongs to the response being prepared, not to persistent generation state.

Keep temporary client buffering because the transport supports packet interleaving: a later `EnterEntity` or `LeaveEntity` notification can finish arriving before the earlier initial batch. With globally ordered event delivery, this buffering would not be needed for that case. Do not change transport ordering as part of this design.

Once the complete initial delivery has arrived, merge by entity GUID: install the batch, then apply buffered enters as add/update and leaves as removal in received order before completing the client task. GUID-based merging prevents duplicate entities when an arrival is also represented in the freshly captured batch.

Do not introduce special handling for hypothetical teleports into unpopulated chunks. Existing collision behavior treats those chunks as blocked.

## Cancellation and replacement

Client cancellation is local:

1. The client cancels its pending `ReadAsync` wait through a cancellation token when that generation is discarded.
2. It discards the pending operation and any delivery state belonging to it.
3. If the response arrives later, it has no effect. Unused managed data can be garbage-collected.

There is no per-chunk cancellation or unsubscribe message to the server.

The client's subsequent chunk request drives a replacement allocation in the paired server-side driver. The existing bounded-region replacement strategy releases the prior server-side subscription in that slot. If another driver still subscribes to the old shared chunk, its data and generation remain available to that driver.

Until replacement or disconnect, the server may retain that driver's old subscription. This is intentional under this model.

`Release` performs subscription/resource cleanup; garbage collection reclaims unused managed objects afterward. Cancellation of the client wait does not itself cancel shared server generation.

The existing transport request waiter handles correlation and cancellation. A reply is consumed only by its original waiter; the client notification listener does not apply chunk responses. Requesting the same coordinate again uses the server driver's ordinary allocation logic: reuse available shared content, or load/generate it again if it was released.

The replacement model also needs the client and server driver regions to use compatible allocation/replacement rules. Verify that wiring during implementation rather than adding cleanup communication speculatively.

## Disconnect cleanup

Collect player-disconnect handling into one orchestrator function. That function coordinates the existing player cleanup and cleanup of the paired source/driver:

- Cancel/discard pending operations belonging to the disconnected player.
- Release that player's server-side driver subscriptions.
- Dispose the source and its event-stream registrations.
- Remove the disconnected player's entity through the agreed existing player lifecycle.

Release only that player's subscriptions. Other subscribers retain their shared chunks and generation work. Pending work for a disconnected source must not deliver into its disposed state.

Use the existing `PlayerConnection.Disconnected` entry point as the server-side cleanup orchestrator. Client world teardown disposes the client source and cancels pending waits. Do not create a second independent disconnect path. Singleplayer must not acquire remote-request work or unnecessary network overhead.

## Visual chunks

Leave visual chunks unchanged: preserve both their normal and fake paths, including local saved-map loading through `ReadVisualAsync(coordinate, depth)`. The attempted forced fake-chunk change has been reverted. Remote visual-map loading remains deferred; `ClientSource.ReadVisualAsync` returns no saved map, allowing the existing default-map fallback. Any separate visual-chunk driver or special handling is deferred to a later design.

## Deferred work

Chunk caching and delivery of map changes to active subscribers will be addressed in the user's follow-up design doc. Visual-chunk drivers remain deferred as described above. Neither is implemented by this change.

## Verification when implemented

Exercise ordinary loading, shared server chunks, replacement while loading, release followed by a new request for the same coordinate, and disconnect during generation. Verify that discarded responses cannot recreate client chunks/entities, replacement releases the correct server subscription, and one player's cleanup does not cancel another player's shared generation.

Verify initial entity delivery with buffered enter/leave notifications and run the existing entity lifecycle end to end on host and client. Confirm that singleplayer continues through its local source without remote-request overhead.

## Implementation verification

The production C# assembly compiles. A temporary harness passed 41 checks against the driver/source and shared codec implementations, with Unity and transport boundaries stubbed. Checks covered shared allocation, replacement, cancellation, discarded replies, entity GUID merging, fresh entity capture for cached chunks, and map encoding/decoding. The temporary harness was removed after verification. These checks do not replace end-to-end Unity host/client testing.
