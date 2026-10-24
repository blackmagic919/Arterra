using System;
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
        public UploadTarget Metadata;
        public UploadTarget DisplayChunk;
    }

    public sealed class SessionView {
        public int ConnectionCount;
        public int? MaxConnections;
        public Guid SessionId;
        public string Name;
        public DateTimeOffset ExpiresAt;
        public ServiceEndpoint HostEndpoint;
        public ServiceEndpoint ConnectivityEndpoint;
        public string MetadataUrl;
        public string DisplayChunkUrl;
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
        public string Id;
        public DateTime CreationTime;
        public DateTime LastAccessTime;
        public Config Config;
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
