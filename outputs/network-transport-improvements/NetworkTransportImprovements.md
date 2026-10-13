# Network variable and transport improvements

Status: implemented; focused protocol checks passed; two-machine Unity/Relay playtesting pending. This document does not expand the chunk-driver design.

## Problem and evidence

During a host/client join, the host queued a 44-byte challenge at 22:07:57.189 UTC and submitted it at 22:08:09.949 UTC: a 12.76-second delay. After receiving the verification request, verification and player setup took approximately 236 ms. The host queued acceptance, but no acceptance send-progress entry appeared before an older outgoing message expired with zero payload bytes sent.

This points to a delay in outgoing delivery. Excessive queued replication traffic is the leading explanation, but queue depth, send error codes, and transport-update timing have not yet been measured, so the precise bottleneck is not confirmed.

Before these improvements, the implementation:

- Publishes each registered NetworkVariable assignment immediately, including repeated assignments of the same value.
- Sends variables readable by everyone to all connected peers, including peers still authenticating.
- Keeps per-peer outgoing FIFO queues.
- Places one message fragment in each transport packet, with up to 900 payload bytes per fragment.
- Attempts at most eight fragments per peer per transport update.
- Uses full-replacement JSON payloads for NetworkVariable values, including writer-version metadata.

## 1. Replace pending NetworkVariable updates

### Implemented behavior

For each variable and recipient, maintain at most:

1. One update currently being sent.
2. One pending replacement containing the latest update.

If A is being sent and B arrives, retain B as pending. If C arrives before A finishes sending, replace B with C. Finish A, then send C. Preserve the existing immediate local setter behavior; replacement controls outgoing delivery rather than delaying local assignment until a simulation boundary.

Keep the current message intact until all its packets have been submitted. While it is current, newer updates replace the separate pending value; promote that pending value only when the current message finishes. Pending replacements must contain the appropriate full value and writer-version metadata together.

This bounds obsolete pending updates for each variable/recipient. It does not bound total traffic across all variables, recipients, or ordinary events.

### Meaning of “in flight”

The agreed boundary is completion of submission: all fragments of the current message have been successfully submitted to Unity Transport. This is not an acknowledgment that the remote application received or applied the value. Unity Transport continues to own reliable delivery and retransmission.

Waiting for a remote acknowledgment would be a different policy and is not included in this proposal.

### EventStream distinction

Full-replacement state can supersede older state. Ordinary events may represent distinct operations: two damage events or two modifier additions cannot generally replace each other.

Do not change EventStream semantics or add a replacement mode in this work. Preserve every event. All streams can benefit from transport-level packet packing without losing events.

### Greedy versioning and ordering

Replacement applies only to unsolicited authoritative NetworkVariable updates. Preserve client write requests, correlated replies, and rejections.

The server continues to validate every client write against the current writer version. An accepted write from a different writer advances the version and changes the last writer; consecutive accepted writes from the same writer retain that version. Server-local writes bypass stale-version rejection and follow the same writer-switch version rule, treating the server as a writer.

Apply those rules when processing writes, before outgoing replacement. Skipping intermediate deliveries must never skip authoritative validation or writer transitions. A pending update carries the latest accepted value and its corresponding version together. A client that still writes with an outdated version is rejected through the existing machinery; replacement must not relax that check or suppress its rejection reply.

Ordering between variable updates and events is not required. A newer variable value may supersede a pending older value even if an event was queued between them. NetworkVariable delivery prioritizes current state rather than preserving intermediate states.

### Implementation

- Transport tracks the current update by `(peer, ReferenceId)` and stores at most one pending replacement in the existing outgoing-message structure. Per-peer queues are linked lists so a pending node can be moved without scanning the queue.
- Values are serialized at publication, preserving a stable value/version pair even for mutable values.
- Replacing a pending update moves that node to the end of the queue. This keeps newer replacement state behind previously queued read/rejection replies, preventing an older reply from overwriting a newer delivered version.
- The pending update starts in a subsequent packet after the current update has completed submission. Other queued messages can share packets where their order permits.
- Unregistering cancels unsent replaceable updates. A partially sent current update finishes, while its pending replacement is discarded. Disconnect and transport reset clear replacement tracking with the outgoing queues.
- Threaded producers continue using the existing concurrent pending queue; per-peer replacement is applied when the transport drains it. The two-update bound applies to each recipient's outgoing queue, not the transient producer batch.

## 2. Pack multiple messages into one packet

### Implemented behavior

Combine multiple small messages or message fragments into each packet, filling the available application payload capacity. Preserve enough framing to recover individual messages, their reference IDs, event IDs, request/reply correlation, and fragment boundaries.

Continue to support messages larger than one packet, such as serialized players and chunks. Packing must preserve the intended per-message ordering and support the existing ability to interleave messages.

### Previous packet sizes

The installed Unity Transport package defaults to a 1,400-byte maximum message size. Available application payload is smaller because transport and pipeline headers consume space; use `GetMaxSupportedPayloadSize` for the active connection and pipeline rather than hard-coding the full 1,400 bytes as application capacity.

Previous application framing used:

| Content | Application bytes before Unity Transport overhead |
| --- | --- |
| First fragment | Up to 900 payload bytes + 30 header bytes |
| Subsequent fragment | Up to 900 payload bytes + 4 header bytes |
| Observed challenge | 44 payload bytes + 30 header bytes |

