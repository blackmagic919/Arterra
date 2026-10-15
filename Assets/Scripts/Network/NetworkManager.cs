using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Newtonsoft.Json;
using Arterra.Core.Network.Authentication;
using Arterra.Core.Network.Service;
using Arterra.GamePlay.UI;
using System.IO;
using Arterra.Engine.Terrain;
using UnityEngine;

namespace Arterra.Core.Network {
    /// <summary>Session advertisement and discovery. Invoke on Unity's main thread.</summary>
    public sealed class NetworkManager : MonoBehaviour {
        public static NetworkManager Instance { get; private set; }
        private static bool initialized;
        private const string MultiplayerHook = "EnableMultiplayer";
        private const int RecoveryInitialDelayMilliseconds = 2000;
        private const int RecoveryMaxDelayMilliseconds = 30000;

        private static readonly SemaphoreSlim lifecycle = new(1, 1);
        private static readonly SemaphoreSlim queries = new(1, 1);
        private static bool joiningClient;
        private static CancellationTokenSource hosting;
        private static string hostedWorldId;
        private static bool recovering;

        private static NetworkSettings settings => Config.CURRENT.System.Network.value;
        private static bool MultiplayerEnabled => Config.CURRENT.GamePlay.Gamemodes.value.EnableMultiplayer;

        public static Guid? SessionId { get; private set; }
        private static GameServiceClient service;
        internal static GameServiceClient Service {
            get {
                string url = settings.ServiceUrl.TrimEnd('/');
                if (service == null || (SessionId == null && service.BaseUrl != url))
                    service = new GameServiceClient(url);
                return service;
            }
        }
        /// <summary>Offline games and hosts own their state; joining clients do not.</summary>
        public static bool IsActingServer => !joiningClient && (!TransportManager.Instance.IsStarted || TransportManager.Instance.IsServer);

        private void Awake() {
            if (Instance == null) {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            } else {
                Destroy(gameObject);
            }
        }

        private void Update() {
            if (Instance == this) 
                TransportManager.Instance.Update();
        }

        private void OnDestroy() {
            if (Instance != this) return;
            Instance = null;
            Observe(ReleaseAsync());
        }

        public static void Initialize() {
            if (initialized) return;

            Config.CURRENT.System.AddHook(MultiplayerHook, ToggleMultiplayer);
            initialized = true;
            if (MultiplayerEnabled && World.GALAXY_TYPE == World.GalaxyType.Local) Observe(EnableMultiplayer());
        }

        public static async Task ReleaseAsync() {
            if (initialized)
                Config.CURRENT.System.RemoveHook(MultiplayerHook, ToggleMultiplayer);

            initialized = false;
            await DisableMultiplayer();
        }

        private static void ToggleMultiplayer(ref object rule) {
            if (World.GALAXY_TYPE == World.GalaxyType.Local && rule is bool enabled)
                Observe(enabled ? EnableMultiplayer() : DisableMultiplayer());
        }

        // Hooks cannot return a Task; explicitly observe their failures.
        private static async void Observe(Task operation) {
            try { await operation; }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        /// <summary>Advertise the selected world's metadata once; repeated calls are safe.</summary>
        public static async Task EnableMultiplayer() {
            await lifecycle.WaitAsync();
            try {
                if (hosting != null && TransportManager.Instance.IsRunning) return;
                if (SessionId != null && hosting == null)
                    throw new InvalidOperationException("Leave the remote world before hosting.");

                World.WorldMeta meta = World.WORLD_SELECTION?.First?.Value;
                if (meta == null || meta.Type != World.GalaxyType.Local)
                    throw new InvalidOperationException("Select a local world before hosting.");

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
            var token = hosting.Token;
            var api = Service;
            PlayerConnection.Initialize();
            OptionsHandler.RefreshDisplayMap();
            var published = new PublishedWorld {
                Id = meta.Id, CreationTime = meta.CreationTime,
                LastAccessTime = meta.LastAccessTime, Config = Config.CURRENT
            };
            byte[] metadata = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(published));
            byte[] chunk = ChunkMapCodec.EncodePreview(OptionsHandler.GetDisplayMap());

            try {
                var created = await api.CreateAsync(meta.Name, metadata, chunk, token);
                SessionId = created.SessionId;
                await Task.WhenAll(GameServiceClient.UploadAsync(created.Metadata, metadata, token),
                    GameServiceClient.UploadAsync(created.DisplayChunk, chunk, token));
                await api.CompleteAsync(created.SessionId, token);
                var ticket = await api.HostAsync(created.SessionId, token);
                await TransportManager.Instance.StartAsync(created.SessionId, ticket, true, token);
            } catch {
                await TransportManager.Instance.StopAsync();
                throw;
            }
        }

        private static void OnTransportStopped(Exception reason) => ScheduleRecovery();

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
                        if (SessionId.HasValue && TransportManager.Instance.IsRunning) return;

                        try { await LeaveSessionAsync(); }
                        catch (Exception exception) { Debug.LogException(exception); }

                        if (!ShouldRecover(lifetime)) return;
                        await CreateHostSessionAsync(World.WORLD_SELECTION.First.Value);
                        if (!ShouldRecover(lifetime)) return;
                        if (SessionId.HasValue && TransportManager.Instance.IsRunning) return;
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
            } finally {
                hostedWorldId = null;
                lifecycle.Release();
            }
        }

