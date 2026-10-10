using System;
using Newtonsoft.Json;

namespace Arterra.Core.Network.Authentication {
    /// <summary>Provider-qualified persistent identity. Never use a transport connection as a save key.</summary>
    public readonly struct AccountId : IEquatable<AccountId> {
        public readonly string Provider;
        public readonly string Id;
        [JsonConstructor]
        public AccountId(string provider, string id) {
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("An account requires a provider and ID.");
            Provider = provider;
            Id = id;
        }
        [JsonIgnore] public string SaveKey => Provider + ":" + Uri.EscapeDataString(Id);
        public bool Equals(AccountId other) => Provider == other.Provider && Id == other.Id;
        public override bool Equals(object obj) => obj is AccountId other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Provider, Id);
    }
}