Small messages are not padded to 1,400 bytes by our application code. The inefficiency is repeated packet overhead, separate reliable-pipeline slots, and consumption of the current packet-count budget. A health value is not currently a four-byte wire payload: it is serialized as JSON with version metadata. Binary value encoding is not part of the agreed improvements.

### Implementation

Each packet contains consecutive fragment records. Every record has a 4-byte message ID and 2-byte fragment payload length. The first record for a message also carries its 16-byte reference ID, 2-byte event ID, 4-byte reply-to ID, and 4-byte total payload length. First-fragment overhead is therefore 32 bytes; continuation overhead is 6 bytes.

The receiver uses its existing per-peer/message assembly state to identify continuation records and uses the fragment length to find the next record in the packet. Empty events remain valid first records. Truncated headers or payloads are rejected.

The sender packs complete messages and partial larger messages into the capacity returned by Unity Transport. Partially filled packets are submitted during the same update. Queue nodes and byte positions are committed only after successful `EndSend`; a failed submission leaves all included messages unchanged for retry.

This changes wire framing. Both host and clients must run the updated build; no legacy framing is retained.

## 3. Use transport backpressure instead of a fixed per-frame packet cap

### Implemented behavior

Remove `MaxChunksPerPeerPerUpdate` as an arbitrary throughput ceiling. Attempt to submit queued data until the queue is empty or Unity Transport cannot currently accept another packet.

On temporary send-capacity exhaustion, retain unsent data and retry after a subsequent transport update. Do not spin on a full send queue in the same frame. Advance message progress only when submission succeeds.

Unity Transport's bounded send buffers and reliable-delivery window determine how much can be accepted. The application still needs to retain work that has not been accepted; it cannot hand an unlimited queue to the network hardware.

### Implementation

- The installed Unity Transport uses `NetworkSendQueueFull` for both send-buffer exhaustion and reliable-window exhaustion. Either send operation returning this status blocks further attempts for that peer until the next update. Other negative results are logged with their status code and disconnect the affected peer.
- Send one packet per peer per round, repeating while progress is possible. Rotate the first peer each update so shared driver capacity is not always consumed by the same peer first.
- Capture the pending producer queue's count once at the start of the update. Drain that finite batch; newly arriving work is handled on the next update. There is no fixed packet-per-frame limit.
- Preserve the existing 60-second deadlines and timeout/disconnect behavior. A current message keeps its original deadline; replacing a pending value carries the new publication's deadline. No timeout extension is added when sending starts.

## 4. Filter broadcasts by registered references

Clients advertise reference registration and unregistration to the server through ordinary reliable transport messages under the reserved `Guid.Empty` reference. Registrations made before connecting are advertised when transport starts, including on reconnect. The server stores `Dictionary<NetworkConnection, HashSet<Guid>>`: one set per subscribing peer, since peers are far fewer than references. Disconnect removes the peer's entry; transport reset clears the map.

Broadcast fanout checks each peer's reference set. Direct requests and replies bypass this filter, including the correlated recipient of a broadcast reply. Subscription is an interest signal; existing read/write permissions still apply.

The existing listener interface has a default `OnSubscribed` callback. NetworkVariable uses it to send its current full value and writer version if the subscriber has read permission. Duplicate subscriptions do not repeat the callback. If the client subscribes before the server registers the reference, server registration invokes the callback for existing subscribers. Entity variable ownership is assigned before registration so initial delivery can check owner permissions. EventStream does not replay events.

Unsubscribe cancels queued, unsent broadcasts and replaceable variable updates for that reference and peer. Partially transmitted messages finish to preserve framing, and correlated replies remain deliverable. Pending producer messages are filtered when drained. Targeted unsolicited variable updates also require subscription.

## Scope

The improvements are pending-update replacement, packet packing, transport backpressure, and reference-subscription filtering.

Authentication prioritization, new authentication restrictions, binary serialization, equality-based suppression of variable writes, and changes to chunk caching or map-edit replication are not agreed additions to this work.

The implementation reuses the existing request/reply mechanism, reference IDs, and greedy-version validation. No new NetworkVariable state fields or EventStream replacement semantics were introduced.

## Validation

- Under repeated writes, receivers eventually obtain the newest value; pending replacement does not corrupt a partially transmitted value or lose a correlated reply.
- Different recipients can progress independently, and disconnect/disposal clears their pending work.
- Packed messages and fragmented messages round-trip correctly, including empty payloads and packet-boundary cases.
- Temporary send exhaustion neither drops data nor duplicates fragments, and permanent errors are surfaced.
- Multiple peers make progress under load.
- Repeat the join scenario while collecting queue depth, send results, and transport-update timing; compare challenge and acceptance latency before and after the changes.

Compilation against the project's Unity references passed. Eighteen focused checks exercised the production queuing, framing, replacement, and NetworkVariable version logic using a fake transport driver, including randomized payloads across four packet capacities, BeginSend/EndSend backpressure, reader bounds, request cancellation, correlation, disposal, multiple peers, stale-write rejection, and version wraparound. The temporary test folder was removed after testing.

Thirteen additional focused checks exercised subscription advertisement and packed decoding, per-peer filtering, duplicate subscriptions, unsubscribe cancellation, completion of partial messages, direct reply delivery, current variable values and versions, late server registration, read permissions, producer-queue filtering, and disconnect/reset cleanup. Compilation against the project's Unity references passed again. The temporary subscription test folder was removed.

Actual Unity/Relay transport and the two-machine join latency remain to be playtested. These focused checks do not establish that the observed live join stall is resolved.
