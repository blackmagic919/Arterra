using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Arterra.Core.Network.Authentication;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Unity.Services.Authentication;

namespace Arterra.Core.Network.Service {
    /// <summary>HTTP management only. Connection tickets are consumed by the UDP discovery protocol.</summary>
    public sealed class GameServiceClient {
        private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
        internal static readonly JsonSerializerSettings Json = new() {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            TypeNameHandling = TypeNameHandling.None
        };

        public string BaseUrl { get; }
        private LoginResponse login;
        internal Guid? UserId => login?.UserId;
        private Task<LoginResponse> loggingIn;

        public GameServiceClient(string baseUrl) {
            var uri = new Uri(baseUrl, UriKind.Absolute);
            if (uri.Scheme != "https") throw new ArgumentException("Game service URL must use HTTPS.");
            BaseUrl = baseUrl.TrimEnd('/');
        }

        public Task<SessionList> ListAsync(string cursor, CancellationToken token) =>
            RequestAsync<SessionList>(HttpMethod.Get, "/sessions?limit=100" +
                (cursor != null ? "&cursor=" + Uri.EscapeDataString(cursor) : ""), null, false, token);

        public Task<SessionView> GetAsync(string id, CancellationToken token) =>
            RequestAsync<SessionView>(HttpMethod.Get, $"/sessions/{Guid.Parse(id)}", null, false, token);

        public Task<CreatedSession> CreateAsync(string name, byte[] metadata, byte[] chunk, int maxPlayers, CancellationToken token) =>
            RequestAsync<CreatedSession>(HttpMethod.Post, "/sessions", new {
                name, metadataBytes = metadata.LongLength,
                displayChunkBytes = chunk.LongLength,
                maxConnections = maxPlayers,
            }, true, token);

        public Task CompleteAsync(Guid id, CancellationToken token) =>
            RequestAsync<object>(HttpMethod.Post, $"/sessions/{id}/complete", null, true, token);

        public Task<ConnectionTicket> HostAsync(Guid id, CancellationToken token) =>
            RequestAsync<ConnectionTicket>(HttpMethod.Post, $"/sessions/{id}/host", null, true, token);

        public Task<JoinedSession> JoinAsync(string id, CancellationToken token) =>
            RequestAsync<JoinedSession>(HttpMethod.Post, $"/sessions/{Guid.Parse(id)}/join", null, true, token);

        public Task DeleteAsync(Guid id, CancellationToken token = default) =>
            RequestAsync<object>(HttpMethod.Delete, $"/sessions/{id}", null, true, token);

        private async Task<LoginResponse> LoginAsync() {
            if (login != null && login.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30)) return login;
            if (loggingIn != null && !loggingIn.IsCompleted) return await loggingIn;
            loggingIn = ExchangeAsync();
            return await loggingIn;
        }

        private async Task<LoginResponse> ExchangeAsync() {
            await ClientAuthentication.EnsureSignedInAsync();
            var result = await RequestAsync<LoginResponse>(HttpMethod.Post, "/login", new {
                credential = new { unity = new { authenticationToken = AuthenticationService.Instance.AccessToken } }
            }, false, CancellationToken.None);
            if (result == null || result.UserId == Guid.Empty || string.IsNullOrEmpty(result.SessionToken))
                throw new InvalidOperationException("Game service returned an invalid login.");

            login = result;
            return result;
        }

        private async Task<T> RequestAsync<T>(HttpMethod method, string path, object body,
            bool authenticated, CancellationToken token) {
            token.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(method, BaseUrl + path);
            if (authenticated) {
                var identity = await LoginAsync();
                token.ThrowIfCancellationRequested();
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.SessionToken);
            }
            if (body != null)
                request.Content = new StringContent(JsonConvert.SerializeObject(body, Json), Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request, token);
            await EnsureSuccessAsync(response);
            string json = await response.Content.ReadAsStringAsync();
            token.ThrowIfCancellationRequested();
            return string.IsNullOrWhiteSpace(json) ? default : JsonConvert.DeserializeObject<T>(json, Json);
        }

        public static async Task UploadAsync(UploadTarget target, byte[] bytes, CancellationToken token) {
            if (bytes.LongLength != target.ContentLength) throw new ArgumentException("Upload length does not match its signed target.");
            using var request = new HttpRequestMessage(HttpMethod.Put, target.Url);
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(target.ContentType);
            request.Content.Headers.ContentLength = bytes.LongLength;
            using var response = await http.SendAsync(request, token);
            await EnsureSuccessAsync(response);
        }

        public static async Task<byte[]> DownloadAsync(string url, CancellationToken token) {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await http.SendAsync(request, token);
            await EnsureSuccessAsync(response);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            token.ThrowIfCancellationRequested();
            return bytes;
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response) {
            if (response.IsSuccessStatusCode) return;
            // Do not include signed URLs, login tokens, or tickets in errors.
            string detail = await response.Content.ReadAsStringAsync();
            if (response.Content.Headers.ContentType?.MediaType != "application/json") detail = response.ReasonPhrase;
            throw new HttpRequestException($"Game service returned {(int)response.StatusCode}: {detail}");
        }
    }
}
