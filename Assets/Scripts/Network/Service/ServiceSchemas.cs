using System;
using Newtonsoft.Json;
using Arterra.Configuration;

namespace Arterra.Core.Network.Service {
    public sealed class ServiceEndpoint {
        public string Host;
        public int Port;
    }

    public sealed class LoginResponse {
        public Guid UserId;
        public string SessionToken;
        public DateTimeOffset ExpiresAt;
    }

    public sealed class UploadTarget {
        public string Url;
        public string ContentType;
        public long ContentLength;
    }

    public sealed class CreatedSession {
        public Guid SessionId;
        public UploadTarget PreviewInfo;
        public UploadTarget DetailedInfo;
    }

    public sealed class SessionView {
        public string HostDisplayName;
        public int ConnectionCount;
        public int? MaxConnections;
        public Guid SessionId;
        public string Name;
        public DateTimeOffset ExpiresAt;
        public ServiceEndpoint HostEndpoint;
        public string PreviewInfoUrl;
        public string DetailedInfoUrl;
    }

    public sealed class SessionList {
        public SessionView[] Sessions;
        public string NextCursor;
    }

    public sealed class ConnectionTicket {
        public string Ticket;
        public DateTimeOffset ExpiresAt;
        public ServiceEndpoint DiscoveryEndpoint;
    }

    public sealed class JoinedSession {
        public SessionView Session;
        public ConnectionTicket Connection;
    }

    // Only public world information belongs here, never a host's save path.
    public sealed class PublishedWorld {
        [JsonProperty("id")] public string Id;
        [JsonProperty("config")] public Config Config;
        [JsonProperty("displayChunk")] public byte[] DisplayChunk;
    }

    public sealed class DiscoveryMessage {
        public string Type;
        public string Code;
        public Guid SessionId;
        public Guid IntroductionId;
        public Guid RemoteUserId;
        public bool Connected;
    }
}
