using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.Utils;
using Newtonsoft.Json;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Arterra.Core.Network {
    /// <summary>Session advertisement and discovery. Invoke on Unity's main thread.</summary>
    public static class NetworkManager {
        private const string WorldMetaProperty = "configJson";
        private const string MultiplayerHook = "EnableMultiplayer";
        private const int PropertyByteLimit = 2048;
        private const int QueryPageSize = 100;
        private const double QueryIntervalSeconds = 1.1;
        private const int MetadataMaxDepth = 8;
        private const int RecoveryInitialDelayMilliseconds = 2000;
        private const int RecoveryMaxDelayMilliseconds = 30000;

        private static readonly SemaphoreSlim lifecycle = new(1, 1);
        private static readonly SemaphoreSlim queries = new(1, 1);
        private static Task servicesInitialization;
        private static bool joiningClient;
        private static CancellationTokenSource hosting;
        private static string hostedWorldId;
        private static bool recovering;

        private static DateTime nextQueryUtc;
        private static NetworkSettings settings => Config.CURRENT.System.Network.value;
        private static bool MultiplayerEnabled => Config.CURRENT.GamePlay.Gamemodes.value.EnableMultiplayer;

        public static ISession Session { get; private set; }
        /// <summary>Offline games and hosts own their state; joining clients do not.</summary>
        public static bool IsActingServer => !joiningClient && (TransportManager.Instance.Configuration == null || TransportManager.Instance.IsServer);

        public static void Initialize() {
            if (RuntimeUpdateTask.Active) return;

            Config.CURRENT.System.AddHook(MultiplayerHook, ToggleMultiplayer);
            RuntimeUpdateTask.Initialize();
            if (MultiplayerEnabled) Observe(EnableMultiplayer());
        }

        public static async Task ReleaseAsync() {
            if (RuntimeUpdateTask.Active)
                Config.CURRENT.System.RemoveHook(MultiplayerHook, ToggleMultiplayer);

            RuntimeUpdateTask.Release();
            await DisableMultiplayer();
        }

        private static void ToggleMultiplayer(ref object rule) {
            if (rule is bool enabled)
                Observe(enabled ? EnableMultiplayer() : DisableMultiplayer());
        }

        // Hooks cannot return a Task; explicitly observe their failures.
        private static async void Observe(Task operation) {
            try { await operation; }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        private static Task EnsureServicesAsync() {
            if (servicesInitialization == null || servicesInitialization.IsFaulted
                || servicesInitialization.IsCanceled || (servicesInitialization.IsCompleted &&
                    (UnityServices.State != ServicesInitializationState.Initialized || !AuthenticationService.Instance.IsSignedIn)))
                servicesInitialization = InitializeServicesAsync();

            return servicesInitialization;

            static async Task InitializeServicesAsync() {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                    await UnityServices.InitializeAsync();

                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        /// <summary>Advertise the selected world's metadata once; repeated calls are safe.</summary>
        public static async Task EnableMultiplayer() {
            await lifecycle.WaitAsync();
            try {
                if (Session?.IsHost == true && TransportManager.Instance.IsRunning) return;
                if (Session != null && !Session.IsHost)
                    throw new InvalidOperationException("Leave the remote world before hosting.");

                World.WorldMeta meta = World.WORLD_SELECTION?.First?.Value;
                if (meta == null || meta.Type != World.GalaxyType.Local)
                    throw new InvalidOperationException("Select a local world before hosting.");

                if (settings.MaxPlayers < 1) throw new InvalidOperationException("MaxPlayers must include at least the host.");
                if (hosting != null) { ScheduleRecovery(); return; }

                hosting = new CancellationTokenSource();
                hostedWorldId = meta.Id;

                TransportManager.Instance.Stopped += OnTransportStopped;
                Application.quitting += StopHostingRecovery;
                Application.runInBackground = true; //Run in background

                try { await CreateHostSessionAsync(meta); }
                catch { ScheduleRecovery(); throw; }
            } finally { lifecycle.Release(); }
        }

        // Caller holds the lifecycle semaphore.
        private static async Task CreateHostSessionAsync(World.WorldMeta meta) {
            string configJson = SerializeMeta(meta);
            var options = new SessionOptions {
                Name = meta.Name,
                MaxPlayers = settings.MaxPlayers,
                IsPrivate = settings.IsPrivate,
                Password = string.IsNullOrEmpty(settings.Password) ? null : settings.Password,
                SessionProperties = new Dictionary<string, SessionProperty> {
                    [WorldMetaProperty] = new(configJson, VisibilityPropertyOptions.Public)
                }
            };
            options.WithRelayNetwork().WithNetworkHandler(TransportManager.Instance);
            await EnsureServicesAsync();

            try {
                Debug.Log("Launching Session");
                Session = await MultiplayerService.Instance.CreateSessionAsync(options);

                Session.StateChanged += OnSessionStateChanged;
                Session.Deleted += OnSessionLost;
                Session.RemovedFromSession += OnSessionLost;
            } catch {
                await TransportManager.Instance.StopAsync();
                throw;
            }
        }

        private static void OnSessionLost() => Observe(TransportManager.Instance.StopAsync());
        private static void OnTransportStopped(Exception reason) => ScheduleRecovery();

        private static void OnSessionStateChanged(SessionState state) {
            if (state == SessionState.Disconnected || state == SessionState.Deleted) ScheduleRecovery();
        }

        private static bool ShouldRecover(CancellationTokenSource lifetime) {
            return hosting == lifetime && !lifetime.IsCancellationRequested && Application.isPlaying
                && Config.CURRENT != null && MultiplayerEnabled
                && World.WORLD_SELECTION?.First?.Value is World.WorldMeta meta
                && meta.Type == World.GalaxyType.Local && meta.Id == hostedWorldId;
        }

        private static void ScheduleRecovery() {
            if (hosting == null || recovering || !ShouldRecover(hosting)) return;

            recovering = true;
            RecoverHostingAsync(hosting);
        }

        private static async void RecoverHostingAsync(CancellationTokenSource lifetime) {
            var token = lifetime.Token;
            int delay = RecoveryInitialDelayMilliseconds;

            try {
                while (ShouldRecover(lifetime)) {
                    await Task.Delay(delay, token);
                    await lifecycle.WaitAsync(token);

                    try {
                        if (!ShouldRecover(lifetime)) return;
                        if (Session?.State == SessionState.Connected && TransportManager.Instance.IsRunning) return;

                        try { await LeaveSessionAsync(); }
                        catch (Exception exception) { Debug.LogException(exception); }

                        if (!ShouldRecover(lifetime)) return;
                        await CreateHostSessionAsync(World.WORLD_SELECTION.First.Value);
                        if (!ShouldRecover(lifetime)) return;
                        if (Session?.State == SessionState.Connected && TransportManager.Instance.IsRunning) return;
                    } catch (Exception exception) {
                        Debug.LogException(exception);
                    } finally { lifecycle.Release(); }

                    delay = Math.Min(delay * 2, RecoveryMaxDelayMilliseconds);
                }
            } catch (OperationCanceledException) when (token.IsCancellationRequested) {
            } catch (Exception exception) {
                Debug.LogException(exception);
            } finally {
                recovering = false;
                if (hosting != null && hosting != lifetime) ScheduleRecovery();
            }
        }

        private static void StopHostingRecovery() {
            var lifetime = hosting;
            if (lifetime == null) return;

            hosting = null;
            lifetime.Cancel();
            lifetime.Dispose();

            TransportManager.Instance.Stopped -= OnTransportStopped;
            Application.quitting -= StopHostingRecovery;
            Application.runInBackground = false;
        }

        public static async Task DisableMultiplayer() {
            StopHostingRecovery();
            await lifecycle.WaitAsync();
            try {
                StopHostingRecovery();
                await LeaveSessionAsync();
            } finally { lifecycle.Release(); }
        }

        /// <summary>
        /// Connect to a session, leaving any previous client session. Replicated state is fetched separately.
        /// </summary>
        public static async Task ConnectSessionAsync(string sessionId,
            CancellationToken token = default, string password = null) {
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("Missing session ID.", nameof(sessionId));
            await lifecycle.WaitAsync(token);

            try {
                if (hosting != null || Session?.IsHost == true) throw new InvalidOperationException("Stop hosting before joining another world.");

                await EnsureServicesAsync();
                token.ThrowIfCancellationRequested();

                if (Session != null && //Leave any current session we're a part of
                    (Session.Id != sessionId || !TransportManager.Instance.IsRunning))
                    await LeaveSessionAsync();

                try {
                    if (Session == null) await JoinSessionAsync(sessionId, password);
                    token.ThrowIfCancellationRequested();
                } catch {
                    try { await LeaveSessionAsync(); }
                    catch (Exception cleanupError) { Debug.LogException(cleanupError); }
                    throw;
                }
            } finally { lifecycle.Release(); }
        }

        // Called while holding the lifecycle semaphore, after services are initialized.
        private static async Task JoinSessionAsync(string sessionId, string password) {
            var options = new JoinSessionOptions { Password = password }
                .WithNetworkHandler(TransportManager.Instance);

            // The host's session metadata supplies Relay configuration.
            joiningClient = true;
            try {
                Session = await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId, options);
            } finally { joiningClient = false; }
        }

        public static async Task DisconnectClientAsync() {
            await lifecycle.WaitAsync();
            try {
                if (hosting == null && Session?.IsHost != true) await LeaveSessionAsync();
            } finally { lifecycle.Release(); }
        }

        // Called while holding the lifecycle semaphore.
        private static async Task LeaveSessionAsync() {
            if (Session != null) {
                Session.StateChanged -= OnSessionStateChanged;
                Session.Deleted -= OnSessionLost;
                Session.RemovedFromSession -= OnSessionLost;
            }

            try {
                if (Session?.IsHost == true) await Session.AsHost().DeleteAsync();
                else if (Session != null) await Session.LeaveAsync();
            } finally {
                Session = null;
                await TransportManager.Instance.StopAsync();
            }
        }

        /// <summary>
        /// Returns discoverable public worlds marked Remote, each with its SessionId populated.
        /// Follows query pagination; private/inactive sessions are not discoverable.
        /// Results are a changing directory, not an atomic snapshot. Remote metadata
        /// is for display/joining only: never pass it to World.SelectWorld as a local save.
        /// </summary>
        public static async Task<List<World.WorldMeta>> QueryWorldMetasAsync(
            CancellationToken cancellationToken = default) {
            await queries.WaitAsync(cancellationToken);
            try {
                await EnsureServicesAsync();

                var worlds = new Dictionary<string, World.WorldMeta>();
                var tokens = new HashSet<string>();
                var options = new QuerySessionsOptions { Count = QueryPageSize };

                while (true) { //query loop
                    cancellationToken.ThrowIfCancellationRequested();
                    TimeSpan delay = nextQueryUtc - DateTime.UtcNow;
                    if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
                    nextQueryUtc = DateTime.UtcNow.AddSeconds(QueryIntervalSeconds);

                    var page = await MultiplayerService.Instance.QuerySessionsAsync(options);
                    cancellationToken.ThrowIfCancellationRequested();

                    foreach (var session in page.Sessions) {
                        if (!session.Properties.TryGetValue(WorldMetaProperty, out var property)) continue;
                        World.WorldMeta meta = DeserializeMeta(property.Value, session.Id);
                        if (meta != null) worlds[session.Id] = meta;
                    }

                    if (page.Sessions.Count == 0 || string.IsNullOrEmpty(page.ContinuationToken)) break;
                    if (!tokens.Add(page.ContinuationToken))
                        throw new InvalidOperationException("Session query returned a repeated continuation token.");
                    options.ContinuationToken = page.ContinuationToken;
                }

                return new List<World.WorldMeta>(worlds.Values);
            } finally { queries.Release(); }
        }

        private static string SerializeMeta(World.WorldMeta meta) {
            if (string.IsNullOrWhiteSpace(meta.Id) || string.IsNullOrWhiteSpace(meta.Name))
                throw new ArgumentException("World metadata needs an Id and Name.");

            // Explicit projection: do not advertise the host's local filesystem path.
            string json = JsonConvert.SerializeObject(new PublishedWorldMeta {
                Id = meta.Id, Name = meta.Name,
                LastAccessTime = meta.LastAccessTime, CreationTime = meta.CreationTime
            });

            if (Encoding.UTF8.GetByteCount(json) > PropertyByteLimit)
                throw new ArgumentException("WorldMeta exceeds the 2 KB session-property limit.");

            return json;
        }

        private static World.WorldMeta DeserializeMeta(string json, string sessionId) {
            if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > PropertyByteLimit) return null;

            try {
                var meta = JsonConvert.DeserializeObject<PublishedWorldMeta>(json, new JsonSerializerSettings {
                    TypeNameHandling = TypeNameHandling.None, MaxDepth = MetadataMaxDepth
                });
                if (meta == null || string.IsNullOrWhiteSpace(meta.Id) || string.IsNullOrWhiteSpace(meta.Name)) return null;

                return new World.WorldMeta(meta.Id) {
                    Type = World.GalaxyType.DirectWAN,
                    SessionId = sessionId, Name = meta.Name,
                    LastAccessTime = meta.LastAccessTime, CreationTime = meta.CreationTime
                };
            } catch (JsonException) { return null; }
        }

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class PublishedWorldMeta {
            [JsonProperty] public string Id;
            [JsonProperty] public string Name;
            [JsonProperty] public DateTime LastAccessTime;
            [JsonProperty] public DateTime CreationTime;
        }
    }
}
