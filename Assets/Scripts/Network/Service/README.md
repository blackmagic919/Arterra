# Self-hosted multiplayer

`NetworkSettings.ServiceUrl` defaults to `https://alpha.api.playarterra.com`.
Use the same environment on both players. Beta and production are
`https://beta.api.playarterra.com` and `https://api.playarterra.com`.
Unity Authentication remains the identity provider; Unity Multiplayer/Relay/Transport are no longer used.

## Flow

1. The host enables multiplayer in a local world. The client exchanges its Unity JWT at `/login`.
2. `/sessions` returns signed upload targets. The host uploads PreviewInfo (PNG) and DetailedInfo (world metadata, config, and display chunk JSON), then calls `/complete`.
3. `/host` returns a single-use discovery ticket. The game connects to the discovery endpoint using LiteNetLib protocol version 1 and authenticates with that ticket and its local endpoint. `hostReady` marks the session discoverable.
4. The remote-world list uses `/sessions`. List entries download PreviewInfo images. Selecting a world downloads DetailedInfo once for both config and the options chunk preview. Neither action opens a game connection.
5. Play calls `/join`, authenticates the returned ticket over UDP, and waits for the introduction. Both NAT traversal and gameplay use the same UDP socket. The joining client initiates the direct connection using the introduction ID. The host binds the peer to the service-provided user ID.
6. The existing transport request/reply machinery fetches live config and requests the owning player. Player creation stays deferred through EntityManager. Chunk requests and entity replication then use the existing drivers.
7. The joining discovery connection closes after introduction; the host's stays connected to maintain its lease. Stopping hosting deletes the session and stops the socket. Hosting recovery recreates a session after failure, as before.

Published metadata is immutable for the session lifetime. Joining fetches current config from the host.
The service stores two opaque objects: PreviewInfo (up to 4 MiB) and DetailedInfo (up to 32 MiB),
returning signed download URLs for each. Empty objects are allowed.
Arterra uses raw PNG bytes for PreviewInfo, with an empty or undecodable image falling back to the default icon.
DetailedInfo is `PublishedWorld` JSON: `id`, `creationTime`, `lastAccessTime`, `config`, and `displayChunk`.
`displayChunk` is Base64-encoded shared `ChunkMapCodec` data (localized materials and gzip map data),
or an empty byte array for a world with no saved chunk. No presence prefixes or local filesystem paths are included.

Remote saves use the service's user GUID, qualified by service URL, as their account key.
The local player's existing local/Unity save identity is unchanged.
No privacy, password, player-cap, or relay options are implemented.

## Playtest

Use two processes/accounts. Enable multiplayer in a local world on the host, then select
that session under DirectWAN on the other process. Verify the preview before clicking Play.
Test player/chunk streaming, disconnect/rejoin, and disabling multiplayer. Use two different
networks to verify real NAT traversal; local tests cannot establish router compatibility.
Transport logs summarize outgoing work once per second (total and peak CPU time, message count, and submitted bytes). Warnings and errors remain enabled. If a direct connection cannot
be established, joining reports a timeout; there is no relay fallback.

Game transport sends one complete reliable-ordered LiteNetLib message per application event.
Its header is message ID (uint), reference ID (Guid), event ID (ushort), and reply-to ID (uint),
followed by the payload. LiteNetLib handles fragmentation, reassembly, and small-packet merging.
The deferred send batch keeps the first variable update and its latest pending replacement;
submission releases it immediately, without waiting for delivery acknowledgment.
Request timeouts and cancellation remain application-level. Partial-message storage and cleanup
belong to LiteNetLib and the connection lifecycle; there is no separate application reassembly timer.
Authenticated user IDs are stored in NetPeer.Tag and cleared when the connection is removed.
Both peers must use this message format; the previous custom-fragment format is incompatible.