        /// <summary>
        /// Connect to a session, leaving any previous client session. Replicated state is fetched separately.
        /// </summary>
        public static async Task ConnectSessionAsync(string sessionId, CancellationToken token = default) {
            Guid id = Guid.Parse(sessionId);
            await lifecycle.WaitAsync(token);
            try {
                if (hosting != null) throw new InvalidOperationException("Stop hosting before joining another world.");
                if (SessionId == id && TransportManager.Instance.IsRunning) return;
                await LeaveSessionAsync();
                var api = Service;
                PlayerConnection.Initialize();
                joiningClient = true;
                try {
                    var joined = await api.JoinAsync(sessionId, token);
                    SessionId = id;
                    await TransportManager.Instance.StartAsync(id, joined.Connection, false, token);
                    // The published preview may be older than the live host configuration.
                    await Config.CurrentVariable.GetAsync(token);
                    Config.CURRENT.System.Network.value.ServiceUrl = api.BaseUrl;
                    await PlayerConnection.JoinAsync(token);
                } catch {
                    await LeaveSessionAsync();
                    throw;
                } finally { joiningClient = false; }
            } finally { lifecycle.Release(); }
        }

        public static async Task DisconnectClientAsync() {
            await lifecycle.WaitAsync();
            try {
                if (hosting == null) await LeaveSessionAsync();
            } finally { lifecycle.Release(); }
        }

        // Called while holding the lifecycle semaphore.
        private static async Task LeaveSessionAsync() {
            var id = SessionId;
            bool hosted = TransportManager.Instance.IsServer || hostedWorldId != null;
            SessionId = null;
            // End the socket lease even if HTTP deletion fails.
            await TransportManager.Instance.StopAsync();
            if (hosted && id.HasValue) {
                await service.DeleteAsync(id.Value);
            }
        }

        public static async Task<List<World.WorldMeta>> QueryWorldMetasAsync(CancellationToken cancellationToken = default) {
            await queries.WaitAsync(cancellationToken);
            try {
                var api = Service;
                var worlds = new List<World.WorldMeta>();
                var cursors = new HashSet<Guid>();
                Guid? cursor = null;
                do {
                    var page = await api.ListAsync(cursor, cancellationToken);
                    foreach (var session in page.Sessions) {
                        worlds.Add(new World.WorldMeta(session.SessionId.ToString()) {
                            Type = World.GalaxyType.DirectWAN,
                            SessionId = session.SessionId.ToString(), Name = session.Name
                        });
                    }
                    cursor = page.NextCursor;
                    if (cursor.HasValue && !cursors.Add(cursor.Value))
                        throw new InvalidDataException("Repeated session-list cursor.");
                    // An empty filtered page can still have a continuation cursor.
                } while (cursor.HasValue);
                return worlds;
            } finally { queries.Release(); }
        }

        public static async Task<Config> LoadPreviewConfigAsync(World.WorldMeta meta, CancellationToken token) {
            var api = Service;
            var session = await api.GetAsync(meta.SessionId, token);
            byte[] bytes = await GameServiceClient.DownloadAsync(session.MetadataUrl, token);
            var world = JsonConvert.DeserializeObject<PublishedWorld>(Encoding.UTF8.GetString(bytes));
            if (world?.Config == null) throw new InvalidDataException("Session has no world configuration.");
            meta.Id = world.Id;
            meta.CreationTime = world.CreationTime;
            meta.LastAccessTime = world.LastAccessTime;
            // Service selection belongs to this machine, not the downloaded world's settings.
            world.Config.System.Network.value.ServiceUrl = api.BaseUrl;
            return world.Config;
        }

        public static async Task<MapData[]> LoadPreviewMapAsync(string id, CancellationToken token = default) {
            var session = await Service.GetAsync(id, token);
            var bytes = await GameServiceClient.DownloadAsync(session.DisplayChunkUrl, token);
            return ChunkMapCodec.DecodePreview(bytes);
        }

    }
}
